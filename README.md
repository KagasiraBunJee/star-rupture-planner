# StarRupture Resource Data Platform

Local StarRupture resource production database with:

- HTTP JSON API for a future frontend
- MCP SSE server for AI clients
- manual refresh from `starrupture.tools`
- local image asset cache

## Commands

```powershell
python -m starrupture_api.main refresh
python -m starrupture_api.main serve --host 127.0.0.1 --port 8010
python -m starrupture_api.main item rotor
```

## HTTP API

- `GET /api/meta`
- `GET /api/items?q=rotor`
- `GET /api/items/{item_id}`
- `GET /api/buildings`
- `GET /api/planner/catalog`
- `GET /api/planner/suggestions?direction=input&item_id=titanium-bar`
- `GET /api/planner/transport-tiers`
- `POST /api/admin/refresh`
- `GET /assets/items/{filename}`
- `GET /assets/buildings/{filename}`

`GET /api/items/{item_id}` returns:

- `item`
- `unlock_requirements`: recipes for this item that require research/unlock items
- `used_to_unlock`: other recipes where this item is an unlock requirement
- `produced_by`: production recipes, rates, inputs, and per-recipe unlock requirements
- `used_in`: production recipes that consume this item as an input
- `meta`

Planner endpoints are graph-oriented for the Windows planner:

- `catalog` returns buildings, recipes, active input/output ports, rates, unlocks, images, and transport tier config.
- `suggestions` returns compatible preselected recipes for Blueprint-style drag-release connection popovers.
- `transport-tiers` reads `data/transport_tiers.json`; empty tiers are allowed and make the planner show missing transport recommendations.

## Windows Planner

```powershell
dotnet run --project src\StarRupturePlanner\StarRupturePlanner.csproj
dotnet run --project tests\StarRupturePlanner.Tests\StarRupturePlanner.Tests.csproj
```

The planner stores schemes in `Documents\StarRupture Planner\Schemes` by default. The app starts the local API on `127.0.0.1:8010` when needed.
General app settings are stored in `%LOCALAPPDATA%\StarRupture Planner\settings.json`.
The Settings window controls canvas-card font, left-list font, dark/light/system theme, and current in-game rail tier.

Planner app extension points:

- service contracts live in `src/StarRupturePlanner/Services/Contracts.cs`
- file-backed scheme storage inherits from an abstract document-store base
- graph nodes and edges inherit from the abstract `SchemeElement`
- canvas alignment is handled by `ICanvasLayoutService`
- the WPF canvas uses a reusable dotted-grid control and snaps machine nodes to the grid

## MCP

The MCP SSE server is mounted at:

- SSE: `/mcp/sse`
- Messages: `/mcp/messages/`

Tools:

- `search_items`
- `get_item_detail`
- `refresh_dataset`
- `get_dataset_meta`
