# StarRupture Resource Data Platform

Local StarRupture production data, planner API, MCP server, and Windows desktop planner.

The repository has two main parts:

- `starrupture_api/`: Python data service that reads/writes the local SQLite dataset, exposes HTTP JSON endpoints, and mounts an MCP SSE server.
- `src/StarRupturePlanner/`: .NET 8 WPF desktop app for building production schemes against that local API.

## Requirements

- Windows for the WPF desktop app.
- .NET 8 SDK for building and running `StarRupturePlanner`.
- Release zip, portable EXE, and installer users do not need Python; the manual release workflow bundles the API as `api\StarRuptureApi.exe`.
- Python 3.11+ is recommended only for source/API development.
- Python packages used by the source API server: `uvicorn`, `starlette`, and `mcp`.

There is currently no checked-in Python dependency manifest. For a fresh environment:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
python -m pip install uvicorn starlette mcp
```

## Data And API

The Python service stores data in `data/starrupture.sqlite3` and serves cached image assets from:

- `data/assets/items`
- `data/assets/buildings`

Refresh the local dataset from `starrupture.tools`:

```powershell
python -m starrupture_api.main refresh
```

Run the HTTP API and MCP SSE server:

```powershell
python -m starrupture_api.main serve --host 127.0.0.1 --port 8010
```

Inspect one item payload from the command line:

```powershell
python -m starrupture_api.main item rotor
```

The HTTP app is a Starlette app created by `starrupture_api.http_app:create_app`. It uses one shared `ResourceService`, so HTTP routes and MCP tools read the same SQLite dataset and localization files.

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
- `POST /api/admin/refresh`
- `GET /assets/items/{filename}`
- `GET /assets/buildings/{filename}`

Supported language codes are normalized by the API; current localization files live in `data/localization`.

`GET /api/items/{item_id}` returns the item, unlock relationships, producers, consumers, and refresh metadata.

Planner endpoints are graph-oriented for the desktop app:

- `catalog` returns buildings, recipes, active ports, rates, corporation unlocks, images, localization metadata, and transport tier config.
- `suggestions` returns compatible preselected recipes for drag-release connection popovers.
- `transport-tiers` reads `data/transport_tiers.json`; if tiers are missing, the planner can still run and show missing transport recommendations.

## MCP Server

The MCP server is mounted into the same Starlette app:

- SSE endpoint: `http://127.0.0.1:8010/mcp/sse`
- Message endpoint: `http://127.0.0.1:8010/mcp/messages/`

Available MCP tools:

- `search_items(query, limit = 20, language = "en")`
- `get_item_detail(item_id, language = "en")`
- `refresh_dataset()`
- `get_dataset_meta()`
- `list_corporations(language = "en")`
- `get_corporation_detail(corporation_id, language = "en")`

Use the MCP server when an AI client needs StarRupture production facts without manually calling HTTP endpoints. Use the HTTP API when building UI, scripts, or direct integrations.

## Desktop Planner

Run from source:

```powershell
dotnet run --project src\StarRupturePlanner\StarRupturePlanner.csproj
```

The planner starts the local API automatically on `127.0.0.1:8010` when needed. Release packages prefer the bundled API executable:

```powershell
api\StarRuptureApi.exe serve --host 127.0.0.1 --port 8010
```

Source/development layouts fall back to searching upward from the app directory and current working directory until `starrupture_api` is found, then start:

