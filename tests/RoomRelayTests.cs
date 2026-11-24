using System;
using System.IO;
using LittleWitchCoop;
class RoomRelayTests {
 static byte[] Frame(RoomState s,RoomState.Link l){using(var m=new MemoryStream()){s.Write(new BinaryWriter(m),l);return m.ToArray();}}
 static void Read(RoomState s,RoomState.Link l,byte[] data){using(var m=new MemoryStream(data))s.Read(new BinaryReader(m),l);}
 static void Need(bool b,string why){if(!b)throw new Exception(why);}
 static void Main(){
  var h=new RoomState("host","Host",true,10);var a=new RoomState("a","Alice",false,2);var b=new RoomState("b","Bob",false,2);
  var ha=h.Add("a","Alice","a");var hb=h.Add("b","Bob","b");var ah=a.Add("host","Host","host");var bh=b.Add("host","Host","host");
  Need(h.Add("a","Spoof","a")==null&&h.Add("host","Spoof","host")==null,"duplicate identity accepted");
  Read(a,ah,Frame(h,ha));Read(b,bh,Frame(h,hb));
  Need(a.Capacity==10&&a.Peers.Length==2,"roster relay");
  Need(a.Send("Impersonated","hello",false),"enqueue");var packet=Frame(a,ah);Read(h,ha,packet);Read(h,ha,packet);
  ChatMessage message;Need(h.Receive(out message)&&message.Author=="Alice"&&message.SenderId=="a","origin bind");Need(!h.Receive(out message),"duplicate host delivery");
  var downstream=Frame(h,hb);Read(b,bh,downstream);Read(b,bh,downstream);
  Need(b.Receive(out message)&&message.Author=="Alice"&&message.SenderId=="a","relayed origin");Need(!b.Receive(out message),"duplicate relayed delivery");
  // Lose acknowledgement and retransmit both ways; no duplicate bubbles/messages.
  Read(h,ha,Frame(a,ah));Read(b,bh,Frame(h,hb));Need(!b.Receive(out message),"lost ACK duplicate");
  Read(h,hb,Frame(b,bh));Read(a,ah,Frame(h,ha));
  Need(a.Send("Alice","second",false),"second enqueue");Read(h,ha,Frame(a,ah));Read(b,bh,Frame(h,hb));Need(b.Receive(out message)&&message.Text=="second","progress after ACK loss");
  var corrupt=Frame(h,hb);corrupt[1]=11;bool rejected=false;try{Read(b,bh,corrupt);}catch(InvalidDataException){rejected=true;}Need(rejected,"invalid capacity accepted");
  Console.WriteLine("PASS roster, duplicate identities, authenticated relay, dropped ACKs, retransmission and invalid capacity rejection");
 }
}

