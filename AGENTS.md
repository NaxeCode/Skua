# Skua Agent Guide

## Project Overview

Skua is a C#/.NET AQW client and scripting environment. This fork adds an Avalonia Linux path that runs AQW through a separate Electron 8 + PPAPI Flash sidecar and bridges Flash `ExternalInterface` calls back into C# over localhost WebSocket RPC.

Primary areas:

- `Skua.Core/` — script engine, services, game abstractions, shared runtime logic.
- `Skua.Core.Interfaces/` — script and service contracts used by scripts/UI.
- `Skua.Core.Models/` — item, quest, faction, settings, and other DTOs.
- `Skua.App.Avalonia/` — Avalonia client app, Linux Flash bridge, Linux file dialogs, main UI.
- `Skua.Manager.Avalonia/` — Avalonia manager/launcher UI.
- `Skua.Shared.Avalonia/` — shared Avalonia controls, themes, and view models.
- `Skua.App.WPF/`, `Skua.WPF/`, `Skua.Manager/` — existing Windows/WPF path.
- `Skua.AS3/` — ActionScript client SWF source; output expected at `Skua.AS3/skua/bin/skua.swf`.
- `tools/linux-flash-host/` — Electron Flash sidecar (`main.js`, `skua.html`, `package.json`).
- `Skua.App.Avalonia.Tests/` — xUnit tests for Linux Flash host/bridge helpers.
- `docs/linux-flash-host-plan.md` — Linux Flash architecture notes and diagnostics.

## Current Branching/Release Context

- Main fork: `NaxeCode/Skua`.
- Upstream: `auqw/Skua`.
- Linux work was merged into fork `master` and is also in `linux-flash-host`.
- Upstream PR exists from `NaxeCode:linux-flash-host` to `auqw:master`.
- Linux release artifacts are generated locally under `releases/` and uploaded to GitHub Releases on the fork.

## Build/Test Commands

Prefer short, single-process commands to avoid file locks:

```bash
dotnet test Skua.App.Avalonia.Tests/Skua.App.Avalonia.Tests.csproj -v:minimal -m:1 --no-restore
dotnet build Skua.App.Avalonia/Skua.App.Avalonia.csproj -v:minimal -m:1 --no-restore
```

If restore may be needed:

```bash
dotnet restore Skua.sln
dotnet test Skua.App.Avalonia.Tests/Skua.App.Avalonia.Tests.csproj -v:minimal -m:1
dotnet build Skua.App.Avalonia/Skua.App.Avalonia.csproj -v:minimal -m:1
```

Linux dev run:

```bash
export SKUA_ELECTRON_BIN=/path/to/electron-8/electron
export SKUA_FLASH_PLUGIN=/path/to/libpepflashplayer.so
./scripts/dev-linux-skua.sh
```

Package Linux release artifact:

```bash
./scripts/package-linux-skua.sh
```

Local/private package with local Electron/Flash runtime files:

```bash
SKUA_PACKAGE_LOCAL_RUNTIME=1 ./scripts/package-linux-skua.sh
```

## Important Runtime Variables

Linux Flash host:

```bash
SKUA_ELECTRON_BIN=/path/to/electron-8/electron
SKUA_FLASH_PLUGIN=/path/to/libpepflashplayer.so
SKUA_SWF_PATH=/path/to/skua.swf
SKUA_FLASH_TRACE=1
SKUA_FLASH_TRACE_PAYLOADS=1
SKUA_FLASH_TRACE_PATH=/tmp/skua-linux-flash-trace.log
```

`SKUA_FLASH_TRACE=1` enables verbose bridge logs. Error/timeout/crash/close events should remain logged even when verbose trace is disabled.

## Linux Flash Bridge Architecture Rules

