using StarRupturePlanner.Models;
using StarRupturePlanner.Services;
using StarRupturePlanner.ViewModels;
using StarRupturePlanner.Api;
using DotNetResourceService = StarRupturePlanner.Api.ResourceService;

var tests = new (string Name, Action Body)[]
{
    ("Default target equals recipe output", DefaultTargetEqualsRecipeOutput),
    ("Machine count and input rates scale with target", MachineCountAndInputsScale),
    ("Machine count scales output and inputs", MachineCountScalesOutputAndInputs),
    ("Legacy target output migrates to machine count", LegacyTargetOutputMigratesToMachineCount),
    ("Output flags serialize and migrate", OutputFlagsSerializeAndMigrate),
    ("Output-only node produces without input demand", OutputOnlyNodeProducesWithoutInputDemand),
    ("Output-only node keeps metric totals", OutputOnlyNodeKeepsMetricTotals),
    ("Output-only target invalidates incoming edge", OutputOnlyTargetInvalidatesIncomingEdge),
    ("Scheme output summary reports scaled output", SchemeOutputSummaryReportsScaledOutput),
    ("Scheme list reads saved output references", SchemeListReadsSavedOutputReferences),
    ("Toolbox enriches scheme output cards", ToolboxEnrichesSchemeOutputCards),
    ("High priority consumer is satisfied first", HighPriorityConsumerIsSatisfiedFirst),
    ("Same priority redistributes capped demand", SamePriorityRedistributesCappedDemand),
    ("Shortage marks edge and creates alert", ShortageMarksEdgeAndCreatesAlert),
    ("Existing producer suggestions use free connected capacity", ExistingProducerSuggestionsUseFreeConnectedCapacity),
    ("Existing consumer suggestions show consumed and produced rates", ExistingConsumerSuggestionsShowConsumedAndProducedRates),
    ("Suggestion input rates format consumed resources", SuggestionInputRatesFormatConsumedResources),
    ("Existing suggestions skip duplicate and self candidates", ExistingSuggestionsSkipDuplicateAndSelfCandidates),
    ("Connection compatibility requires matching item", ConnectionCompatibilityRequiresMatchingItem),
    ("Transport tier recommendation chooses smallest sufficient tier", TransportTierRecommendation),
    ("Canvas layout snaps to grid", CanvasLayoutSnapsToGrid),
    ("Scheme serialization round trips", SchemeSerializationRoundTrips),
    ("Scheme store deletes saved schemes", SchemeStoreDeletesSavedSchemes),
    ("Scheme store imports scheme JSON into active folder", SchemeStoreImportsSchemeJsonIntoActiveFolder),
    ("Scheme store removes deleted blueprint references", SchemeStoreRemovesDeletedBlueprintReferences),
    ("Scheme store rejects delete outside folder", SchemeStoreRejectsDeleteOutsideFolder),
    ("Machine node can persist without selected recipe", MachineNodeCanPersistWithoutRecipe),
    ("Connection route points persist", ConnectionRoutePointsPersist),
    ("Comment rectangles persist", CommentRectanglesPersist),
    ("App settings serialization round trips", AppSettingsSerializationRoundTrips),
    ("View model applies configured scheme folder", ViewModelAppliesConfiguredSchemeFolder),
    ("View model refreshes localized saved state", ViewModelRefreshesLocalizedSavedState),
    ("Async command tracks running state", AsyncCommandTracksRunningState),
    ("Canvas view model creates compatible edge", CanvasViewModelCreatesCompatibleEdge),
    ("Canvas view model creates scheme output source nodes", CanvasViewModelCreatesSchemeOutputSourceNodes),
    ("Blueprint source output feeds consumers", BlueprintSourceOutputFeedsConsumers),
    ("Canvas geometry routes through divider points", CanvasGeometryRoutesThroughDividerPoints),
    ("Edge label includes transport tier", EdgeLabelIncludesTransportTier),
    ("Scheme serialization round trips corporation settings", SchemeSerializationRoundTripsCorporationSettings),
    ("Corporation defaults unlock training rail availability", CorporationDefaultsUnlockTrainingRailAvailability),
    ("Locked building is hidden until corporation level allows it", LockedBuildingRequiresCorporationLevel),
    ("Rail recommendation uses available tiers", RailRecommendationUsesAvailableTiers),
    ("Building metrics deserialize", BuildingMetricsDeserialize),
    ("Catalog metadata deserializes language", CatalogMetadataDeserializesLanguage),
    ("Scheme metrics scale by machine count", SchemeMetricsScaleByMachineCount),
    ("Temperature counts for placed machine without recipe", TemperatureCountsForPlacedMachineWithoutRecipe),
    ("Missing building metrics are ignored", MissingBuildingMetricsAreIgnored),
    ("Source API project discovery finds .NET project", SourceApiProjectDiscoveryFindsDotNetProject),
    ("Bundled API discovery finds .NET executable", BundledApiDiscoveryFindsDotNetExecutable),
    ("Bundled API discovery ignores legacy Python executable", BundledApiDiscoveryIgnoresLegacyPythonExecutable),
    ("API port discovery increments past busy port", ApiPortDiscoveryIncrementsPastBusyPort),
    ("DotNet API settings parse port override", DotNetApiSettingsParsePortOverride),
    ("Planner API client configures port", PlannerApiClientConfiguresPort),
    ("DotNet API HTTP endpoints and MCP are open", DotNetApiHttpEndpointsAndMcpAreOpen),
    ("DotNet API meta matches dataset", DotNetApiMetaMatchesDataset),
    ("DotNet API planner catalog contains graph ready recipes", DotNetApiPlannerCatalogContainsGraphReadyRecipes),
    ("DotNet API localized search matches Ukrainian name", DotNetApiLocalizedSearchMatchesUkrainianName),
    ("App version info marks alpha build", AppVersionInfoMarksAlphaBuild),
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static RecipeInfo TitaniumRodRecipe()
{
    return new RecipeInfo
    {
        RecipeKey = "crafter:titanium-rod",
        BuildingId = "crafter",
        BuildingName = "Fabricator",
        Output = new RecipePortInfo
        {
            ItemId = "titanium-rod",
            Name = "Titanium Rod",
            QuantityPerMinute = 30,
        },
        Inputs =
        [
            new RecipePortInfo
            {
                ItemId = "titanium-bar",
                Name = "Titanium Bar",
                QuantityPerMinute = 30,
            },
        ],
    };
}

static void AppVersionInfoMarksAlphaBuild()
{
    AssertTrue(AppVersionInfo.DisplayVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase));
    AssertTrue(AppVersionInfo.IsAlpha);
    AssertEqual("ALPHA", AppVersionInfo.ChannelLabel);
    AssertEqual("0.2.8", AppVersionInfo.SupportedGameVersion);
}

static RecipeInfo SourceRecipe(double outputPerMinute)
{
    return new RecipeInfo
    {
        RecipeKey = "source:part",
        BuildingId = "source",
        BuildingName = "Source",
        Output = new RecipePortInfo
        {
            ItemId = "part",
            Name = "Part",
            QuantityPerMinute = outputPerMinute,
        },
    };
}

static RecipeInfo ConsumerRecipe(string key, string outputName, double requiredPerMinute)
{
    return new RecipeInfo
    {
        RecipeKey = key,
        BuildingId = key,
        BuildingName = outputName,
        Output = new RecipePortInfo
        {
            ItemId = key,
            Name = outputName,
            QuantityPerMinute = 1,
        },
        Inputs =
        [
            new RecipePortInfo
            {
                ItemId = "part",
                Name = "Part",
                QuantityPerMinute = requiredPerMinute,
            },
        ],
    };
}

static SchemeDocument PriorityScheme(RecipeInfo sourceRecipe, RecipeInfo lowDemandRecipe, RecipeInfo highDemandRecipe)
{
    return new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode { Id = "source", SelectedRecipeKey = sourceRecipe.RecipeKey, MachineCount = 1 },
            new SchemeNode { Id = "low", SelectedRecipeKey = lowDemandRecipe.RecipeKey, MachineCount = 1 },
            new SchemeNode { Id = "high", SelectedRecipeKey = highDemandRecipe.RecipeKey, MachineCount = 1 },
        ],
        Edges =
        [
            new SchemeEdge
            {
                Id = "edge-low",
                SourceNodeId = "source",
                SourceItemId = "part",
                TargetNodeId = "low",
                TargetItemId = "part",
            },
            new SchemeEdge
            {
                Id = "edge-high",
                SourceNodeId = "source",
                SourceItemId = "part",
                TargetNodeId = "high",
                TargetItemId = "part",
            },
        ],
    };
}

