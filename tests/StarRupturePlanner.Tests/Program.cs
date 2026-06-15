using StarRupturePlanner.Models;
using StarRupturePlanner.Services;
using StarRupturePlanner.ViewModels;

var tests = new (string Name, Action Body)[]
{
    ("Default target equals recipe output", DefaultTargetEqualsRecipeOutput),
    ("Machine count and input rates scale with target", MachineCountAndInputsScale),
    ("Machine count scales output and inputs", MachineCountScalesOutputAndInputs),
    ("Legacy target output migrates to machine count", LegacyTargetOutputMigratesToMachineCount),
    ("High priority consumer is satisfied first", HighPriorityConsumerIsSatisfiedFirst),
    ("Same priority redistributes capped demand", SamePriorityRedistributesCappedDemand),
    ("Shortage marks edge and creates alert", ShortageMarksEdgeAndCreatesAlert),
    ("Connection compatibility requires matching item", ConnectionCompatibilityRequiresMatchingItem),
    ("Transport tier recommendation chooses smallest sufficient tier", TransportTierRecommendation),
    ("Canvas layout snaps to grid", CanvasLayoutSnapsToGrid),
    ("Scheme serialization round trips", SchemeSerializationRoundTrips),
    ("Machine node can persist without selected recipe", MachineNodeCanPersistWithoutRecipe),
    ("Connection route points persist", ConnectionRoutePointsPersist),
    ("Comment rectangles persist", CommentRectanglesPersist),
    ("App settings serialization round trips", AppSettingsSerializationRoundTrips),
    ("Async command tracks running state", AsyncCommandTracksRunningState),
    ("Canvas view model creates compatible edge", CanvasViewModelCreatesCompatibleEdge),
    ("Canvas geometry routes through divider points", CanvasGeometryRoutesThroughDividerPoints),
    ("Edge label includes transport tier", EdgeLabelIncludesTransportTier),
    ("API root discovery finds repo", ApiRootDiscoveryFindsRepo),
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

    AssertEqual(2, scheme.Version);
    AssertEqual(3, scheme.Nodes[0].MachineCount);
    AssertEqual(0d, scheme.Nodes[0].TargetOutputPerMinute);
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
        CurrentRailTierId = "rail-2",
        CanvasCardFont = new FontSettings { Family = "Segoe UI", Size = 14, Color = "#112233" },
        LeftBarListFont = new FontSettings { Family = "Consolas", Size = 11, Color = "#445566" },
    });

    var loaded = store.Load();
    AssertEqual(AppTheme.Light, loaded.Theme);
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

    var label = PlannerEdgeService.EdgeLabel(
        scheme,
        catalog,
        new AppSettings { CurrentRailTierId = "rail-1" },
        new PlannerCalculator(),
        scheme.Edges[0]);

    AssertTrue(label.Contains("del 30/min", StringComparison.Ordinal));
    AssertTrue(label.Contains("req 20/min", StringComparison.Ordinal));
    AssertTrue(label.Contains("Rail tier 1 120/min", StringComparison.Ordinal));
}

static void ApiRootDiscoveryFindsRepo()
{
    var root = LocalApiProcessManager.FindRepoRoot(Environment.CurrentDirectory);
    AssertTrue(root is not null && Directory.Exists(Path.Combine(root, "starrupture_api")));
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
