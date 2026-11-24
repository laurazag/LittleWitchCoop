using System;using System.IO;using LittleWitchCoop;
class Capacity99Tests {
 static byte[] Frame(RoomState s,RoomState.Link l){using(var m=new MemoryStream()){s.Write(new BinaryWriter(m),l);return m.ToArray();}}
 static void Main(){
  var host=new RoomState("host","Host",true,99);var links=new RoomState.Link[98];
  for(int i=0;i<98;i++){
   links[i]=host.Add("g"+i,"Guest "+i,"g"+i);if(links[i]==null)throw new Exception("Guest rejected");
   var poses=new SpritePose[4];for(int j=0;j<4;j++){poses[j]=new SpritePose{Key="EllieTexture/Ellie_Walk_"+j};poses[j].Transform[6]=1;poses[j].Transform[7]=1;poses[j].Transform[8]=1;poses[j].Transform[9]=1;}
   links[i].Snapshot=new PlayerSnapshot{Scene="Forest",Sprites=poses};
  }
  if(host.Capacity!=99||host.Peers.Length!=98||host.Add("extra","Extra","extra")!=null)throw new Exception("Capacity bound");
  if(!host.Send("Host","Test 99",false))throw new Exception("Send");
  for(int i=0;i<98;i++){
   var guest=new RoomState("g"+i,"Guest "+i,false,2);var link=guest.Add("host","Host","host");byte[] packet=Frame(host,links[i]);if(packet.Length>262144)throw new Exception("Transport frame too large");
   using(var m=new MemoryStream(packet))guest.Read(new BinaryReader(m),link);
   ChatMessage message;if(guest.Capacity!=99||guest.Peers.Length!=98||!guest.Receive(out message)||message.Text!="Test 99")throw new Exception("Roster/chat");
  }
  Console.WriteLine("PASS 99 participants, 100th rejected, full roster and host chat to 98 clients; representative four-layer frames fit transport");
 }
}