static void DefaultTargetEqualsRecipeOutput()
{
    IPlannerCalculator calculator = new PlannerCalculator();
    AssertEqual(30, calculator.DefaultTargetOutput(TitaniumRodRecipe()));
}

static void MachineCountAndInputsScale()
{
    var recipe = TitaniumRodRecipe();
    IPlannerCalculator calculator = new PlannerCalculator();
    AssertEqual(2, calculator.MachineCount(recipe, 60));
    AssertEqual(60, calculator.RequiredInputPerMinute(recipe, recipe.Inputs[0], 60d));
}

static void MachineCountScalesOutputAndInputs()
{
    var recipe = TitaniumRodRecipe();
    IPlannerCalculator calculator = new PlannerCalculator();
    AssertEqual(90, calculator.OutputPerMinute(recipe, 3));
    AssertEqual(90, calculator.RequiredInputPerMinute(recipe, recipe.Inputs[0], 3));
}

static void LegacyTargetOutputMigratesToMachineCount()
{
    var recipe = TitaniumRodRecipe();
    var scheme = new SchemeDocument
    {
        Version = 1,
        Nodes =
        [
            new SchemeNode
            {
                Id = "source",
                SelectedRecipeKey = recipe.RecipeKey,
                TargetOutputPerMinute = 61,
            },
        ],
    };

    SchemeMigrationService.Migrate(
        scheme,
        new PlannerCatalog { Recipes = [recipe] },
        new PlannerCalculator());

    AssertEqual(4, scheme.Version);
    AssertEqual(3, scheme.Nodes[0].MachineCount);
    AssertEqual(0d, scheme.Nodes[0].TargetOutputPerMinute);
    AssertFalse(scheme.Nodes[0].OnlyOutput);
    AssertFalse(scheme.Nodes[0].IsSchemeOutput);
}

static void OutputFlagsSerializeAndMigrate()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-tests-" + Guid.NewGuid().ToString("N"));
    ISchemeStore store = new SchemeStore(temp);
    var document = new SchemeDocument
    {
        Name = "Output Flags",
        Nodes =
        [
            new SchemeNode
            {
                Id = "node-a",
                BuildingId = "crafter",
                SelectedRecipeKey = "crafter:titanium-rod",
                OnlyOutput = true,
                IsSchemeOutput = true,
            },
        ],
    };

    var path = store.Save(document);
    var loaded = store.Load(path);
    AssertTrue(loaded.Nodes[0].OnlyOutput);
    AssertTrue(loaded.Nodes[0].IsSchemeOutput);
    Directory.Delete(temp, recursive: true);
}

static void OutputOnlyNodeProducesWithoutInputDemand()
{
    var sourceRecipe = SourceRecipe(12);
    var consumerRecipe = ConsumerRecipe("consumer", "Consumer", 10);
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode { Id = "source", SelectedRecipeKey = sourceRecipe.RecipeKey, MachineCount = 1, OnlyOutput = true },
            new SchemeNode { Id = "consumer", SelectedRecipeKey = consumerRecipe.RecipeKey, MachineCount = 1 },
        ],
        Edges =
        [
            new SchemeEdge
            {
                Id = "edge-a",
                SourceNodeId = "source",
                SourceItemId = "part",
                TargetNodeId = "consumer",
                TargetItemId = "part",
            },
        ],
    };

    var analysis = ProductionAnalysisService.Analyze(
        scheme,
        new PlannerCatalog { Recipes = [sourceRecipe, consumerRecipe] },
        new PlannerCalculator());

    AssertEqual(12d, analysis.NodeRates["source"].OutputPerMinute);
    AssertFalse(analysis.Inputs.ContainsKey(ProductionInputKey.For("source", "part")));
    AssertEqual(10d, analysis.Inputs[ProductionInputKey.For("consumer", "part")].DeliveredPerMinute);
    AssertEqual(0, analysis.Alerts.Count);
}

static void OutputOnlyNodeKeepsMetricTotals()
{
    var recipe = TitaniumRodRecipe();
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode
            {
                Id = "fabricator",
                BuildingId = "crafter",
                SelectedRecipeKey = recipe.RecipeKey,
                MachineCount = 2,
                OnlyOutput = true,
            },
        ],
    };
    var catalog = new PlannerCatalog
    {
        Recipes = [recipe],
        Buildings =
        [
            new BuildingInfo { BuildingId = "crafter", Power = -10, Temperature = 5 },
        ],
    };

    var totals = PlannerMetricService.CalculateTotals(scheme, catalog);
    AssertEqual(20d, totals.PowerConsumption);
    AssertEqual(10d, totals.Temperature);
}

static void OutputOnlyTargetInvalidatesIncomingEdge()
{
    var sourceRecipe = SourceRecipe(12);
    var consumerRecipe = ConsumerRecipe("consumer", "Consumer", 10);
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode { Id = "source", SelectedRecipeKey = sourceRecipe.RecipeKey, MachineCount = 1 },
            new SchemeNode { Id = "consumer", SelectedRecipeKey = consumerRecipe.RecipeKey, MachineCount = 1, OnlyOutput = true },
        ],
        Edges =
        [
            new SchemeEdge
            {
                Id = "edge-a",
                SourceNodeId = "source",
                SourceItemId = "part",
                TargetNodeId = "consumer",
                TargetItemId = "part",
            },
        ],
    };

    AssertFalse(PlannerEdgeService.IsEdgeValid(
        scheme,
        new PlannerCatalog { Recipes = [sourceRecipe, consumerRecipe] },
        new PlannerCalculator(),
        scheme.Edges[0]));
}

static void SchemeOutputSummaryReportsScaledOutput()
{
    var recipe = TitaniumRodRecipe();
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode
            {
                Id = "fabricator",
                SelectedRecipeKey = recipe.RecipeKey,
                MachineCount = 3,
                IsSchemeOutput = true,
            },
        ],
    };

    var outputs = PlannerMetricService.SchemeOutputs(
        scheme,
        new PlannerCatalog { Recipes = [recipe] },
        new PlannerCalculator());

    AssertEqual(1, outputs.Count);
    AssertEqual("Fabricator", outputs[0].MachineName);
    AssertEqual("Titanium Rod", outputs[0].ItemName);
    AssertEqual(90d, outputs[0].RatePerMinute);
}

static void SchemeListReadsSavedOutputReferences()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-tests-" + Guid.NewGuid().ToString("N"));
    ISchemeStore store = new SchemeStore(temp);
    var document = new SchemeDocument
    {
        Name = "Saved Outputs",
        Nodes =
        [
            new SchemeNode
            {
                Id = "fabricator",
                SelectedRecipeKey = "crafter:titanium-rod",
                MachineCount = 4,
                IsSchemeOutput = true,
            },
            new SchemeNode
            {
                Id = "internal",
                SelectedRecipeKey = "crafter:titanium-plate",
                MachineCount = 2,
                IsSchemeOutput = false,
            },
        ],
    };

    store.Save(document);
    var listed = store.ListSchemes().Single();
    AssertEqual(1, listed.Outputs.Count);
    AssertEqual("crafter:titanium-rod", listed.Outputs[0].RecipeKey);
    AssertEqual(4, listed.Outputs[0].MachineCount);
    Directory.Delete(temp, recursive: true);
}

static void ToolboxEnrichesSchemeOutputCards()
{
    var recipe = TitaniumRodRecipe();
    recipe.Output.ImageUrl = "/assets/items/titanium-rod.png";
    var toolbox = new ToolboxViewModel(
        new TestPlannerApiClient(),
        new ImmediateUiDispatcher(),
        new ImmediateBackgroundTaskRunner());

    toolbox.SetSchemesAsync(
        [
            new SchemeListItem
            {
                Name = "Rotor Line",
                FilePath = "rotor-line.json",
                Outputs =
                [
                    new SchemeListOutputItem
                    {
                        RecipeKey = recipe.RecipeKey,
                        MachineCount = 2,
                    },
                ],
            },
        ]).GetAwaiter().GetResult();

    toolbox.SetCatalogAsync(new PlannerCatalog { Recipes = [recipe] }).GetAwaiter().GetResult();

    AssertEqual(1, toolbox.Schemes.Count);
    AssertEqual("Titanium Rod", toolbox.Schemes[0].Outputs[0].ItemName);
    AssertEqual("http://localhost/assets/items/titanium-rod.png", toolbox.Schemes[0].Outputs[0].ImageUrl);
    AssertEqual(60d, toolbox.Schemes[0].Outputs[0].RatePerMinute);
}

