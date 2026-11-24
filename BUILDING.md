# Building version 0.10.6

## Prerequisites

- Windows with .NET Framework 4.x C# compiler at
  `%WINDIR%/Microsoft.NET/Framework64/v4.0.30319/csc.exe`.
- PowerShell to run build scripts (the end-user CMD installer itself does not
  require PowerShell). Use an environment where local scripts are permitted;
  these scripts do not modify your execution policy.
- A legally installed Windows Steam copy of Little Witch in the Woods with its
  `LWIW.exe` and `LWIW_Data/Managed` directory. The locally tested game was 7.5.0.0
  on Unity 2022.3.62. Other builds can change APIs and need independent testing.
- BepInEx 5.4.23.5 Windows x64, Unity Mono:
  https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5
  Extract the official release under `dependencies/loader/` in this repository.
  The expected file is `dependencies/loader/BepInEx/core/BepInEx.dll`.

Game and third-party binary dependencies are not redistributed in the source
archive. The compiler references Unity, InputSystem, Steamworks.NET and game
assemblies from YOUR `LWIW_Data/Managed`; the full list is in Build.ps1.
The original 50 Twemoji PNGs are in assets/emojis.zip, an embedded data resource.

## Commands (PowerShell, repository root)

```powershell
./Build.ps1 -GameFolder 'C:/Games/Little Witch in the Woods'
./Build.ps1 -GameFolder 'C:/Games/Little Witch in the Woods' -Development
```

Outputs: `build/public/LittleWitchCoop.dll` and
`build/development/LittleWitchCoop.dll`. The DEVELOPMENT_BUILD symbol controls
preview buttons. The latest build is also copied to build/LittleWitchCoop.dll.

For the minimal public archive:

```powershell
./Package-PluginOnly.ps1 -GameFolder 'C:/Games/Little Witch in the Woods'
```

For the full public archive (includes official loader files and setup scripts):

```powershell
./Package.ps1 -GameFolder 'C:/Games/Little Witch in the Woods'
```

Packaging does not upload anything. Public archives omit personal fonts/WAVs.
Tests that do not need the game:

```powershell
./tests/PrivateRoomCodeTests.ps1
./tests/LobbyListingTests.ps1
./tests/LanguageTests.ps1
./tests/EmoteWavTests.ps1
./tests/RunMultiRoomTests.ps1
```

Run Build.ps1 first so the build folder exists. The multi-room test uses local
TCP clients. It is not a 99-PC Steam integration or performance test.

## Binary comparison

The .NET Framework compiler can generate different COFF timestamps and module
MVIDs between builds, so identical source need not yield byte-identical DLLs.
BUILD-REFERENCE.json records the exact supplied source-file hashes and hashes
of available release artifacts, including the DLL extracted from each ZIP.
When investigating a warning, identify the actual uploaded ZIP first; a different
package or a later rebuild can have a different hash. Do not treat a hash match
alone as proof of safety.
