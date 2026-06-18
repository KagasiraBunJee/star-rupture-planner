using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StarRupturePlanner.Api;

public sealed class ResourceService
{
    private static readonly string[] PlaceholderResourceIds =
    [
        "empty-item",
        "placeholder-crafting-ingredient",
        "placeholder-crafting-result",
        "test-resource-item",
    ];

    private readonly ApiSettings _settings;
    private readonly LocalizationStore _localization;

    public ResourceService(ApiSettings settings, LocalizationStore localization)
    {
        _settings = settings;
        _localization = localization;
    }

    public Dictionary<string, object?> GetMeta()
    {
        using SqliteConnection connection = OpenConnection();
        Dictionary<string, object?> counts = new()
        {
            ["items"] = ScalarLong(connection, CleanResourceCountSql()),
            ["related_items"] = ScalarLong(connection, "SELECT COUNT(*) FROM items"),
            ["buildings"] = ScalarLong(connection, "SELECT COUNT(*) FROM buildings"),
            ["recipes"] = ScalarLong(connection, "SELECT COUNT(*) FROM recipes"),
        };

        return new()
        {
            ["dataset"] = "starrupture_resources",
            ["game_version"] = ApiSettings.SupportedGameVersion,
            ["source"] = "local bundled dataset",
            ["counts"] = counts,
            ["languages"] = _localization.Languages,
        };
    }

