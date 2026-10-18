# MOTrainer

[English](README.md) | [简体中文](README.zh-CN.md)

MOTrainer is an independent, unofficial, open-source offline trainer for
[Mental Omega](https://mentalomega.com/). It provides selected-unit editing,
economy and production controls, battlefield utilities, and superweapon grants
for strictly verified game builds.

> Offline use only. MOTrainer does not provide anti-cheat bypasses and should
> not be used in multiplayer environments.

## Enhanced build in this repository

This repository is an enhanced build based on the original open-source
[cmdrmo/MOTrainer](https://github.com/cmdrmo/MOTrainer) project. The original
author and project remain the upstream source.

Enhancements in this build:

- Configure credits, selected-unit attributes, and every feature toggle before
  the game or match starts.
- Queue feature toggles while the game is closed and apply them automatically
  after the verified game modules load.
- Automatically apply the configured credit value once per match without
  overwriting normal in-match spending.
- Keep selected-unit editing and feature policies active across map changes,
  menu returns, and new matches until the trainer is closed.
- Restore patches and policies that this trainer owns when it exits.
- Run the executable with the administrator privileges required to access a
  game process running at the same privilege level.
- Add smoke-test coverage for pre-launch queues, per-match reapplication, and
  shutdown restoration.

![MOTrainer English interface](docs/images/motrainer-english.png)

## Project status

This enhanced branch supports `Auto`, English, and Simplified Chinese display
languages in one executable. It also supports the verified Classic and Steam
YR 1.001 game profiles documented below.

- [Original upstream project](https://github.com/cmdrmo/MOTrainer)
- [Compatibility and troubleshooting](docs/COMPATIBILITY.md)
- [Report an issue for this enhanced build](https://github.com/wangluoxiaozhazha/MOTrainer/issues)

## Features

### Selected-unit editor

- Edit veterancy and health for selected units owned by the current player.
- Apply once when a selection is formed, or continuously in compatibility
  mode.
- Transfer selected units to the current player.
- Heal all selected objects or set their health to 1 regardless of ownership.

### Persistent match controls

- Lock credits and power.
- Current-player-only instant production.
- Build anywhere and automatic repair.
- Remove build-count and queue limits.
- Control all units, detect psychic effects, reveal cloaked units, and reveal
  the map.
- Safe technology unlock with faction and extended modes.

All feature toggles may be configured before the game starts. Toggles remain
pending until the verified game modules are available, then apply automatically.
Policies remain active across matches until the trainer is closed.

### Automatic values

- Credits can be configured before a match and are reapplied once when each
  new match starts.
- Selected-unit rank and health editing can be armed before a match.
- Automatic values are released when the trainer closes.

### One-time actions

- Set current-player credits.
- Zero the faction economy identified by the first selected object.
- Mark that faction as surrendered.
- Grant any selected combination of eight faction superweapons.

Every hotkey can be changed, saved, or restored from inside the application.
Hotkeys and mouse controls call the same validated action paths.

## Compatibility

| Requirement | Supported scope |
| --- | --- |
| Operating system | Windows |
| Game process | 32-bit `gamemd.exe` |
| Default runtime | .NET Framework 4.8 |
| Display language | Auto, English, Simplified Chinese |
| Intended use | Mental Omega offline matches |
| Verified game builds | Classic YR 1.001; Steam YR 1.001 (2024) |

MOTrainer validates the SHA-256 fingerprints of `gamemd.exe` and `Ares.dll`.
Unknown modules, unavailable match structures, inconsistent bytes, or failed
readback cause the affected operation to be refused. The program does not guess
addresses for an unknown build.

See [Compatibility and troubleshooting](docs/COMPATIBILITY.md) for the verified
fingerprints, status meanings, permissions guidance, and issue-reporting
requirements.

## Download and use

Download packages only from this repository's
[GitHub Releases](https://github.com/wangluoxiaozhazha/MOTrainer/releases).
Avoid third-party repacks. When a `.sha256` file is provided, verify the ZIP
before extracting it.

The release package contains three files that must remain in the same
directory:

```text
MOTrainer.exe
MOTrainer.Core.dll
MOTrainer.exe.config
```

Bilingual builds also contain a `zh-Hans` directory with two satellite resource
DLLs. Keep that directory beside `MOTrainer.exe`; removing it disables the
Simplified Chinese resources.

1. Start `MOTrainer.exe` and approve the Windows UAC prompt.
2. Configure credits, selected-unit attributes, and feature toggles first.
3. Start Mental Omega. Queued features apply after the game build is verified.
4. Enter an offline match and use the configured controls.

The enhanced executable includes an administrator manifest and does not require
a separate script launcher.

`MOTrainer.exe.config` stores the language, editor values, selected
superweapons, feature modes, and custom hotkeys. Back it up before replacing a
release if you want to preserve those settings. If an older configuration is
reported as invalid, restore the default configuration included with the new
package.

Install the official
[.NET Framework 4.8 Runtime](https://dotnet.microsoft.com/download/dotnet-framework/net48)
if it is not already available.

## Safety model

MOTrainer does not copy or modify Mental Omega game files. It reads and writes
the running game process and attempts to restore persistent patches installed
by the current trainer instance when the trainer exits.

One-time results cannot be undone automatically. Some changes may also be
retained by the game in the current save. Save your progress before testing a
release candidate.

Opening another process, writing memory, allocating temporary remote code pages,
and registering global hotkeys may trigger heuristic antivirus detection. Use
only official release assets, verify the published SHA-256, and review or build
the source if you remain uncertain. The project does not ask users to disable
security software or exclude an entire game directory.

## Build from source

Install a .NET SDK capable of building both .NET 8 and .NET Framework 4.8
targets, then run:

```powershell
dotnet build .\app\MOTrainer.sln --configuration Release
dotnet run --project .\app\tests\MOTrainer.Core.SmokeTests `
  --configuration Release
```

The current suite contains 41 smoke tests covering core state transitions,
safety refusals, rollback paths, pre-launch queues, cross-match policy
reapplication, localization resources, and settings compatibility. These tests
do not replace validation against the exact supported Mental Omega build.

Publish the compact x86 .NET Framework build with:

```powershell
dotnet publish .\app\src\MOTrainer.WinForms\MOTrainer.WinForms.csproj `
  --configuration Release `
  --framework net48 `
  -p:PlatformTarget=x86 `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  --no-self-contained `
  --output .\dist\win-x86-net48
```

## Repository layout

```text
app/src/MOTrainer.Core/              process, memory, profiles, and safety logic
app/src/MOTrainer.WinForms/          Windows user interface
app/tests/MOTrainer.Core.SmokeTests/ lightweight smoke-test harness
docs/                                compatibility and maintenance notes
```

Future structural work follows an incremental approach: refactor along real
feature boundaries without weakening version checks, readback, rollback, or
shutdown restoration. The current internal plan is available in
[Simplified Chinese](docs/MAINTENANCE_PLAN.md).

## Independence and acknowledgements

This enhanced build is based on the original
[cmdrmo/MOTrainer](https://github.com/cmdrmo/MOTrainer) project and preserves
the upstream GPL-3.0-only license. It is not affiliated with or endorsed by
Mental Omega, Electronic Arts, or any existing trainer project. See
[Acknowledgements and project boundaries](ACKNOWLEDGEMENTS.md).

This repository does not contain or distribute the game, Mental Omega files,
third-party trainer files, or their assets.

## License

MOTrainer source code is licensed under the
[GNU General Public License v3.0 only](LICENSE) (`GPL-3.0-only`). The license does
not grant rights to any third-party games, mods, names, or assets.
