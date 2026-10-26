using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameMate.Models;

namespace GameMate.Services;

/// <summary>
/// Compares the running application version with the latest release published on GitHub.
/// </summary>
public interface IUpdateCheckService
{
    /// <summary>
    /// Gets the result of the update check.
    /// </summary>
    /// <param name="force">
    /// <see langword="true"/> to bypass the session cache and query GitHub again; <see langword="false"/> to
    /// reuse an earlier result when one exists.
    /// </param>
    /// <returns>The outcome. Never <see langword="null"/> and never throws.</returns>
    Task<UpdateCheckResult> CheckAsync(bool force);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// The result is cached for the lifetime of this instance, and the composition root registers a single instance
/// for the whole process. That matters because <c>SystemInfoViewModel</c> is rebuilt every time the information
/// window opens, so a cache living in the view model would pay one request per open.
/// </para>
/// <para>
/// The motive is the rate limit: the unauthenticated GitHub REST API allows only 60 requests per hour per IP
/// address, so an automatic check on every window open could exhaust it on a busy machine. Caching the result
/// and offering an explicit refresh keeps normal use at one request per application run.
/// </para>
/// <para>
/// A single shared <see cref="HttpClient"/> is reused with a short timeout. Creating one per check would leak
/// sockets, and an unbounded timeout would leave the window waiting on "checking" indefinitely.
/// </para>
/// </remarks>
public sealed class UpdateCheckService : IUpdateCheckService
{
    /// <summary>
    /// REST endpoint listing the repository releases, newest first.
    /// </summary>
    private const string ReleasesApiUrl = "https://api.github.com/repos/colibri-rocks/GameMate/releases";

    /// <summary>
    /// Human-facing release page, and the base the download asset URL is built from.
    /// </summary>
    private const string ReleasesPageUrl = "https://github.com/colibri-rocks/GameMate/releases";

    /// <summary>
    /// File name stem of the published archive, which is <c>GameMate_v0.7.1.zip</c> for tag <c>v0.7.1</c>.
    /// </summary>
    private const string ArchiveNamePrefix = "GameMate_";

    /// <summary>
    /// Shared client. GitHub requires a User-Agent and rejects requests without one.
    /// </summary>
    private static readonly HttpClient Client = CreateClient();

