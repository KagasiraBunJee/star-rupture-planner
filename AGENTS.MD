# Agent Instructions

This repository is the StarRupture Production Planner. It ships a Windows WPF desktop planner plus a bundled local .NET API/MCP server over the checked-in StarRupture data set.

These instructions apply to the whole repository.

## Project Map

- `StarRupturePlanner.sln` contains the two .NET projects.
- `src/StarRupturePlanner/` is the .NET 8 WPF desktop app.
- `src/StarRupturePlanner.Api/` is the .NET 8 local HTTP API and MCP server used by the packaged app.
- `tests/StarRupturePlanner.Tests/` is a console-style .NET test harness.
- `tests/test_*.py` covers the legacy Python data service.
- `starrupture_api/` is the legacy Python API kept for comparison and Python tests; it is not the packaged app runtime.
- `data/` is source data: SQLite, localization, transport tiers, and cached assets.
- `images/` contains README screenshots.
- `installer/`, `publish/`, and `.github/workflows/manual-wpf-release.yml` are release/package related.

Do not treat generated `bin/`, `obj/`, `publish/`, or Python `__pycache__/` outputs as source unless the task explicitly concerns packaging output.

## Build, Run, And Test

Use PowerShell commands from the repository root.

Restore:

```powershell
dotnet restore StarRupturePlanner.sln
```

Build:

```powershell
dotnet build StarRupturePlanner.sln
```

Run the local .NET API:

```powershell
dotnet run --project src\StarRupturePlanner.Api\StarRupturePlanner.Api.csproj
```

The API listens on `127.0.0.1:8010` by default. Use `-- --port 8020` when that port is busy.

Run the WPF planner:

```powershell
dotnet run --project src\StarRupturePlanner\StarRupturePlanner.csproj
```

The planner can start the repo-local .NET API automatically if no compatible API is already running.

Run the .NET test harness:

```powershell
dotnet run --project tests\StarRupturePlanner.Tests\StarRupturePlanner.Tests.csproj
```

Run the Python legacy tests:

```powershell
python -m unittest discover -s tests
```

For broad changes, prefer at least `dotnet build StarRupturePlanner.sln` plus the .NET test harness. Run Python tests when touching `starrupture_api/`, `data/`, or Python tests/scripts.

## C# And WPF Conventions

- Target the existing frameworks: `net8.0-windows` for the WPF app and `net8.0` for the API.
- Nullable reference types and implicit usings are enabled; keep code null-safe.
- Use file-scoped namespaces.
- Follow the existing MVVM structure:
  - `Models/` for data shapes.
  - `ViewModels/` for UI state and commands.
  - `Views/` for XAML and minimal view wiring.
  - `Services/` for app logic, persistence, calculations, API clients, and platform helpers.
- Reuse the existing `ViewModelBase`, `RelayCommand`, and `AsyncRelayCommand`. Do not add `CommunityToolkit.Mvvm` or another MVVM package without a specific request.
- Keep code-behind focused on WPF view concerns: initialization, visual tree interactions, pointer/keyboard handling, and UI-only coordination. Put business logic in services or view models.
- Keep async UI work responsive. Avoid `.Result`, `.Wait()`, and blocking the WPF dispatcher.
- Use the existing dispatcher abstraction (`IUiDispatcher`/`WpfUiDispatcher`) and background runner patterns when view models need thread coordination.
- Keep user-facing strings in the localization resource dictionaries when they are part of the WPF UI.
- Preserve the existing visual language in `Themes/ControlStyles.xaml`, `Themes/DarkTheme.xaml`, and `Themes/LightTheme.xaml`. Verify XAML changes by building; visually inspect when changing layout or interaction-heavy UI.

## API And Data Conventions

