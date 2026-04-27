# Athena Windows Companion Demo

Athena is a transparent WPF desktop companion for Windows. She walks above the taskbar, can pause for voice mode, and can open a text chat mode with shared screen and image-generation tools.

## Run

```powershell
dotnet run --project .\AthenaCompanion\AthenaCompanion.csproj
```

## Test

```powershell
dotnet test .\AthenaCompanion.sln
```

## Build Installer

```powershell
.\scripts\build-release.ps1 -Version 0.1.1
```

The installer is written to:

```text
artifacts\installer\AthenaCompanionSetup-0.1.1.exe
```

## GitHub Release

Push a version tag to build and publish a GitHub release:

```powershell
git tag v0.1.1
git push origin v0.1.1
```

The release workflow builds the Windows installer and attaches it to the tagged GitHub release.
