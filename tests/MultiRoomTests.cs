using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using LittleWitchCoop;
class MultiRoomTests {
 static void Need(bool value,string why){if(!value)throw new Exception(why);}
 static void Wait(Func<bool> f,string why){for(int i=0;i<400;i++){if(f())return;Thread.Sleep(25);}throw new Exception("Timeout: "+why);}
 static bool Has(NetworkRoom room,string text,out ChatMessage found){ChatMessage m;while(room.TryReceiveChat(out m))if(m.Text==text){found=m;return true;}found=null;return false;}
 static void Main(){L10n.Language="pt-BR";string file=Path.Combine(Path.GetTempPath(),"lw-multi-"+Guid.NewGuid()+".txt");var guests=new List<NetworkRoom>();var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();int port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();
 using(var host=new NetworkRoom("Host",new BanStore(file),10))try{
  host.Start(true,"",port);Wait(()=>host.Status.Contains("Aguardando"),"listen");
  for(int i=0;i<9;i++){var g=new NetworkRoom("Guest "+i);guests.Add(g);g.Start(false,"127.0.0.1",port);}
  Wait(()=>host.Peers.Length==9,"host nine guests");
  foreach(var g in guests)Wait(()=>g.Peers.Length==9 && g.Capacity==10,"all rosters");
  Console.WriteLine("PASS ten participants and replicated roster");
  for(int i=0;i<9;i++)guests[i].Publish(new PlayerSnapshot{Scene="Area"+i});
  foreach(var g in guests)Wait(()=>Array.Exists(g.Peers,p=>p.Name=="Guest 0"?p.Snapshot.Scene=="Area0":false)||g==guests[0],"distinct snapshots");
  var extra=new NetworkRoom("Extra");extra.Start(false,"127.0.0.1",port);Wait(()=>extra.Finished,"full rejection");Need(host.Peers.Length==9,"over capacity");extra.Dispose();Console.WriteLine("PASS capacity and independent snapshots");
  Need(guests[0].SendChat("Spoof","hello"),"send");ChatMessage found=null;
  Wait(()=>Has(host,"hello",out found),"host chat");Need(found.Author=="Guest 0","host binds author");
  for(int i=1;i<9;i++){var g=guests[i];Wait(()=>Has(g,"hello",out found),"guest relay");Need(found.Author=="Guest 0"&&found.SenderId.Length>0,"origin");}
  Need(host.SendEmote("Host","✨"),"emote");foreach(var g in guests)Wait(()=>Has(g,"✨",out found),"emote fanout");Console.WriteLine("PASS chat relay, authenticated names, and emote fanout");
  var target=Array.Find(host.Peers,p=>p.Name=="Guest 3");Need(!guests[0].RemovePeer(target.Id,false),"guest cannot moderate");Need(host.RemovePeer(target.Id,false),"kick");Wait(()=>guests[3].Finished,"guest kicked");Need(guests[3].CloseReason==RemovalReason.Kicked,"typed kick reason");Wait(()=>host.Peers.Length==8,"room stays open");
  var replacement=new NetworkRoom("Replacement");guests.Add(replacement);replacement.Start(false,"127.0.0.1",port);Wait(()=>host.Peers.Length==9&&replacement.Peers.Length==9,"replacement join");
  target=Array.Find(host.Peers,p=>p.Name=="Replacement");Need(host.RemovePeer(target.Id,true),"ban");Wait(()=>replacement.Finished,"banned disconnect");Need(replacement.CloseReason==RemovalReason.Banned,"typed ban reason");
  var denied=new NetworkRoom("BannedAgain");denied.Start(false,"127.0.0.1",port);Wait(()=>denied.Finished,"ban reject");Need(denied.Status.Contains("banido") && denied.CloseReason==RemovalReason.Banned,"ban reason");denied.Dispose();Console.WriteLine("PASS host-only kick/ban, reconnect, ban rejection; other players stay connected");
  Need(host.Peers.Length==8,"remaining peers");host.Dispose();foreach(var g in guests)Wait(()=>g.Finished,"host disconnect");Need(guests[0].CloseReason==RemovalReason.None,"normal disconnect is not a kick");Console.WriteLine("PASS host disconnect cleanup and typed removal reasons");
 }finally{foreach(var g in guests)g.Dispose();if(File.Exists(file))File.Delete(file);}
 }
}