static void HighPriorityConsumerIsSatisfiedFirst()
{
    var sourceRecipe = SourceRecipe(12);
    var lowDemandRecipe = ConsumerRecipe("consumer-low", "Widget", 4);
    var highDemandRecipe = ConsumerRecipe("consumer-high", "Gadget", 20);
    var scheme = PriorityScheme(sourceRecipe, lowDemandRecipe, highDemandRecipe);
    scheme.Nodes.First(node => node.Id == "low").Priority = ProductionPriority.High;
    scheme.Nodes.First(node => node.Id == "high").Priority = ProductionPriority.Mid;

    var analysis = ProductionAnalysisService.Analyze(
        scheme,
        new PlannerCatalog { Recipes = [sourceRecipe, lowDemandRecipe, highDemandRecipe] },
        new PlannerCalculator());

    AssertEqual(4d, analysis.Inputs[ProductionInputKey.For("low", "part")].DeliveredPerMinute);
    AssertEqual(8d, analysis.Inputs[ProductionInputKey.For("high", "part")].DeliveredPerMinute);
    AssertEqual(1, analysis.Alerts.Count);
}

static void SamePriorityRedistributesCappedDemand()
{
    var sourceRecipe = SourceRecipe(12);
    var lowDemandRecipe = ConsumerRecipe("consumer-low", "Widget", 4);
    var highDemandRecipe = ConsumerRecipe("consumer-high", "Gadget", 20);
    var scheme = PriorityScheme(sourceRecipe, lowDemandRecipe, highDemandRecipe);

    var analysis = ProductionAnalysisService.Analyze(
        scheme,
        new PlannerCatalog { Recipes = [sourceRecipe, lowDemandRecipe, highDemandRecipe] },
        new PlannerCalculator());

    AssertEqual(4d, analysis.Inputs[ProductionInputKey.For("low", "part")].DeliveredPerMinute);
    AssertEqual(8d, analysis.Inputs[ProductionInputKey.For("high", "part")].DeliveredPerMinute);
}

static void ShortageMarksEdgeAndCreatesAlert()
{
    var sourceRecipe = SourceRecipe(12);
    var targetRecipe = ConsumerRecipe("consumer-high", "Gadget", 20);
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode { Id = "source", SelectedRecipeKey = sourceRecipe.RecipeKey, MachineCount = 1 },
            new SchemeNode { Id = "target", SelectedRecipeKey = targetRecipe.RecipeKey, MachineCount = 1 },
        ],
        Edges =
        [
            new SchemeEdge
            {
                Id = "edge-short",
                SourceNodeId = "source",
                SourceItemId = "part",
                TargetNodeId = "target",
                TargetItemId = "part",
            },
        ],
    };

    var analysis = ProductionAnalysisService.Analyze(
        scheme,
        new PlannerCatalog { Recipes = [sourceRecipe, targetRecipe] },
        new PlannerCalculator());

    AssertTrue(analysis.ShortEdges.Contains("edge-short"));
    AssertEqual(1, analysis.Alerts.Count);
}

static void ExistingProducerSuggestionsUseFreeConnectedCapacity()
{
    var sourceRecipe = SourceRecipe(120);
    var existingConsumerRecipe = ConsumerRecipe("existing", "Existing Consumer", 40);
    var targetRecipe = ConsumerRecipe("target", "Target Consumer", 60);
    var catalog = new PlannerCatalog { Recipes = [sourceRecipe, existingConsumerRecipe, targetRecipe] };
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode { Id = "source", SelectedRecipeKey = sourceRecipe.RecipeKey, MachineCount = 1 },
            new SchemeNode { Id = "existing", SelectedRecipeKey = existingConsumerRecipe.RecipeKey, MachineCount = 1 },
            new SchemeNode { Id = "target", SelectedRecipeKey = targetRecipe.RecipeKey, MachineCount = 1 },
        ],
        Edges =
        [
            new SchemeEdge
            {
                Id = "edge-existing",
                SourceNodeId = "source",
                SourceItemId = "part",
                TargetNodeId = "existing",
                TargetItemId = "part",
            },
        ],
    };

    IPlannerCalculator calculator = new PlannerCalculator();
    var analysis = ProductionAnalysisService.Analyze(scheme, catalog, calculator);
    var suggestions = PlannerSuggestionService.ExistingMachineSuggestions(
        scheme,
        catalog,
        analysis,
        calculator,
        "target",
        "input",
        "part");

    var sourceSuggestion = suggestions.Single(item => item.ExistingNodeId == "source");
    AssertEqual(120d, sourceSuggestion.MaxProductionPerMinute);
    AssertEqual(80d, sourceSuggestion.FreePerMinute);
    AssertEqual(60d, sourceSuggestion.RequiredPerMinute);
    AssertEqual(60d, sourceSuggestion.ConsumptionPerMinute);
    AssertFalse(sourceSuggestion.HasShortageRisk);

    targetRecipe.Inputs[0].QuantityPerMinute = 90;
    analysis = ProductionAnalysisService.Analyze(scheme, catalog, calculator);
    sourceSuggestion = PlannerSuggestionService.ExistingMachineSuggestions(
            scheme,
            catalog,
            analysis,
            calculator,
            "target",
            "input",
            "part")
        .Single(item => item.ExistingNodeId == "source");
    AssertEqual(80d, sourceSuggestion.FreePerMinute);
    AssertEqual(90d, sourceSuggestion.RequiredPerMinute);
    AssertTrue(sourceSuggestion.HasShortageRisk);
}

static void ExistingConsumerSuggestionsShowConsumedAndProducedRates()
{
    var sourceRecipe = SourceRecipe(120);
    var furnaceRecipe = new RecipeInfo
    {
        RecipeKey = "furnace:calcium-powder",
        BuildingId = "furnace",
        BuildingName = "Furnace v2",
        Output = new RecipePortInfo
        {
            ItemId = "calcium-powder",
            Name = "Calcium powder",
            QuantityPerMinute = 200,
        },
        Inputs =
        [
            new RecipePortInfo
            {
                ItemId = "part",
                Name = "Calcium block",
                QuantityPerMinute = 40,
            },
        ],
    };
    var catalog = new PlannerCatalog { Recipes = [sourceRecipe, furnaceRecipe] };
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode { Id = "source", SelectedRecipeKey = sourceRecipe.RecipeKey, MachineCount = 1 },
            new SchemeNode { Id = "furnace", SelectedRecipeKey = furnaceRecipe.RecipeKey, MachineCount = 1 },
        ],
    };

    IPlannerCalculator calculator = new PlannerCalculator();
    var analysis = ProductionAnalysisService.Analyze(scheme, catalog, calculator);
    var suggestion = PlannerSuggestionService.ExistingMachineSuggestions(
            scheme,
            catalog,
            analysis,
            calculator,
            "source",
            "output",
            "part")
        .Single(item => item.ExistingNodeId == "furnace");

    AssertEqual("Calcium block - 40/min", suggestion.Subtitle);
    AssertEqual("Calcium powder - 200/min", suggestion.Detail);
    AssertEqual(40d, suggestion.ConsumptionPerMinute);
    AssertEqual(200d, suggestion.MaxProductionPerMinute);
}

static void SuggestionInputRatesFormatConsumedResources()
{
    var recipe = TitaniumRodRecipe();

    AssertEqual("Titanium Bar - 30/min", PlannerSuggestionService.FormatInputRates(recipe));
}

static void ExistingSuggestionsSkipDuplicateAndSelfCandidates()
{
    var sourceRecipe = SourceRecipe(120);
    var targetRecipe = ConsumerRecipe("target", "Target Consumer", 60);
    var catalog = new PlannerCatalog { Recipes = [sourceRecipe, targetRecipe] };
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode { Id = "source", SelectedRecipeKey = sourceRecipe.RecipeKey, MachineCount = 1 },
            new SchemeNode { Id = "target", SelectedRecipeKey = targetRecipe.RecipeKey, MachineCount = 1 },
        ],
        Edges =
        [
            new SchemeEdge
            {
                Id = "edge-target",
                SourceNodeId = "source",
                SourceItemId = "part",
                TargetNodeId = "target",
                TargetItemId = "part",
            },
        ],
    };

    IPlannerCalculator calculator = new PlannerCalculator();
    var analysis = ProductionAnalysisService.Analyze(scheme, catalog, calculator);
    var suggestions = PlannerSuggestionService.ExistingMachineSuggestions(
        scheme,
        catalog,
        analysis,
        calculator,
        "target",
        "input",
        "part");

    AssertFalse(suggestions.Any(item => item.ExistingNodeId == "source"));
    AssertFalse(suggestions.Any(item => item.ExistingNodeId == "target"));
}

