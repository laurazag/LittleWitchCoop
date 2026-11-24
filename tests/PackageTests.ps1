param([Parameter(Mandatory=$true)][string]$PackageFolder)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixtureRoot = Join-Path $workspace ('build/install-check-' + [Guid]::NewGuid().ToString('N'))
$installer = Join-Path $PackageFolder 'Install.cmd'
function Invoke-Installer([string]$root) {
    $outputLog = Join-Path $fixtureRoot 'installer-output.txt'
    $errorLog = Join-Path $fixtureRoot 'installer-error.txt'
    $arguments = '/d /s /c ""{0}" "{1}" /quiet"' -f $installer, $root
    $process = Start-Process -FilePath $env:ComSpec -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $outputLog -RedirectStandardError $errorLog
    Get-Content -LiteralPath $outputLog | ForEach-Object { Write-Host $_ }
    Get-Content -LiteralPath $errorLog | ForEach-Object { Write-Host $_ }
    return $process.ExitCode
}
function New-FakeGame([string]$name) {
    $root = Join-Path $fixtureRoot $name
    New-Item -ItemType Directory -Path (Join-Path $root 'LWIW_Data/Managed') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'LWIW.exe'), 'fixture')
    [IO.File]::WriteAllText((Join-Path $root 'LWIW_Data/Managed/Assembly-CSharp.dll'), 'fixture')
    return $root
}
try {
    $fresh = New-FakeGame 'Fresh game with spaces'
    $result = Invoke-Installer $fresh
    if ($result[-1] -ne 0) { throw 'Fresh install failed' }
    $expected = (Get-FileHash -LiteralPath (Join-Path $PackageFolder 'Mod files/BepInEx/plugins/LittleWitchCoop/LittleWitchCoop.dll')).Hash
    if ((Get-FileHash -LiteralPath (Join-Path $fresh 'BepInEx/plugins/LittleWitchCoop/LittleWitchCoop.dll')).Hash -ne $expected) { throw 'Wrong plugin installed' }
    if (!(Test-Path -LiteralPath (Join-Path $fresh 'winhttp.dll'))) { throw 'Loader missing' }
    if (Test-Path -LiteralPath (Join-Path $fresh 'LittleWitchCoop-backups')) { throw 'Fresh install backed up a new file' }

    $existing = New-FakeGame 'Existing loader and translation'
    foreach ($folder in @('BepInEx/core','BepInEx/config','BepInEx/plugins/Traducao','BepInEx/plugins/LittleWitchCoop')) {
        New-Item -ItemType Directory -Path (Join-Path $existing $folder) -Force | Out-Null
    }
    $preserved = @('winhttp.dll','doorstop_config.ini','BepInEx/core/BepInEx.dll','BepInEx/config/LittleWitchCoop.bans.txt','BepInEx/plugins/Traducao/Traducao.dll')
    foreach ($file in $preserved) { [IO.File]::WriteAllText((Join-Path $existing $file), 'preserve ' + $file) }
    $before = @{}
    foreach ($file in $preserved) { $before[$file] = (Get-FileHash -LiteralPath (Join-Path $existing $file)).Hash }
    [IO.File]::WriteAllText((Join-Path $existing 'BepInEx/plugins/LittleWitchCoop/LittleWitchCoop.dll'), 'old plugin')
    $result = Invoke-Installer $existing
    if ($result[-1] -ne 0) { throw 'Existing install failed' }
    foreach ($file in $preserved) {
        if ((Get-FileHash -LiteralPath (Join-Path $existing $file)).Hash -ne $before[$file]) { throw ('Installer replaced ' + $file) }
    }
    $backup = Get-ChildItem -LiteralPath (Join-Path $existing 'LittleWitchCoop-backups') -Recurse -Filter '*.dll'
    if (!$backup -or [IO.File]::ReadAllText($backup[0].FullName) -ne 'old plugin') { throw 'Old plugin backup missing' }
    if ((Get-FileHash -LiteralPath (Join-Path $existing 'BepInEx/plugins/LittleWitchCoop/LittleWitchCoop.dll')).Hash -ne $expected) { throw 'Update missed plugin' }

    $conflict = New-FakeGame 'Other loader'
    [IO.File]::WriteAllText((Join-Path $conflict 'winhttp.dll'), 'another loader')
    $result = Invoke-Installer $conflict
    if ($result[-1] -eq 0 -or [IO.File]::ReadAllText((Join-Path $conflict 'winhttp.dll')) -ne 'another loader') { throw 'Conflicting loader was overwritten' }
    Write-Output 'PASS clean install, paths with spaces, plugin update/backup, translation/config/loader preservation and conflict rejection'
} finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $intendedBuild = [IO.Path]::GetFullPath((Join-Path $workspace 'build')) + [IO.Path]::DirectorySeparatorChar
    if (!$resolvedFixture.StartsWith($intendedBuild, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}