- Do not patch or extract the Artix Launcher; copy architecture only.
- Electron sidecar is only the Flash runtime/game window.
- C# owns proxying, transport, diagnostics, and Skua runtime state.
- Keep protocol localhost-only.
- Keep Windows ActiveX Flash behavior guarded behind `IS_WINDOWS` paths.
- Do not commit proprietary/local runtime blobs:
  - `tools/linux-flash-host/electron8/`
  - `tools/linux-flash-host/plugins/`
  - `tools/linux-flash-host/node_modules/`
  - `tools/linux-flash-host/package-lock.json`
- `Game*.swf` is patched in memory only to change the secure `SharedObject.getLocal("AQWChars", "/", true)` flag to false. Do not patch title/background SWFs.
- Do not execute Flash callbacks on the WebSocket receive loop. Queue them off-loop to avoid reentrancy deadlocks.
- C# -> Flash RPCs should stay serialized unless there is a measured reason and tests for concurrent behavior.
- JS Flash -> C# callbacks should be deferred/queued outside the Flash `ExternalInterface` call stack.
- Direct string packet sending should use the AS3 `sendPacket(packet:String)` bridge path.

## Diagnostics

Useful logs/paths:

- `/tmp/skua-live.log` — launcher/app capture if used by local launcher.
- `/tmp/skua-linux-flash-trace.log` — Linux Flash bridge trace default.
- `/tmp/skua-breadcrumb.log` — legacy mirror path for important bridge events.
- `/tmp/skua-script-error.log` — only when `SKUA_SCRIPT_ERROR_LOG=1`.
- `/tmp/skua-script-smoke.log` — smoke script output.

When debugging Linux Flash:

1. Reproduce with `SKUA_FLASH_TRACE=1`.
2. Add `SKUA_FLASH_TRACE_PAYLOADS=1` only when payload details are needed.
3. Correlate `call`, `result`, `flashCall`, `timeout`, `pending-failed`, and socket close events.
4. Check Electron renderer console, network, plugin crash, renderer crash, and window close logs.

## Coding Conventions

- Use nullable-aware C# (`<Nullable>enable</Nullable>` in projects).
- Central package versions live in `Directory.Packages.props`; add package versions there, not inline unless necessary.
- Prefer dependency injection through `AppStartup/Services.cs`.
- Keep UI-specific code in app projects (`Skua.App.Avalonia`, `Skua.Manager.Avalonia`, WPF projects), not in `Skua.Core`.
- Keep script-facing contracts in `Skua.Core.Interfaces`.
- Keep DTOs/models in `Skua.Core.Models`.
- Add `InternalsVisibleTo` only when tests need internal helpers.
- Avoid noisy default logging; gate verbose logs behind env vars/options.
- Preserve existing public script APIs where possible.
- Prefer small tests for serialization, XML codecs, SWF patching, and transport state machines.

## AQW Script Safety Rules

When creating scripts for the user:

- Use tiny, focused scripts under `~/.config/Skua/Scripts/` unless explicitly adding repo sample scripts.
- Avoid `CoreBots.cs` includes when testing Linux bridge basics; CoreBots can contain Windows-only sections.
- Do not route XP farming back to `/join battlegrounde` unless requested; current preference is `/join shadowbattleon`.
- Do not use “Early Autopsy” for ShadowBattleon XP unless user confirms it appears.
- Do not automate risky combat/farming unless the user explicitly asks for that script.
- For Doomkitten, avoid Lucky/Spiral Carve/crit-heavy setups; current plan is Dragon of Time because it cannot crit.
- Keep guidance small: exact `/join`, build, rotation, what to check, what to report back.

## Known Local User Context

- User launches the Linux client with `skua`.
- Official `aqw` alias must remain untouched.
- Skua launcher env typically sets:
  - `SKUA_ELECTRON_BIN="$repo/tools/linux-flash-host/electron8/electron"`
  - `SKUA_FLASH_PLUGIN="$repo/tools/linux-flash-host/plugins/libpepflashplayer.so"`
  - `SKUA_SWF_PATH="$repo/Skua.AS3/skua/bin/skua.swf"`
- Hyprland Flash sidecar window class: `skua-linux-flash-host`; title: `Skua AQW Flash Host`.