static void ConnectionCompatibilityRequiresMatchingItem()
{
    var source = TitaniumRodRecipe();
    var target = new RecipeInfo
    {
        Output = new RecipePortInfo { ItemId = "rotor", QuantityPerMinute = 10 },
        Inputs =
        [
            new RecipePortInfo { ItemId = "titanium-rod", QuantityPerMinute = 20 },
        ],
    };

    IPlannerCalculator calculator = new PlannerCalculator();
    AssertTrue(calculator.CanConnectOutputToInput(source, target, "titanium-rod"));
    AssertFalse(calculator.CanConnectOutputToInput(source, target, "titanium-bar"));
}

static void TransportTierRecommendation()
{
    var tiers = new[]
    {
        new TransportTierInfo { Name = "Rail I", ItemsPerMinute = 60, Level = 1 },
        new TransportTierInfo { Name = "Rail II", ItemsPerMinute = 120, Level = 2 },
    };

    IPlannerCalculator calculator = new PlannerCalculator();
    AssertEqual("Rail II", calculator.RecommendTransportTier(tiers, 90)?.Name);
    AssertNull(calculator.RecommendTransportTier(tiers, 200));
}

static void CanvasLayoutSnapsToGrid()
{
    ICanvasLayoutService layout = new CanvasLayoutService(24);
    var snapped = layout.Snap(new System.Windows.Point(37, 59));
    AssertEqual(48d, snapped.X);
    AssertEqual(48d, snapped.Y);
}

static void SchemeSerializationRoundTrips()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-tests-" + Guid.NewGuid().ToString("N"));
    ISchemeStore store = new SchemeStore(temp);
    var document = new SchemeDocument
    {
        Name = "Test Scheme",
        Nodes =
        [
            new SchemeNode
            {
                Id = "node-a",
                BuildingId = "crafter",
                SelectedRecipeKey = "crafter:titanium-rod",
                TargetOutputPerMinute = 30,
                X = 10,
                Y = 20,
            },
        ],
    };

    var path = store.Save(document);
    var loaded = store.Load(path);
    AssertEqual("Test Scheme", loaded.Name);
    AssertEqual("node-a", loaded.Nodes[0].Id);
    Directory.Delete(temp, recursive: true);
}

static void SchemeStoreDeletesSavedSchemes()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-tests-" + Guid.NewGuid().ToString("N"));
    ISchemeStore store = new SchemeStore(temp);
    var path = store.Save(new SchemeDocument { Name = "Delete Me" });

    AssertTrue(File.Exists(path));
    AssertTrue(store.ListSchemes().Any(item => item.FilePath == path));

    store.Delete(path);

    AssertFalse(File.Exists(path));
    AssertFalse(store.ListSchemes().Any(item => item.FilePath == path));
    Directory.Delete(temp, recursive: true);
}

static void SchemeStoreImportsSchemeJsonIntoActiveFolder()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-tests-" + Guid.NewGuid().ToString("N"));
    var sourceFolder = Path.Combine(temp, "source");
    var targetFolder = Path.Combine(temp, "target");
    ISchemeStore sourceStore = new SchemeStore(sourceFolder);
    ISchemeStore targetStore = new SchemeStore(targetFolder);
    var sourcePath = sourceStore.Save(new SchemeDocument { Name = "Imported Line" });

    var imported = targetStore.ImportSchemeFile(sourcePath);

    AssertTrue(File.Exists(sourcePath));
    AssertTrue(File.Exists(imported.FilePath));
    AssertTrue(imported.FilePath.StartsWith(targetFolder, StringComparison.OrdinalIgnoreCase));
    AssertEqual("Imported Line", targetStore.Load(imported.FilePath).Name);
    AssertTrue(targetStore.SchemeFileNameExists(sourcePath));

    var duplicate = targetStore.ImportSchemeFile(sourcePath, SchemeImportMode.KeepBoth);
    AssertTrue(File.Exists(duplicate.FilePath));
    AssertFalse(PathUtil.SamePath(imported.FilePath, duplicate.FilePath));
    AssertTrue(Path.GetFileNameWithoutExtension(duplicate.FilePath).Contains("(2)", StringComparison.Ordinal));

    var replacementSourcePath = sourceStore.Save(new SchemeDocument { Name = "Replacement Line", FilePath = sourcePath });
    var replaced = targetStore.ImportSchemeFile(replacementSourcePath, SchemeImportMode.Replace);
    AssertTrue(PathUtil.SamePath(imported.FilePath, replaced.FilePath));
    AssertEqual("Replacement Line", targetStore.Load(replaced.FilePath).Name);

    var invalidPath = Path.Combine(sourceFolder, "not-a-scheme.json");
    File.WriteAllText(invalidPath, """{"foo":true}""");
    AssertThrows<InvalidOperationException>(() => targetStore.ImportSchemeFile(invalidPath));
    Directory.Delete(temp, recursive: true);
}

static void SchemeStoreRemovesDeletedBlueprintReferences()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-tests-" + Guid.NewGuid().ToString("N"));
    ISchemeStore store = new SchemeStore(temp);
    var sourcePath = store.Save(new SchemeDocument { Name = "Source Factory" });
    var consumer = new SchemeDocument
    {
        Name = "Consumer Factory",
        Nodes =
        [
            new SchemeNode
            {
                Id = "source-node",
                NodeType = SchemeNodeType.BlueprintSource,
                SourceSchemeName = "Source Factory",
                SourceSchemePath = sourcePath,
                BlueprintOutputs =
                [
                    new BlueprintOutputPort { ItemId = "part", Name = "Part", RatePerMinute = 12 },
                ],
            },
            new SchemeNode
            {
                Id = "consumer-node",
                SelectedRecipeKey = "consumer:part",
            },
        ],
        Edges =
        [
            new SchemeEdge
            {
                SourceNodeId = "source-node",
                SourceItemId = "part",
                TargetNodeId = "consumer-node",
                TargetItemId = "part",
            },
        ],
    };
    var consumerPath = store.Save(consumer);

    store.Delete(sourcePath);
    var loaded = store.Load(consumerPath);

    AssertFalse(loaded.Nodes.Any(node => node.Id == "source-node"));
    AssertFalse(loaded.Edges.Any());
    AssertTrue(loaded.Nodes.Any(node => node.Id == "consumer-node"));
    Directory.Delete(temp, recursive: true);
}

static void SchemeStoreRejectsDeleteOutsideFolder()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-tests-" + Guid.NewGuid().ToString("N"));
    var schemeFolder = Path.Combine(temp, "schemes");
    var outsidePath = Path.Combine(temp, "outside.json");
    Directory.CreateDirectory(temp);
    File.WriteAllText(outsidePath, "{}");
    ISchemeStore store = new SchemeStore(schemeFolder);

    AssertThrows<InvalidOperationException>(() => store.Delete(outsidePath));
    AssertTrue(File.Exists(outsidePath));
    Directory.Delete(temp, recursive: true);
}

static void MachineNodeCanPersistWithoutRecipe()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-tests-" + Guid.NewGuid().ToString("N"));
    ISchemeStore store = new SchemeStore(temp);
    var document = new SchemeDocument
    {
        Name = "Machine Only",
        Nodes =
        [
            new SchemeNode
            {
                Id = "node-machine",
                BuildingId = "crafter",
                SelectedRecipeKey = null,
                TargetOutputPerMinute = 0,
                X = 24,
                Y = 48,
            },
        ],
    };

    var path = store.Save(document);
    var loaded = store.Load(path);
    AssertNull(loaded.Nodes[0].SelectedRecipeKey);
    AssertEqual("crafter", loaded.Nodes[0].BuildingId);
    Directory.Delete(temp, recursive: true);
}

static void ConnectionRoutePointsPersist()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-tests-" + Guid.NewGuid().ToString("N"));
    ISchemeStore store = new SchemeStore(temp);
    var document = new SchemeDocument
    {
        Name = "Routed Connection",
        Edges =
        [
            new SchemeEdge
            {
                Id = "edge-a",
                SourceNodeId = "source",
                SourceItemId = "titanium-rod",
                TargetNodeId = "target",
                TargetItemId = "titanium-rod",
                RoutePoints =
                [
                    new RoutePoint { X = 120, Y = 240 },
                    new RoutePoint { X = 360, Y = 240 },
                ],
            },
        ],
    };

    var path = store.Save(document);
    var loaded = store.Load(path);
    AssertEqual(2, loaded.Edges[0].RoutePoints.Count);
    AssertEqual(360d, loaded.Edges[0].RoutePoints[1].X);
    Directory.Delete(temp, recursive: true);
}

