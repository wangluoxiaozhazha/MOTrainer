# Compatibility and troubleshooting

[English](COMPATIBILITY.md) | [简体中文](COMPATIBILITY.zh-CN.md)

MOTrainer deliberately uses strict version binding. Operations are enabled only
when the target process and critical modules match fingerprints that have been
validated against the expected code and object layouts.

Different Mental Omega, Ares, compatibility-patch, or locally replaced builds
may use different layouts. Refusing an unknown target is safer than guessing an
address and risking a crash or corrupt match state.

## Supported environment

| Item | Current scope |
| --- | --- |
| Operating system | Windows |
| Target process | 32-bit `gamemd.exe` |
| Default runtime | .NET Framework 4.8 |
| Display language | Auto, English, Simplified Chinese |
| Intended use | Mental Omega offline matches |

Verified module fingerprints:

| Module | SHA-256 |
| --- | --- |
| `gamemd.exe` | `7CD005D263FDE203D9C84548200A057A8DF61D724DA3C6BD1E521EEB61CD0747` |
| `Ares.dll` | `93CE890F387A6C1B3240A2DD7B674F25FEC5A0E96F0B0D0306796B2EDF7B5B83` |

These fingerprints identify files more precisely than the version shown by a
launcher. Two installations may display the same Mental Omega version while
still differing because of their source, compatibility patches, or local file
replacement.

## Check local fingerprints

Open PowerShell in the Mental Omega directory and run:

```powershell
Get-FileHash -Algorithm SHA256 .\gamemd.exe, .\Ares.dll
```

A mismatch does not mean that the game files are defective. It only means that
they are not the currently verified target. Do not replace game files from an
untrusted source merely to match this trainer.

## Status messages

The top status bar reports what the trainer can safely access:

- **Game is not running:** no target process was found.
- **Version verified; waiting to enter a match:** modules are compatible, but
  player or match structures are not ready.
- **Match structures are not ready:** the process is loaded, but related
  objects cannot yet be accessed safely.
- **Unsupported game:** one or more SHA-256 fingerprints are not supported.
- **Match ready:** operations that depend on match objects are available.

Remove-build-limit and remove-queue-limit can be preset. They stay pending until
the corresponding game structures exist. Other match-dependent actions are not
executed early.

## Runtime and updates

The original `v0.1.0-rc1` release consists of three files that must remain
together:

```text
MOTrainer.exe
MOTrainer.Core.dll
MOTrainer.exe.config
```

Bilingual builds additionally contain `zh-Hans\MOTrainer.resources.dll` and
`zh-Hans\MOTrainer.Core.resources.dll`. Preserve the directory structure when
copying or packaging the application.

.NET Framework 4.8 is the default public-release dependency:

- [.NET Framework 4.8 download](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- [Microsoft installation guidance](https://learn.microsoft.com/dotnet/framework/install/)

The source also targets `net8.0-windows` for development and compatibility
testing. It is not the default compact release target.

## Process permissions and multiple instances

MOTrainer must inspect the main module, module list, and match memory of the game
process. Run the game and trainer at the same privilege level. If the game runs
as administrator, the trainer also needs administrator privileges. If the game
runs normally, do not elevate only the trainer.

When several `gamemd.exe` processes exist, MOTrainer examines every candidate
and prefers a supported process with the most complete match state. Failure to
access one candidate does not prevent checking the others. If no candidate can
be accessed, the status bar explains the permission mismatch. Module-list and
other probe failures are reported separately.

`MOTrainer.exe.config` stores the language, veterancy, health, auto-edit mode,
credit input, technology mode, selected superweapons, and hotkeys. Persistent
memory state and the active auto-edit state are not enabled automatically at the
next launch. Back up the configuration before updating. Restore the default
configuration if an older file produces warnings or unexpected behavior.

## Feature boundaries

- Veterancy and combined health editing normally affect only selected objects
  owned by the current player; allied and enemy objects are skipped.
- Standalone heal and set-health-to-1 actions affect every selected object and
  do not filter by ownership.
- Ownership transfer, faction-economy reset, and faction surrender determine
  their target from the current selection. Confirm the selection first.
- Instant production affects only the current player. Other factions retain
  normal production behavior.
- Tech-unlock faction mode retains normal faction and factory-owner filtering.
  Extended mode additionally relaxes factory ownership for the current player.
- On exit, the trainer attempts to restore persistent patches installed by the
  current trainer instance. One-time results are not undone, and state persisted
  by the game may continue to affect the current save.
- Multiplayer use and anti-cheat bypasses are not supported.

## Antivirus notices

A process-memory trainer uses capabilities that ordinary desktop applications
rarely need: opening another process, reading and writing memory, creating
temporary remote code pages, and registering global hotkeys. These behaviors
may trigger heuristic detection; a detection alone does not establish the
origin of a file.

Recommended practice:

1. Download only from the official GitHub Releases page.
2. Verify the published SHA-256.
3. Avoid third-party repacks.
4. Review and build the public source if you remain uncertain.

The project does not ask users to disable security software or exclude an entire
game directory.

## Report another build

Open an Issue and include:

- the displayed Mental Omega version and where it was obtained;
- SHA-256 values for `gamemd.exe` and `Ares.dll`;
- the complete status shown at the top of MOTrainer;
- the shortest reliable reproduction steps;
- whether compatibility patches or replacement modules were used.

Do not upload game files, third-party trainers, private save contents, account
credentials, or tokens. Fingerprints and version labels alone are not enough to
add support; every new target still requires focused analysis and live
validation.