## Manual Validation Checklist

For Linux Flash changes:

1. `dotnet test Skua.App.Avalonia.Tests/Skua.App.Avalonia.Tests.csproj -v:minimal -m:1 --no-restore`
2. `dotnet build Skua.App.Avalonia/Skua.App.Avalonia.csproj -v:minimal -m:1 --no-restore`
3. Launch with `skua` or `./scripts/dev-linux-skua.sh`.
4. Confirm Flash host opens.
5. Confirm AQW loads and emits `loaded`.
6. Confirm login/server selection works.
7. Confirm basic script load/start works.
8. Confirm app exit kills sidecar.

Do not run or restart the live app if the user says Skua is currently running and should not be disturbed.

## Git/PR Workflow

- Check current branch and status before edits:

```bash
git status --short
git branch --show-current
```

- Do not include ignored blobs or local runtime assets in commits.
- Generated release archives under `releases/` are ignored; upload them through `gh release` if needed.
- If modifying workstation config outside this repo, follow `~/AGENTS.md`/chezmoi workflow.
- For upstream changes, push to `NaxeCode/Skua` fork and open/update PR against `auqw/Skua`.
- Agent usually lacks permission to merge upstream PRs.

## Pitfalls

- `master` and older upstream branches may differ from `avalonia`; verify project presence before rebasing/retargeting.
- Full solution builds may touch many Windows/WPF projects; prefer targeted Avalonia builds/tests on Linux.
- Electron 8 can fail with modern global Node flags; launcher/dev script should `unset NODE_OPTIONS`.
- Bank UI blank after `/join bank` is a known issue tracked in fork issue #1.
- Build/test commands can write to `bin/`/`obj/`; ask before building if the user has the app running from the same working tree.

## Script Logging / Telemetry Architecture

Current phase status:

- Phase 1 — durable script logs: implemented and targeted build passed.
- Phase 2 — script lifecycle run folders: implemented and targeted build passed.
- Phase 3 — automatic runtime sampler: implemented and targeted build passed.
- Phase 4 — script cleanup/API hook hardening: implemented; targeted build passed.
- Phase 5 — reward/packet parsing and simple latest-run report: implemented; targeted build passed.

Engine-level script telemetry now lives in:

```text
Skua.Core.Interfaces/Services/IScriptRunTelemetryService.cs
Skua.Core/Services/ScriptRunTelemetryService.cs
```

Lifecycle/logging hooks:

- `Skua.Core/Scripts/ScriptManager.cs` starts/stops telemetry per script run.
- `Skua.Core/Services/LogService.cs` mirrors `Bot.Log`/script logs into current run `script.log`.
- `Skua.App.Avalonia/AppStartup/Services.cs` registers `IScriptRunTelemetryService`.

Per-run output:

```text
~/.local/state/skua/logs/script-runs/<timestamp>_<script-name>/
├── script.log
├── telemetry.jsonl
└── summary.json
```

Latest-run pointers are also written at log root:

```text
~/.local/state/skua/logs/script-runs/latest-run.txt
~/.local/state/skua/logs/script-runs/latest-summary.json
```

Automatic sampler captures every ~5 seconds:

- level, XP, required XP, XP/hour estimate
- gold and gold/hour estimate
- class and class rank
- HP/MP, alive/combat state, deaths
- map/cell/pad/room, map changes
- target and alive enemies summary
- XP/rep/class/gold boost active flags

Semantic API hooks also write JSONL telemetry events for:

- `map.join.request`, `map.join.complete`, `map.join_packet`, `map.jump`
- `quest.accept.request`, `quest.accept.result`
- `quest.complete.request`, `quest.complete.result`
- `boost.timer.start`, `boost.timer.stop`, `boost.use`
- `combat.attack.name`, `combat.attack.id`, `combat.attack.player`, `combat.cancel_target`
- `skill.load_advanced.*`, `skill.timer.start`, `skill.timer.stop`, `skill.use`

Phase 5 packet/game-event telemetry adds:

