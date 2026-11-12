using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GameMate.Services;

/// <summary>
/// Replaces the running installation with a staged release by handing the work to a short-lived helper that
/// can overwrite <c>GameMate.exe</c> because it does not hold the lock.
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>
    /// Determines whether the installation folder accepts new files without elevation.
    /// </summary>
    /// <returns><see langword="true"/> when a probe file could be created and removed.</returns>
    bool IsInstallDirectoryWritable();

    /// <summary>
    /// Writes the swap script and starts it, elevated when the installation folder needs it.
    /// </summary>
    /// <param name="stagingDirectory">Validated folder produced by <see cref="IUpdateDownloadService.Stage"/>.</param>
    /// <param name="version">Version being installed, used to name the update folder.</param>
    /// <exception cref="UnauthorizedAccessException">Elevation was required and the user declined it.</exception>
    /// <exception cref="InvalidOperationException">The helper process could not be started.</exception>
    void Launch(string stagingDirectory, string version);

    /// <summary>
    /// Removes leftovers of a previous update. Safe to call on every application start.
    /// </summary>
    void CleanupAfterUpdate();
}

/// <summary>
/// Default <see cref="IUpdateInstaller"/>.
/// </summary>
/// <remarks>
/// <para>
/// The swap is performed by a PowerShell script written into the update workspace at the moment of use. A
/// helper executable shipped inside the application folder could never overwrite the folder that contains it,
/// and a separate project would change both the solution and the release archive, so a generated script is
/// the smallest thing that works.
/// </para>
/// <para>
/// Every path is embedded as a single-quoted PowerShell literal with quotes doubled, so folder names
/// containing spaces or apostrophes need no command-line quoting. No arguments are passed at all.
/// </para>
/// </remarks>
public sealed class UpdateInstaller : IUpdateInstaller
{
    /// <summary>
    /// Name of the application executable, and of the file the archive must contain.
    /// </summary>
    private const string ExecutableName = "GameMate.exe";

    /// <summary>
    /// Win32 error reported when the user dismisses a UAC prompt.
    /// </summary>
    private const int ElevationCancelled = 1223;

