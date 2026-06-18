using System.Diagnostics;
using System.Globalization;
using StarRupturePlanner.Models;
using Directory = System.IO.Directory;
using DirectoryInfo = System.IO.DirectoryInfo;
using File = System.IO.File;
using Path = System.IO.Path;

namespace StarRupturePlanner.Services;

public sealed class LocalApiProcessManager : IApiProcessManager
{
    private static readonly string SourceApiProjectRelativePath = Path.Combine(
        "src",
        "StarRupturePlanner.Api",
        "StarRupturePlanner.Api.csproj");

    private readonly IPlannerApiClient _apiClient;
    private Process? _process;

    public LocalApiProcessManager(IPlannerApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<string> EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        var requestedPort = _apiClient.BaseUri.Port;
        var resolution = await ResolveStartupPortAsync(requestedPort, cancellationToken);
        if (resolution.ApiAlreadyRunning)
        {
            return FormatStartupStatus("API is already running.", requestedPort, resolution.Port);
        }

        var startInfo = CreateApiStartInfo(resolution.Port);
        if (startInfo is null)
        {
            return "Could not find bundled .NET API or source API project beside the app. Start the API manually.";
        }

        _process = Process.Start(startInfo);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken);
            if (await _apiClient.IsApiAvailableAsync(cancellationToken))
            {
                if (await IsCompatibleApiAsync(cancellationToken))
                {
                    var status = IsBundledApiExecutable(startInfo.FileName)
                        ? "Started bundled API."
                        : "Started local .NET API.";
                    return FormatStartupStatus(status, requestedPort, resolution.Port);
                }
            }
        }

        return $"Started API process, but it did not answer on port {resolution.Port}.";
    }

    private async Task<StartupPortResolution> ResolveStartupPortAsync(
        int requestedPort,
        CancellationToken cancellationToken)
    {
        for (var port = AppSettings.NormalizeApiPort(requestedPort); port <= 65535; port++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _apiClient.ConfigurePort(port);

            if (await _apiClient.IsApiAvailableAsync(cancellationToken))
            {
                if (await IsCompatibleApiAsync(cancellationToken))
                {
                    return new StartupPortResolution(port, ApiAlreadyRunning: true);
                }

                StopStaleApiOnPort(port);
            }
            else
            {
                StopStaleApiOnPort(port);
            }

            if (ListeningProcessIds(port).Count == 0)
            {
                return new StartupPortResolution(port, ApiAlreadyRunning: false);
            }
        }

        throw new InvalidOperationException(
            $"No free API port was found at or above {requestedPort.ToString(CultureInfo.InvariantCulture)}.");
    }

    public static int FindAvailablePort(int startPort)
    {
        for (var port = AppSettings.NormalizeApiPort(startPort); port <= 65535; port++)
        {
            if (ListeningProcessIds(port).Count == 0)
            {
                return port;
            }
        }

        throw new InvalidOperationException(
            $"No free TCP port was found at or above {startPort.ToString(CultureInfo.InvariantCulture)}.");
    }

    private static string FormatStartupStatus(string status, int requestedPort, int resolvedPort)
    {
        return requestedPort == resolvedPort
            ? status
            : $"API port changed from {requestedPort.ToString(CultureInfo.InvariantCulture)} to {resolvedPort.ToString(CultureInfo.InvariantCulture)} because the configured port was busy. {status}";
    }

    private readonly record struct StartupPortResolution(int Port, bool ApiAlreadyRunning);

    private static ProcessStartInfo? CreateApiStartInfo(int port)
    {
        var bundledApi = FindBundledApiExecutable();
        if (bundledApi is not null)
        {
            return new ProcessStartInfo
            {
                FileName = bundledApi,
                Arguments = $"--port {port.ToString(CultureInfo.InvariantCulture)}",
                WorkingDirectory = Path.GetDirectoryName(bundledApi)!,
                CreateNoWindow = true,
                UseShellExecute = false,
            };
        }

        var sourceApiProject = FindSourceApiProject();
        if (sourceApiProject is null)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = FindRepoRoot(Path.GetDirectoryName(sourceApiProject)) ?? Path.GetDirectoryName(sourceApiProject)!,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(sourceApiProject);
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
        return startInfo;
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
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalApiProcessManager] API compatibility check failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> SupportsLocalizationAsync(CancellationToken cancellationToken)
    {
        var previousLanguage = _apiClient.PlannerLanguage;
        try
        {
            _apiClient.PlannerLanguage = PlannerLanguages.English;
            var englishCatalog = await _apiClient.GetCatalogAsync(cancellationToken);
            if (!HasOfficialEnglishNames(englishCatalog))
            {
                return false;
            }

            _apiClient.PlannerLanguage = PlannerLanguages.Ukrainian;
            var ukrainianCatalog = await _apiClient.GetCatalogAsync(cancellationToken);
            return HasOfficialUkrainianNames(ukrainianCatalog);
        }
        finally
        {
            _apiClient.PlannerLanguage = previousLanguage;
        }
    }

    private static bool HasOfficialEnglishNames(PlannerCatalog catalog)
    {
        if (!string.Equals(catalog.Meta.Language, PlannerLanguages.English, StringComparison.Ordinal))
        {
            return false;
        }

        var powerium = catalog.Recipes.FirstOrDefault(recipe => recipe.Output.ItemId == "magic-oil-ore");
        var sulfuricAcid = catalog.Recipes.FirstOrDefault(recipe => recipe.Output.ItemId == "sulphuric-acid");
        var pressurizer = catalog.Recipes.FirstOrDefault(recipe => recipe.BuildingId == "pressurizer");
        var refinery = catalog.Recipes.FirstOrDefault(recipe => recipe.BuildingId == "refinery");
        return string.Equals(powerium?.Output.Name, "Powerium", StringComparison.Ordinal)
            && string.Equals(sulfuricAcid?.Output.Name, "Sulfuric Acid", StringComparison.Ordinal)
            && string.Equals(pressurizer?.BuildingName, "Pressurizer", StringComparison.Ordinal)
            && string.Equals(refinery?.BuildingName, "Refinery", StringComparison.Ordinal);
    }

    private static bool HasOfficialUkrainianNames(PlannerCatalog catalog)
    {
        if (!string.Equals(catalog.Meta.Language, PlannerLanguages.Ukrainian, StringComparison.Ordinal))
        {
            return false;
        }

        var powerium = catalog.Recipes.FirstOrDefault(recipe => recipe.Output.ItemId == "magic-oil-ore");
        var sulfuricAcid = catalog.Recipes.FirstOrDefault(recipe => recipe.Output.ItemId == "sulphuric-acid");
        var pressurizer = catalog.Recipes.FirstOrDefault(recipe => recipe.BuildingId == "pressurizer");
        var refinery = catalog.Recipes.FirstOrDefault(recipe => recipe.BuildingId == "refinery");
        return string.Equals(powerium?.Output.Name, "Енергіум", StringComparison.Ordinal)
            && string.Equals(sulfuricAcid?.Output.Name, "Сірчана кислота", StringComparison.Ordinal)
            && string.Equals(pressurizer?.BuildingName, "Нагнітач", StringComparison.Ordinal)
            && string.Equals(refinery?.BuildingName, "Очисник", StringComparison.Ordinal);
    }

    private static void StopStaleApiOnPort(int port)
    {
        var processIds = ListeningProcessIds(port);
        foreach (var processId in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!IsManagedApiProcess(process.ProcessName))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LocalApiProcessManager] Failed to stop stale API process {processId}: {ex.Message}");
                // Best effort only. If the stale API cannot be stopped, startup
                // will report that the replacement process never became ready.
            }
        }
    }

    private static bool IsManagedApiProcess(string processName)
    {
        return processName.Contains("python", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("StarRupturePlanner.Api", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("StarRuptureApi", StringComparison.OrdinalIgnoreCase);
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
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalApiProcessManager] Failed to inspect listeners on port {port}: {ex.Message}");
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
            if (File.Exists(Path.Combine(directory.FullName, SourceApiProjectRelativePath)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, SourceApiProjectRelativePath)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    public static string? FindSourceApiProject(string? startPath = null)
    {
        var root = FindRepoRoot(startPath);
        return root is null ? null : Path.Combine(root, SourceApiProjectRelativePath);
    }

    public static string? FindBundledApiExecutable(string? startPath = null)
    {
        var directory = new DirectoryInfo(startPath ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var executableName in BundledApiExecutableNames())
            {
                var apiPath = Path.Combine(directory.FullName, "api", executableName);
                if (File.Exists(apiPath))
                {
                    return apiPath;
                }
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsBundledApiExecutable(string fileName) =>
        BundledApiExecutableNames().Any(executableName =>
            fileName.EndsWith(executableName, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> BundledApiExecutableNames() =>
    [
        "StarRupturePlanner.Api.exe",
    ];

    public void Dispose()
    {
        StopStartedProcess();
        _process?.Dispose();
        _process = null;
    }

    public void StopStartedProcess()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalApiProcessManager] Failed to stop owned API process: {ex.Message}");
            // Best effort during app shutdown; if Windows has already reaped
            // the process or access is denied there is nothing useful to show.
        }
    }
}