    /// <summary>
    /// Guards <see cref="_cachedCheck"/> so concurrent callers share one request instead of racing.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Task<UpdateCheckResult>? _cachedCheck;

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(bool force)
    {
        if (!force && _cachedCheck is not null)
        {
            return await _cachedCheck.ConfigureAwait(false);
        }

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            // Re-checked under the lock: a concurrent caller may have populated the cache while this one waited.
            if (!force && _cachedCheck is not null)
            {
                return await _cachedCheck.ConfigureAwait(false);
            }

            Task<UpdateCheckResult> check = QueryAsync();
            _cachedCheck = check;

            return await check.ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Builds the shared client.
    /// </summary>
    /// <returns>A client configured for the GitHub API.</returns>
    private static HttpClient CreateClient()
    {
        HttpClient client = new()
        {
            // Short enough that the window never sits on "checking" for long, long enough for a slow link.
            Timeout = TimeSpan.FromSeconds(8),
        };

        // GitHub returns 403 to requests without a User-Agent, so this header is not optional.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"GameMate/{ResolveRunningVersion()}");

        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        return client;
    }

    /// <summary>
    /// Performs the request and reduces the response to a result.
    /// </summary>
    /// <returns>The outcome, or a failure result for every error path.</returns>
    private static async Task<UpdateCheckResult> QueryAsync()
    {
        try
        {
            using HttpResponseMessage response = await Client
                .GetAsync(ReleasesApiUrl, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // Covers 403 from the rate limit as well as 404 and 5xx.
                return UpdateCheckResult.Failed();
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

            ReleaseDto[]? releases = await JsonSerializer
                .DeserializeAsync<ReleaseDto[]>(stream)
                .ConfigureAwait(false);

            if (releases is null || releases.Length == 0)
            {
                return UpdateCheckResult.Failed();
            }

            (Version Newest, string Tag)? newest = SelectNewestRelease(releases);

            if (newest is null)
            {
                return UpdateCheckResult.Failed();
            }

            if (!TryParseVersion(ResolveRunningVersion(), out Version running))
            {
                return UpdateCheckResult.Failed();
            }

            if (newest.Value.Newest <= running)
            {
                return UpdateCheckResult.UpToDate();
            }

            string tag = newest.Value.Tag;

            // Built from the tag rather than from assets[].browser_download_url because the published archive
            // name is deterministic for this repository. A renamed asset would make this URL 404, which is the
            // trade-off for not depending on the asset list being present in the response.
            string downloadUrl = $"{ReleasesPageUrl}/download/{tag}/{ArchiveNamePrefix}{tag}.zip";

            return UpdateCheckResult.NewVersion(tag.TrimStart('v', 'V'), downloadUrl);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            or TaskCanceledException
            or TimeoutException
            or JsonException
            or InvalidOperationException
            or IOException)
        {
            // The window must never show an unhandled error because a release feed was unreachable.
            return UpdateCheckResult.Failed();
        }
    }

    /// <summary>
    /// Picks the highest published version, ignoring drafts, prereleases and unparseable tags.
    /// </summary>
    /// <param name="releases">Deserialised release list.</param>
    /// <returns>The highest version with its tag, or <see langword="null"/> when nothing usable was published.</returns>
    /// <remarks>
    /// The highest version is chosen rather than the first entry, so the result does not rely on the API's
    /// ordering and a back-dated release cannot mask a newer one.
    /// </remarks>
    private static (Version Newest, string Tag)? SelectNewestRelease(ReleaseDto[] releases)
    {
        Version? newest = null;
        string? newestTag = null;

        foreach (ReleaseDto release in releases)
        {
            if (release.Draft || release.Prerelease)
            {
                continue;
            }

            if (release.TagName is not { Length: > 0 } tag)
            {
                continue;
            }

            if (!TryParseVersion(tag, out Version candidate))
            {
                continue;
            }

            if (newest is null || candidate > newest)
            {
                newest = candidate;
                newestTag = tag;
            }
        }

        return newest is null || newestTag is null ? null : (newest, newestTag);
    }

    /// <summary>
    /// Parses a version out of a git tag or release title.
    /// </summary>
    /// <param name="text">Text such as <c>v0.7.1</c>, <c>0.7</c> or <c>v0.7.1-beta</c>.</param>
    /// <param name="version">Receives the parsed version.</param>
    /// <returns><see langword="true"/> when a usable version was found.</returns>
    /// <remarks>
    /// Comparison is numeric through <see cref="Version"/>, never textual, so <c>0.10.0</c> correctly outranks
    /// <c>0.9.0</c> - a string comparison would report the opposite.
    /// </remarks>
    private static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string candidate = text.Trim();

        if (candidate.StartsWith('v') || candidate.StartsWith('V'))
        {
            candidate = candidate[1..];
        }

        // A pre-release or build suffix is cut away: this compares published versions, not channel names.
        int cut = candidate.IndexOfAny(['-', '+']);

        if (cut >= 0)
        {
            candidate = candidate[..cut];
        }

        // Version.TryParse rejects "0.7" and "1", and hand-written tags use both.
        candidate = candidate.Count(character => character == '.') switch
        {
            0 => candidate + ".0.0",
            1 => candidate + ".0",
            _ => candidate,
        };

        if (Version.TryParse(candidate, out Version? parsed) && parsed is not null)
        {
            version = parsed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the version of this assembly.
    /// </summary>
    /// <returns>The informational version without a build suffix, else the assembly version, else <c>0.0.0</c>.</returns>
    /// <remarks>
    /// Resolved here rather than injected so the service stays usable without the system information service and
    /// without a second call to it.
    /// </remarks>
    private static string ResolveRunningVersion()
    {
        Assembly assembly = typeof(UpdateCheckService).Assembly;

        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (informational is { Length: > 0 })
        {
            int plus = informational.IndexOf('+');

            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>
    /// One entry of the releases response, reduced to the fields this check needs.
    /// </summary>
    /// <remarks>
    /// <c>name</c> and <c>assets</c> are deliberately not mapped: the tag already carries the version, and the
    /// download URL is built from that tag, so mapping them would only add dead state.
    /// </remarks>
    private sealed class ReleaseDto
    {
        /// <summary>
        /// Gets the git tag, for example <c>v0.7.1</c>.
        /// </summary>
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        /// <summary>
        /// Gets a value indicating whether the release is a draft, which is not published yet.
        /// </summary>
        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        /// <summary>
        /// Gets a value indicating whether the release is marked pre-release.
        /// </summary>
        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }
    }
}