    /// <inheritdoc />
    public bool IsInstallDirectoryWritable()
    {
        try
        {
            string probe = Path.Combine(AppContext.BaseDirectory, $".update-probe-{Guid.NewGuid():N}.tmp");

            // DeleteOnClose means a successful probe leaves nothing behind even if the process stops abruptly.
            using FileStream stream = new(
                probe,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A typical Program Files installation lands here when the process is not elevated.
            return false;
        }
    }

    /// <inheritdoc />
    public void Launch(string stagingDirectory, string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(stagingDirectory);
        ArgumentException.ThrowIfNullOrEmpty(version);

        string workspace = UpdatePaths.RootFor(version);
        Directory.CreateDirectory(workspace);

        string scriptPath = Path.Combine(workspace, "apply-update.ps1");

        // A BOM is required: PowerShell 5.1 reads a BOM-less script as ANSI and would mangle non-ASCII paths.
        File.WriteAllText(
            scriptPath,
            BuildScript(AppContext.BaseDirectory, stagingDirectory, workspace),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        ProcessStartInfo startInfo = new("powershell.exe")
        {
            UseShellExecute = true,
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
        };

        if (!IsInstallDirectoryWritable())
        {
            // Only the short-lived helper is elevated, not the whole UI. Re-running the application elevated
            // would grant far more than the file copy actually needs.
            startInfo.Verb = "runas";
        }

        try
        {
            using Process? process = Process.Start(startInfo);

            if (process is null)
            {
                throw new InvalidOperationException("The update helper could not be started.");
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ElevationCancelled)
        {
            // Mapped to the localised Update.Error.ElevationDenied message by the caller.
            throw new UnauthorizedAccessException("The user declined the elevation prompt.", exception);
        }
    }

    /// <inheritdoc />
    public void CleanupAfterUpdate()
    {
        try
        {
            if (!Directory.Exists(UpdatePaths.Root))
            {
                return;
            }

            string current = Path.GetFileName(UpdatePaths.RootFor(GitHubHttpClient.ResolveVersion()));

            foreach (string directory in Directory.GetDirectories(UpdatePaths.Root))
            {
                // The running version's folder is kept: its backup is the rollback copy for this very start.
                if (string.Equals(Path.GetFileName(directory), current, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A stale folder is harmless; the next start tries again.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup must never prevent the application from starting.
        }
    }

    /// <summary>
    /// Builds the PowerShell script that performs the swap.
    /// </summary>
    /// <param name="installDirectory">Folder that holds the running application.</param>
    /// <param name="stagingDirectory">Validated payload produced by the download service.</param>
    /// <param name="workspace">Folder for the backup and the log.</param>
    /// <returns>The complete script text.</returns>
    private static string BuildScript(string installDirectory, string stagingDirectory, string workspace)
    {
        string executable = Path.Combine(installDirectory, ExecutableName);
        string backup = Path.Combine(workspace, "backup");
        string log = Path.Combine(workspace, "apply-update.log");

        StringBuilder script = new();

        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine($"$processId = {Environment.ProcessId}");
        script.AppendLine($"$install = {Quote(installDirectory)}");
        script.AppendLine($"$staging = {Quote(stagingDirectory)}");
        script.AppendLine($"$backup = {Quote(backup)}");
        script.AppendLine($"$log = {Quote(log)}");
        script.AppendLine($"$executable = {Quote(executable)}");
        script.AppendLine();

        // Wait up to 60 s in 200 ms steps for the application to release its file locks.
        script.AppendLine("for ($i = 0; $i -lt 300; $i++) {");
        script.AppendLine("    if (-not (Get-Process -Id $processId -ErrorAction SilentlyContinue)) { break }");
        script.AppendLine("    Start-Sleep -Milliseconds 200");
        script.AppendLine("}");
        script.AppendLine();

        script.AppendLine("if (Get-Process -Id $processId -ErrorAction SilentlyContinue) {");
        script.AppendLine("    'GameMate did not exit in time; no files were changed.' | Out-File -LiteralPath $log -Encoding utf8");
        script.AppendLine("    exit 2");
        script.AppendLine("}");
        script.AppendLine();

        // The backup is taken before anything is overwritten, so a failed copy can be undone.
        script.AppendLine("if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }");
        script.AppendLine("Copy-Item -LiteralPath $install -Destination $backup -Recurse -Force");
        script.AppendLine();

        script.AppendLine("try {");
        script.AppendLine("    for ($attempt = 0; $attempt -lt 20; $attempt++) {");
        script.AppendLine("        try {");
        script.AppendLine("            Copy-Item -Path (Join-Path $staging '*') -Destination $install -Recurse -Force");
        script.AppendLine("            break");
        script.AppendLine("        } catch {");
        script.AppendLine("            if ($attempt -eq 19) { throw }");
        script.AppendLine("            Start-Sleep -Milliseconds 500");
        script.AppendLine("        }");
        script.AppendLine("    }");
        script.AppendLine("    'update completed' | Out-File -LiteralPath $log -Encoding utf8");
        script.AppendLine("    Start-Process -FilePath $executable");
        script.AppendLine("    exit 0");
        script.AppendLine("} catch {");
        script.AppendLine("    'update failed: ' + $_.Exception.Message | Out-File -LiteralPath $log -Encoding utf8");
        script.AppendLine("    'restoring the previous installation' | Out-File -LiteralPath $log -Encoding utf8 -Append");
        script.AppendLine("    if (Test-Path -LiteralPath $backup) {");
        script.AppendLine("        Copy-Item -Path (Join-Path $backup '*') -Destination $install -Recurse -Force");
        script.AppendLine("    }");
        script.AppendLine("    Start-Process -FilePath $executable");
        script.AppendLine("    exit 3");
        script.AppendLine("}");

        return script.ToString();
    }

    /// <summary>
    /// Turns a path into a PowerShell single-quoted literal.
    /// </summary>
    /// <param name="value">Path to quote.</param>
    /// <returns>The quoted literal.</returns>
    /// <remarks>
    /// Doubling the apostrophe is the escape rule inside single-quoted PowerShell strings, and it avoids every
    /// backslash and space problem the command line would otherwise introduce.
    /// </remarks>
    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
