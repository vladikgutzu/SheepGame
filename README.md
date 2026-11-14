# Baggage Jam

Mobile puzzle game built with Unity. The player organizes luggage on an airport conveyor system, matches pieces and colours, clears the board, and unlocks new stages and upgrades.

## Gameplay

- Grid-based puzzle board with movable luggage pieces.
- Conveyor-belt and airport check-in presentation.
- Multiple level modes, including colour-matching rules.
- Procedural and handcrafted level generation with a built-in solver.
- Boosters, tutorials, progression gates, currency, shop, and persistent saves.
- Mobile-focused UI, safe-area handling, haptics, adaptive quality, and device-specific settings.

## Technologies

| Area | Technology |
|---|---|
| Engine | Unity 6000.4.7f1 |
| Language | C# |
| Rendering | Universal Render Pipeline 17.4.0 |
| Input | Unity Input System 1.19.0 |
| Navigation | Unity AI Navigation 2.0.12 |
| UI | uGUI 2.0.0 |
| Analytics | GameAnalytics 8.3.0 |
| Monetization | Unity LevelPlay 9.5.1, local store infrastructure |
| Testing/content | Unity Recorder 5.1.7, custom Editor tools |

## Architecture highlights

- `Board`, `PuzzlePiece`, `LevelData`, `LevelGenerator`, and `LevelSolver` form the puzzle core.
- Scriptable configuration separates economy, levels, shops, and visual content from runtime logic.
- Save data and game state are isolated behind a persistence layer.
- A custom Editor toolchain builds levels, UI, terminal visuals, piece meshes, palettes, and mobile settings.
- Advertising, privacy choices, telemetry, and store services are wrapped behind project-level adapters.

## Project structure

- `Assets/_Game/Scripts/Core` — puzzle, progression, saves, economy, and app lifecycle.
- `Assets/_Game/Scripts/UI` — menus, HUD, shop, settings, tutorials, and mobile layout.
- `Assets/_Game/Scripts/Editor` — level, UI, content, and build tooling.
- `Packages` — Unity dependencies and analytics package.
- `ProjectSettings` — Unity project configuration.

## Status

The repository contains the complete Unity project snapshot used for development. Generated Unity caches and local build artifacts are excluded through `.gitignore`.
