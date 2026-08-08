# English Support Plan

## Current status

- Localization foundation and application text coverage are complete in the
  current source branch.
- The interface refreshes once after a language choice is saved; the process
  does not need to be restarted manually.
- The GitHub documentation and repository presentation are being converted to
  English-first with preserved Simplified Chinese versions.
- The public `v0.1.0-rc1` package predates bilingual UI support.

## Goal

Make MOTrainer usable and maintainable in both Simplified Chinese and English
without creating separate feature branches, executables, or release packages.
The English version must preserve the behavior and safety boundaries already
validated by the Chinese release.

## Product decisions

- Ship one codebase, one executable, and one release package.
- Support `Auto`, `Simplified Chinese`, and `English` as persisted language
  choices.
- In `Auto`, use Simplified Chinese on Chinese Windows installations and
  English elsewhere.
- Keep configuration keys, patch IDs, hotkey action IDs, hashes, and memory
  profiles language-neutral and backward compatible.
- Rebuild the main interface once after changing the display language while
  preserving the normal shutdown restoration path.
- Use terminology familiar to the Red Alert 2 and Mental Omega communities
  rather than translating Chinese labels word for word.

## Localization boundary

All user-visible text belongs in localized resources, including:

- window titles, group labels, controls, tooltips, and selection choices;
- hotkey action names and hotkey configuration messages;
- session status, validation failures, safety refusals, and recovery results;
- feature, patch, hook, and delayed-application status messages;
- dialogs shown during startup, shutdown, and settings persistence.

Internal identifiers, diagnostic constants, profile names, and tests that
describe program state do not need translation. Exception details originating
from Windows or .NET may remain in the operating system language, but the
surrounding MOTrainer message must be localized.

## Technical direction

- Use the built-in .NET resource system; do not add a localization dependency.
- Keep invariant resources in English and add `zh-Hans` resources for the
  existing Simplified Chinese text.
- Resolve the requested UI culture before constructing `MainForm` or any
  service that can create user-visible messages.
- Store the explicit language choice in the existing application config.
- Keep formatting parameters in resource templates so values such as counts,
  addresses, IDs, and error details are not assembled differently by language.
- Package the generated Simplified Chinese satellite resources with the same
  x86 .NET Framework 4.8 application currently distributed.

## Delivery phases

### 1. Localization foundation

- Add language selection and config migration with `Auto` as the default for
  existing installations.
- Add Core and WinForms resource sets.
- Localize startup and session-probe messages first so failures are useful in
  either language.

### 2. Complete application coverage

- Move all remaining MainForm, hotkey window, action, patch, hook, and delayed
  feature text into resources.
- Review English terminology as a coherent UI rather than as isolated strings.
- Adjust the fixed WinForms layout for longer English labels.

### 3. Documentation and release surface

- Make the repository root `README.md` English-first.
- Preserve the current Chinese documentation as `README.zh-CN.md` and link the
  two documents at the top of both files.
- Provide English compatibility, offline-use, antivirus, permissions, build,
  and issue-reporting guidance.
- Publish bilingual release notes in the same GitHub release.

### 4. Verification

- Build and run the existing x86 .NET Framework 4.8 target in both languages.
- Run Core smoke tests under English and Simplified Chinese UI cultures.
- Check the main window and hotkey window at common Windows scaling levels for
  clipping, overlap, and unreadable controls.
- Confirm an existing Chinese config loads without losing hotkeys or feature
  selections.
- Confirm an English config can switch back to Chinese.
- Re-run the release-package audit and independent extraction test after the
  satellite resource files are introduced.

## Acceptance criteria

English support is ready when:

- every normal user workflow can be completed without encountering Chinese
  application text in English mode;
- every safety refusal and compatibility failure remains understandable;
- Chinese behavior, saved settings, hotkeys, and validated game modifications
  remain unchanged;
- both languages are delivered from the same commit and release asset;
- the English README gives a new user enough information to install, verify,
  use, troubleshoot, and report an issue without relying on Chinese material.

## Non-goals for the first English release

- Additional languages or a community translation plugin system.
- Separate English and Chinese binaries.
- Runtime language switching without restarting.
- Rewriting game-version profiles or changing modification behavior merely to
  support localization.
- Video production or platform-specific promotion work.
