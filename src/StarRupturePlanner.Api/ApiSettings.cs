namespace StarRupturePlanner.Api;

public sealed class ApiSettings
{
    public const int DefaultPort = 8010;
    public const string SupportedGameVersion = "0.2.8";

    public string SourceSiteUrl { get; init; } = "https://starrupture.tools";
    public string BaseDir { get; init; } = DiscoverBaseDir();
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = DefaultPort;

    public string DataDir => Path.Combine(BaseDir, "data");
    public string AssetDir => Path.Combine(DataDir, "assets");
    public string ItemAssetDir => Path.Combine(AssetDir, "items");
    public string BuildingAssetDir => Path.Combine(AssetDir, "buildings");
    public string DbPath => Path.Combine(DataDir, "starrupture.sqlite3");
    public string LocalizationDir => Path.Combine(DataDir, "localization");
    public string TransportTiersPath => Path.Combine(DataDir, "transport_tiers.json");

    public static ApiSettings FromArgs(string[] args)
    {
        return new ApiSettings
        {
            Port = ParsePort(args),
        };
    }

    public static int ParsePort(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (arg.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizePort(arg["--port=".Length..]);
            }

            if (string.Equals(arg, "--port", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-p", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count)
                {
                    throw new ArgumentException("Missing value for --port.");
                }

                return NormalizePort(args[index + 1]);
            }
        }

        return DefaultPort;
    }

    private static int NormalizePort(string value)
    {
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Port must be a number from 1 to 65535.");
        }

        return port;
    }

    private static string DiscoverBaseDir()
    {
        foreach (string startPath in CandidateStartPaths())
        {
            DirectoryInfo? directory = new(startPath);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "data", "starrupture.sqlite3")))
                {
                    return directory.FullName;
                }

                if (string.Equals(directory.Name, "api", StringComparison.OrdinalIgnoreCase)
                    && directory.Parent is not null
                    && File.Exists(Path.Combine(directory.Parent.FullName, "data", "starrupture.sqlite3")))
                {
                    return directory.Parent.FullName;
                }

                directory = directory.Parent;
            }
        }

        string executableDir = AppContext.BaseDirectory;
        DirectoryInfo appDirectory = new(executableDir);
        return string.Equals(appDirectory.Name, "api", StringComparison.OrdinalIgnoreCase) && appDirectory.Parent is not null
            ? appDirectory.Parent.FullName
            : executableDir;
    }

    private static IEnumerable<string> CandidateStartPaths()
    {
        yield return AppContext.BaseDirectory;
        yield return Environment.CurrentDirectory;
    }
}
