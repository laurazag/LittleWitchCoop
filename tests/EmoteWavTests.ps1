$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '../src/EmoteWav.cs') -Raw
Add-Type ($source + @'
public static class WavProbe {
 public static void Run() {
  var stream = new System.IO.MemoryStream();
  var w = new System.IO.BinaryWriter(stream);
  w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(40);
  w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
  w.Write((short)1); w.Write((short)1); w.Write(22050); w.Write(44100); w.Write((short)2); w.Write((short)16);
  w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(4); w.Write(short.MinValue); w.Write(short.MaxValue);
  byte[] bytes=stream.ToArray(); int channels, rate;
  var samples=LittleWitchCoop.EmoteWav.Decode(bytes,out channels,out rate);
  if(channels!=1 || rate!=22050 || samples.Length!=2 || samples[0]!=-1f || samples[1]<0.999f) throw new System.Exception("PCM decode failed");
  byte[] bad=(byte[])bytes.Clone(); bad[34]=8;
  Reject(bad); Reject(new byte[0]);
  bad=(byte[])bytes.Clone(); bad[40]=100; Reject(bad);
  bad=(byte[])bytes.Clone(); bad[22]=3; Reject(bad);
  bad=(byte[])bytes.Clone(); bad[24]=1; bad[25]=0; Reject(bad);
 }
 static void Reject(byte[] bytes) {
  try {int c,r; LittleWitchCoop.EmoteWav.Decode(bytes,out c,out r);}
  catch(System.IO.InvalidDataException) {return;}
  throw new System.Exception("Invalid WAV accepted");
 }
}
'@)
[WavProbe]::Run()
Write-Output 'PASS WAV PCM samples, format, channels, sample rate and truncation checks'

