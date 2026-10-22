# SealBreaker

Dalamud plugin that automates the Grand Company seal/gil farming loop:

1. Runs a dungeon or trial of your choice via Theseus, AutoDuty or ADS (Duty Support/Trust — works on free trial accounts)
2. Teleports to your Grand Company via Lifestream (or your GC's aetheryte tickets)
3. Turns in armor loot to Expert Delivery for seals (with blacklist/whitelist filter)
4. Spends seals on your priority buy list at the GC shop (Duck Bones, Kingcakes, aetheryte tickets, anything)
5. Loops indefinitely until stopped

Other farm modes: a **tomestone relic farm** (spend capped tomestones on Phantom Village arcanite with per-item keep amounts), **job leveling** (round-robin through your jobs with Charon), and a **Moogle tomestone farm** (synced trials, solo or as a LAN group over the Daedalus relay, with repair holds and tomestones-per-hour tracking).

Extras: automatic repairs (mender route or ADS), materia extraction, Kingcake desynth EV tracking with live Universalis prices, per-run clear-time stats, a compact mini widget, and an edge-of-screen tab to minimize to.

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

Updates are delivered through the same repo automatically. The same URL also serves [Daedalus](https://github.com/ofnature/Daedalus), [Charon](https://github.com/ofnature/Charon), [Theseus](https://github.com/ofnature/Theseus) and [Ariadne](https://github.com/ofnature/Ariadne).

## Requirements

SealBreaker drives other plugins over IPC. You need **one duty runner**, **one movement provider**, and a teleporter — pick each in General and the Duty section. The Dashboard shows a live ✓/✗ chip for every one it expects.

**Duty runner** (one of):
- [Theseus](https://github.com/ofnature/Theseus) — from the same repo as SealBreaker; dungeons and trials
- [AutoDuty](https://github.com/ffxivcode/AutoDuty)
- ADS (AI Duty Solver) — Duty Support dungeons only

**Movement** (one of):
- [Ariadne](https://github.com/ofnature/Ariadne) — from the same repo as SealBreaker; cached navmeshes shared across a fleet
- [vnavmesh](https://github.com/awgil/ffxiv_navmesh)

**Teleports:**
- [Lifestream](https://github.com/NightmareXIV/Lifestream), or turn on **Teleport with GC aetheryte tickets** in Farm settings to use your GC's tickets instead

**Optional:**
- [Charon](https://github.com/ofnature/Charon) — gear equipping and job switching for the leveling mode (SealBreaker falls back to the game's recommended-gear flow without it)
- [Daedalus](https://github.com/ofnature/Daedalus) — the LAN relay for Moogle farm groups

---

## Usage

- `/seal` or `/sealbreaker` — open the plugin window; `/seal mini` for the compact widget
- The title-bar minimize button drops to the mini widget, or to a tab on the screen edge with **Minimize to an edge tab** on (General)
- Pick your farm mode under Farm settings, and the duty and runner under Duty; Duty Support/Trust needs no other players
- Set up your buy list (Buy list — the Duck Bones preset is the classic gil loop)
- Click **Start farm** — the Setup guide has a first-run checklist
- **Stop farm** halts cleanly at any time; the flag button stops after the current run instead

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
├── Plugin.cs                      — Entry point, /seal + /sealbreaker commands, window modes
├── Configuration.cs               — Serialized settings
├── Services/
│   ├── Service.cs                 — Dalamud service locator
│   ├── IpcManager.cs              — Theseus / AutoDuty / ADS / vnavmesh / Ariadne / Lifestream / Charon / Daedalus IPC wrappers
│   ├── FarmController.cs          — State machine (the actual farm logic, all farm modes)
│   ├── AutoDutyCatalog.cs         — Dungeon and trial catalog for the AutoDuty and Theseus runners
│   ├── DutySupportCatalog.cs      — Duty Support catalog for the ADS runner
│   ├── RelicFarmCatalog.cs        — Arcanite shop catalog for the tomestone relic farm
│   ├── RepairHoldRelay.cs         — Moogle group repair holds over the Daedalus LAN relay
│   ├── CharonLevelingClient.cs    — Charon job levels / switching for the leveling mode
│   ├── GcShopCatalog.cs (+ resolvers) — GC exchange sheet data and buy-entry defaults
│   ├── GcNavRoutes.cs             — Baked town navigation routes
│   ├── BuyListTransfer.cs         — Buy-list export/import
│   ├── KingcakeDesynth.cs / DesynthTracker.cs — Desynth EV and stats
│   └── UniversalisClient.cs       — Live market prices
└── Windows/
    ├── MainWindow.cs              — ImGui UI (sidebar + banner shell)
    ├── MiniWindow.cs              — Compact status widget
    ├── Dock/                      — Edge dock (tab on the screen edge; layout shared with Daedalus and Charon)
    └── UiTheme.cs                 — Palette and style helpers
```
