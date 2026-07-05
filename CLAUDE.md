# SealBreaker — Claude Code rules

Dalamud plugin (C# / .NET 10, `AllowUnsafeBlocks`, FFXIVClientStructs) that automates the seal/gil loop: duty runs → GC teleport → Expert Delivery → GC shop buys → repeat. Opened in game with `/sealbreaker`.

## Versioning (read before every release-facing change)

- Bug fixes increment the 4th digit: `1.0.0.x`.
- Major feature releases increment the 2nd digit and reset 3rd/4th: `1.x.0.0`.
- **Never increment the 3rd digit** — it is reserved.
- Current target: `1.1.0.0` once all 3 Grand Companies are confirmed working (Gridania still needs testing).
- On every bump, update **all three files together**:
  - `SealBreaker/SealBreaker.csproj` (`Version`, `AssemblyVersion`, `FileVersion` — identical four-part strings)
  - `SealBreaker/SealBreaker.json` (`AssemblyVersion`)
  - `pluginmaster.json` (`AssemblyVersion` **and** `LastUpdate` = current Unix timestamp)
- Bump `Configuration.Version` when changing serialized config shape; migrate legacy fields in `EnsureGcTownNav()`.

### GitHub release procedure

**Release on every version bump.** Users only receive code through `releases/latest` — a version bump pushed to `main` without a release ships nothing. After committing and pushing a version bump, publish the GitHub release as part of the same task (skip only if the user explicitly says to hold it).

`pluginmaster.json` points at `releases/latest/download/SealBreaker.zip`, so publishing = making a new latest release:

1. Build Release; zip **flat** (no folder) from `SealBreaker/bin/Release/net10.0-windows/`: `SealBreaker.dll`, `SealBreaker.json`, `icon.png`, `banner.png`, `CinzelDecorative-Bold.ttf` → `SealBreaker.zip`.
2. Tag `v<version>` (e.g. `v1.0.0.91`) on the release commit and push the tag.
3. Create the release named `v<version>` with the tag and upload `SealBreaker.zip` as the asset. No `gh` CLI on this machine — use the GitHub REST API with the token from `git credential fill` (works from Git Bash; PowerShell 5.1 mangles the stdin handshake).

## Project layout

- `SealBreaker/Plugin.cs` — entry, `/sealbreaker` command, config load
- `SealBreaker/Configuration.cs` — serialized settings (`IPluginConfiguration`)
- `SealBreaker/Services/FarmController.cs` — **main state machine (~6k lines)**; most farm logic lives here
- `SealBreaker/Services/IpcManager.cs` — AutoDuty, ADS, vnavmesh, Lifestream IPC (always gate on `*Available` before calls)
- `SealBreaker/Services/AutoDutyCatalog.cs` — all-dungeon catalog for the AutoDuty runner (any dungeon + duty mode)
- `SealBreaker/Services/DutySupportCatalog.cs` — Duty Support dungeon catalog for the ADS runner (Mistwake fallback, territory 1314)
- `SealBreaker/Services/GcShopCatalog.cs`, `GcExchangeItemResolver.cs`, `GcShopCategoryResolver.cs`, `GcShopDefaults.cs` — GC exchange sheet/catalog and buy-entry defaults
- `SealBreaker/Services/GcNavRoutes.cs`, `MaelstromZone128Nav.cs` — GC/repair nav waypoints
- `SealBreaker/Windows/MainWindow.cs` — ImGui UI (tabs: Farm, Config, Buy List, Desynth, Stats, GC Towns, Setup Guide); title-bar button minimizes to the mini widget
- `SealBreaker/Windows/MiniWindow.cs` — compact status widget (state, run progress, Start/Stop, expand); `/seal mini` toggles it, `Configuration.MiniModeActive` remembers the last-used mode
- `SealBreaker/Windows/UiTheme.cs` — palette + style helpers (cards, chips, metric cells, solid buttons). Use these instead of raw `PushStyleColor` for new UI.

Build: `dotnet build SealBreaker/SealBreaker.csproj` (Dalamud DLLs from `%APPDATA%\XIVLauncher\addon\Hooks\`, default `15.0.2.0`).
**Always build both configurations** after changes: `-c Debug` (loaded in game as the dev plugin) **and** `-c Release` (ships in the release zip).

**Encoding warning:** source files are UTF-8 without BOM and contain em-dashes/arrows. Do not round-trip them through PowerShell 5.1 `Get-Content`/`Set-Content` (ANSI default mangles them). Use targeted editor tools or .NET `[IO.File]` APIs with explicit UTF-8.

## External plugins (required for full farm)

- **AutoDuty** or **ADS** (`Configuration.DutyRunner`: 0 = AutoDuty, 1 = ADS)
  - AutoDuty: duty from `AutoDutyCatalog` (`AutoDutyTerritoryType`), duty mode pushed via `AutoDuty.SetConfig("dutyModeEnum", ...)` unless mode = "keep AutoDuty setting"; path checked via `AutoDuty.ContentHasPath`
  - ADS: Duty Support duty from `DutySupportCatalog`; SealBreaker queues via `AgentDawnStory`, ADS takes over inside
- **vnavmesh** — GC/repair navigation
- **Lifestream** — teleport to GC city

Never assume IPC exists; gate on `IpcManager.*Available` and log actionable errors (e.g. "AutoDuty loaded but Run IPC missing").

## State machine (`FarmController`)

States are in `FarmState`. High-level loop:

`CheckSealSpend` → `StartDuty` → `WaitingForDutyStart` / `WaitingForDutyComplete` → (mid-cycle repeats `StartDuty`) → `WaitingForDutyExit` → GC delivery/shop states → `CheckGcLoop` / `CycleComplete` → `StartDuty`.

**Duty vs repair (do not regress):**

- **Mid duty cycle** (`_runsThisCycle > 0 && _runsThisCycle < RunsPerCycle`): `StartDuty` uses `ContinueDutyAsync()` — **no repair**, no `TotalCycles` bump, no `_runsThisCycle` reset.
- **New cycle** (run 1): `LogPreDutyGearCheck()` then `TryBeginRepairBeforeDuty()` if gear below threshold; else `StartDutyAsync()` (increments `TotalCycles`, sets `_runsThisCycle = 0`).
- `WaitingForDutyComplete` always `GotoState(StartDuty)` for the next run — never branch to repair there.
- `TryBeginRepairBeforeDuty()` must return false when `InDuty()` or `IsMidDutyCycle()`.
- `ShouldRepairBetweenRuns()` must **not** require being in a GC officer zone; repair can navigate there first.
- After repair: `FinishRepair` → `StartDuty` (pre-duty check runs again).

GC officer zones: Maelstrom **128**, Twin Adder **132** (New Gridania — the Adders' Nest; 133 is Old Gridania), Immortal Flames **130** (`GcOfficerZoneId`).
Route status: Limsa and Ul'dah confirmed working; **Gridania untested**.

## GC Expert Delivery

- Dismiss officer menu when done (`GcOfficerDismissOption` / auto-dismiss config).
- Close delivery UI before starting duties (`PrepareForDutyLaunchAsync`).
- Filter modes and item IDs are config-driven; use the framework thread for UI/agent work.

## GC shop buying (fragile — follow carefully)

Wrong-item buys usually mean **wrong tab, rank, or list row**, not a single bad click.

- Resolve tabs/rank from **sheet data** via `GcExchangeItemResolver` / `GcShopCategoryResolver`, not stale config alone.
- **Never** trust a sheet row without **label match** (`ShopItemNamesMatch`); fuzzy tokens help (e.g. duck/bone).
- Row index: use per-category list logic (`ComputeCategoryListRowForPlayer`-style), not a merged UI list index.
- Seal costs: resolve from sheet when possible; defaults in `GcShopDefaults` (Duck Bones **600**, port aetheryte tickets **2000**, Materiel tab **2**).
- Presets: `GcShopDefaults.CreateDuckboneBuyEntry` / `CreatePortTicketBuyEntry`; catalog via `GcShopCatalog.EnsureInitialized()`.
- Buy list: priority entries with `KeepAmount`; per-entry shop tuning lives in the Buy List tab row editor — avoid expanding config surface unless needed.

## Configuration conventions

- `GrandCompanyIndex`: 0 Maelstrom, 1 Twin Adder, 2 Immortal Flames (auto-detected unless override enabled)
- `GcTownNav[]`: per city (0 Limsa, 1 Gridania, 2 Ul'dah) — mender, repair threshold, nav waypoints
- AutoDuty duty selection: `AutoDutyTerritoryType` / `AutoDutyContentFinderConditionId` / `AutoDutyDutyName` / `AutoDutyDutyMode` (0 keep, 1 Support, 2 Trust, 3 Regular, 4 Squadron)
- ADS duty selection: `AdsDutySupport*` fields

## UI conventions (`MainWindow` + `UiTheme`)

- Wrap window drawing in `using var theme = UiTheme.Begin();` (already done in `Draw`).
- Cards: `using (UiTheme.Card()) { ... }` — draw-list channel trick, do **not** nest cards.
- Status/plugin indicators: `UiTheme.Chip(FontAwesomeIcon..., text, color)`; icon-only buttons: `ImGuiComponents.IconButton`.
- Big Start/Stop: `UiTheme.StartButton` / `StopButton`; metric cells: `UiTheme.MetricCell` inside a `##farmMetrics`-style table.
- Colors come from `UiTheme` (Accent gold, Green/Red/Yellow/Gray); the legacy `Col*` constants in `MainWindow` remain for old sections — prefer `UiTheme` in new code.

## Coding standards for this repo

- **Minimal diffs** — `FarmController` is large; extend existing patterns instead of new abstractions.
- UI/game state: `Service.Framework.RunOnFrameworkThread` for agents/addons; async farm tasks use `GotoStateAsync`, `LogAsync`, `SetErrorAsync`.
- Prefer `unsafe` + FFXIVClientStructs only where the file already does; match existing addon names (`Repair`, `SelectString`, `SelectYesno`, etc.).
- Throttle GC UI actions with `ThrottleGcAction` / `GcActionReady()`.
- Log user-visible progress via `Log` / `Status`; warnings for recoverable timeouts (e.g. repair phase).
- Do not add tests or docs unless asked. Do not commit unless the user asks.

## Common pitfalls

| Mistake | Correct approach |
|--------|-------------------|
| Repair between runs 2..N | Only `TryBeginRepairBeforeDuty` on new cycle entry to `StartDuty` |
| `IsMidDutyCycle` using only `RunsPerCycle` | Use `_runsThisCycle > 0 && _runsThisCycle < cfg.RunsPerCycle` |
| Repair gated on zone 128 only | Navigate to GC zone first; don't skip repair because player is in dungeon zone |
| Shop buy by blind `ListRow` | Verify visible label; scroll/list scan when needed |
| AutoDuty vs ADS | Branch on `cfg.DutyRunner`; inside duty use `AdsStartDutyFromInside` / resume |
| Hardcoding Mistwake/1314 | Use `AutoDutyCatalog.SelectedOrDefault` / `DutySupportCatalog.SelectedOrDefault` |

## When changing behavior

1. Trace the `FarmState` case and any `_currentTask` async method it starts.
2. Consider test modes: `_repairTestMode`, `_deliveryTestMode`, `_shopTestMode`, `_extractTestMode` on the Farm tab.
3. Build after edits; in-game verify duty mid-cycle, pre-duty repair, and one GC shop buy with log labels.
