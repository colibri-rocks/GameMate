using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;

namespace GameMate.Services;

/// <summary>
/// Owns the single <see cref="HttpClient"/> used for every GitHub request, so the update check and the
/// update download share one connection pool and one set of request headers.
/// </summary>
/// <remarks>
/// The client deliberately carries no overall timeout. A multi-megabyte release archive must not be aborted
/// after the few seconds that suit a small API call, so each caller enforces its own limit through a
/// cancellation token instead. Creating a client per request would also exhaust sockets.
/// </remarks>
internal static class GitHubHttpClient
{
    /// <summary>
    /// Gets the shared client.
    /// </summary>
    internal static HttpClient Instance { get; } = Create();

    /// <summary>
    /// Reads the version of the running assembly.
    /// </summary>
    /// <returns>
    /// The informational version without any build suffix, otherwise the assembly version, otherwise
    /// <c>0.0.0</c>.
    /// </returns>
    internal static string ResolveVersion()
    {
        Assembly assembly = typeof(GitHubHttpClient).Assembly;

        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (informational is { Length: > 0 })
        {
            int plus = informational.IndexOf('+');

            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>
    /// Builds the shared client.
    /// </summary>
    /// <returns>A client configured for the GitHub API.</returns>
    private static HttpClient Create()
    {
        HttpClient client = new()
        {
            // Per-request cancellation tokens carry the real limits.
            Timeout = Timeout.InfiniteTimeSpan,
        };

        // GitHub answers 403 to a request that carries no User-Agent, so this header is not optional.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"GameMate/{ResolveVersion()}");

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        return client;
    }
}
