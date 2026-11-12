using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace GameMate.Services;

/// <summary>
/// Downloads a published GameMate release archive and unpacks it into a staging folder that has been
/// verified to contain the application executable.
/// </summary>
public interface IUpdateDownloadService
{
    /// <summary>
    /// Downloads the release archive and returns the path of the completed file.
    /// </summary>
    /// <param name="downloadUrl">Direct URL of the release archive.</param>
    /// <param name="version">Version the archive belongs to; names the staging folder.</param>
    /// <param name="progress">
    /// Receives whole percentages from 0 to 100, or <c>-1</c> once when the server reports no content length.
    /// May be <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Aborts the download and removes the partial file.</param>
    /// <returns>The path of the downloaded archive.</returns>
    /// <exception cref="HttpRequestException">The server refused or the transfer failed.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled the download.</exception>
    Task<string> DownloadAsync(
        Uri downloadUrl,
        string version,
        IProgress<double>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Unpacks a downloaded archive into a staging folder and validates the payload.
    /// </summary>
    /// <param name="archivePath">Path returned by <see cref="DownloadAsync"/>.</param>
    /// <param name="version">Version the archive belongs to.</param>
    /// <returns>The folder that must be copied over the installation directory.</returns>
    /// <exception cref="InvalidDataException">The archive is corrupt or lacks <c>GameMate.exe</c>.</exception>
    string Stage(string archivePath, string version);

    /// <summary>
    /// Deletes an update folder without throwing.
    /// </summary>
    /// <param name="path">Folder to remove; ignored when empty or missing.</param>
    void Delete(string? path);
}

/// <summary>
/// Default <see cref="IUpdateDownloadService"/>.
/// </summary>
/// <remarks>
/// The archive is written to a <c>.part</c> file that is renamed only once complete, so a cancelled or failed
/// transfer can never leave a truncated file that later looks like a finished download.
/// </remarks>
public sealed class UpdateDownloadService : IUpdateDownloadService
{
    /// <summary>
    /// Size of the transfer buffer. Large enough to keep the progress reports meaningful without holding much
    /// memory.
    /// </summary>
    private const int BufferSize = 81920;

    /// <summary>
    /// Name of the executable the archive must contain before it may be installed.
    /// </summary>
    private const string ExecutableName = "GameMate.exe";

    /// <inheritdoc />
    public async Task<string> DownloadAsync(
        Uri downloadUrl,
        string version,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloadUrl);

        string root = UpdatePaths.RootFor(version);
        Directory.CreateDirectory(root);

        string archivePath = Path.Combine(root, $"GameMate_{version}.zip");
        string partialPath = archivePath + ".part";

        try
        {
            using HttpResponseMessage response = await GitHubHttpClient.Instance
                .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;

            // Scoped so both streams are closed before the rename below; Windows refuses to move an open file.
            await using (Stream source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                await using FileStream target = new(
                    partialPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    useAsync: true);

                byte[] buffer = new byte[BufferSize];
                long received = 0;
                int lastPercent = -1;
                bool reportedUnknown = false;

                int read;

                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

                    received += read;

                    if (total is > 0)
                    {
                        int percent = (int)Math.Clamp(received * 100 / total.Value, 0, 100);

                        // Only whole percentages are reported, so the UI updates a hundred times at most.
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress?.Report(percent);
                        }
                    }
                    else if (!reportedUnknown)
                    {
                        // Without a content length the UI switches to wording that has no percentage in it.
                        reportedUnknown = true;
                        progress?.Report(-1);
                    }
                }
            }

            File.Move(partialPath, archivePath, overwrite: true);

            return archivePath;
        }
        catch
        {
            // A cancelled or failed transfer must not leave a partial file behind.
            TryRemoveFile(partialPath);

            throw;
        }
    }

    /// <inheritdoc />
    public string Stage(string archivePath, string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(archivePath);

        string root = UpdatePaths.RootFor(version);
        string staging = Path.Combine(root, "staging");

        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(staging);

        // ZipFile rejects entries that escape the destination, so no extra traversal check is needed here.
        ZipFile.ExtractToDirectory(archivePath, staging, overwriteFiles: true);

        string payload = staging;
        string executable = Path.Combine(staging, ExecutableName);

        if (!File.Exists(executable))
        {
            // An archive created by compressing a folder wraps everything in one top-level entry.
            string? wrapped = Directory
                .GetDirectories(staging)
                .FirstOrDefault(candidate => File.Exists(Path.Combine(candidate, ExecutableName)));

            if (wrapped is not null)
            {
                payload = wrapped;
                executable = Path.Combine(wrapped, ExecutableName);
            }
        }

        if (!File.Exists(executable) || new FileInfo(executable).Length == 0)
        {
            throw new InvalidDataException("The release archive does not contain GameMate.exe.");
        }

        return payload;
    }

    /// <inheritdoc />
    public void Delete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Leftover temporary data is not worth failing an update over; a later start sweeps it up.
        }
    }

    /// <summary>
    /// Removes a single file without throwing.
    /// </summary>
    /// <param name="path">File to remove.</param>
    private static void TryRemoveFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover .part file is harmless: the next attempt overwrites it with FileMode.Create.
        }
    }
}

/// <summary>
/// Locations used by the update flow.
/// </summary>
internal static class UpdatePaths
{
    /// <summary>
    /// Gets the root of the update workspace, under the current user's temporary folder.
    /// </summary>
    internal static string Root { get; } = Path.Combine(Path.GetTempPath(), "GameMate", "update");

    /// <summary>
    /// Gets the workspace folder for one version.
    /// </summary>
    /// <param name="version">Version the folder belongs to.</param>
    /// <returns>An absolute path safe to create.</returns>
    /// <remarks>
    /// The version is reduced to digits and dots so a malformed or hostile tag cannot introduce a path
    /// separator or an invalid character into the folder name.
    /// </remarks>
    internal static string RootFor(string version)
    {
        string safe = new(version.Where(character => char.IsDigit(character) || character == '.').ToArray());

        return Path.Combine(Root, safe.Length == 0 ? "unknown" : safe);
    }
}