static void CommentRectanglesPersist()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-tests-" + Guid.NewGuid().ToString("N"));
    ISchemeStore store = new SchemeStore(temp);
    var document = new SchemeDocument
    {
        Name = "Commented Scheme",
        Comments =
        [
            new SchemeComment
            {
                Id = "comment-a",
                Text = "Initialize Nakama Client",
                X = 48,
                Y = 72,
                Width = 520,
                Height = 260,
            },
        ],
    };

    var path = store.Save(document);
    var loaded = store.Load(path);
    AssertEqual("comment-a", loaded.Comments[0].Id);
    AssertEqual("Initialize Nakama Client", loaded.Comments[0].Text);
    AssertEqual(520d, loaded.Comments[0].Width);
    Directory.Delete(temp, recursive: true);
}

static void AppSettingsSerializationRoundTrips()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-settings-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    var path = Path.Combine(temp, "settings.json");
    IAppSettingsStore store = new AppSettingsStore(path);
    store.Save(new AppSettings
    {
        Theme = AppTheme.Light,
        PlannerLanguage = PlannerLanguages.Ukrainian,
        ApiPort = 8020,
        CurrentRailTierId = "rail-2",
        SchemeFolderPath = Path.Combine(temp, "schemes"),
        CanvasCardFont = new FontSettings { Family = "Segoe UI", Size = 14, Color = "#112233" },
        LeftBarListFont = new FontSettings { Family = "Consolas", Size = 11, Color = "#445566" },
    });

    var loaded = store.Load();
    AssertEqual(AppTheme.Light, loaded.Theme);
    AssertEqual(PlannerLanguages.Ukrainian, loaded.PlannerLanguage);
    AssertEqual(8020, loaded.ApiPort);
    AssertEqual("rail-2", loaded.CurrentRailTierId);
    AssertEqual(Path.Combine(temp, "schemes"), loaded.SchemeFolderPath);
    AssertEqual("Segoe UI", loaded.CanvasCardFont.Family);
    AssertEqual(14d, loaded.CanvasCardFont.Size);
    AssertEqual("#445566", loaded.LeftBarListFont.Color);
    Directory.Delete(temp, recursive: true);
}

static void ViewModelAppliesConfiguredSchemeFolder()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-vm-scheme-folder-" + Guid.NewGuid().ToString("N"));
    var initialFolder = Path.Combine(temp, "initial");
    var configuredFolder = Path.Combine(temp, "configured");
    var changedFolder = Path.Combine(temp, "changed");
    Directory.CreateDirectory(temp);

    try
    {
        var settingsStore = new AppSettingsStore(Path.Combine(temp, "settings.json"));
        settingsStore.Save(new AppSettings { SchemeFolderPath = configuredFolder });
        ISchemeStore schemeStore = new SchemeStore(initialFolder);
        var viewModel = new MainWindowViewModel(
            new TestPlannerApiClient(),
            new TestApiProcessManager(),
            schemeStore,
            settingsStore,
            new ImmediateUiDispatcher(),
            new ImmediateBackgroundTaskRunner());

        AssertEqual(configuredFolder, schemeStore.FolderPath);
        AssertEqual(configuredFolder, viewModel.SchemeFolderPath);

        viewModel.SaveSettings(new AppSettings { SchemeFolderPath = changedFolder });

        AssertEqual(changedFolder, schemeStore.FolderPath);
        AssertEqual(changedFolder, settingsStore.Load().SchemeFolderPath);
    }
    finally
    {
        Directory.Delete(temp, recursive: true);
    }
}

static void ViewModelRefreshesLocalizedSavedState()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-vm-saved-state-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);

    try
    {
        var settingsStore = new AppSettingsStore(Path.Combine(temp, "settings.json"));
        settingsStore.Save(new AppSettings { SchemeFolderPath = temp });
        var viewModel = new MainWindowViewModel(
            new TestPlannerApiClient(),
            new TestApiProcessManager(),
            new SchemeStore(temp),
            settingsStore,
            new ImmediateUiDispatcher(),
            new ImmediateBackgroundTaskRunner());
        viewModel.Scheme.Name = "test";
        viewModel.SaveSchemeAsync().GetAwaiter().GetResult();

        var field = typeof(MainWindowViewModel).GetField(
            "_lastSavedText",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(viewModel, "Сохранено: Сегодня, 15:51");

        viewModel.RefreshLocalizedText();

        AssertTrue(viewModel.LastSavedText.StartsWith("Last saved:", StringComparison.Ordinal));
        AssertFalse(viewModel.LastSavedText.StartsWith("Сохранено:", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(temp, recursive: true);
    }
}

static void AsyncCommandTracksRunningState()
{
    var started = new TaskCompletionSource();
    var release = new TaskCompletionSource();
    var command = new AsyncRelayCommand(async _ =>
    {
        started.SetResult();
        await release.Task;
    });

    command.Execute(null);
    started.Task.GetAwaiter().GetResult();
    AssertFalse(command.CanExecute(null));
    release.SetResult();
    SpinWait.SpinUntil(() => command.CanExecute(null), TimeSpan.FromSeconds(2));
    AssertTrue(command.CanExecute(null));
}

static void CanvasViewModelCreatesCompatibleEdge()
{
    var sourceRecipe = TitaniumRodRecipe();
    var targetRecipe = new RecipeInfo
    {
        RecipeKey = "assembler:rotor",
        BuildingId = "assembler",
        BuildingName = "Assembler",
        Output = new RecipePortInfo { ItemId = "rotor", Name = "Rotor", QuantityPerMinute = 10 },
        Inputs =
        [
            new RecipePortInfo { ItemId = "titanium-rod", Name = "Titanium Rod", QuantityPerMinute = 20 },
        ],
    };
    var viewModel = new PlannerCanvasViewModel(new PlannerCalculator(), new CanvasLayoutService(24))
    {
        Catalog = new PlannerCatalog
        {
            Recipes = [sourceRecipe, targetRecipe],
        },
    };
    var source = viewModel.CreateRecipeNode(sourceRecipe, new System.Windows.Point(10, 10));
    source.Id = "source";
    var target = viewModel.CreateRecipeNode(targetRecipe, new System.Windows.Point(100, 10));
    target.Id = "target";
    viewModel.Scheme.Nodes.Add(source);
    viewModel.Scheme.Nodes.Add(target);

    var connected = viewModel.TryCreateEdge(
        new PlannerPortReference("source", "output", "titanium-rod"),
        new PlannerPortReference("target", "input", "titanium-rod"),
        out var edge);

    AssertTrue(connected);
    AssertEqual("source", edge?.SourceNodeId);
    AssertEqual(1, viewModel.Scheme.Edges.Count);
}

static void CanvasViewModelCreatesSchemeOutputSourceNodes()
{
    var titaniumRod = TitaniumRodRecipe();
    var rotor = new RecipeInfo
    {
        RecipeKey = "assembler:rotor",
        BuildingId = "assembler",
        BuildingName = "Assembler",
        Output = new RecipePortInfo { ItemId = "rotor", Name = "Rotor", QuantityPerMinute = 10 },
    };
    var viewModel = new PlannerCanvasViewModel(new PlannerCalculator(), new CanvasLayoutService(24))
    {
        Catalog = new PlannerCatalog
        {
            Recipes = [titaniumRod, rotor],
        },
    };

    var node = viewModel.CreateBlueprintSourceNode(
        new SchemeListItem
        {
            Name = "Existing Factory",
            FilePath = "existing-factory.json",
            Outputs =
            [
                new SchemeListOutputItem { RecipeKey = titaniumRod.RecipeKey, MachineCount = 2 },
                new SchemeListOutputItem { RecipeKey = rotor.RecipeKey, MachineCount = 3 },
            ],
        },
        new System.Windows.Point(37, 59));

    AssertTrue(node is not null);
    AssertEqual(SchemeNodeType.BlueprintSource, node!.NodeType);
    AssertTrue(node.OnlyOutput);
    AssertFalse(node.IsSchemeOutput);
    AssertEqual("Existing Factory", node.SourceSchemeName);
    AssertEqual("existing-factory.json", node.SourceSchemePath);
    AssertEqual(2, node.BlueprintOutputs.Count);
    AssertEqual(60d, node.BlueprintOutputs.First(output => output.ItemId == "titanium-rod").RatePerMinute);
    AssertEqual(30d, node.BlueprintOutputs.First(output => output.ItemId == "rotor").RatePerMinute);
    AssertEqual(48d, node.X);
    AssertEqual(48d, node.Y);
}

static void BlueprintSourceOutputFeedsConsumers()
{
    var targetRecipe = ConsumerRecipe("consumer", "Consumer", 20);
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode
            {
                Id = "blueprint",
                NodeType = SchemeNodeType.BlueprintSource,
                OnlyOutput = true,
                SourceSchemeName = "Existing Factory",
                BlueprintOutputs =
                [
                    new BlueprintOutputPort
                    {
                        ItemId = "part",
                        Name = "Part",
                        RatePerMinute = 12,
                    },
                ],
            },
            new SchemeNode { Id = "target", SelectedRecipeKey = targetRecipe.RecipeKey, MachineCount = 1 },
        ],
        Edges =
        [
            new SchemeEdge
            {
                Id = "edge-blueprint",
                SourceNodeId = "blueprint",
                SourceItemId = "part",
                TargetNodeId = "target",
                TargetItemId = "part",
            },
        ],
    };
    var catalog = new PlannerCatalog { Recipes = [targetRecipe] };

    var analysis = ProductionAnalysisService.Analyze(scheme, catalog, new PlannerCalculator());

    AssertTrue(PlannerEdgeService.IsEdgeValid(scheme, catalog, new PlannerCalculator(), scheme.Edges[0]));
    AssertEqual(12d, analysis.EdgeDeliveries["edge-blueprint"]);
    AssertEqual(12d, analysis.Inputs[ProductionInputKey.For("target", "part")].DeliveredPerMinute);
    AssertEqual(1, analysis.Alerts.Count);
}