- Packet reward events from `ScriptInterface` pext parsing:
  - `packet.add_gold_exp` for `addGoldExp` packets; captures typ/id/exp/gold/classPoints/rep/faction when present plus current level/XP/gold snapshot.
  - `packet.quest_reward` for `ccqr` quest completion packets; captures success/questId/exp/gold/classPoints/rep/faction when present plus current level/XP/gold snapshot.
- Messenger-derived events from `ScriptRunTelemetryService`:
  - `game.player_death`, `game.monster_killed`, `game.quest_accepted`, `game.quest_turnin`
  - `game.map_changed`, `game.cell_changed`
  - `game.item_dropped`, `game.item_bought`, `game.item_sold`, `game.item_added_to_bank`

Script guidance going forward:

- Do not add manual XP/hour calculators to every script.
- Use `Bot.Log(...)` for important human-readable milestones only.
- Let engine telemetry handle XP/hour, deaths, boost uptime, map/cell, target/enemies, reward packets.
- Avoid `/tmp/*.log` for normal scripts; keep `/tmp` logs only for high-volume diagnostics like raw bank packet traces.
- Naxe XP scripts should not use manual `/tmp` file logs; use engine telemetry under `~/.local/state/skua/logs/script-runs/`.
- New local Naxe scripts should include `//cs_include Scripts/Naxe/Lib/NaxeRuntime.cs` and use `NaxeRuntime` guardrails: `ReadyWait`, option preservation, `LoadQuestDataOnce`, throttled quest accept, skill timer ensure, map/cell/death recovery, and cleanup.
- Always keep a settle buffer after `/join` and room/cell hops before quest turn-ins, skill timer restarts, or attacks. Use `Runtime.Join(...)`, `Runtime.BlockCombat(...)`, `Runtime.PrepareMapStep(...)`, and `Runtime.EnsureCombatReadyOrSleep()`.
- Do not call `Bot.Map.Jump(...)` or `Bot.Combat.Attack(...)` directly in normal scripts; use runtime methods so settle buffers and invalid-target recovery stay consistent.
- Do not call `Bot.Quests.Load(...)` inside hot loops; it can make the quest menu repeatedly open/close.
- Run `~/.config/Skua/Scripts/Naxe/Tools/naxe-script-lint.sh` after local script edits.

Build status:

```bash
dotnet build Skua.App.Avalonia/Skua.App.Avalonia.csproj -v:minimal -m:1 --no-restore
# passed after telemetry phases 1-5 and fixing pre-existing bank patch isBankApi declaration order
```

## Current AQW Progression Context

This section is gameplay/script context for local scripts under `~/.config/Skua/Scripts/`, not source repo release notes.

Communication/user preference:

- Give tiny steps and best answer first.
- For gameplay guidance, include exact `/join`, class/build, rotation, what to check, and what to report back.
- Do not dump huge guides unless user asks.
- Do not kill/restart/build over the live Skua client while user is farming without asking.

Current main goal:

1. Use active rep boost efficiently.
2. Then level from 68 to 75.
3. Get Dragon of Time.
4. Use Dragon of Time for Doomkitten.
5. Finish ArchPaladin `Proof of Valor` and continue ArchPaladin chain.
6. Keep doing Lord of Order daily once per day.

Current account/progress facts:

- Level: 68.
- Good Rank 10.
- Loremaster Rank 4 achieved; Dragon of Time Loremaster prereq done.
- Arcangrove Rank 10, Mythsong Rank 10, Brightoak Rank 10.
- StoneCrusher class is owned.
- Blade of Awe Rank 10.
- ArchPaladin progress:
  - Stone Paladin Armor bought.
  - Exalted Paladin Seal bought and used.
  - `A Strong Base` turned in.
  - Current quest: `Proof of Valor`.
  - Done: Undead Energy x1000, Binky's Uni-horn, Dreadhaven General, Desterrat Moya.
  - Remaining: Doomkitten and Vordred.
