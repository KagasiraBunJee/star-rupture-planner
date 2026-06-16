using StarRupturePlanner.Models;
using StarRupturePlanner.Services;
using StarRupturePlanner.ViewModels;

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
    ("Connection compatibility requires matching item", ConnectionCompatibilityRequiresMatchingItem),
    ("Transport tier recommendation chooses smallest sufficient tier", TransportTierRecommendation),
    ("Canvas layout snaps to grid", CanvasLayoutSnapsToGrid),
    ("Scheme serialization round trips", SchemeSerializationRoundTrips),
    ("Scheme store deletes saved schemes", SchemeStoreDeletesSavedSchemes),
    ("Scheme store removes deleted blueprint references", SchemeStoreRemovesDeletedBlueprintReferences),
    ("Scheme store rejects delete outside folder", SchemeStoreRejectsDeleteOutsideFolder),
    ("Machine node can persist without selected recipe", MachineNodeCanPersistWithoutRecipe),
    ("Connection route points persist", ConnectionRoutePointsPersist),
    ("Comment rectangles persist", CommentRectanglesPersist),
    ("App settings serialization round trips", AppSettingsSerializationRoundTrips),
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
    ("API root discovery finds repo", ApiRootDiscoveryFindsRepo),
    ("Bundled API discovery finds release executable", BundledApiDiscoveryFindsReleaseExecutable),
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
    AssertEqual("0.2.7", AppVersionInfo.SupportedGameVersion);
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
        CurrentRailTierId = "rail-2",
        CanvasCardFont = new FontSettings { Family = "Segoe UI", Size = 14, Color = "#112233" },
        LeftBarListFont = new FontSettings { Family = "Consolas", Size = 11, Color = "#445566" },
    });

    var loaded = store.Load();
    AssertEqual(AppTheme.Light, loaded.Theme);
    AssertEqual(PlannerLanguages.Ukrainian, loaded.PlannerLanguage);
    AssertEqual("rail-2", loaded.CurrentRailTierId);
    AssertEqual("Segoe UI", loaded.CanvasCardFont.Family);
    AssertEqual(14d, loaded.CanvasCardFont.Size);
    AssertEqual("#445566", loaded.LeftBarListFont.Color);
    Directory.Delete(temp, recursive: true);
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

static void ApiRootDiscoveryFindsRepo()
{
    var root = LocalApiProcessManager.FindRepoRoot(Environment.CurrentDirectory);
    AssertTrue(root is not null && Directory.Exists(Path.Combine(root, "starrupture_api")));
}

static void BundledApiDiscoveryFindsReleaseExecutable()
{
    var temp = Path.Combine(Path.GetTempPath(), "sr-planner-api-" + Guid.NewGuid().ToString("N"));
    var nested = Path.Combine(temp, "app", "nested");
    var apiDir = Path.Combine(temp, "app", "api");
    Directory.CreateDirectory(nested);
    Directory.CreateDirectory(apiDir);
    var apiPath = Path.Combine(apiDir, "StarRuptureApi.exe");
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

sealed class TestPlannerApiClient : IPlannerApiClient
{
    public Uri BaseUri { get; } = new("http://localhost/");

    public string PlannerLanguage { get; set; } = "en";

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
