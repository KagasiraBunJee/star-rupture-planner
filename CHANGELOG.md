# Changelog

All notable changes to StarRupture Production Planner are tracked here.

The project is currently in alpha. Versions should keep the `-alpha` suffix until the project leaves alpha stage.

## 0.5.0-alpha - 2026-06-19

### Added

- Added existing-machine connection suggestions with max and free production details.
- Added persisted per-node input/output port ordering for cleaner graph routing.
- Added canvas autoscroll while dragging nodes, comments, route points, and connections near viewport edges.

### Changed

- Connection suggestions are now grouped into existing machines and new machines.
- Scheme JSON version advanced to support persisted port ordering.

## 0.4.3-alpha - 2026-06-18

### Documentation

- Added the MIT License and linked it from the README.

### Added

- Added a persisted scheme folder setting with folder selection and reset-to-default controls.
- Added scheme JSON import from the top command bar, copying selected schemes into the active schemes folder.
- Added drag-and-drop scheme JSON import onto the schemes list.

### Changed

- Renamed the top command bar scheme action from Open to Add.
- Scheme imports now prompt to replace, keep both, or cancel when a filename already exists.

## 0.4.2-alpha - 2026-06-18

### Documentation

- Added repository-specific agent instructions for project structure, build/test commands, WPF conventions, API/data conventions, Git workflow, versioning, and changelog maintenance.
- Clarified that API changes must keep MCP tools and behavior in sync.

### Internal

- Ignored local `tasks/` agent lesson files.
- Bumped the alpha patch version for the documentation/workflow update.
