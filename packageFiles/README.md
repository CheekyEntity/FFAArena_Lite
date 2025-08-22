# FFA Arena Lite

A simple, lightweight Free‑For‑All (FFA) mode for Mage Arena. Install it, play with friends, and the last player standing wins.

## What you get
* **Free‑For‑All rules** – Everyone vs everyone. Last player standing wins.
* **Stable end screen + stats** – Uses the game's victory/defeat UI and scoreboard.
* **Safer spawns** – Helps avoid bad or missing spawn points.
* **Lightweight** – Minimal changes, designed to be compatible.

## How to play
1. Host or join a game as usual.
2. Make sure everyone (host and players) has this mod installed.
3. Start a match and fight until one player remains.
4. Everyone will see victory/defeat and the stats screen.

## Features
* **Clean flow** – Lobby → Fight → Victory/defeat + stats.
* **Safer spawns** – Fewer null/overlap spawns.
* **Built‑in networking only** – No extra lobby setup needed.

## Requirements
* BepInEx 5 (BepInExPack)
* ModSync (required – this mod uses ModSync "all", so everyone must have it)

Installing with Thunderstore Mod Manager (or r2modman) will automatically install these for you.

## Installation
### Easiest (Thunderstore Mod Manager or r2modman)
1. Open your mod manager and select Mage Arena.
2. Search for "FFA Arena Lite" and click Install.
3. Launch the game from the mod manager.

### Manual (advanced users)
1. Install BepInExPack for Mage Arena.
2. Download FFA Arena Lite from Thunderstore.
3. Extract into your game folder, keeping the BepInEx folder structure (place the DLL under `BepInEx/plugins/FFAArena_Lite/`).
4. Make sure ModSync is also installed.

## Configuration
* Initial lives per player can be set in BepInEx config (section "FFA"). Default is tuned for last‑player‑standing.

## Compatibility
* All players (and the host) must have the mod (enforced by ModSync).
* Designed to be low‑impact and compatible with most content/visual mods.

## Notes
* Late joiners may briefly show inaccurate lives until new events sync. Eliminations still resolve correctly.

## Support
* Questions or issues: GitHub (see manifest) or Thunderstore comments.
* Discord: cheekyentity

Have fun!
