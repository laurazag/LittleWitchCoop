$ErrorActionPreference='Stop'
$source=Get-Content (Join-Path $PSScriptRoot '../src/LobbyListing.cs') -Raw
Add-Type ($source + @"
public static class LobbyProbe {
 public static void Run() {
  ulong owner; int capacity,members;
  if(!LittleWitchCoop.LobbyListing.TryRead("v6","1","76561198000000000","10","1","v6",out owner,out capacity,out members) || owner!=76561198000000000UL || capacity!=10 || members!=1) throw new System.Exception("Prejoin public listing rejected");
  if(!LittleWitchCoop.LobbyListing.TryRead("v6","1","76561198000000000","2","2","v6",out owner,out capacity,out members)) throw new System.Exception("Full listing metadata rejected");
  if(!LittleWitchCoop.LobbyListing.TryRead("v6","1","76561198000000000","99","99","v6",out owner,out capacity,out members) || capacity!=99 || members!=99) throw new System.Exception("99-player listing rejected");
  string[][] bad = {
   new [] {"old","1","76561198000000000","2","1"},
   new [] {"v6","0","76561198000000000","2","1"},
   new [] {"v6","1","0","2","1"},
   new [] {"v6","1","76561198000000000","100","1"},
   new [] {"v6","1","76561198000000000","2","3"},
   new [] {"v6","1","76561198000000000","","1"}
  };
  foreach(var row in bad) if(LittleWitchCoop.LobbyListing.TryRead(row[0],row[1],row[2],row[3],row[4],"v6",out owner,out capacity,out members)) throw new System.Exception("Invalid/private listing accepted");
 }
}
"@)
[LobbyProbe]::Run()
'PASS public prejoin metadata, no membership dependency, capacity/member bounds, private/incompatible filtering'


