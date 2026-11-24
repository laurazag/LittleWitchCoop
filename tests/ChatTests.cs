using System;
using System.IO;
using System.Threading;
using LittleWitchCoop;

class ChatTests
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Transfer(ChatChannel sender, ChatChannel receiver)
    {
        using (var stream = new MemoryStream())
        {
            sender.Write(new BinaryWriter(stream));
            stream.Position = 0;
            receiver.Read(new BinaryReader(stream));
            Require(stream.Position == stream.Length, "Unexpected trailing bytes");
        }
    }

    static void Wait(Func<bool> condition)
    {
        for (int i = 0; i < 100; i++)
        {
            if (condition()) return;
            Thread.Sleep(50);
        }
        throw new Exception("Network test timed out");
    }

    static int Main()
    {
        L10n.Language = "pt-BR";
        var a = new ChatChannel();
        var b = new ChatChannel();
        Require(a.Send("TestUser", "Olá, Flor de Bruxa ✨"), "Enqueue failed");
        Require(a.Send("TestUser", new string('á', 512)), "Long message failed");
        Transfer(a, b);
        Transfer(a, b); // Simula a perda da confirmação e a retransmissão.
        ChatMessage message;
        Require(b.TryReceive(out message) && message.Text == "Olá, Flor de Bruxa ✨", "UTF-8 changed");
        Require(!b.TryReceive(out message), "Duplicate message reached chat");
        Require(b.Send("Amiga", "Recebido!"), "Reverse enqueue failed");
        Transfer(b, a);
        Require(a.TryReceive(out message) && message.Author == "Amiga", "Reverse chat failed");
        Transfer(a, b);
        Require(b.TryReceive(out message) && message.Text.Length == 512, "Queued long message lost");
        Transfer(a, b);
        Require(!b.TryReceive(out message), "Second message duplicated");
        Transfer(b, a);
        Console.WriteLine("PASS lost acknowledgements, retransmission, deduplication, ordering and UTF-8");

        var emoteSender = new ChatChannel();
        var emoteReceiver = new ChatChannel();
        Require(!emoteSender.Send("TestUser", "not-an-emote", true), "Unknown emote accepted");
        foreach (string emoji in EmoteChoices.Icons)
            Require(emoteSender.Send("TestUser", emoji, true), "Emote enqueue failed");
        Require(emoteSender.Send("TestUser", "❤️"), "Ordinary emoji chat failed");
        foreach (string emoji in EmoteChoices.Icons)
        {
            Transfer(emoteSender, emoteReceiver);
            Transfer(emoteSender, emoteReceiver);
            Require(emoteReceiver.TryReceive(out message) && message.IsEmote && message.Text == emoji, "Emote type or order lost");
            Require(!emoteReceiver.TryReceive(out message), "Emote duplicated after retransmission");
            Transfer(emoteReceiver, emoteSender);
        }
        Transfer(emoteSender, emoteReceiver);
        Require(emoteReceiver.TryReceive(out message) && !message.IsEmote && message.Text == "❤️", "Chat emoji became an emote");
        Console.WriteLine("PASS eight typed emotes, retransmission, deduplication and distinction from chat");
        using (var packet = new MemoryStream())
        {
            new ChatMessage { Id = 1, Author = "Amiga", Text = "emote desconhecido", IsEmote = true }.Write(new BinaryWriter(packet));
            packet.Position = 0;
            bool rejected = false;
            try { ChatMessage.Read(new BinaryReader(packet)); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Unknown remote emote accepted");
        }
        Console.WriteLine("PASS invalid remote emote rejected");

        var bounded = new ChatChannel();
        for (int i = 0; i < 32; i++) Require(bounded.Send("A", "Oi"), "Queue filled early");
        Require(!bounded.Send("A", "Extra"), "Queue exceeded its bound");
        Require(!a.Send("A", "  \n "), "Empty message accepted");
        Require(ChatMessage.Clean("a\n\tb", 512) == "a  b", "Multiline paste not sanitized");
        Require(ChatMessage.Clean(new string('x', 511) + "✨", 512).Length == 512, "Boundary lost a character");
        string cleanSurrogate = ChatMessage.Clean("a\ud800b", 512);
        Require(a.Send("A", cleanSurrogate), "Malformed local Unicode handling failed");
        Transfer(a, b);
        Require(b.TryReceive(out message) && message.Text == cleanSurrogate, "Malformed local Unicode was not normalized on wire");
        Console.WriteLine("PASS queue bound, paste sanitation and Unicode boundary");

        using (var stream = new MemoryStream())
        {
            var writer = new BinaryWriter(stream);
            writer.Write(uint.MaxValue);
            writer.Write(false);
            stream.Position = 0;
            bool rejected = false;
            try { new ChatChannel().Read(new BinaryReader(stream)); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Forged acknowledgement accepted");
        }
        Console.WriteLine("PASS invalid acknowledgement rejection");

        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using (var host = new NetworkRoom("Host real"))
        using (var guest = new NetworkRoom("Convidada real"))
        {
            host.Start(true, "", port);
            Wait(() => host.Status.Contains("Aguardando"));
            guest.Start(false, "127.0.0.1", port);
            Wait(() => host.Connected && guest.Connected);
            Require(host.PeerName == "Convidada real" && guest.PeerName == "Host real", "Peer identity was not exchanged");
            Require(host.SendChat("Host", "Mensagem do host"), "Host send failed");
            Require(guest.SendChat("Convidada", "Mensagem da convidada"), "Guest send failed");
            ChatMessage toHost = null;
            ChatMessage toGuest = null;
            Wait(() => { if (toHost == null) host.TryReceiveChat(out toHost); if (toGuest == null) guest.TryReceiveChat(out toGuest); return toHost != null && toGuest != null; });
            Require(toHost.Author == "Convidada real" && toGuest.Text == "Mensagem do host", "Bidirectional TCP chat failed");
            Require(host.SendEmote("Host", "✨") && guest.SendEmote("Convidada", "🌸"), "TCP emote send failed");
            toHost = null; toGuest = null;
            Wait(() => { if (toHost == null) host.TryReceiveChat(out toHost); if (toGuest == null) guest.TryReceiveChat(out toGuest); return toHost != null && toGuest != null; });
            Require(toHost.IsEmote && toHost.Text == "🌸" && toGuest.IsEmote && toGuest.Text == "✨", "Bidirectional TCP emote failed");
            Thread.Sleep(150);
            Require(!host.TryReceiveChat(out message) && !guest.TryReceiveChat(out message), "TCP duplicate");
        }
        Console.WriteLine("PASS bidirectional chat over the real TCP room");
        return 0;
    }
}


