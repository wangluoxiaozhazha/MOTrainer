# MOTrainer

MOTrainer is an unofficial, independently implemented offline trainer for
Mental Omega.

The project is currently being prepared for its first public release. The
application source and smoke tests live under `app/`.

## Current build

- Windows x86
- .NET Framework 4.8 compact build
- .NET 8 development and fallback target
- No game files or third-party trainer files are included

MOTrainer reads and writes the target game process at runtime. It is intended
for offline use only and does not provide anti-cheat bypass functionality.

## Build and test

```powershell
dotnet build .\app\MOTrainer.sln --configuration Release
dotnet run --project .\app\tests\MOTrainer.Core.SmokeTests `
  --configuration Release
```

The current automated suite contains 30 smoke tests.

## Release status

The compatibility guide, acknowledgements, license and public release package
are still being prepared. Until a license is added, the source is not offered
under an open-source license.
