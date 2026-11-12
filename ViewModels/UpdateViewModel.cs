using System.IO;
using System.Net.Http;
using System.Windows.Input;
using GameMate.Infrastructure;
using GameMate.Localization;
using GameMate.Models;
using GameMate.Services;

namespace GameMate.ViewModels;

/// <summary>
/// Drives the update window: the confirmation, the download progress, cancellation and the hand-over to the
/// out-of-process helper.
/// </summary>
/// <remarks>
/// The window offers four states taken from <see cref="UpdatePhase"/>: idle, downloading, staging and failed.
/// Nothing is transferred until the user activates <see cref="InstallCommand"/>, which is the confirmation
/// step; the update check itself only ever reports availability.
/// </remarks>
public sealed class UpdateViewModel : ObservableObject, IDisposable
{
    private readonly IUpdateCheckService _updateCheckService;
    private readonly IUpdateDownloadService _downloadService;
    private readonly IUpdateInstaller _installer;

    private UpdateCheckResult _result;
    private UpdatePhase _phase = UpdatePhase.Idle;
    private double _progress;
    private string? _failure;
    private CancellationTokenSource? _cancellation;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateViewModel"/> class.
    /// </summary>
    /// <param name="updateCheckService">Supplies the running version and re-checks on demand.</param>
    /// <param name="downloadService">Downloads and stages the release archive.</param>
    /// <param name="installer">Starts the helper that swaps the files.</param>
    /// <param name="initialResult">Check result the main window already knows about.</param>
    /// <exception cref="ArgumentNullException">Thrown when any dependency is null.</exception>
    public UpdateViewModel(
        IUpdateCheckService updateCheckService,
        IUpdateDownloadService downloadService,
        IUpdateInstaller installer,
        UpdateCheckResult initialResult)
    {
        ArgumentNullException.ThrowIfNull(updateCheckService);
        ArgumentNullException.ThrowIfNull(downloadService);
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(initialResult);

        _updateCheckService = updateCheckService;
        _downloadService = downloadService;
        _installer = installer;
        _result = initialResult;

        InstallCommand = new RelayCommand(() => _ = InstallAsync());
        CancelCommand = new RelayCommand(Cancel);
        CheckAgainCommand = new RelayCommand(() => _ = CheckAgainAsync());
        OpenDownloadPageCommand = new RelayCommand(OpenDownloadPage);
    }

    /// <summary>
    /// Occurs when the payload is staged, the helper is running and the application must exit.
    /// </summary>
    public event EventHandler? RestartRequested;

    /// <summary>
    /// Gets the line describing the current state, in the selected language.
    /// </summary>
    public string StatusText => _result.State switch
    {
        UpdateCheckState.UpToDate => LanguageService.Current["Update.Status.UpToDate"],
        UpdateCheckState.NewVersionAvailable => LanguageService.Current.Format(
            "Update.Status.Available",
            _result.LatestVersion ?? string.Empty),
        UpdateCheckState.Failed => LanguageService.Current["Update.Status.Failed"],
        _ => LanguageService.Current["Update.Status.Checking"],
    };

    /// <summary>
    /// Gets the progress line, or an empty string while no transfer is running.
    /// </summary>
    public string ProgressText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the download completion percentage, from 0 to 100.
    /// </summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>
    /// Gets the localised failure explanation, or <see langword="null"/>.
    /// </summary>
    public string? Failure
    {
        get => _failure;
        private set => SetProperty(ref _failure, value);
    }

    /// <summary>
    /// Gets a value indicating whether the failure line is visible.
    /// </summary>
    public bool HasFailure => Failure is { Length: > 0 };

    /// <summary>
    /// Gets a value indicating whether the progress controls are visible.
    /// </summary>
    public bool IsBusy => _phase is UpdatePhase.Downloading or UpdatePhase.Staging;

    /// <summary>
    /// Gets a value indicating whether the download can be cancelled right now.
    /// </summary>
    public bool CanCancel => _phase == UpdatePhase.Downloading;

    /// <summary>
    /// Gets a value indicating whether the install action is offered.
    /// </summary>
    public bool CanInstall => _phase == UpdatePhase.Idle
        && _result.State == UpdateCheckState.NewVersionAvailable
        && _result.DownloadUrl is { Length: > 0 };

    /// <summary>
    /// Gets a value indicating whether the re-check action is offered.
    /// </summary>
    public bool CanCheck => _phase == UpdatePhase.Idle;

    /// <summary>
    /// Gets the command that starts the download, staging and swap.
    /// </summary>
    public ICommand InstallCommand { get; }