static void CanvasGeometryRoutesThroughDividerPoints()
{
    var edge = new SchemeEdge
    {
        RoutePoints =
        [
            new RoutePoint { X = 120, Y = 80 },
        ],
    };
    var points = CanvasGeometryService.EdgePoints(
        edge,
        new System.Windows.Point(0, 0),
        new System.Windows.Point(240, 0));
    var geometry = CanvasGeometryService.CreateRoutedGeometry(points);

    AssertEqual(3, points.Count);
    AssertTrue(!geometry.IsEmpty());
    AssertEqual(120d, points[1].X);
}

static void EdgeLabelIncludesTransportTier()
{
    var sourceRecipe = TitaniumRodRecipe();
    var targetRecipe = new RecipeInfo
    {
        RecipeKey = "assembler:rotor",
        BuildingId = "assembler",
        BuildingName = "Assembler",
        Output = new RecipePortInfo { ItemId = "rotor", Name = "Rotor", QuantityPerMinute = 10 },
        Inputs =
        [
            new RecipePortInfo { ItemId = "titanium-rod", Name = "Titanium Rod", QuantityPerMinute = 20 },
        ],
    };
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode { Id = "source", SelectedRecipeKey = sourceRecipe.RecipeKey, MachineCount = 1 },
            new SchemeNode { Id = "target", SelectedRecipeKey = targetRecipe.RecipeKey, MachineCount = 1 },
        ],
        Edges =
        [
            new SchemeEdge
            {
                SourceNodeId = "source",
                SourceItemId = "titanium-rod",
                TargetNodeId = "target",
                TargetItemId = "titanium-rod",
            },
        ],
    };
    var catalog = new PlannerCatalog
    {
        Recipes = [sourceRecipe, targetRecipe],
        TransportTiers = new TransportTierPayload
        {
            Tiers =
            [
                new TransportTierInfo { Id = "rail-1", Name = "Rail tier 1", ItemsPerMinute = 120 },
            ],
        },
    };

    var settings = new AppSettings { CurrentRailTierId = "rail-1" };
    var calculator = new PlannerCalculator();

    var label = PlannerEdgeService.EdgeLabel(scheme, catalog, settings, calculator, scheme.Edges[0]);
    AssertTrue(label.Contains("Titanium Rod", StringComparison.Ordinal));
    AssertTrue(label.Contains("30/min", StringComparison.Ordinal));
    AssertTrue(label.Contains("meets demand", StringComparison.Ordinal));
    AssertTrue(label.Contains("Rail tier 1", StringComparison.Ordinal));

    var detail = PlannerEdgeService.EdgeDetail(scheme, catalog, settings, calculator, scheme.Edges[0]);
    AssertTrue(detail.Contains("Item: Titanium Rod", StringComparison.Ordinal));
    AssertTrue(detail.Contains("30 / 20 /min", StringComparison.Ordinal));
    AssertTrue(detail.Contains("Rail tier 1", StringComparison.Ordinal));
    AssertTrue(detail.Contains("120/min capacity (OK)", StringComparison.Ordinal));
}

static void SchemeSerializationRoundTripsCorporationSettings()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-tests-" + Guid.NewGuid().ToString("N"));
    ISchemeStore store = new SchemeStore(temp);
    var document = new SchemeDocument
    {
        Name = "Corporation Scheme",
        CorporationLevels = new Dictionary<string, int>
        {
            ["starting"] = 5,
            ["selenian"] = 3,
        },
    };

    var path = store.Save(document);
    var loaded = store.Load(path);
    AssertEqual(5, loaded.CorporationLevels["starting"]);
    AssertEqual(3, loaded.CorporationLevels["selenian"]);
    AssertNull(loaded.SelectedRailTierId);
    Directory.Delete(temp, recursive: true);
}

static void CorporationDefaultsUnlockTrainingRailAvailability()
{
    var scheme = new SchemeDocument();
    var catalog = UnlockCatalog();

    PlannerUnlockService.EnsureSchemeDefaults(scheme, catalog);

    AssertEqual(5, scheme.CorporationLevels["starting"]);
    AssertEqual(0, scheme.CorporationLevels["selenian"]);
    AssertNull(scheme.SelectedRailTierId);
    AssertEqual("rail-tier-1", PlannerUnlockService.MaxAvailableRailTier(catalog, scheme)?.Id);
    AssertTrue(PlannerUnlockService.IsBuildingUnlocked(catalog, scheme, "smelter"));
    AssertFalse(PlannerUnlockService.IsBuildingUnlocked(catalog, scheme, "factory"));
}

static void LockedBuildingRequiresCorporationLevel()
{
    var scheme = new SchemeDocument
    {
        CorporationLevels = new Dictionary<string, int>
        {
            ["starting"] = 5,
            ["selenian"] = 0,
        },
    };
    var catalog = UnlockCatalog();

    AssertFalse(PlannerUnlockService.IsBuildingUnlocked(catalog, scheme, "factory"));
    scheme.CorporationLevels["selenian"] = 12;
    AssertTrue(PlannerUnlockService.IsBuildingUnlocked(catalog, scheme, "factory"));
}

static void RailRecommendationUsesAvailableTiers()
{
    var sourceRecipe = TitaniumRodRecipe();
    var targetRecipe = new RecipeInfo
    {
        RecipeKey = "assembler:rotor",
        BuildingId = "assembler",
        BuildingName = "Assembler",
        Output = new RecipePortInfo { ItemId = "rotor", Name = "Rotor", QuantityPerMinute = 10 },
        Inputs =
        [
            new RecipePortInfo { ItemId = "titanium-rod", Name = "Titanium Rod", QuantityPerMinute = 150 },
        ],
    };
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode { Id = "source", SelectedRecipeKey = sourceRecipe.RecipeKey, MachineCount = 1 },
            new SchemeNode { Id = "target", SelectedRecipeKey = targetRecipe.RecipeKey, MachineCount = 1 },
        ],
        Edges =
        [
            new SchemeEdge
            {
                SourceNodeId = "source",
                SourceItemId = "titanium-rod",
                TargetNodeId = "target",
                TargetItemId = "titanium-rod",
            },
        ],
    };
    var catalog = new PlannerCatalog
    {
        Recipes = [sourceRecipe, targetRecipe],
        TransportTiers = new TransportTierPayload
        {
            Tiers =
            [
                new TransportTierInfo { Id = "rail-tier-1", Name = "Rail tier 1", ItemsPerMinute = 120 },
                new TransportTierInfo { Id = "rail-tier-2", Name = "Rail tier 2", ItemsPerMinute = 240 },
            ],
        },
    };
    var settings = new AppSettings { CurrentRailTierId = "rail-tier-1" };

    var detail = PlannerEdgeService.EdgeDetail(scheme, catalog, settings, new PlannerCalculator(), scheme.Edges[0]);
    AssertTrue(detail.Contains("Rail tier 2", StringComparison.Ordinal));
}

