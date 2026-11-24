using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
namespace LittleWitchCoop {
 internal sealed class NetworkRoom : ICoopRoom {
  private const string Protocol="LWCOOP/7\n";
  private readonly string name, id="tcp:"+Guid.NewGuid().ToString("N");
  private readonly BanStore bans;
  private readonly int requestedCapacity;
  private readonly object gate=new object();
  private readonly Dictionary<string,TcpClient> clients=new Dictionary<string,TcpClient>();
  private readonly HashSet<TcpClient> sockets=new HashSet<TcpClient>();
  private readonly CancellationTokenSource cancel=new CancellationTokenSource();
  private RoomState state;
  private TcpListener listener;
  private volatile bool finished;
  private volatile string status=L10n.T("Preparando conexão...");
  private int heartbeats;
  public NetworkRoom(string displayName="Ellie",BanStore banStore=null,int capacity=2){name=ChatMessage.Clean(displayName,40);if(name.Length==0)name="Ellie";bans=banStore??new BanStore();requestedCapacity=capacity;}
  public bool IsHost {get;private set;}
  public RemovalReason CloseReason {get;private set;}
  public RoomPeer[] Peers {get{return state==null?new RoomPeer[0]:state.Peers;}}
  public int Capacity {get{return state==null?Math.Max(2,Math.Min(99,requestedCapacity)):state.Capacity;}}
  public string PeerName {get{var p=Peers;return p.Length==0?"":p[0].Name;}}
  public string PeerId {get{var p=Peers;return p.Length==0?"":p[0].Id;}}
  public PlayerSnapshot Remote {get{var p=Peers;return p.Length==0?new PlayerSnapshot():p[0].Snapshot;}}
  public bool Connected {get{return !finished && Peers.Length>0;}}
  public bool Finished {get{return finished;}}
  public string Status {get{return status;}}
  public int Heartbeats {get{return Interlocked.CompareExchange(ref heartbeats,0,0);}}
  public void Publish(PlayerSnapshot p){if(state!=null)state.Publish(p);}
  public bool SendChat(string a,string t){return state!=null&&state.Send(a,t,false);}
  public bool SendEmote(string a,string t){return state!=null&&state.Send(a,t,true);}
  public bool TryReceiveChat(out ChatMessage m){m=null;return state!=null&&state.Receive(out m);}
  public bool RemovePeer(string expectedId,bool ban){if(!IsHost||state==null)return false;foreach(var l in state.Links)if(l.Id==expectedId&&l.Removal==0){if(ban)bans.Add(l.BanId,l.Name);l.Removal=(byte)(ban?2:1);return true;}return false;}
  public void Start(bool host,string address,int port){IsHost=host;state=new RoomState(id,name,host,requestedCapacity);Task.Run(()=>Run(address,port));}
  private void Run(string address,int port){try{
   if(IsHost){lock(gate){cancel.Token.ThrowIfCancellationRequested();listener=new TcpListener(IPAddress.Any,port);listener.Start(128);}status=L10n.T("Sala aberta na porta ")+port+L10n.T(". Aguardando convidado...");
    while(!cancel.IsCancellationRequested){var client=listener.AcceptTcpClient();lock(gate){if(sockets.Count>=109){client.Close();continue;}sockets.Add(client);}Task.Run(()=>Peer(client));}
   }else {var client=new TcpClient();lock(gate){cancel.Token.ThrowIfCancellationRequested();sockets.Add(client);}var task=client.ConnectAsync(address,port);if(!task.Wait(5000,cancel.Token))throw new TimeoutException();task.GetAwaiter().GetResult();Peer(client);}
  }catch(Exception e){if(!cancel.IsCancellationRequested)status=e.GetBaseException().Message;}finally{if(!IsHost||cancel.IsCancellationRequested)finished=true;}}
  private void Peer(TcpClient client){RoomState.Link link=null;try{
   client.NoDelay=true;client.ReceiveTimeout=5000;client.SendTimeout=5000;
   var stream=client.GetStream();byte[] hello=Encoding.ASCII.GetBytes(Protocol);stream.Write(hello,0,hello.Length);for(int i=0;i<hello.Length;i++)if(stream.ReadByte()!=hello[i])throw new InvalidOperationException("Incompatible mod version");
   var w=new BinaryWriter(stream,Encoding.UTF8,true);var r=new BinaryReader(stream,Encoding.UTF8,true);
   ChatMessage.WriteText(w,id);ChatMessage.WriteText(w,name);w.Flush();string otherId=ChatMessage.ReadText(r,96),otherName=ChatMessage.ReadText(r,40);if(!otherId.StartsWith("tcp:")||otherName.Length==0)throw new InvalidDataException();
   string banId="ip:"+((IPEndPoint)client.Client.RemoteEndPoint).Address;
   if(IsHost){lock(gate){byte reason=(byte)(bans.Contains(banId)?2:state.Links.Length>=Capacity-1?3:0);if(reason==0)link=state.Add(otherId,otherName,banId);if(link==null&&reason==0)reason=3;w.Write(reason);w.Flush();if(reason!=0)return;clients[otherId]=client;}}
   else{byte reason=r.ReadByte();if(reason==2)throw new RoomRemovalException(RemovalReason.Banned);if(reason!=0)throw new InvalidOperationException("Room full");link=state.Add(otherId,otherName,banId);}
   while(!cancel.IsCancellationRequested){
    byte[] bytes;using(var ms=new MemoryStream())using(var writer=new BinaryWriter(ms)){state.Write(writer,link);bytes=ms.ToArray();}w.Write(bytes.Length);w.Write(bytes);w.Flush();if(link.Removal!=0){
     // Finish the send side, then drain the guest's in-flight frame. Closing
     // immediately with unread bytes can reset TCP and lose the removal reason.
     client.Client.Shutdown(SocketShutdown.Send);client.ReceiveTimeout=500;
     var discard=new byte[4096];int remaining=262144;
     try{while(remaining>0){int n=stream.Read(discard,0,Math.Min(discard.Length,remaining));if(n==0)break;remaining-=n;}}catch(IOException){}
     break;
    }
    int length=r.ReadInt32();if(length<1||length>262144)throw new InvalidDataException();bytes=r.ReadBytes(length);if(bytes.Length!=length)throw new EndOfStreamException();
    using(var ms=new MemoryStream(bytes))using(var reader=new BinaryReader(ms)){state.Read(reader,link);if(ms.Position!=ms.Length)throw new InvalidDataException();}
    Interlocked.Increment(ref heartbeats);status=(L10n.Portuguese?"Na sala • ":"In room • ")+(Peers.Length+1)+" / "+Capacity;
    if(cancel.Token.WaitHandle.WaitOne(50))break;
   }
  }catch(Exception e){if(!cancel.IsCancellationRequested){var removed=e as RoomRemovalException;if(!IsHost && removed!=null)CloseReason=removed.Reason;status=IsHost?L10n.T("A outra pessoa saiu. A sala continua aberta."):e.GetBaseException().Message;}}
   finally{if(link!=null){state.Remove(link.Id);lock(gate){clients.Remove(link.Id);}}lock(gate){sockets.Remove(client);}client.Close();if(!IsHost)finished=true;}
  }
  public void Dispose(){cancel.Cancel();finished=true;lock(gate){if(listener!=null)listener.Stop();foreach(var socket in sockets)socket.Close();}}
 }
}



