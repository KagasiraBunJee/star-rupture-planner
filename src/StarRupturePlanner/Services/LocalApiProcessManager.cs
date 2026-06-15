using System.Diagnostics;
using Directory = System.IO.Directory;
using DirectoryInfo = System.IO.DirectoryInfo;
using Path = System.IO.Path;

namespace StarRupturePlanner.Services;

public sealed class LocalApiProcessManager : IApiProcessManager
{
    private readonly IPlannerApiClient _apiClient;
    private Process? _process;

    public LocalApiProcessManager(IPlannerApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<string> EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        if (await _apiClient.IsApiAvailableAsync(cancellationToken))
        {
            return "API is already running.";
        }

        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return "Could not find starrupture_api beside the app. Start the API manually.";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = "-m starrupture_api.main serve --host 127.0.0.1 --port 8010",
            WorkingDirectory = repoRoot,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        _process = Process.Start(startInfo);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken);
            if (await _apiClient.IsApiAvailableAsync(cancellationToken))
            {
                return "Started local API.";
            }
        }

        return "Started API process, but it did not answer on port 8010.";
    }

    public static string? FindRepoRoot(string? startPath = null)
    {
        var directory = new DirectoryInfo(startPath ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "starrupture_api")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "starrupture_api")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    public void Dispose()
    {
        _process?.Dispose();
    }
}