    /// <summary>
    /// Gets the command that aborts the running download.
    /// </summary>
    public ICommand CancelCommand { get; }

    /// <summary>
    /// Gets the command that re-runs the update check with the per-session cache bypassed.
    /// </summary>
    public ICommand CheckAgainCommand { get; }

    /// <summary>
    /// Gets the command that opens the release page in the default browser.
    /// </summary>
    public ICommand OpenDownloadPageCommand { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cancellation?.Dispose();
        _cancellation = null;
    }

    /// <summary>
    /// Downloads, stages and launches the swap, reporting progress as it goes.
    /// </summary>
    /// <returns>A task that completes when the attempt has finished, successfully or not.</returns>
    private async Task InstallAsync()
    {
        if (_result.State != UpdateCheckState.NewVersionAvailable
            || _result.DownloadUrl is not { Length: > 0 } url
            || _result.LatestVersion is not { Length: > 0 } version)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();

        string? archive = null;

        try
        {
            SetPhase(UpdatePhase.Downloading);
            Progress = 0;

            // Created on the UI thread so Progress<T> marshals the reports back automatically.
            Progress<double> progress = new(percent =>
            {
                if (percent < 0)
                {
                    ProgressText = LanguageService.Current["Update.Progress.DownloadingUnknown"];
                }
                else
                {
                    Progress = percent;
                    ProgressText = LanguageService.Current.Format("Update.Progress.Downloading", (int)percent);
                }

                OnPropertyChanged(nameof(ProgressText));
            });

            archive = await _downloadService
                .DownloadAsync(new Uri(url), version, progress, _cancellation.Token)
                .ConfigureAwait(true);

            SetPhase(UpdatePhase.Staging);
            ProgressText = LanguageService.Current["Update.Progress.Staging"];
            OnPropertyChanged(nameof(ProgressText));

            string staging = _downloadService.Stage(archive, version);

            _installer.Launch(staging, version);

            SetPhase(UpdatePhase.Ready);
            ProgressText = LanguageService.Current["Update.Progress.Ready"];
            OnPropertyChanged(nameof(ProgressText));

            RestartRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Cancelling is a normal outcome: drop the partial file and return to the offer.
            _downloadService.Delete(archive);
            SetPhase(UpdatePhase.Idle);
        }
        catch (InvalidDataException exception)
        {
            Fail("Update.Error.Extract", exception.Message);
        }
        catch (UnauthorizedAccessException)
        {
            Fail("Update.Error.ElevationDenied", null);
        }
        catch (HttpRequestException exception)
        {
            Fail("Update.Error.Download", exception.Message);
        }
        catch (Exception exception)
        {
            Fail("Update.Error.Start", exception.Message);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    /// <summary>
    /// Re-runs the check with the cache bypassed.
    /// </summary>
    /// <returns>A task that completes once the new result has been published.</returns>
    private async Task CheckAgainAsync()
    {
        SetPhase(UpdatePhase.Idle);
        Failure = null;

        _result = await _updateCheckService.CheckAsync(force: true).ConfigureAwait(true);

        RaiseStateChanged();
    }

    /// <summary>
    /// Replaces the current attempt.
    /// </summary>
    private void Cancel()
    {
        _cancellation?.Cancel();
    }

    /// <summary>
    /// Opens the release page so a user who cannot self-update still has a path forward.
    /// </summary>
    private void OpenDownloadPage()
    {
        string page = string.IsNullOrEmpty(_result.DownloadUrl)
            ? "https://github.com/colibri-rocks/GameMate/releases"
            : "https://github.com/colibri-rocks/GameMate/releases";

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(page) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // No default browser: a link must never be able to take the window down.
        }
    }

    /// <summary>
    /// Records a failure and shows the localised explanation.
    /// </summary>
    /// <param name="key">Resource key of the explanation.</param>
    /// <param name="detail">Underlying message appended to the text, or <see langword="null"/>.</param>
    private void Fail(string key, string? detail)
    {
        Failure = detail is { Length: > 0 }
            ? LanguageService.Current.Format(key, detail)
            : LanguageService.Current[key];

        SetPhase(UpdatePhase.Failed);
    }

    /// <summary>
    /// Moves to another phase and republishes everything derived from it.
    /// </summary>
    /// <param name="phase">Phase to move to.</param>
    private void SetPhase(UpdatePhase phase)
    {
        _phase = phase;

        if (phase == UpdatePhase.Idle)
        {
            Failure = null;
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Raises the notifications every phase or result change affects.
    /// </summary>
    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(HasFailure));
        OnPropertyChanged(nameof(ProgressText));
    }
}
