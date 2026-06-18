using ModelContextProtocol.Server;
using System.ComponentModel;

namespace StarRupturePlanner.Api;

[McpServerToolType]
public static class McpTools
{
    [McpServerTool(Name = "search_items")]
    [Description("Search kept StarRupture resource items.")]
    public static Dictionary<string, object?> SearchItems(
        ResourceService service,
        [Description("Text to search in item ids and localized item names.")] string query,
        [Description("Maximum number of matching items to return.")] int limit = 20,
        [Description("Language code: en, ru, de, or uk.")] string language = "en") =>
        service.SearchItems(query, limit, language);

    [McpServerTool(Name = "get_item_detail")]
    [Description("Return the full production and usage preview for a resource item.")]
    public static Dictionary<string, object?> GetItemDetail(
        ResourceService service,
        [Description("Item id, such as rotor or titanium-rod.")] string item_id,
        [Description("Language code: en, ru, de, or uk.")] string language = "en")
    {
        try
        {
            return service.GetItemDetail(item_id, language);
        }
        catch (DataNotFoundException)
        {
            return new() { ["error"] = "item_not_found", ["item_id"] = item_id };
        }
    }

    [McpServerTool(Name = "get_dataset_meta")]
    [Description("Return local dataset counts and supported languages.")]
    public static Dictionary<string, object?> GetDatasetMeta(ResourceService service) => service.GetMeta();

    [McpServerTool(Name = "list_corporations")]
    [Description("Return StarRupture corporation levels and unlock rewards.")]
    public static Dictionary<string, object?> ListCorporations(
        ResourceService service,
        [Description("Language code: en, ru, de, or uk.")] string language = "en") =>
        service.GetCorporations(language);

    [McpServerTool(Name = "get_corporation_detail")]
    [Description("Return one StarRupture corporation with all level rewards.")]
    public static Dictionary<string, object?> GetCorporationDetail(
        ResourceService service,
        [Description("Corporation id.")] string corporation_id,
        [Description("Language code: en, ru, de, or uk.")] string language = "en")
    {
        try
        {
            return service.GetCorporationDetail(corporation_id, language);
        }
        catch (DataNotFoundException)
        {
            return new() { ["error"] = "corporation_not_found", ["corporation_id"] = corporation_id };
        }
    }
}
