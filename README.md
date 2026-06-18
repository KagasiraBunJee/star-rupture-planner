# StarRupture Production Planner

Convenient production planner for StarRupture. It helps keep supply chains, production buildings, inputs, outputs, bottlenecks, and saved schemes in one place while planning factories.

Currently the project ships as a Windows desktop planner with a bundled local API. The API also exposes an MCP server so AI agents can connect to the same production data. Later, the plan is to let MCP-connected agents help create and edit production schemes directly in the app.

## Download The App

Most users should download the Windows installer from the [StarRupture Planner releases page](https://github.com/KagasiraBunJee/star-rupture-planner/releases).

Open the latest release and download the `StarRupturePlanner-...-Installer.exe` file. The installer includes the desktop app, local API, data files, localization, and cached images, so Python is not required.

The repository has three main parts:

- `starrupture_api/`: Python data service that reads/writes the local SQLite dataset, exposes HTTP JSON endpoints, and mounts an MCP SSE server.
- `src/StarRupturePlanner.Api/`: .NET 8 local API and MCP server used by packaged desktop builds.
- `src/StarRupturePlanner/`: .NET 8 WPF desktop app for building production schemes against that local API.

## Screenshots

![Scheme builder canvas](images/scheme_builder.PNG)

Canvas comment sections:

![Canvas comment sections](images/comment_sections.PNG)

Node suggestion helper:

![Node suggestion list](images/node_suggestion_list.png)

| Resource and machine selector | Corporation level settings |
| --- | --- |
| ![Resource and machine selector](images/resource_machine_selector.PNG) | ![Corporation level settings](images/corporation_level_settings.PNG) |

## Requirements For Development

### Installed App

Most users only need:

- Windows x64.
- The release installer or manual extraction zip from GitHub Releases.

The packaged app is self-contained. It includes:

- `StarRupturePlanner.exe`, the WPF desktop planner.
- `api\StarRupturePlanner.Api.exe`, the local .NET API and MCP server.
- `data\`, including SQLite data, localization, transport tiers, and cached images.

Users do not need to install .NET, Python, SQLite, or Node.js to run the packaged app.

### Run From Source

For normal source development:

- Windows, because the desktop planner is WPF.
- .NET 8 SDK.
- The checked-in `data\` folder.

Python is not required if you run the .NET API project. Python is only needed for the legacy Python API or Python tests.

### Build Release Packages

To build the same kind of package produced by the release workflow:

- Windows x64.
- .NET 8 SDK.
- Inno Setup 6, only if building the `.exe` installer locally.
- PowerShell.

The GitHub Actions release workflow installs Inno Setup automatically. It sets up Python only for release-note tooling; Python is not used to build or run the packaged API.

## Data And API

The .NET API reads `data/starrupture.sqlite3` and serves cached image assets from:

- `data/assets/items`
- `data/assets/buildings`

The bundled dataset/API metadata is marked for StarRupture `0.2.8`.

Run the .NET HTTP API and MCP server from source:

```powershell
dotnet run --project src\StarRupturePlanner.Api\StarRupturePlanner.Api.csproj
```

The API listens on `127.0.0.1:8010` by default. Use `--port` when that port collides with another local process:

```powershell
dotnet run --project src\StarRupturePlanner.Api\StarRupturePlanner.Api.csproj -- --port 8020
```

Then open an endpoint such as:

```powershell
Invoke-RestMethod http://127.0.0.1:8010/api/items/rotor
```

The API and MCP tools use one shared resource service, so HTTP routes and MCP calls read the same SQLite dataset and localization files.

The legacy Python API remains in `starrupture_api/` for comparison work and Python tests. It is not used by the packaged app or by the planner's source-development startup path.

## HTTP Endpoints

Common endpoints:

- `GET /api/meta`
- `GET /api/items?q=rotor`
- `GET /api/items?produced=true&used=true&limit=100&offset=0`
- `GET /api/items/{item_id}`
- `GET /api/buildings`
- `GET /api/corporations`
- `GET /api/corporations/{corporation_id}`
- `GET /api/planner/catalog?lang=en`
- `GET /api/planner/suggestions?direction=input&item_id=titanium-bar&lang=en`
- `GET /api/planner/transport-tiers?lang=en`
- `GET /assets/items/{filename}`
- `GET /assets/buildings/{filename}`

Supported language codes are normalized by the API; current localization files live in `data/localization`.

`GET /api/items/{item_id}` returns the item, unlock relationships, producers, and consumers.

Planner endpoints are graph-oriented for the desktop app:

- `catalog` returns buildings, recipes, active ports, rates, corporation unlocks, images, localization metadata, and transport tier config.
- `suggestions` returns compatible preselected recipes for drag-release connection popovers.
- `transport-tiers` reads `data/transport_tiers.json`; if tiers are missing, the planner can still run and show missing transport recommendations.

## MCP Server

The MCP server is mounted into the same local API app:

- Streamable HTTP endpoint: `http://127.0.0.1:8010/mcp`
- Legacy SSE endpoint: `http://127.0.0.1:8010/mcp/sse`
- Legacy message endpoint: `http://127.0.0.1:8010/mcp/message`

The local MCP server does not require authorization, tokens, or API keys. It is intended for local agent sessions running on the same machine. If you change the API port in planner settings or with `--port`, use that port in the MCP URL too.

Available MCP tools:

- `search_items(query, limit = 20, language = "en")`
- `get_item_detail(item_id, language = "en")`
- `get_dataset_meta()`
- `list_corporations(language = "en")`
- `get_corporation_detail(corporation_id, language = "en")`

Use the MCP server when an AI client needs StarRupture production facts without manually calling HTTP endpoints. Use the HTTP API when building UI, scripts, or direct integrations.

## Desktop Planner

For source development, you can start the .NET API in one terminal:

```powershell
dotnet run --project src\StarRupturePlanner.Api\StarRupturePlanner.Api.csproj
```

Then start the WPF planner in another terminal:

```powershell
dotnet run --project src\StarRupturePlanner\StarRupturePlanner.csproj
```

The planner talks to `http://127.0.0.1:8010` by default. If that API is already running, the planner uses it.
If the configured port is busy, the planner automatically tries the next port (`8011`, then `8012`, and so on) and saves the working port for future starts.
You can also change the local API port manually in Settings -> General -> Local API.

If you start only the planner from a source checkout and no compatible API is running, the planner starts the repo-local .NET API project automatically:

```powershell
dotnet run --project src\StarRupturePlanner.Api\StarRupturePlanner.Api.csproj -- --port 8010
```

It does not start the legacy Python API.

For Visual Studio, open `StarRupturePlanner.sln`. It contains both projects:

- `StarRupturePlanner`: the WPF planner.
- `StarRupturePlanner.Api`: the .NET API and MCP server.

You can set `StarRupturePlanner` as the startup project; it will start the .NET API itself when needed. If you want to debug both processes from Visual Studio at the same time, configure multiple startup projects and start both `StarRupturePlanner.Api` and `StarRupturePlanner`.

Release packages start the bundled .NET API automatically when needed:

```powershell
api\StarRupturePlanner.Api.exe
```

You can also run the bundled API without the desktop planner:

```powershell
api\StarRupturePlanner.Api.exe --port 8020
```

If another managed API process is already listening on that port but does not match the expected current catalog shape, the app tries to stop that stale process and start the bundled or repo-local API.

Main workflow:

1. Start the planner.
2. Wait for the status bar to report that the local API/catalog loaded.
3. Drag machines, recipes, and saved scheme outputs onto the canvas.
4. Connect matching output and input ports.
5. Use the inspector to set machine counts, recipe choices, priorities, output-only nodes, and scheme outputs.
6. Save schemes and reuse marked outputs as blueprint source nodes in other schemes.

Default user files:

- Schemes: `Documents\StarRupture Planner\Schemes`
- Settings: `%LOCALAPPDATA%\StarRupture Planner\settings.json`
- App log: `%LOCALAPPDATA%\StarRupture Planner\app.log`

The Settings window controls planner language, dark/light/system theme, canvas-card font, left-list font, and current rail tier.

## Build From Sources

Restore the solution:

```powershell
dotnet restore StarRupturePlanner.sln
```

Build the solution:

```powershell
dotnet build StarRupturePlanner.sln
```

Build release configurations:

```powershell
dotnet build StarRupturePlanner.sln -c Release
```

Publish the desktop app and the bundled API into a package-like layout:

```powershell
dotnet publish src\StarRupturePlanner\StarRupturePlanner.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\StarRupturePlanner-win-x64
dotnet publish src\StarRupturePlanner.Api\StarRupturePlanner.Api.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\StarRupturePlanner-win-x64\api
Copy-Item -LiteralPath data -Destination publish\StarRupturePlanner-win-x64 -Recurse -Force
```

After publishing, the layout should contain:

- `publish\StarRupturePlanner-win-x64\StarRupturePlanner.exe`
- `publish\StarRupturePlanner-win-x64\api\StarRupturePlanner.Api.exe`
- `publish\StarRupturePlanner-win-x64\data\`

To build the installer locally, install Inno Setup 6 and run the release packaging steps in `.github\workflows\manual-wpf-release.yml`, or trigger the GitHub Actions workflow.

## Tests

Run Python tests:

```powershell
python -m unittest discover -s tests
```

Run the .NET planner test harness:

```powershell
dotnet run --project tests\StarRupturePlanner.Tests\StarRupturePlanner.Tests.csproj
```