static PlannerCatalog UnlockCatalog()
{
    return new PlannerCatalog
    {
        Corporations =
        [
            new CorporationInfo { CorporationId = "starting", Name = "Training Corporation", MaxLevel = 5 },
            new CorporationInfo { CorporationId = "selenian", Name = "Selenian Corporation", MaxLevel = 13 },
        ],
        BuildingUnlocks = new Dictionary<string, List<BuildingUnlockInfo>>
        {
            ["smelter"] =
            [
                new BuildingUnlockInfo
                {
                    CorporationId = "starting",
                    CorporationName = "Training Corporation",
                    Level = 4,
                },
            ],
            ["factory"] =
            [
                new BuildingUnlockInfo
                {
                    CorporationId = "selenian",
                    CorporationName = "Selenian Corporation",
                    Level = 12,
                },
            ],
        },
        TransportTiers = new TransportTierPayload
        {
            Tiers =
            [
                new TransportTierInfo
                {
                    Id = "rail-tier-1",
                    Name = "Rail tier 1",
                    Level = 1,
                    ItemsPerMinute = 120,
                    UnlockRequirements =
                    [
                        new CorporationUnlockRequirementInfo { CorporationId = "starting", Level = 2 },
                    ],
                },
                new TransportTierInfo
                {
                    Id = "rail-tier-2",
                    Name = "Rail tier 2",
                    Level = 2,
                    ItemsPerMinute = 240,
                    UnlockRequirements =
                    [
                        new CorporationUnlockRequirementInfo { CorporationId = "selenian", Level = 8 },
                    ],
                },
            ],
        },
    };
}

static void BuildingMetricsDeserialize()
{
    var building = System.Text.Json.JsonSerializer.Deserialize<BuildingInfo>(
        "{\"building_id\":\"smelter\",\"name\":\"Smelter\",\"category\":\"crafting\",\"power\":-5,\"temperature\":3}",
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    AssertEqual(-5d, building?.Power ?? 0);
    AssertEqual(3d, building?.Temperature ?? 0);
}

static void CatalogMetadataDeserializesLanguage()
{
    var catalog = System.Text.Json.JsonSerializer.Deserialize<PlannerCatalog>(
        "{\"meta\":{\"building_count\":21,\"recipe_count\":132,\"language\":\"uk\"}}",
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    AssertEqual("uk", catalog?.Meta.Language);
    AssertEqual(132, catalog?.Meta.RecipeCount ?? 0);
}

static void SchemeMetricsScaleByMachineCount()
{
    var recipe = TitaniumRodRecipe();
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode
            {
                Id = "fabricator",
                BuildingId = "crafter",
                SelectedRecipeKey = recipe.RecipeKey,
                MachineCount = 2,
            },
        ],
    };
    var catalog = new PlannerCatalog
    {
        Recipes = [recipe],
        Buildings =
        [
            new BuildingInfo
            {
                BuildingId = "crafter",
                Name = "Fabricator",
                Category = "crafting",
                Power = -10,
                Temperature = 5,
            },
        ],
    };

    var totals = PlannerMetricService.CalculateTotals(scheme, catalog);
    AssertEqual(20d, totals.PowerConsumption);
    AssertEqual(0d, totals.PowerGeneration);
    AssertEqual(10d, totals.Temperature);
    AssertEqual("20 kW", PlannerMetricService.FormatNodePower(catalog.Buildings[0], 2));
    AssertEqual("+10 temp", PlannerMetricService.FormatNodeTemperature(catalog.Buildings[0], 2));
}

static void MissingBuildingMetricsAreIgnored()
{
    var recipe = TitaniumRodRecipe();
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode
            {
                Id = "fabricator",
                BuildingId = "crafter",
                SelectedRecipeKey = recipe.RecipeKey,
                MachineCount = 3,
            },
        ],
    };
    var catalog = new PlannerCatalog
    {
        Recipes = [recipe],
        Buildings =
        [
            new BuildingInfo
            {
                BuildingId = "crafter",
                Name = "Fabricator",
                Category = "crafting",
            },
        ],
    };

    var totals = PlannerMetricService.CalculateTotals(scheme, catalog);
    AssertEqual(0d, totals.PowerConsumption);
    AssertEqual(0d, totals.Temperature);
    AssertEqual("-", PlannerMetricService.FormatNodePower(catalog.Buildings[0], 3));
}

static void TemperatureCountsForPlacedMachineWithoutRecipe()
{
    var scheme = new SchemeDocument
    {
        Nodes =
        [
            new SchemeNode
            {
                Id = "fabricator",
                BuildingId = "crafter",
                SelectedRecipeKey = null,
                MachineCount = 3,
            },
        ],
    };
    var catalog = new PlannerCatalog
    {
        Buildings =
        [
            new BuildingInfo
            {
                BuildingId = "crafter",
                Name = "Fabricator",
                Category = "crafting",
                Power = -10,
                Temperature = 5,
            },
        ],
    };

    var totals = PlannerMetricService.CalculateTotals(scheme, catalog);
    AssertEqual(0d, totals.PowerConsumption);
    AssertEqual(15d, totals.Temperature);
}

static void SourceApiProjectDiscoveryFindsDotNetProject()
{
    var root = LocalApiProcessManager.FindRepoRoot(Environment.CurrentDirectory);
    var apiProject = LocalApiProcessManager.FindSourceApiProject(Environment.CurrentDirectory);

    if (root is null || apiProject is null)
    {
        throw new InvalidOperationException("Expected source API project to be discoverable.");
    }

    AssertTrue(File.Exists(apiProject));
    AssertTrue(apiProject.EndsWith(
        Path.Combine("src", "StarRupturePlanner.Api", "StarRupturePlanner.Api.csproj"),
        StringComparison.OrdinalIgnoreCase));
}

static void BundledApiDiscoveryFindsDotNetExecutable()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-api-" + Guid.NewGuid().ToString("N"));
    var nested = Path.Combine(temp, "app", "nested");
    var apiDir = Path.Combine(temp, "app", "api");
    Directory.CreateDirectory(nested);
    Directory.CreateDirectory(apiDir);
    var apiPath = Path.Combine(apiDir, "StarRupturePlanner.Api.exe");
    File.WriteAllText(apiPath, "");

    try
    {
        AssertEqual(apiPath, LocalApiProcessManager.FindBundledApiExecutable(nested));
    }
    finally
    {
        Directory.Delete(temp, recursive: true);
    }
}

static void BundledApiDiscoveryIgnoresLegacyPythonExecutable()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-api-" + Guid.NewGuid().ToString("N"));
    var nested = Path.Combine(temp, "app", "nested");
    var apiDir = Path.Combine(temp, "app", "api");
    Directory.CreateDirectory(nested);
    Directory.CreateDirectory(apiDir);
    var legacyApiPath = Path.Combine(apiDir, "StarRuptureApi.exe");
    File.WriteAllText(legacyApiPath, "");

    try
    {
        AssertNull(LocalApiProcessManager.FindBundledApiExecutable(nested));
    }
    finally
    {
        Directory.Delete(temp, recursive: true);
    }
}

static void ApiPortDiscoveryIncrementsPastBusyPort()
{
    using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var busyPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

    var availablePort = LocalApiProcessManager.FindAvailablePort(busyPort);

    AssertTrue(availablePort > busyPort);
    using var verifier = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, availablePort);
    verifier.Start();
}

static void DotNetApiSettingsParsePortOverride()
{
    AssertEqual(8010, ApiSettings.ParsePort(Array.Empty<string>()));
    AssertEqual(8020, ApiSettings.ParsePort(["--port", "8020"]));
    AssertEqual(8030, ApiSettings.ParsePort(["--port=8030"]));
    AssertEqual(8040, ApiSettings.ParsePort(["-p", "8040"]));
    AssertThrows<ArgumentOutOfRangeException>(() => ApiSettings.ParsePort(["--port", "0"]));
    AssertThrows<ArgumentException>(() => ApiSettings.ParsePort(["--port"]));
}

static void PlannerApiClientConfiguresPort()
{
    var client = new PlannerApiClient();

    AssertEqual(8010, client.BaseUri.Port);
    client.ConfigurePort(8020);

    AssertEqual(8020, client.BaseUri.Port);
    AssertEqual("http://127.0.0.1:8020/assets/items/rotor.png", client.ToAbsoluteAssetUrl("/assets/items/rotor.png"));
}

static void DotNetApiHttpEndpointsAndMcpAreOpen()
{
    DotNetApiHttpEndpointsAndMcpAreOpenAsync().GetAwaiter().GetResult();
}

