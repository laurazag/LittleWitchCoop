using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using LittleWitchCoop;

class ModerationTests
{
    static void Require(bool value, string why) { if (!value) throw new Exception(why); }
    static void Wait(Func<bool> value)
    {
        for (int i = 0; i < 200; i++) { if (value()) return; Thread.Sleep(25); }
        throw new Exception("Moderation test timed out");
    }
    static int Port()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }
    static void Connect(NetworkRoom host, NetworkRoom guest, int port)
    {
        guest.Start(false, "127.0.0.1", port);
        Wait(() => host.Connected && guest.Connected);
    }
    static int Main()
    {
        L10n.Language = "pt-BR";
        string file = Path.Combine(Path.GetTempPath(), "lwcoop-moderation-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var bans = new BanStore(file);
            int port = Port();
            using (var host = new NetworkRoom("Host", bans))
            {
                host.Start(true, "", port);
                Wait(() => host.Status.Contains("Aguardando"));
                using (var first = new NetworkRoom("Nome igual"))
                {
                    Connect(host, first, port);
                    Require(!first.RemovePeer(first.PeerId, true), "Guest could moderate the host");
                    Require(!host.RemovePeer("steam:123", true), "Stale/wrong identity was removed");
                    Require(host.RemovePeer(host.PeerId, false), "Host kick failed");
                    Wait(() => first.Finished);
                    Require(first.Status.Contains("expulso") && !host.Finished, "Kick closed the host or lost the reason");
                    Require(bans.Entries.Length == 0, "Kick created a ban");
                }
                using (var second = new NetworkRoom("Nome igual"))
                {
                    Connect(host, second, port);
                    Require(host.SendChat("Host", "Depois do kick"), "Reconnected chat failed");
                    ChatMessage chat = null;
                    Wait(() => second.TryReceiveChat(out chat));
                    Require(chat.Text == "Depois do kick", "Chat sequence was not reset");
                    Require(host.RemovePeer(host.PeerId, true), "Host ban failed");
                    Wait(() => second.Finished);
                    Require(second.Status.Contains("banido") && !host.Finished, "Ban closed host or lost reason: " + second.Status + " | " + host.Status);
                }
                var reloaded = new BanStore(file);
                Require(reloaded.Contains("ip:127.0.0.1") && reloaded.Entries[0].Name == "Nome igual", "Ban was not persisted");
                using (var renamed = new NetworkRoom("Outro apelido"))
                {
                    renamed.Start(false, "127.0.0.1", port);
                    Wait(() => renamed.Finished);
                    Require(!renamed.Connected && renamed.Status.Contains("banido") && !host.Finished, "Renaming bypassed ban or killed listener");
                }
                bans.Remove("ip:127.0.0.1");
                Require(new BanStore(file).Entries.Length == 0, "Unban was not persisted");
                using (var unbanned = new NetworkRoom("Outro apelido"))
                {
                    Connect(host, unbanned, port);
                    Require(host.RemovePeer(host.PeerId, false), "Kick after unban failed");
                    Wait(() => unbanned.Finished);
                }
                // A forged moderation byte from the guest only disconnects that guest.
                using (var forged = new TcpClient("127.0.0.1", port))
                {
                    forged.ReceiveTimeout = 5000;
                    var stream = forged.GetStream();
                    byte[] hello = Encoding.ASCII.GetBytes("LWCOOP/7\n");
                    stream.Write(hello, 0, hello.Length);
                    for (int i = 0; i < hello.Length; i++) Require(stream.ReadByte() == hello[i], "Handshake changed");
                    var writer = new BinaryWriter(stream);
                    var reader = new BinaryReader(stream);
                    ChatMessage.WriteText(writer, "tcp:forged"); ChatMessage.WriteText(writer, "Forjada"); writer.Flush();
                    ChatMessage.ReadText(reader, 96); ChatMessage.ReadText(reader, 40);
                    Require(reader.ReadByte() == 0, "Forged guest admission failed");
                    writer.Write(1); writer.Write((byte)2); writer.Flush();
                    Wait(() => !host.Connected);
                    Require(!host.Finished && bans.Entries.Length == 0, "Guest forged authority");
                }
                using (var healthy = new NetworkRoom("Saudável"))
                {
                    Connect(host, healthy, port);
                    healthy.Dispose();
                    Wait(() => !host.Connected);
                    Require(!host.Finished, "Normal departure closed the room");
                }
            }
            bans.Add("steam:76561198000000000", "Mesmo nome");
            Require(new BanStore(file).Contains("steam:76561198000000000"), "Steam ban persistence failed");
            Console.WriteLine("PASS host authority, stale identity, kick/rejoin, ban/renamed rejection, durable unban, forged guest command, healthy reconnect and Steam ID storage");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
        return 0;
    }
}


