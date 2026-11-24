param([Parameter(Mandatory=$true)][string]$GameFolder)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build.ps1') -GameFolder $GameFolder
$version = '0.10.6'
$package = Join-Path $PSScriptRoot ('build/plugin-only-' + $version + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$plugins = Join-Path $package 'BepInEx/plugins/LittleWitchCoop'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build/public/LittleWitchCoop.dll') -Destination $plugins
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/ATTRIBUTION.txt') -Destination (Join-Path $plugins 'EMOJI-CREDITS.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/LICENSE-TWEMOJI.txt') -Destination $plugins
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging/EMOTE-SOUNDS.txt') -Destination $package
@'
LITTLE WITCH CO-OP 0.10.6 — PLUGIN ONLY / EXPERIMENTAL

REQUIREMENT
BepInEx 5.4.23.5, Windows x64 (Unity Mono), installed separately.
Official release: https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5
Official instructions: https://docs.bepinex.dev/articles/user_guide/installation/index.html
This mod targets the Windows Steam version of Little Witch in the Woods.

INSTALL
1. Close the game.
2. If you already have a working compatible BepInEx installation, keep it.
   Otherwise install BepInEx Windows x64 from its official release in the game
   directory containing LWIW.exe. Launch once to generate its configuration,
   then close the game. Do not overwrite an unrelated existing mod loader.
3. Extract the BepInEx folder from THIS archive into the same game directory.
   Final path: BepInEx/plugins/LittleWitchCoop/LittleWitchCoop.dll
4. Launch through Steam. Press F2. The panel should show version 0.10.6.
   Language follows Windows by default; change it using the F2 settings gear.

UPDATE / REMOVE
For updates, close the game, back up the old plugin DLL and replace it at the
same path. Do not install duplicate copies of the plugin in different folders.
To remove, close the game and delete LittleWitchCoop.dll from that plugin folder.
Leave BepInEx if another mod or translation uses it. Saves/settings are separate.

CONTENTS
This package contains the public plugin DLL, emoji credits/license, this guide,
custom-sound instructions and checksums. No installer scripts, EXEs, BepInEx
loader/core, game files, personal fonts or personal WAVs are included.
This packaging option does not modify the plugin's code; the loader is simply
a separate prerequisite. It is not an antivirus clearance or security review.
CHECKSUMS.sha256 identifies the exact files supplied in this archive.

PLAY / TEST STATUS
Use the same mod version on all PCs. Load saves in the same area to see players.
F2: Steam rooms (public browsing or private-code creation), Connection (private
code entry or direct IP/Radmin), players and moderation. Y: chat. B: emotes.
Copy private codes from the private host's Steam rooms page. Codes normally
have 30 case-sensitive characters. The new private-code flow still requires
real testing between accounts. Two-PC public Steam and Radmin worked in earlier
builds. Up to 99 slots is experimental; large real-player sessions/performance
have not been validated. Report reproducible issues with BepInEx/LogOutput.log.
Custom WAV instructions are in EMOTE-SOUNDS.txt.
'@ | Set-Content -LiteralPath (Join-Path $package 'README.txt') -Encoding UTF8
$hashes = Get-ChildItem -LiteralPath $package -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($package.Length + 1).Replace('\','/')
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $relative
}
$hashes | Set-Content -LiteralPath (Join-Path $package 'CHECKSUMS.sha256') -Encoding ASCII
$zip = Join-Path $PSScriptRoot ('build/LittleWitchCoop-' + $version + '-Plugin-Only.zip')
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $binaries = @($archive.Entries | Where-Object { $_.Name -match '\.(dll|exe|cmd|bat|ps1)$' })
    if ($binaries.Count -ne 1 -or $binaries[0].FullName -ne 'BepInEx/plugins/LittleWitchCoop/LittleWitchCoop.dll') { throw 'Unexpected executable payload' }
    if ($archive.Entries | Where-Object { $_.FullName -match '(^|/)(core|patchers)/|(^|/)winhttp.dll$|\.(ttf|wav|zip|7z|rar)$' }) { throw 'Unexpected bundled dependency or personal asset' }
    $stream = $binaries[0].Open()
    try { $actual = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($stream)).Replace('-','') } finally { $stream.Dispose() }
    if ($actual -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'build/public/LittleWitchCoop.dll')).Hash) { throw 'Plugin hash mismatch' }
} finally { $archive.Dispose() }
Write-Output ('PASS plugin-only ZIP contents and DLL SHA256: ' + $zip)

