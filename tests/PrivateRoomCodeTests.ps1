$ErrorActionPreference='Stop'
$source=Get-Content (Join-Path $PSScriptRoot '../src/PrivateRoomCode.cs') -Raw
Add-Type ($source+@"
public static class CodeProbe {
 public static void Run(){
  var seen=new System.Collections.Generic.HashSet<string>();
  for(int i=0;i<1000;i++){
   var c=LittleWitchCoop.PrivateRoomCode.Create(76561198000000000UL,109775241122057418UL);
   LittleWitchCoop.PrivateRoomCode decoded;
   if(c.Text.Length!=30||!seen.Add(c.Text)||!LittleWitchCoop.PrivateRoomCode.TryParse("  "+c.Text+"  ",out decoded)||decoded.Owner!=c.Owner||decoded.Lobby!=c.Lobby||decoded.Text!=c.Text)throw new System.Exception("Random code round trip failed");
  }
  var oldData=new byte[32];System.Array.Copy(System.BitConverter.GetBytes(76561198000000000UL),0,oldData,0,8);System.Array.Copy(System.BitConverter.GetBytes(109775241122057418UL),0,oldData,8,8);
  var legacy="LW1-"+System.Convert.ToBase64String(oldData).TrimEnd('=').Replace('+','-').Replace('/','_');
  LittleWitchCoop.PrivateRoomCode oldCode;if(!LittleWitchCoop.PrivateRoomCode.TryParse(legacy,out oldCode)||oldCode.Text!=legacy)throw new System.Exception("Legacy code rejected");
  foreach(var text in new[]{"", "123456", "LW1-abc", "LW1-"+new string('!',43),"LW1-"+new string('A',43)}){
   LittleWitchCoop.PrivateRoomCode c;if(LittleWitchCoop.PrivateRoomCode.TryParse(text,out c))throw new System.Exception("Invalid code accepted");
  }
 }
}
"@)
[CodeProbe]::Run()
'PASS 1000 unique random codes, round-trip, trim, invalid and zero-ID rejection'