static async Task DotNetApiHttpEndpointsAndMcpAreOpenAsync()
{
    var port = LocalApiProcessManager.FindAvailablePort(49152);
    var process = StartDotNetApiProcess(port);

    try
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromSeconds(5),
        };

        await WaitForApiAsync(client, process);

        using var meta = await GetJsonDocumentAsync(client, "api/meta");
        AssertEqual("0.2.8", meta.RootElement.GetProperty("game_version").GetString());
        AssertEqual(135, meta.RootElement.GetProperty("counts").GetProperty("items").GetInt32());

        using var item = await GetJsonDocumentAsync(client, "api/items/rotor?lang=en");
        AssertEqual("rotor", item.RootElement.GetProperty("item").GetProperty("item_id").GetString());

        using var catalog = await GetJsonDocumentAsync(client, "api/planner/catalog?lang=uk");
        AssertEqual("uk", catalog.RootElement.GetProperty("meta").GetProperty("language").GetString());
        AssertTrue(catalog.RootElement.GetProperty("recipes").GetArrayLength() >= 100);

        using var assetResponse = await client.GetAsync("assets/items/rotor.png");
        AssertTrue(assetResponse.IsSuccessStatusCode);
        AssertTrue(assetResponse.Content.Headers.ContentLength is null or > 0);

        using var request = new HttpRequestMessage(HttpMethod.Options, "mcp");
        request.Headers.TryAddWithoutValidation("Origin", "http://localhost:3000");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "content-type");
        using var mcpResponse = await client.SendAsync(request);

        AssertEqual(System.Net.HttpStatusCode.NoContent, mcpResponse.StatusCode);
        AssertEqual("http://localhost:3000", HeaderValue(mcpResponse, "Access-Control-Allow-Origin"));
        AssertTrue(HeaderValue(mcpResponse, "Access-Control-Allow-Methods").Contains("POST", StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        StopProcess(process);
    }
}

static void DotNetApiMetaMatchesDataset()
{
    var service = DotNetApiService();
    var meta = service.GetMeta();
    var counts = AsDictionary(meta["counts"]);

    AssertEqual("0.2.8", meta["game_version"]);
    AssertEqual(135L, counts["items"]);
    AssertTrue(Convert.ToInt64(counts["buildings"]) >= 20);
    AssertTrue(Convert.ToInt64(counts["recipes"]) >= 100);
}

static void DotNetApiPlannerCatalogContainsGraphReadyRecipes()
{
    var service = DotNetApiService();
    var catalog = service.GetPlannerCatalog();
    var recipes = AsList(catalog["recipes"]);
    var recipe = recipes
        .Select(AsDictionary)
        .First(entry =>
            string.Equals(Convert.ToString(entry["building_name"]), "Fabricator", StringComparison.Ordinal)
            && string.Equals(Convert.ToString(entry["recipe_id"]), "titanium-rod", StringComparison.Ordinal));
    var output = AsDictionary(recipe["output"]);
    var inputs = AsList(recipe["inputs"]).Select(AsDictionary).ToList();

    AssertEqual("titanium-rod", output["item_id"]);
    AssertEqual(30L, output["quantity_per_minute"]);
    AssertEqual("titanium-bar", inputs[0]["item_id"]);
    AssertEqual(30L, inputs[0]["quantity_per_minute"]);
    AssertTrue(Convert.ToString(recipe["building_image_url"])?.StartsWith("/assets/buildings/", StringComparison.Ordinal) == true);
}

static void DotNetApiLocalizedSearchMatchesUkrainianName()
{
    var service = DotNetApiService();
    var catalog = service.GetPlannerCatalog("uk");
    var recipes = AsList(catalog["recipes"]).Select(AsDictionary);
    var recipe = recipes.First(entry => string.Equals(Convert.ToString(entry["recipe_id"]), "titanium-rod", StringComparison.Ordinal));
    var output = AsDictionary(recipe["output"]);
    var localizedName = Convert.ToString(output["name"]) ?? "";
    var payload = service.SearchItems(localizedName, lang: "uk");
    var items = AsList(payload["items"]).Select(AsDictionary);

    AssertTrue(items.Any(item => string.Equals(Convert.ToString(item["item_id"]), "titanium-rod", StringComparison.Ordinal)));
}

static DotNetResourceService DotNetApiService()
{
    var settings = new ApiSettings();
    return new DotNetResourceService(settings, new LocalizationStore(settings));
}

static System.Diagnostics.Process StartDotNetApiProcess(int port)
{
    var apiAssemblyPath = typeof(ApiSettings).Assembly.Location;
    if (string.IsNullOrWhiteSpace(apiAssemblyPath) || !File.Exists(apiAssemblyPath))
    {
        throw new InvalidOperationException("Could not locate StarRupturePlanner.Api assembly.");
    }

    var startInfo = new System.Diagnostics.ProcessStartInfo
    {
        FileName = "dotnet",
        WorkingDirectory = LocalApiProcessManager.FindRepoRoot(Environment.CurrentDirectory) ?? Environment.CurrentDirectory,
        CreateNoWindow = true,
        UseShellExecute = false,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
    };
    startInfo.ArgumentList.Add(apiAssemblyPath);
    startInfo.ArgumentList.Add("--port");
    startInfo.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));

    return System.Diagnostics.Process.Start(startInfo)
        ?? throw new InvalidOperationException("Could not start StarRupturePlanner.Api process.");
}

static async Task WaitForApiAsync(HttpClient client, System.Diagnostics.Process process)
{
    for (var attempt = 0; attempt < 40; attempt++)
    {
        if (process.HasExited)
        {
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"API process exited early. stdout: {output} stderr: {error}");
        }

        try
        {
            using var response = await client.GetAsync("api/meta");
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException)
        {
        }

        await Task.Delay(250);
    }

    throw new InvalidOperationException("API process did not respond before timeout.");
}

static async Task<System.Text.Json.JsonDocument> GetJsonDocumentAsync(HttpClient client, string url)
{
    using var response = await client.GetAsync(url);
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"{url} returned {(int)response.StatusCode}: {body}");
    }

    return System.Text.Json.JsonDocument.Parse(body);
}

static string HeaderValue(HttpResponseMessage response, string name)
{
    return response.Headers.TryGetValues(name, out var values)
        ? string.Join(",", values)
        : "";
}

static void StopProcess(System.Diagnostics.Process process)
{
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(3000);
        }
    }
    finally
    {
        process.Dispose();
    }
}

static IReadOnlyDictionary<string, object?> AsDictionary(object? value) =>
    value as IReadOnlyDictionary<string, object?>
    ?? throw new InvalidOperationException($"Expected dictionary, got {value?.GetType().Name ?? "null"}.");

static IReadOnlyList<object?> AsList(object? value)
{
    if (value is IReadOnlyList<object?> objectList)
    {
        return objectList;
    }

    if (value is System.Collections.IEnumerable enumerable and not string)
    {
        return enumerable.Cast<object?>().ToList();
    }

    throw new InvalidOperationException($"Expected list, got {value?.GetType().Name ?? "null"}.");
}

static void AssertTrue(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void AssertFalse(bool condition)
{
    if (condition)
    {
        throw new InvalidOperationException("Expected false.");
    }
}

static void AssertNull(object? value)
{
    if (value is not null)
    {
        throw new InvalidOperationException($"Expected null but got {value}.");
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Expected {typeof(TException).Name}, got {ex.GetType().Name}.");
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }

    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(action());
    }
}

sealed class ImmediateBackgroundTaskRunner : IBackgroundTaskRunner
{
    public Task RunAsync(Action action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }

    public Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(action());
    }
}

sealed class TestApiProcessManager : IApiProcessManager
{
    public Task<string> EnsureStartedAsync(CancellationToken cancellationToken = default) => Task.FromResult("API ready.");

    public void StopStartedProcess()
    {
    }

    public void Dispose()
    {
    }
}

sealed class TestPlannerApiClient : IPlannerApiClient
{
    public Uri BaseUri { get; private set; } = new("http://localhost/");

    public string PlannerLanguage { get; set; } = "en";

    public void ConfigurePort(int port)
    {
        BaseUri = new Uri($"http://127.0.0.1:{AppSettings.NormalizeApiPort(port)}/");
    }

    public Task<bool> IsApiAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<PlannerCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PlannerCatalog());

    public Task<SuggestionResponse> GetSuggestionsAsync(string direction, string itemId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SuggestionResponse { Direction = direction, ItemId = itemId });

    public string ToAbsoluteAssetUrl(string? assetUrl)
    {
        if (string.IsNullOrWhiteSpace(assetUrl))
        {
            return "";
        }

        return assetUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? assetUrl
            : new Uri(BaseUri, assetUrl.TrimStart('/')).ToString();
    }
}