- The .NET API is the primary runtime API. It reads `data/starrupture.sqlite3`, `data/localization`, `data/transport_tiers.json`, and cached assets under `data/assets`.
- HTTP endpoints and MCP tools share `ResourceService`; keep behavior consistent between them.
- If an API contract, response shape, endpoint behavior, or exposed data changes, update the MCP tools in the same change so MCP clients receive matching behavior and schema expectations.
- Common API endpoints include:
  - `GET /api/meta`
  - `GET /api/items`
  - `GET /api/items/{item_id}`
  - `GET /api/buildings`
  - `GET /api/corporations`
  - `GET /api/planner/catalog`
  - `GET /api/planner/suggestions`
  - `GET /api/planner/transport-tiers`
  - `GET /assets/items/{filename}`
  - `GET /assets/buildings/{filename}`
- MCP endpoints are mounted at `/mcp`, `/mcp/sse`, and `/mcp/message`.
- If changing catalog shape, suggestions, transport tiers, localization, or corporation unlocks, update both API behavior and planner consumers/tests together.
- The current bundled dataset metadata is for StarRupture `0.2.8`; keep version-sensitive text and tests aligned when changing data assumptions.

## Plan Mode

Enter plan mode for **any non-trivial task** — defined as anything requiring
3+ steps or involving an architectural decision.

- **Always plan before building.** Write detailed specs upfront to reduce
  ambiguity and surface edge cases before touching code.
- **If something goes sideways — STOP.** Do not keep pushing forward.
  Re-plan immediately before continuing.
- **Use plan mode for verification too** — not just implementation. Before
  marking a task done, plan the verification steps and execute them.
- **A plan must include:**
  1. **Goal** — what outcome we're after, in one or two sentences.
  2. **Affected files** — full paths of every file that will be touched.
  3. **Step-by-step approach** — ordered actions, each small enough to
     execute and verify in isolation.
  4. **Potential risks** — what could go wrong, where the blast radius is.
  5. **Rollback strategy** — concrete steps to undo if the change misfires.

Trivial tasks (typo fix, single-line rename, dependency bump) may skip plan
mode — but still need a quick sanity check before applying.

---

## Subagent Strategy

Use subagents liberally to keep the main context window clean and focused.

- **Offload** research, exploration, and parallel analysis to subagents —
  don't pollute the main thread with it.
- **One task per subagent** for focused, reliable execution. Do not give a
  subagent multiple unrelated responsibilities.
- **For complex problems, throw more compute at it** — spawn multiple
  subagents for parallel analysis rather than doing everything serially in
  one context.
- **Use subagents for:** dependency research, reading large files,
  exploring unknown codebases, generating alternatives, running isolated
  experiments.

---

## Self-Improvement Loop

After **any correction from the user**, Agent must:

1. **Update `tasks/lessons.md`** immediately with the pattern that caused
   the mistake.
2. **Write a rule** for itself that prevents the same mistake from
   recurring.
3. **Ruthlessly iterate** on these lessons — if the same mistake happens
   twice, the rule was not specific enough. Rewrite it.
4. **Review `tasks/lessons.md` at the start of every session** for rules
   relevant to the current project before writing any code.

Lesson format:

```markdown
- [YYYY-MM-DD] **<pattern name>**: <what went wrong> → <rule to prevent it>
```

## Planner Domain Notes

