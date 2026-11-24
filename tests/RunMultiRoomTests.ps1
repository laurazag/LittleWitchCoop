$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$names=@('tests/MultiRoomTests.cs','src/NetworkRoom.cs','src/RoomState.cs','src/ICoopRoom.cs','src/PlayerSnapshot.cs','src/ChatChannel.cs','src/ChatMessage.cs','src/EmoteChoices.cs','src/BanStore.cs','src/L10n.cs')
$sources=$names | ForEach-Object {Join-Path $root $_}
$output=Join-Path $root 'build/MultiRoomTests.exe'
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe ('/out:'+$output) $sources
if($LASTEXITCODE -ne 0){throw 'MultiRoomTests compile failed'}
& $output
if($LASTEXITCODE -ne 0){throw 'MultiRoomTests failed'}

