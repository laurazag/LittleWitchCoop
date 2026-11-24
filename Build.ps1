param([Parameter(Mandatory=$true)][string]$GameFolder, [switch]$Development)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $GameFolder 'LWIW_Data/Managed'
$references = @('mscorlib.dll','netstandard.dll','System.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.InputLegacyModule.dll') | ForEach-Object { '/reference:' + (Join-Path $managed $_) }
$references += '/reference:' + (Join-Path $PSScriptRoot 'dependencies/loader/BepInEx/core/BepInEx.dll')
$references += '/reference:' + (Join-Path $managed 'com.rlabrecque.steamworks.net.dll')
$references += '/reference:' + (Join-Path $managed 'SunnySideUp.Steam.dll')
$references += '/reference:' + (Join-Path $managed 'GameplayIngredients.dll')
$references += '/reference:' + (Join-Path $managed 'Unity.InputSystem.dll')
$references += '/reference:' + (Join-Path $managed 'System.Core.dll')
$references += '/reference:' + (Join-Path $managed 'System.IO.Compression.dll')
$references += '/reference:' + (Join-Path $managed 'UnityEngine.ImageConversionModule.dll')
$references += '/reference:' + (Join-Path $managed 'UnityEngine.AudioModule.dll')
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'build') | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$resources = @(
    '/resource:' + (Join-Path $PSScriptRoot 'assets/emojis.zip') + ',LittleWitchCoop.Emojis'
    '/resource:' + (Join-Path $PSScriptRoot 'assets/ATTRIBUTION.txt') + ',LittleWitchCoop.EmojiAttribution'
    '/resource:' + (Join-Path $PSScriptRoot 'assets/LICENSE-TWEMOJI.txt') + ',LittleWitchCoop.EmojiLicense'
)
$variant = if ($Development) { 'development' } else { 'public' }
$outputDirectory = Join-Path $PSScriptRoot ('build/' + $variant)
New-Item -ItemType Directory -Force $outputDirectory | Out-Null
$compilerOptions = @()
if ($Development) { $compilerOptions += '/define:DEVELOPMENT_BUILD' }
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /noconfig /nologo /nostdlib /target:library /codepage:65001 ('/out:' + (Join-Path $outputDirectory 'LittleWitchCoop.dll')) @compilerOptions @references @resources @sources
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o plugin.' }
Copy-Item -LiteralPath (Join-Path $outputDirectory 'LittleWitchCoop.dll') -Destination (Join-Path $PSScriptRoot 'build/LittleWitchCoop.dll') -Force