```powershell
python -m starrupture_api.main serve --host 127.0.0.1 --port 8010
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

Restore and build the WPF app:

```powershell
dotnet restore src\StarRupturePlanner\StarRupturePlanner.csproj
dotnet build src\StarRupturePlanner\StarRupturePlanner.csproj
```

Build release:

```powershell
dotnet build src\StarRupturePlanner\StarRupturePlanner.csproj -c Release
```

Publish a self-contained Windows x64 build:

```powershell
dotnet publish src\StarRupturePlanner\StarRupturePlanner.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish\StarRupturePlanner-win-x64
```

Manual release packages include a bundled API executable. Local source publishes still need Python if you run the published WPF app outside the release package layout.

## Manual GitHub Release

The repository includes a manual GitHub Actions workflow at `.github/workflows/manual-wpf-release.yml`.

To publish a desktop app build:

1. Open GitHub Actions.
2. Select `Manual WPF Release`.
3. Click `Run workflow`.
4. Enter a version/tag such as `v0.2.7-alpha`.
5. Keep `draft` enabled if you want to review the release before publishing it.

The workflow runs on `windows-latest`, restores/builds the .NET 8 WPF project, runs the planner test harness, builds the Python API into `api\StarRuptureApi.exe` with PyInstaller, publishes a self-contained `win-x64` package, builds a Windows installer with Inno Setup, builds a portable self-extracting EXE with 7-Zip SFX, and uploads the zip, portable EXE, installer, and SHA256 files as workflow artifacts.

Desktop versions are alpha builds while the planner is being validated against StarRupture `0.2.7`. Use tags such as `v0.2.7-alpha`. If the manual workflow receives `v0.2.7`, it still publishes the desktop as `0.2.7-alpha`.

When `create_github_release` is enabled, it also creates a GitHub Release for the entered tag. If the release already exists, it uploads the new zip, portable EXE, installer, and hash files with `--clobber`.

All release app artifacts contain:

- the published WPF app
- `api\StarRuptureApi.exe`
- `data/`
- `README.md`

The release package is intended to run out of the box on Windows without installing Python or Python packages. The WPF app prefers the bundled API executable and only falls back to `python -m starrupture_api.main ...` for source/development layouts.

Download choices:

- `StarRupturePlanner-v0.2.7-alpha-win-x64-Setup.exe`: normal Windows installer.
- `StarRupturePlanner-v0.2.7-alpha-win-x64-Portable.exe`: single-file, no-install launcher that extracts to a temporary folder and starts the app.
- `StarRupturePlanner-v0.2.7-alpha-win-x64.zip`: portable folder for manual extraction.

The installer artifact is named like:

```text
StarRupturePlanner-v0.2.7-alpha-win-x64-Setup.exe
```

It installs per user into:

```text
%LOCALAPPDATA%\Programs\StarRupture Planner
```

This avoids an admin prompt and keeps the bundled `data` folder writable for refreshes. The installer creates a Start Menu shortcut, optionally creates a desktop shortcut, and registers a standard Windows uninstaller.

The portable EXE is intended for quick no-install use. Because it self-extracts when launched, persistent user files such as schemes, settings, and logs still live in the normal user locations listed in the Desktop Planner section. If you need refreshed bundled dataset files to remain beside the app between launches, use the installer or extracted zip.

## Tests

Run Python tests:

```powershell
python -m unittest discover -s tests
```

Run the .NET planner test harness:

```powershell
dotnet run --project tests\StarRupturePlanner.Tests\StarRupturePlanner.Tests.csproj
```

## Project Map

- `starrupture_api/config.py`: default paths, host/port, source URLs.
- `starrupture_api/main.py`: CLI entrypoint for refresh, serve, and item inspection.
- `starrupture_api/http_app.py`: Starlette HTTP routes and static asset mounts.
- `starrupture_api/mcp_app.py`: MCP tool definitions and SSE app.
- `starrupture_api/service.py`: read/query layer over SQLite and localization.
- `starrupture_api/scraper.py`: source-site scraper and asset downloader.
- `src/StarRupturePlanner/App.xaml.cs`: WPF composition root and crash handlers.
- `src/StarRupturePlanner/MainWindow.xaml`: main planner UI.
- `src/StarRupturePlanner/ViewModels`: planner view models and commands.
- `src/StarRupturePlanner/Services`: app services, API client, scheme storage, layout, calculations.
- `src/StarRupturePlanner/Models`: planner catalog, settings, and scheme document models.
