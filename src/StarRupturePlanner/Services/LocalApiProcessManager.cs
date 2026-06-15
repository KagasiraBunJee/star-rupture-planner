using System.Diagnostics;
using System.Globalization;
using StarRupturePlanner.Models;
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
            if (await IsCompatibleApiAsync(cancellationToken))
            {
                return "API is already running.";
            }

            StopStalePythonApiOnPort(_apiClient.BaseUri.Port);
        }
        else
        {
            StopStalePythonApiOnPort(_apiClient.BaseUri.Port);
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
                if (await IsCompatibleApiAsync(cancellationToken))
                {
                    return "Started local API.";
                }
            }
        }

        return "Started API process, but it did not answer on port 8010.";
    }

    private async Task<bool> IsCompatibleApiAsync(CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await _apiClient.GetCatalogAsync(cancellationToken);
            var hasCurrentShape = catalog.Corporations.Count > 0
                && catalog.TransportTiers.Tiers.Count >= 3
                && catalog.Buildings.Any(building =>
                    building.Power is not null || building.Temperature is not null);
            return hasCurrentShape && await SupportsLocalizationAsync(cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> SupportsLocalizationAsync(CancellationToken cancellationToken)
    {
        var previousLanguage = _apiClient.PlannerLanguage;
        try
        {
            _apiClient.PlannerLanguage = PlannerLanguages.Ukrainian;
            var catalog = await _apiClient.GetCatalogAsync(cancellationToken);
            var titaniumRod = catalog.Recipes.FirstOrDefault(recipe => recipe.RecipeId == "titanium-rod");
            return string.Equals(catalog.Meta.Language, PlannerLanguages.Ukrainian, StringComparison.Ordinal)
                && !string.Equals(titaniumRod?.Output.Name, "Titanium Rod", StringComparison.Ordinal);
        }
        finally
        {
            _apiClient.PlannerLanguage = previousLanguage;
        }
    }

    private static void StopStalePythonApiOnPort(int port)
    {
        var processIds = ListeningProcessIds(port);
        foreach (var processId in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.ProcessName.Contains("python", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            catch
            {
                // Best effort only. If the stale API cannot be stopped, startup
                // will report that the replacement process never became ready.
            }
        }
    }

    private static IReadOnlyList<int> ListeningProcessIds(int port)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano -p tcp",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            });
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return output
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => ListeningProcessId(line, port))
                .Where(processId => processId > 0)
                .Distinct()
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static int ListeningProcessId(string line, int port)
    {
        var columns = line
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (columns.Length < 5 || !string.Equals(columns[0], "TCP", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (!string.Equals(columns[3], "LISTENING", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (!PortMatches(columns[1], port))
        {
            return 0;
        }

        return int.TryParse(columns[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId)
            ? processId
            : 0;
    }

    private static bool PortMatches(string endpoint, int port)
    {
        var marker = ":" + port.ToString(CultureInfo.InvariantCulture);
        return endpoint.EndsWith(marker, StringComparison.OrdinalIgnoreCase);
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