- Doomkitten issue: heals from crit-related mechanics / Curse of Blades. Avoid Lucky, Spiral Carve, and crit-heavy setups. Oracle and Pyromancer attempts were not enough. Preferred solution is Dragon of Time because it cannot crit.

Current local custom script layout:

```text
~/.config/Skua/Scripts/Naxe/
├── Classes/
├── Dailies/
├── Diagnostics/
├── Docs/
├── Rep/
└── XP/
```

Important scripts:

- `Naxe/XP/Naxe_ShadowBattleon_XP_68to75.cs` — original ShadowBattleon XP route.
- `Naxe/XP/Naxe_Level65To75_BattleGroundE.cs` — BattleGroundE XP route; first script using shared `NaxeRuntime` guardrails and currently better in short-window telemetry.
- `Naxe/Rep/Naxe_RepBoost_ExtraRanks.cs` — optional active-rep-boost route: Embersea → Evil → Yokai Rank 10.
- `Naxe/Rep/Naxe_RepWindow_LoremasterRank4.cs` — Loremaster Rank 4 helper.
- `Naxe/Rep/Naxe_RepWindow_StoneCrusherReps.cs` — StoneCrusher reps helper.
- `Naxe/Rep/Naxe_Unlock_Brightoak_GuardianMouth.cs` — unlocks Brightoak quest 4667.
- `Naxe/Classes/Naxe_GetStoneCrusher_NoFullBrightoak.cs` — StoneCrusher class helper; only needed if class missing.
- `Naxe/Classes/Naxe_GetDragonOfTime.cs` — Dragon of Time wrapper; run after level 75.
- `Naxe/Dailies/Naxe_Daily_LoO_And_DoomCheck.cs` — Lord of Order daily and `/join doom` boost check.
- `Naxe/Diagnostics/*Bank*.cs` — Linux bank diagnostics; do not use during normal farming.

Current recommended scripts/order:

- While server rep boost is active and user wants extras: `Naxe/Rep/Naxe_RepBoost_ExtraRanks.cs`.
- After rep boost: `Naxe/XP/Naxe_ShadowBattleon_XP_68to75.cs` until level 75.
- At level 75: `Naxe/Classes/Naxe_GetDragonOfTime.cs`.
- After Dragon of Time: Doomkitten, then Vordred, then continue ArchPaladin chain.

Preferred builds:

- General farming/XP/rep: Legion Evolved Dark Caster Rank 10, Full Wizard, Blade of Awe + Spiral Carve.
- Undead farming: Legion Evolved Dark Caster Rank 10, Full Wizard, Blinding Light of Destiny.
- General boss soloing: Darkblood StormKing Rank 10, Full Wizard, Blade of Awe + Spiral Carve.
- Doomkitten: do not recommend Lucky/Spiral Carve/crit-heavy setups; use Dragon of Time plan.
- StoneCrusher: useful support/boss/group class, but not preferred over LEDC for XP farming.

AQW script constraints:

- Avoid bank access/searches in scripts on Linux unless explicitly debugging; bank UI/API path is broken/blank.
- Avoid `/join battlegrounde` for XP unless user asks; ShadowBattleon is preferred.
- Do not include/use `Early Autopsy` unless user confirms it appears.
- Use Skua `AdvancedSkills` rather than custom skill loops when possible.
- Do not use `UseSkill(5)` in scripts; Skua class skills are typically `1..4`.
- Prefer inventory-only boost search (`searchBank: false`) on Linux.

Known Linux bank issue summary:

- Manual `/join bank` shows blank bank tabs.
- `POST /game/api/char/bank` returned literal `what`.
- Cookie jar forwarding did not fix it; login response had no useful AQW auth cookie, only `__cflb`.
- Login JSON contains `sToken`.
- Current hypothesis/patch in repo worktree: send login `sToken` as `ccid` header for bank API requests (`URLRequestHeader ccid api/char/` string found in AQW SWF). Patch was made but not yet rebuilt/tested because user paused restarts to farm.
