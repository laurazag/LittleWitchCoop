param([Parameter(Mandatory=$true)][string]$GameFolder, [switch]$Development)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build.ps1') -GameFolder $GameFolder -Development:$Development
$variant = if ($Development) { 'Development' } else { 'Public' }
$package = Join-Path $PSScriptRoot ('build/package-0.10.6-' + $variant + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$payload = Join-Path $package 'Mod files'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dependencies/loader/BepInEx') -Destination $payload -Recurse
foreach ($name in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('dependencies/loader/' + $name)) -Destination $payload
}
$plugins = Join-Path $payload 'BepInEx/plugins/LittleWitchCoop'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build/LittleWitchCoop.dll') -Destination $plugins
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/ATTRIBUTION.txt') -Destination (Join-Path $plugins 'EMOJI-CREDITS.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/LICENSE-TWEMOJI.txt') -Destination $plugins
foreach ($name in @('Install.cmd','Uninstall.cmd','README.txt','EMOTE-SOUNDS.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('packaging/' + $name)) -Destination $package }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/LICENSE-BEPINEX.txt') -Destination $package
Set-Content -LiteralPath (Join-Path $package 'VERSION.txt') -Value ('0.10.6 - ' + $variant + '. Experimental build. Private-code entry and large sessions still need real-world validation.') -Encoding UTF8
Compress-Archive -Path (Join-Path $package '*') -DestinationPath (Join-Path $PSScriptRoot ('build/LittleWitchCoop-0.10.6-' + $variant + '-Steam.zip')) -Force
Write-Output $package