- A scheme is a graph of production nodes and edges.
- Nodes can represent machines, selected recipes, output-only sources, comment sections, and saved scheme outputs.
- Edges connect matching output/input item ids and may carry route points and transport tier recommendations.
- Machine counts, recipe rates, priorities, shortages, power, temperature, corporation unlocks, and rail availability are calculated by services under `src/StarRupturePlanner/Services/`.
- Saved schemes default to `Documents\StarRupture Planner\Schemes`.
- Settings and logs default to `%LOCALAPPDATA%\StarRupture Planner\`.

When changing planner behavior, look for existing logic in `PlannerCalculator`, `ProductionAnalysisService`, `PlannerMetricService`, `PlannerEdgeService`, `PlannerUnlockService`, `SchemeMigrationService`, `SchemeStore`, and `SchemeSession` before adding new paths.

## Testing Expectations

- Add or update .NET harness tests in `tests/StarRupturePlanner.Tests/Program.cs` for planner calculations, scheme serialization/migration, API contracts, and process discovery.
- Keep tests deterministic. Use temp directories for filesystem cases and clean them up.
- For API tests that start a process, prefer available loopback ports rather than hard-coded ports.
- Python tests should stay focused on the legacy Python API and shared data expectations.
- If a change cannot be tested in the current environment, document what was not run and why.

## Git Workflow

- Every task must have a Linear issue before work starts. If the requested task does not already have one, create it in Linear, assign it to `me`, add labels based on the change context, and attach it to the relevant Planner project/milestone when known.
- Move the Linear issue to `In Progress` when implementation starts, to `In Review` when the branch is pushed and ready for review, and to `Done` only after the PR has merged.
- Every new change must be made on a separate branch unless the user explicitly asks to update an existing integration branch.
- Branch names must start with a change-type prefix, then the lowercase Linear issue id, then a short kebab-case description:
  - `feat/sta-11/short-context` for new features.
  - `fix/sta-12/short-context` for bug fixes.
  - `refactor/sta-13/short-context` for refactoring without behavior changes.
  - `chore/sta-14/short-context` for internal maintenance, dependency updates, version bumps, and housekeeping.
  - `docs/sta-15/short-context` for documentation-only changes.
  - `test/sta-16/short-context` for test-only changes.
  - `build/sta-17/short-context` for build or packaging changes.
  - `ci/sta-18/short-context` for workflow changes.
- Commit messages must use the same conventional prefix style, for example `feat: add scheme output picker`, `fix: handle stale api process`, `refactor: simplify canvas routing`, `docs: update agent workflow`.
- Keep commits focused. Do not bundle unrelated app, data, and documentation work into one commit.
- The default PR target branch is `develop` unless the user says otherwise.
- When preparing a committed change, push the branch to the remote and create a pull request to `develop` using the GitHub MCP(github_mcp) tools when available. Include a PR description with:
  - what changed,
  - how it was tested,
  - version and changelog updates,
  - any known risks or follow-up work.
- Preserve user work in the tree. Check status before committing and do not include unrelated uncommitted changes.

## Versioning And Changelog

- Bump the app version after each completed change according to SemVer.
- The project is currently in alpha, so public versions must keep the `-alpha` suffix while this stage continues.
- Update version values consistently:
  - `src/StarRupturePlanner/StarRupturePlanner.csproj`: `VersionPrefix`, `VersionSuffix`, `AssemblyVersion`, and `FileVersion`.
  - `src/StarRupturePlanner/Services/AppVersionInfo.cs`: fallback informational version, if it still hard-codes the old value.
- Use SemVer intent:
  - Patch for bug fixes, docs, tests, chores, and small compatible improvements.
  - Minor for new backward-compatible features.
  - Major only for intentional breaking changes.
- Keep `CHANGELOG.md` updated for the current version. Add a version section for each bumped version and summarize user-visible changes, fixes, internal changes, and docs where relevant.
- Changelog sections should use version headings like `## 0.4.2-alpha - YYYY-MM-DD`.

## Packaging And Releases

- Release packages are Windows x64, self-contained .NET publishes.
- The desktop app and API are published separately, then combined with `data/`.
- Installer packaging uses Inno Setup via `installer/StarRupturePlanner.iss` and `.github/workflows/manual-wpf-release.yml`.
- Do not hand-edit generated release output in `publish/` as the source of truth. Change project files, installer config, workflow steps, or source assets instead.

## General Agent Guidance

- Read nearby code before editing; this project already has many small domain services.
- Keep changes scoped to the requested behavior.
- Do not add NuGet or Python dependencies unless the user asks or approves.
- Do not rewrite data files, localization, screenshots, or generated outputs incidentally.
- Prefer `rg` for searching.
- On this machine Git may report dubious ownership for this repo. If Git status/diff is needed, use a safe-directory override for this path rather than changing global config.
- Preserve user work in the tree. Do not revert unrelated changes.