    public Dictionary<string, object?> ListItems(
        string? q = null,
        bool? produced = null,
        bool? used = null,
        int limit = 100,
        int offset = 0,
        string? lang = null)
    {
        string language = LocalizationStore.NormalizeLanguage(lang);
        List<string> clauses = [CleanResourceWhere("i")];
        if (produced is not null)
        {
            clauses.Add($"{(produced.Value ? "EXISTS" : "NOT EXISTS")} (SELECT 1 FROM recipes r WHERE r.output_item_id = i.item_id)");
        }

        if (used is not null)
        {
            clauses.Add($"{(used.Value ? "EXISTS" : "NOT EXISTS")} (SELECT 1 FROM item_usages u WHERE u.item_id = i.item_id)");
        }

        string where = string.Join(" AND ", clauses);
        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(offset, 0);

        using SqliteConnection connection = OpenConnection();
        List<Row> rows = Query(
            connection,
            $"""
            SELECT i.*,
                EXISTS(SELECT 1 FROM recipes r WHERE r.output_item_id = i.item_id) AS produced,
                EXISTS(SELECT 1 FROM item_usages u WHERE u.item_id = i.item_id) AS used,
                EXISTS(
                    SELECT 1
                    FROM recipes r
                    JOIN recipe_unlock_requirements rr ON rr.recipe_key = r.recipe_key
                    WHERE r.output_item_id = i.item_id
                ) AS requires_unlock,
                EXISTS(
                    SELECT 1
                    FROM item_unlock_usages uu
                    WHERE uu.item_id = i.item_id
                ) AS used_to_unlock
            FROM items i
            WHERE {where}
            ORDER BY i.name COLLATE NOCASE
            """);

        List<(Row Row, Dictionary<string, object?> Payload)> entries = rows
            .Select(row => (row, ItemSummary(row, language)))
            .ToList();
        if (!string.IsNullOrWhiteSpace(q))
        {
            string needle = q.Trim().ToLowerInvariant();
            entries = entries
                .Where(entry =>
                    entry.Payload.String("item_id").Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || entry.Payload.String("name").Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || entry.Row.String("name").Contains(needle, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        entries.Sort((left, right) =>
        {
            int nameComparison = string.Compare(
                left.Payload.String("name"),
                right.Payload.String("name"),
                StringComparison.OrdinalIgnoreCase);
            return nameComparison != 0
                ? nameComparison
                : string.Compare(left.Payload.String("item_id"), right.Payload.String("item_id"), StringComparison.Ordinal);
        });

        return new()
        {
            ["items"] = entries.Skip(offset).Take(limit).Select(entry => entry.Payload).ToList(),
            ["total"] = entries.Count,
            ["limit"] = limit,
            ["offset"] = offset,
        };
    }

    public Dictionary<string, object?> SearchItems(string query, int limit = 20, string? lang = null) =>
        ListItems(q: query, limit: limit, offset: 0, lang: lang);

    public Dictionary<string, object?> ListBuildings(string? lang = null)
    {
        string language = LocalizationStore.NormalizeLanguage(lang);
        using SqliteConnection connection = OpenConnection();
        List<Row> rows = Query(
            connection,
            """
            SELECT b.*,
                COUNT(r.recipe_key) AS recipe_count
            FROM buildings b
            LEFT JOIN recipes r ON r.building_id = b.building_id
            GROUP BY b.building_id
            ORDER BY b.name COLLATE NOCASE, b.building_id
            """);
        return new() { ["buildings"] = rows.Select(row => BuildingPayload(row, language)).ToList() };
    }

    public Dictionary<string, object?> GetTransportTiers(string? lang = null)
    {
        string language = LocalizationStore.NormalizeLanguage(lang);
        if (!File.Exists(_settings.TransportTiersPath))
        {
            return new()
            {
                ["tiers"] = Array.Empty<object>(),
                ["missing"] = true,
                ["message"] = "Transport tier speeds are not configured.",
            };
        }

        JsonNode? payload = JsonNode.Parse(File.ReadAllText(_settings.TransportTiersPath));
        JsonArray tiers = payload?["tiers"] as JsonArray ?? [];
        List<Dictionary<string, object?>> localizedTiers = [];
        foreach (JsonNode? tierNode in tiers)
        {
            if (tierNode is not JsonObject tier)
            {
                continue;
            }

            Dictionary<string, object?> localized = JsonObjectToDictionary(tier);
            string? tierId = localized.StringOrNull("id");
            localized["name"] = _localization.Text(language, "transport_tiers", tierId, "name", localized.GetValueOrDefault("name"));
            localizedTiers.Add(localized);
        }

        string? fallbackMessage = payload?["message"]?.GetValue<string>();
        return new()
        {
            ["tiers"] = localizedTiers,
            ["missing"] = localizedTiers.Count == 0,
            ["message"] = _localization.Ui(language, "transport.default_message", fallbackMessage),
        };
    }

    public Dictionary<string, object?> GetCorporations(string? lang = null)
    {
        string language = LocalizationStore.NormalizeLanguage(lang);
        using SqliteConnection connection = OpenConnection();
        return new() { ["corporations"] = CorporationsPayload(connection, language) };
    }

    public Dictionary<string, object?> GetCorporationDetail(string corporationId, string? lang = null)
    {
        string language = LocalizationStore.NormalizeLanguage(lang);
        using SqliteConnection connection = OpenConnection();
        foreach (Dictionary<string, object?> corporation in CorporationsPayload(connection, language))
        {
            if (string.Equals(corporation.String("corporation_id"), corporationId, StringComparison.Ordinal))
            {
                return corporation;
            }
        }

        throw new DataNotFoundException(corporationId);
    }

    public Dictionary<string, object?> GetPlannerCatalog(string? lang = null)
    {
        string language = LocalizationStore.NormalizeLanguage(lang);
        using SqliteConnection connection = OpenConnection();
        List<Row> buildingRows = Query(
            connection,
            """
            SELECT b.*,
                COUNT(r.recipe_key) AS recipe_count
            FROM buildings b
            LEFT JOIN recipes r ON r.building_id = b.building_id
            GROUP BY b.building_id
            ORDER BY b.name COLLATE NOCASE, b.building_id
            """);
        List<Row> recipeRows = Query(
            connection,
            """
            SELECT r.*, b.name AS building_name, b.category AS building_category,
                   b.family_name AS building_family_name, b.tier AS building_tier,
                   b.image_url AS building_image_url,
                   oi.name AS output_item_name, oi.image_url AS output_item_image_url
            FROM recipes r
            JOIN buildings b ON b.building_id = r.building_id
            JOIN items oi ON oi.item_id = r.output_item_id
            ORDER BY b.name COLLATE NOCASE, r.recipe_level, r.recipe_id
            """);
        List<Dictionary<string, object?>> recipes = recipeRows
            .Select(row => PlannerRecipePayload(connection, row, language))
            .ToList();

        return new()
        {
            ["buildings"] = buildingRows.Select(row => BuildingPayload(row, language)).ToList(),
            ["recipes"] = recipes,
            ["transport_tiers"] = GetTransportTiers(language),
            ["corporations"] = CorporationsPayload(connection, language),
            ["building_unlocks"] = BuildingUnlocksPayload(connection, language),
            ["meta"] = new Dictionary<string, object?>
            {
                ["building_count"] = buildingRows.Count,
                ["recipe_count"] = recipes.Count,
                ["language"] = language,
            },
        };
    }

    public Dictionary<string, object?> GetPlannerSuggestions(string direction, string itemId, string? lang = null)
    {
        string language = LocalizationStore.NormalizeLanguage(lang);
        direction = direction.Trim().ToLowerInvariant();
        if (direction is not ("input" or "output"))
        {
            return new()
            {
                ["direction"] = direction,
                ["item_id"] = itemId,
                ["suggestions"] = Array.Empty<object>(),
                ["error"] = "direction must be input or output",
            };
        }

        string where;
        string order;
        if (direction == "input")
        {
            where = "r.output_item_id = $p0";
            order = "b.name COLLATE NOCASE, r.recipe_level, r.recipe_id";
        }
        else
        {
            where = """
                EXISTS (
                    SELECT 1
                    FROM recipe_inputs ri
                    WHERE ri.recipe_key = r.recipe_key
                      AND ri.input_item_id = $p0
                )
                """;
            order = "oi.name COLLATE NOCASE, b.name COLLATE NOCASE, r.recipe_level, r.recipe_id";
        }

        using SqliteConnection connection = OpenConnection();
        List<Row> rows = Query(
            connection,
            $"""
            SELECT r.*, b.name AS building_name, b.category AS building_category,
                   b.family_name AS building_family_name, b.tier AS building_tier,
                   b.image_url AS building_image_url,
                   oi.name AS output_item_name, oi.image_url AS output_item_image_url
            FROM recipes r
            JOIN buildings b ON b.building_id = r.building_id
            JOIN items oi ON oi.item_id = r.output_item_id
            WHERE {where}
            ORDER BY {order}
            """,
            itemId);

        return new()
        {
            ["direction"] = direction,
            ["item_id"] = itemId,
            ["suggestions"] = rows.Select(row => PlannerRecipePayload(connection, row, language)).ToList(),
        };
    }

    public Dictionary<string, object?> GetItemDetail(string itemId, string? lang = null)
    {
        string language = LocalizationStore.NormalizeLanguage(lang);
        using SqliteConnection connection = OpenConnection();
        Row? item = Query(connection, "SELECT * FROM items WHERE item_id = $p0", itemId).FirstOrDefault();
        if (item is null)
        {
            throw new DataNotFoundException(itemId);
        }

        List<Dictionary<string, object?>> producedBy = ProducedBy(connection, itemId, language);
        List<Dictionary<string, object?>> usedIn = UsedIn(connection, itemId, language);
        List<Dictionary<string, object?>> unlockRequirements = UnlockRequirementsForItem(connection, itemId, language);
        List<Dictionary<string, object?>> usedToUnlock = UsedToUnlock(connection, itemId, language);

        return new()
        {
            ["item"] = ItemPayload(item, language),
            ["unlock_requirements"] = unlockRequirements,
            ["used_to_unlock"] = usedToUnlock,
            ["produced_by"] = producedBy,
            ["used_in"] = usedIn,
            ["meta"] = new Dictionary<string, object?>
            {
                ["producer_count"] = producedBy.Count,
                ["usage_count"] = usedIn.Count,
                ["unlock_recipe_count"] = unlockRequirements.Count,
                ["unlocks_recipe_count"] = usedToUnlock.Count,
            },
        };
    }

    private List<Dictionary<string, object?>> ProducedBy(SqliteConnection connection, string itemId, string language)
    {
        List<Row> rows = Query(
            connection,
            """
            SELECT r.*, b.name AS building_name, b.category AS building_category,
                   b.image_url AS building_image_url
            FROM recipes r
            JOIN buildings b ON b.building_id = r.building_id
            WHERE r.output_item_id = $p0
            ORDER BY b.name COLLATE NOCASE, r.recipe_level, r.recipe_id
            """,
            itemId);
        return rows.Select(row => ProducerPayload(connection, row, language)).ToList();
    }

    private List<Dictionary<string, object?>> UsedIn(SqliteConnection connection, string itemId, string language)
    {
        List<Row> rows = Query(
            connection,
            """
            SELECT u.*, r.recipe_id, r.recipe_level, r.duration_seconds,
                   r.output_item_id, r.output_quantity, r.output_per_minute,
                   r.original_rate_text, b.building_id, b.name AS building_name,
                   b.category AS building_category, oi.name AS output_item_name,
                   oi.image_url AS output_item_image_url
            FROM item_usages u
            JOIN recipes r ON r.recipe_key = u.recipe_key
            JOIN buildings b ON b.building_id = r.building_id
            JOIN items oi ON oi.item_id = r.output_item_id
            WHERE u.item_id = $p0
            ORDER BY b.name COLLATE NOCASE, r.recipe_level, r.recipe_id
            """,
            itemId);
        return rows.Select(row => new Dictionary<string, object?>
        {
            ["building_id"] = row["building_id"],
            ["building_name"] = BuildingText(language, row.StringOrNull("building_id"), "name", row["building_name"]),
            ["building_category"] = row["building_category"],
            ["recipe_id"] = row["recipe_id"],
            ["recipe_level"] = row["recipe_level"],
            ["consumed_quantity_per_cycle"] = CleanNumber(row["consumed_quantity_per_cycle"]),
            ["consumed_per_minute"] = CleanNumber(row["consumed_per_minute"]),
            ["output_item_id"] = row["output_item_id"],
            ["output_item_name"] = ItemText(language, row.StringOrNull("output_item_id"), "name", row["output_item_name"]),
            ["output_item_image_url"] = row["output_item_image_url"],
            ["output_quantity"] = CleanNumber(row["output_quantity"]),
            ["duration_seconds"] = CleanNumber(row["duration_seconds"]),
            ["items_per_minute"] = CleanNumber(row["output_per_minute"]),
            ["original_rate_text"] = row["original_rate_text"],
        }).ToList();
    }

    private List<Dictionary<string, object?>> UnlockRequirementsForItem(SqliteConnection connection, string itemId, string language)
    {
        List<Row> rows = Query(
            connection,
            """
            SELECT r.*, b.name AS building_name, b.category AS building_category,
                   b.image_url AS building_image_url, oi.name AS output_item_name,
                   oi.image_url AS output_item_image_url
            FROM recipes r
            JOIN recipe_unlock_requirements rr ON rr.recipe_key = r.recipe_key
            JOIN buildings b ON b.building_id = r.building_id
            JOIN items oi ON oi.item_id = r.output_item_id
            WHERE r.output_item_id = $p0
            GROUP BY r.recipe_key
            ORDER BY b.name COLLATE NOCASE, r.recipe_level, r.recipe_id
            """,
            itemId);
        return rows.Select(row => UnlockRecipePayload(connection, row, language)).ToList();
    }

    private List<Dictionary<string, object?>> UsedToUnlock(SqliteConnection connection, string itemId, string language)
    {
        List<Row> rows = Query(
            connection,
            """
            SELECT uu.*, r.recipe_id, r.recipe_level, r.duration_seconds,
                   r.output_item_id, r.output_quantity, r.output_per_minute,
                   r.original_rate_text, b.building_id, b.name AS building_name,
                   b.category AS building_category, oi.name AS output_item_name,
                   oi.image_url AS output_item_image_url
            FROM item_unlock_usages uu
            JOIN recipes r ON r.recipe_key = uu.recipe_key
            JOIN buildings b ON b.building_id = r.building_id
            JOIN items oi ON oi.item_id = r.output_item_id
            WHERE uu.item_id = $p0
            ORDER BY oi.name COLLATE NOCASE, b.name COLLATE NOCASE, r.recipe_level, r.recipe_id
            """,
            itemId);
        return rows.Select(row => new Dictionary<string, object?>
        {
            ["required_quantity"] = CleanNumber(row["required_quantity"]),
            ["building_id"] = row["building_id"],
            ["building_name"] = BuildingText(language, row.StringOrNull("building_id"), "name", row["building_name"]),
            ["building_category"] = row["building_category"],
            ["recipe_id"] = row["recipe_id"],
            ["recipe_level"] = row["recipe_level"],
            ["output_item_id"] = row["output_item_id"],
            ["output_item_name"] = ItemText(language, row.StringOrNull("output_item_id"), "name", row["output_item_name"]),
            ["output_item_image_url"] = row["output_item_image_url"],
            ["output_quantity"] = CleanNumber(row["output_quantity"]),
            ["duration_seconds"] = CleanNumber(row["duration_seconds"]),
            ["items_per_minute"] = CleanNumber(row["output_per_minute"]),
            ["original_rate_text"] = row["original_rate_text"],
        }).ToList();
    }

    private Dictionary<string, object?> ProducerPayload(SqliteConnection connection, Row row, string language)
    {
        List<Row> inputs = RecipeInputs(connection, row.String("recipe_key"));
        List<Row> unlocks = RecipeUnlocks(connection, row.String("recipe_key"));
        return new()
        {
            ["building_id"] = row["building_id"],
            ["building_name"] = BuildingText(language, row.StringOrNull("building_id"), "name", row["building_name"]),
            ["building_category"] = row["building_category"],
            ["building_image_url"] = row["building_image_url"],
            ["recipe_id"] = row["recipe_id"],
            ["recipe_level"] = row["recipe_level"],
            ["output_quantity"] = CleanNumber(row["output_quantity"]),
            ["duration_seconds"] = CleanNumber(row["duration_seconds"]),
            ["original_rate_text"] = row["original_rate_text"],
            ["items_per_minute"] = CleanNumber(row["output_per_minute"]),
            ["inputs"] = inputs.Select(entry => new Dictionary<string, object?>
            {
                ["item_id"] = entry["input_item_id"],
                ["name"] = ItemText(language, entry.StringOrNull("input_item_id"), "name", entry["name"]),
                ["image_url"] = entry["image_url"],
                ["quantity_per_cycle"] = CleanNumber(entry["input_quantity"]),
                ["quantity_per_minute"] = CleanNumber(entry["input_per_minute"]),
            }).ToList(),
            ["unlock_requirements"] = unlocks.Select(entry => new Dictionary<string, object?>
            {
                ["item_id"] = entry["item_id"],
                ["name"] = ItemText(language, entry.StringOrNull("item_id"), "name", entry["name"]),
                ["image_url"] = entry["image_url"],
                ["required_quantity"] = CleanNumber(entry["required_quantity"]),
            }).ToList(),
        };
    }

    private Dictionary<string, object?> PlannerRecipePayload(SqliteConnection connection, Row row, string language)
    {
        List<Row> inputs = RecipeInputs(connection, row.String("recipe_key"));
        List<Row> unlocks = RecipeUnlocks(connection, row.String("recipe_key"));
        return new()
        {
            ["recipe_key"] = row["recipe_key"],
            ["recipe_id"] = row["recipe_id"],
            ["recipe_level"] = row["recipe_level"],
            ["building_id"] = row["building_id"],
            ["building_name"] = BuildingText(language, row.StringOrNull("building_id"), "name", row["building_name"]),
            ["building_category"] = row["building_category"],
            ["building_family_name"] = BuildingText(language, row.StringOrNull("building_id"), "family_name", row["building_family_name"]),
            ["building_tier"] = row["building_tier"],
            ["building_image_url"] = row["building_image_url"],
            ["duration_seconds"] = CleanNumber(row["duration_seconds"]),
            ["output"] = new Dictionary<string, object?>
            {
                ["item_id"] = row["output_item_id"],
                ["name"] = ItemText(language, row.StringOrNull("output_item_id"), "name", row["output_item_name"]),
                ["image_url"] = row["output_item_image_url"],
                ["quantity_per_cycle"] = CleanNumber(row["output_quantity"]),
                ["quantity_per_minute"] = CleanNumber(row["output_per_minute"]),
            },
            ["original_rate_text"] = row["original_rate_text"],
            ["inputs"] = inputs.Select(entry => new Dictionary<string, object?>
            {
                ["item_id"] = entry["input_item_id"],
                ["name"] = ItemText(language, entry.StringOrNull("input_item_id"), "name", entry["name"]),
                ["image_url"] = entry["image_url"],
                ["quantity_per_cycle"] = CleanNumber(entry["input_quantity"]),
                ["quantity_per_minute"] = CleanNumber(entry["input_per_minute"]),
            }).ToList(),
            ["unlock_requirements"] = unlocks.Select(entry => new Dictionary<string, object?>
            {
                ["item_id"] = entry["item_id"],
                ["name"] = ItemText(language, entry.StringOrNull("item_id"), "name", entry["name"]),
                ["image_url"] = entry["image_url"],
                ["required_quantity"] = CleanNumber(entry["required_quantity"]),
            }).ToList(),
        };
    }

    private Dictionary<string, object?> UnlockRecipePayload(SqliteConnection connection, Row row, string language)
    {
        List<Row> requirements = RecipeUnlocks(connection, row.String("recipe_key"));
        return new()
        {
            ["building_id"] = row["building_id"],
            ["building_name"] = BuildingText(language, row.StringOrNull("building_id"), "name", row["building_name"]),
            ["building_category"] = row["building_category"],
            ["building_image_url"] = row["building_image_url"],
            ["recipe_id"] = row["recipe_id"],
            ["recipe_level"] = row["recipe_level"],
            ["output_item_id"] = row["output_item_id"],
            ["output_item_name"] = ItemText(language, row.StringOrNull("output_item_id"), "name", row["output_item_name"]),
            ["output_item_image_url"] = row["output_item_image_url"],
            ["output_quantity"] = CleanNumber(row["output_quantity"]),
            ["duration_seconds"] = CleanNumber(row["duration_seconds"]),
            ["items_per_minute"] = CleanNumber(row["output_per_minute"]),
            ["original_rate_text"] = row["original_rate_text"],
            ["required_items"] = requirements.Select(entry => new Dictionary<string, object?>
            {
                ["item_id"] = entry["item_id"],
                ["name"] = ItemText(language, entry.StringOrNull("item_id"), "name", entry["name"]),
                ["image_url"] = entry["image_url"],
                ["required_quantity"] = CleanNumber(entry["required_quantity"]),
            }).ToList(),
        };
    }

    private Dictionary<string, object?> ItemPayload(Row row, string language) => new()
    {
        ["item_id"] = row["item_id"],
        ["name"] = ItemText(language, row.StringOrNull("item_id"), "name", row["name"]),
        ["description"] = ItemText(language, row.StringOrNull("item_id"), "description", row["description"]),
        ["category"] = row["category"],
        ["stack"] = row["stack"],
        ["level"] = row["level"],
        ["source_url"] = row["source_url"],
        ["image_url"] = row["image_url"],
        ["image_source_url"] = row["image_source_url"],
    };

    private Dictionary<string, object?> ItemSummary(Row row, string language)
    {
        Dictionary<string, object?> payload = ItemPayload(row, language);
        payload["produced"] = row.Bool("produced");
        payload["used"] = row.Bool("used");
        payload["requires_unlock"] = row.Bool("requires_unlock");
        payload["used_to_unlock"] = row.Bool("used_to_unlock");
        return payload;
    }

    private Dictionary<string, object?> BuildingPayload(Row row, string language) => new()
    {
        ["building_id"] = row["building_id"],
        ["name"] = BuildingText(language, row.StringOrNull("building_id"), "name", row["name"]),
        ["family_name"] = BuildingText(language, row.StringOrNull("building_id"), "family_name", row["family_name"]),
        ["tier"] = row["tier"],
        ["category"] = row["category"],
        ["description"] = BuildingText(language, row.StringOrNull("building_id"), "description", row["description"]),
        ["power"] = row["power"] is null ? null : CleanNumber(row["power"]),
        ["temperature"] = row["temperature"] is null ? null : CleanNumber(row["temperature"]),
        ["source_url"] = row["source_url"],
        ["image_url"] = row["image_url"],
        ["image_source_url"] = row["image_source_url"],
        ["recipe_count"] = row["recipe_count"],
    };

    private List<Dictionary<string, object?>> CorporationsPayload(SqliteConnection connection, string language)
    {
        List<Row> rows = Query(
            connection,
            """
            SELECT *
            FROM corporations
            ORDER BY name COLLATE NOCASE, corporation_id
            """);
        List<Dictionary<string, object?>> payload = [];
        foreach (Row row in rows)
        {
            List<Row> levels = Query(
                connection,
                """
                SELECT *
                FROM corporation_levels
                WHERE corporation_id = $p0
                ORDER BY level
                """,
                row["corporation_id"]);
            payload.Add(new()
            {
                ["corporation_id"] = row["corporation_id"],
                ["name"] = CorporationText(language, row.StringOrNull("corporation_id"), "name", row["name"]),
                ["description"] = CorporationText(language, row.StringOrNull("corporation_id"), "description", row["description"]),
                ["source_url"] = row["source_url"],
                ["icon_url"] = SiteAssetUrl(row.StringOrNull("icon_url")),
                ["colour"] = row["colour"],
                ["max_level"] = row["max_level"],
                ["levels"] = levels.Select(level => new Dictionary<string, object?>
                {
                    ["level"] = level["level"],
                    ["reputation"] = level["reputation"],
                    ["building_rewards"] = CorporationBuildingRewards(connection, row.String("corporation_id"), Convert.ToInt32(level["level"], CultureInfo.InvariantCulture), language),
                    ["item_rewards"] = CorporationItemRewards(connection, row.String("corporation_id"), Convert.ToInt32(level["level"], CultureInfo.InvariantCulture), language),
                }).ToList(),
            });
        }

        return payload;
    }

    private List<Dictionary<string, object?>> CorporationBuildingRewards(SqliteConnection connection, string corporationId, int level, string language)
    {
        List<Row> rows = Query(
            connection,
            """
            SELECT *
            FROM corporation_building_rewards
            WHERE corporation_id = $p0 AND level = $p1
            ORDER BY name COLLATE NOCASE, building_id
            """,
            corporationId,
            level);
        return rows.Select(row => new Dictionary<string, object?>
        {
            ["building_id"] = row["building_id"],
            ["name"] = BuildingText(language, row.StringOrNull("building_id"), "name", row["name"]),
            ["category"] = row["category"],
            ["icon_url"] = SiteAssetUrl(row.StringOrNull("icon_url")),
        }).ToList();
    }

    private List<Dictionary<string, object?>> CorporationItemRewards(SqliteConnection connection, string corporationId, int level, string language)
    {
        List<Row> rows = Query(
            connection,
            """
            SELECT *
            FROM corporation_item_rewards
            WHERE corporation_id = $p0 AND level = $p1
            ORDER BY name COLLATE NOCASE, item_id
            """,
            corporationId,
            level);
        return rows.Select(row => new Dictionary<string, object?>
        {
            ["item_id"] = row["item_id"],
            ["name"] = ItemText(language, row.StringOrNull("item_id"), "name", row["name"]),
            ["category"] = row["category"],
            ["icon_url"] = SiteAssetUrl(row.StringOrNull("icon_url")),
        }).ToList();
    }

    private Dictionary<string, List<Dictionary<string, object?>>> BuildingUnlocksPayload(SqliteConnection connection, string language)
    {
        List<Row> rows = Query(
            connection,
            """
            SELECT cbr.building_id, cbr.corporation_id, c.name AS corporation_name,
                   MIN(cbr.level) AS level
            FROM corporation_building_rewards cbr
            JOIN corporations c ON c.corporation_id = cbr.corporation_id
            GROUP BY cbr.building_id, cbr.corporation_id, c.name
            ORDER BY cbr.building_id, level, c.name COLLATE NOCASE
            """);
        Dictionary<string, List<Dictionary<string, object?>>> result = [];
        foreach (Row row in rows)
        {
            string buildingId = row.String("building_id");
            if (!result.TryGetValue(buildingId, out List<Dictionary<string, object?>>? unlocks))
            {
                unlocks = [];
                result[buildingId] = unlocks;
            }

            unlocks.Add(new()
            {
                ["corporation_id"] = row["corporation_id"],
                ["corporation_name"] = CorporationText(language, row.StringOrNull("corporation_id"), "name", row["corporation_name"]),
                ["level"] = row["level"],
            });
        }

        return result;
    }

    private List<Row> RecipeInputs(SqliteConnection connection, string recipeKey) => Query(
        connection,
        """
        SELECT ri.*, i.name, i.image_url
        FROM recipe_inputs ri
        LEFT JOIN items i ON i.item_id = ri.input_item_id
        WHERE ri.recipe_key = $p0
        ORDER BY i.name COLLATE NOCASE, ri.input_item_id
        """,
        recipeKey);

    private List<Row> RecipeUnlocks(SqliteConnection connection, string recipeKey) => Query(
        connection,
        """
        SELECT rr.*, i.name, i.image_url
        FROM recipe_unlock_requirements rr
        LEFT JOIN items i ON i.item_id = rr.item_id
        WHERE rr.recipe_key = $p0
        ORDER BY i.name COLLATE NOCASE, rr.item_id
        """,
        recipeKey);

    private object? ItemText(string language, string? itemId, string field, object? fallback) =>
        _localization.Text(language, "items", itemId, field, fallback);

    private object? BuildingText(string language, string? buildingId, string field, object? fallback) =>
        _localization.Text(language, "buildings", buildingId, field, fallback);

    private object? CorporationText(string language, string? corporationId, string field, object? fallback) =>
        _localization.Text(language, "corporations", corporationId, field, fallback);

    private string? SiteAssetUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? value
            : _settings.SourceSiteUrl + value;
    }

    private static string CleanResourceWhere(string alias)
    {
        string excluded = string.Join(", ", PlaceholderResourceIds.Order().Select(itemId => $"'{itemId}'"));
        return $"{alias}.category = 'resource' AND {alias}.item_id NOT IN ({excluded})";
    }

    private static string CleanResourceCountSql() => $"SELECT COUNT(*) FROM items WHERE {CleanResourceWhere("items")}";

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new($"Data Source={_settings.DbPath};Mode=ReadOnly");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON";
        command.ExecuteNonQuery();
        return connection;
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? result = command.ExecuteScalar();
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static List<Row> Query(SqliteConnection connection, string sql, params object?[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        for (int i = 0; i < parameters.Length; i++)
        {
            command.Parameters.AddWithValue("$p" + i.ToString(CultureInfo.InvariantCulture), parameters[i] ?? DBNull.Value);
        }

        using SqliteDataReader reader = command.ExecuteReader();
        List<Row> rows = [];
        while (reader.Read())
        {
            Dictionary<string, object?> values = new(StringComparer.Ordinal);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                object value = reader.GetValue(i);
                values[reader.GetName(i)] = value is DBNull ? null : value;
            }

            rows.Add(new Row(values));
        }

        return rows;
    }

    private static object? CleanNumber(object? value)
    {
        if (value is null or bool)
        {
            return value;
        }

        double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (double.IsFinite(number) && Math.Abs(number - Math.Round(number)) < 0.0000001)
        {
            return Convert.ToInt64(Math.Round(number), CultureInfo.InvariantCulture);
        }

        return Math.Round(number, 6);
    }

    private static Dictionary<string, object?> JsonObjectToDictionary(JsonObject source)
    {
        Dictionary<string, object?> result = [];
        foreach (KeyValuePair<string, JsonNode?> pair in source)
        {
            result[pair.Key] = JsonNodeToObject(pair.Value);
        }

        return result;
    }

    private static object? JsonNodeToObject(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonArray array)
        {
            return array.Select(JsonNodeToObject).ToList();
        }

        if (node is JsonObject obj)
        {
            return JsonObjectToDictionary(obj);
        }

        JsonValue value = (JsonValue)node;
        return value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.Number when value.TryGetValue(out long number) => number,
            JsonValueKind.Number when value.TryGetValue(out double number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private sealed record Row(IReadOnlyDictionary<string, object?> Values)
    {
        public object? this[string key] => Values.TryGetValue(key, out object? value) ? value : null;

        public string String(string key) => Convert.ToString(this[key], CultureInfo.InvariantCulture) ?? "";

        public string? StringOrNull(string key) => this[key] is null ? null : String(key);

        public bool Bool(string key) => this[key] switch
        {
            bool value => value,
            long value => value != 0,
            int value => value != 0,
            double value => Math.Abs(value) > double.Epsilon,
            string value => value is "1" or "true" or "True",
            _ => false,
        };
    }
}

internal static class ResourcePayloadExtensions
{
    public static string String(this IReadOnlyDictionary<string, object?> payload, string key) =>
        Convert.ToString(payload.GetValueOrDefault(key), CultureInfo.InvariantCulture) ?? "";

    public static string? StringOrNull(this IReadOnlyDictionary<string, object?> payload, string key) =>
        payload.GetValueOrDefault(key) is null ? null : payload.String(key);
}
