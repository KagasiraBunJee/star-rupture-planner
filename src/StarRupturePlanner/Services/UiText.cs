using System.Globalization;
using System.Windows;

namespace StarRupturePlanner.Services;

public static class UiText
{
    private static readonly Dictionary<string, string> Fallback = new()
    {
        ["Text.InvalidConnection"] = "Invalid connection",
        ["Text.InvalidInput"] = "Invalid input",
        ["Text.TransportTierMissing"] = "transport tier missing",
        ["Text.MeetsDemand"] = "meets demand",
        ["Text.RailOverCapacity"] = "rail over capacity",
        ["Text.Short"] = "short",
        ["Text.Item"] = "Item",
        ["Text.From"] = "From",
        ["Text.Throughput"] = "Throughput",
        ["Text.Transport"] = "Transport",
        ["Text.Ok"] = "OK",
        ["Text.OverCapacity"] = "over capacity",
        ["Text.NoRailTierAvailable"] = "no rail tier available",
        ["Text.ExceedsAvailableRails"] = "exceeds available rails",
        ["Text.Max"] = "max",
        ["Text.NoRecipeSelected"] = "No recipe selected. Ports are disabled.",
        ["Text.Building"] = "Building",
        ["Text.Machines"] = "Machines",
        ["Text.Output"] = "Output",
        ["Text.RecipeBase"] = "Recipe base",
        ["Text.Priority"] = "Priority",
        ["Text.None"] = "None",
        ["Text.Available"] = "Available",
        ["Text.Level"] = "level",
        ["Text.LockedByCorporationLevels"] = "is locked by corporation levels",
        ["Text.LockedRequires"] = "locked: requires",
        ["Text.NoProductionShortages"] = "No production shortages",
        ["Text.Issues"] = "ISSUES",
        ["Text.NotAvailableForConnection"] = "is not available for connection with current corporation levels.",
        ["Text.Temp"] = "temp",
        ["Status.StartingApi"] = "Starting local API...",
        ["Status.LoadedRecipes"] = "Loaded {0} recipes.",
        ["Status.ApiStartupFailed"] = "API startup failed: {0}",
        ["Status.NewEmptyScheme"] = "New empty scheme.",
        ["Status.Opened"] = "Opened {0}.",
        ["Status.CouldNotOpenScheme"] = "Could not open scheme: {0}",
        ["Status.Saved"] = "Saved {0}.",
        ["Status.SettingsSaved"] = "Settings saved.",
        ["Status.ReloadingLanguage"] = "Reloading planner language...",
        ["Status.CouldNotReloadLanguage"] = "Could not reload language: {0}",
        ["Status.NotSavedYet"] = "Not saved yet",
        ["Status.LastSaved"] = "Last saved: {0}, {1}",
        ["Status.Today"] = "Today",
        ["Status.Yesterday"] = "Yesterday",
    };

    public static string T(string key)
    {
        var resourceKey = "Ui." + key;
        if (Application.Current?.TryFindResource(resourceKey) is string value && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return Fallback.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, T(key), args);
    }
}
