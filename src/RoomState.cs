using System;
using System.Collections.Generic;
using System.IO;
namespace LittleWitchCoop {
 internal sealed class RoomPeer {
  public string Id, Name;
  public bool IsHost;
  public PlayerSnapshot Snapshot = new PlayerSnapshot();
 }
 // Shared host-authoritative roster and message relay for TCP and Steam.
 internal sealed class RoomState {
  internal sealed class Link {
   public string Id, Name, BanId;
   public PlayerSnapshot Snapshot = new PlayerSnapshot();
   public readonly ChatChannel Chat = new ChatChannel();
   public byte Removal;
  }
  private readonly object gate = new object();
  private readonly Dictionary<string,Link> links = new Dictionary<string,Link>();
  private readonly Queue<ChatMessage> incoming = new Queue<ChatMessage>();
  private readonly Queue<ChatMessage> relay = new Queue<ChatMessage>();
  private RoomPeer[] remote = new RoomPeer[0];
  private PlayerSnapshot local = new PlayerSnapshot();
  public readonly string Id, Name;
  public readonly bool Host;
  public int Capacity { get; private set; }
  public RoomState(string id,string name,bool host,int capacity) {Id=id;Name=name;Host=host;Capacity=Math.Max(2,Math.Min(99,capacity));}
  public Link Add(string id,string name,string banId) { lock(gate) {if(id==Id || links.ContainsKey(id)) return null; if(links.Count>=Capacity-1) return null; var link=new Link {Id=id,Name=name,BanId=banId}; links.Add(id,link);return link;} }
  public void Remove(string id) {lock(gate){links.Remove(id);}}
  public Link[] Links {get {lock(gate){var result=new Link[links.Count];links.Values.CopyTo(result,0);return result;}}}
  public RoomPeer[] Peers {get {lock(gate){if(!Host)return (RoomPeer[])remote.Clone();var result=new List<RoomPeer>();foreach(var link in links.Values)if(link.Removal==0)result.Add(new RoomPeer {Id=link.Id,Name=link.Name,Snapshot=link.Snapshot});return result.ToArray();}}}
  public void Publish(PlayerSnapshot value) {lock(gate){local=value;}}
  public bool Send(string author,string text,bool emote) {lock(gate){if(links.Count==0)return false;foreach(var link in links.Values)if(link.Removal==0 && !link.Chat.CanSend)return false;foreach(var link in links.Values)if(link.Removal==0 && !link.Chat.Send(Name,text,emote,Id))return false;return true;}}
  public bool Receive(out ChatMessage value) {lock(gate){value=incoming.Count==0?null:incoming.Dequeue();return value!=null;}}
  public void Write(BinaryWriter writer, Link link) {lock(gate){
   FlushRelay();
   writer.Write(Host?link.Removal:(byte)0);
   if(Host && link.Removal!=0)return;
   if(Host){writer.Write((byte)Capacity);var peers=Peers;writer.Write((byte)(peers.Length+1));WritePeer(writer,new RoomPeer {Id=Id,Name=Name,IsHost=true,Snapshot=local});foreach(var p in peers)WritePeer(writer,p);}
   else local.Write(writer);
   link.Chat.Write(writer);
  }}
  public void Read(BinaryReader reader,Link link) {lock(gate){
   byte removal=reader.ReadByte();
   if(removal!=0){if(Host || removal>2)throw new InvalidDataException("Invalid moderation command");throw new RoomRemovalException(removal==2?RemovalReason.Banned:RemovalReason.Kicked);}
   if(Host)link.Snapshot=PlayerSnapshot.Read(reader);
   else {
    int capacity=reader.ReadByte(), count=reader.ReadByte(); if(capacity<2||capacity>99||count<1||count>capacity)throw new InvalidDataException("Invalid roster size");
    var peers=new List<RoomPeer>();var ids=new HashSet<string>();int hosts=0;bool self=false;
    for(int i=0;i<count;i++){var p=ReadPeer(reader);if(!ids.Add(p.Id))throw new InvalidDataException("Duplicate player");if(p.IsHost)hosts++;if(p.Id==Id)self=true;else peers.Add(p);}
    if(hosts!=1||!self)throw new InvalidDataException("Invalid host roster");Capacity=capacity;remote=peers.ToArray();
   }
   link.Chat.Read(reader);ChatMessage message;
   while(link.Chat.TryReceive(out message)) {
    if(Host){message.Author=link.Name;message.SenderId=link.Id;if(relay.Count>=200)throw new InvalidDataException("Relay overloaded");relay.Enqueue(message);FlushRelay();}
    else if(message.SenderId==Id)continue;
    if(incoming.Count<200)incoming.Enqueue(message);
   }
  }}
  private void FlushRelay(){while(relay.Count>0){var m=relay.Peek();foreach(var l in links.Values)if(l.Id!=m.SenderId && l.Removal==0 && !l.Chat.CanSend)return;foreach(var l in links.Values)if(l.Id!=m.SenderId && l.Removal==0)l.Chat.Send(m.Author,m.Text,m.IsEmote,m.SenderId);relay.Dequeue();}}
  private static void WritePeer(BinaryWriter w,RoomPeer p){ChatMessage.WriteText(w,p.Id);ChatMessage.WriteText(w,p.Name);w.Write(p.IsHost);p.Snapshot.Write(w);}
  private static RoomPeer ReadPeer(BinaryReader r){var p=new RoomPeer{Id=ChatMessage.ReadText(r,96),Name=ChatMessage.ReadText(r,40),IsHost=r.ReadBoolean(),Snapshot=PlayerSnapshot.Read(r)};if(p.Id.Length==0||p.Name.Length==0)throw new InvalidDataException();return p;}
 }
}

