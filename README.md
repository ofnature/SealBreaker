# SealBreaker

Dalamud plugin that automates the Grand Company seal/gil farming loop:

1. Runs a dungeon of your choice via AutoDuty or ADS (Duty Support/Trust — works on free trial accounts)
2. Teleports to your Grand Company via Lifestream
3. Turns in armor loot to Expert Delivery for seals (with blacklist/whitelist filter)
4. Spends seals on your priority buy list at the GC shop (Duck Bones, Kingcakes, aetheryte tickets, anything)
5. Loops indefinitely until stopped

Extras: automatic repairs (mender route or ADS), materia extraction, Kingcake desynth EV tracking with live Universalis prices, and per-run clear-time stats.

---

## Installing (Dalamud)

1. In game, run `/xlsettings` → **Experimental** tab
2. Under **Custom Plugin Repositories**, add:
   ```
   https://raw.githubusercontent.com/ofnature/Daedalus/main/repo.json
   ```
3. Click the **+** then **Save and Close**
4. Open `/xlplugins`, search for **SealBreaker**, and install
5. Open the window with `/seal` (or `/sealbreaker`)

Updates are delivered through the same repo automatically. The same URL also serves [Daedalus](https://github.com/ofnature/Daedalus) and [Charon](https://github.com/ofnature/Charon).

## Requirements

Install these from their own repos before starting a farm:

- [AutoDuty](https://github.com/ffxivcode/AutoDuty) **or** ADS (AI Duty Solver) — pick the duty runner in Config
- [vnavmesh](https://github.com/awgil/ffxiv_navmesh) — town navigation
- [Lifestream](https://github.com/NightmareXIV/Lifestream) — teleports

The Farm tab shows a live ✓/✗ status chip for each.

---

## Usage

- `/seal` or `/sealbreaker` — open the plugin window
- Pick your dungeon and duty mode (Config tab → AutoDuty Dungeon); Duty Support/Trust needs no other players
- Set up your buy list (Buy List tab — Duck Bones preset is the classic gil loop)
- Click **Start farm** — the Setup Guide tab has a first-run checklist
- **Stop farm** halts cleanly at any time

### Finding item IDs

Hover any item in your inventory and run `/xldata items` in chat — it prints the item ID. Add it to the filter list in the plugin UI.

---

## Building from source

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- XIVLauncher installed (provides the Dalamud DLLs)

### Build steps

```bash
git clone https://github.com/ofnature/SealBreaker
cd SealBreaker
dotnet build SealBreaker/SealBreaker.csproj
```

The `.csproj` picks up Dalamud DLLs from `%APPDATA%\XIVLauncher\addon\Hooks\` (versioned folders, e.g. `15.0.2.0`). Override with `-p:DalamudLibPath=...` if yours lives elsewhere.

### Installing for development

1. Build in Debug configuration
2. In-game: `/xlsettings` → Developer Mode on → `/xlplugins` → Dev Plugin Locations → add the `bin/Debug/net10.0-windows/` folder
3. Enable SealBreaker in the plugin list

---

## Project structure

```
SealBreaker/
├── Plugin.cs                      — Entry point, /seal + /sealbreaker commands
├── Configuration.cs               — Serialized settings
├── Services/
│   ├── Service.cs                 — Dalamud service locator
│   ├── IpcManager.cs              — AutoDuty / ADS / vnavmesh / Lifestream IPC wrappers
│   ├── FarmController.cs          — State machine (the actual farm logic)
│   ├── AutoDutyCatalog.cs         — Dungeon catalog for the AutoDuty runner
│   ├── DutySupportCatalog.cs      — Duty Support catalog for the ADS runner
│   ├── GcShopCatalog.cs (+ resolvers) — GC exchange sheet data and buy-entry defaults
│   ├── GcNavRoutes.cs             — Baked town navigation routes
│   ├── KingcakeDesynth.cs / DesynthTracker.cs — Desynth EV and stats
│   └── UniversalisClient.cs       — Live market prices
└── Windows/
    ├── MainWindow.cs              — ImGui UI
    └── UiTheme.cs                 — Palette and style helpers
```
