using System.Reflection;

namespace StarRupturePlanner.Services;

public static class AppVersionInfo
{
    private const string AlphaLabel = "ALPHA";

    public const string SupportedGameVersion = "0.2.8";

    public static string InformationalVersion { get; } = ResolveInformationalVersion();

    public static string DisplayVersion =>
        InformationalVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? InformationalVersion
            : $"v{InformationalVersion}";

    public static bool IsAlpha =>
        InformationalVersion.Contains("alpha", StringComparison.OrdinalIgnoreCase);

    public static string ChannelLabel => IsAlpha ? AlphaLabel : "PREVIEW";

    public static string WindowTitle(string title) => $"{title} - {DisplayVersion}";

    public static string Subtitle(string subtitle) =>
        $"{subtitle} - StarRupture {SupportedGameVersion} - {DisplayVersion}";

    private static string ResolveInformationalVersion()
    {
        var attribute = typeof(AppVersionInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        return string.IsNullOrWhiteSpace(attribute?.InformationalVersion)
            ? "0.4.3-alpha"
            : attribute.InformationalVersion;
    }
}
