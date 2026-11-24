using System;
using LittleWitchCoop;

class SteamPingTests
{
    static void Require(bool condition, string why) { if (!condition) throw new Exception(why); }
    static int Main()
    {
        foreach (bool isReply in new[] { false, true })
        {
            byte[] packet = SteamPingPacket.Write(isReply, 109775241234567890UL, ulong.MaxValue);
            bool reply; ulong lobby; ulong nonce;
            Require(SteamPingPacket.TryRead(packet, out reply, out lobby, out nonce), "Valid ping rejected");
            Require(reply == isReply && lobby == 109775241234567890UL && nonce == ulong.MaxValue, "Ping identity changed");
            for (int length = 0; length < packet.Length; length++)
            {
                var truncated = new byte[length]; Array.Copy(packet, truncated, length);
                Require(!SteamPingPacket.TryRead(truncated, out reply, out lobby, out nonce), "Truncated packet accepted");
            }
            var extra = new byte[packet.Length + 1]; Array.Copy(packet, extra, packet.Length);
            Require(!SteamPingPacket.TryRead(extra, out reply, out lobby, out nonce), "Trailing bytes accepted");
            packet[0] ^= 1;
            Require(!SteamPingPacket.TryRead(packet, out reply, out lobby, out nonce), "Wrong protocol accepted");
            packet[0] ^= 1; packet[4] = 3;
            Require(!SteamPingPacket.TryRead(packet, out reply, out lobby, out nonce), "Unknown ping type accepted");
            Require(!SteamPingPacket.TryRead(SteamPingPacket.Write(isReply, 0, 123), out reply, out lobby, out nonce), "Empty lobby accepted");
            Require(!SteamPingPacket.TryRead(SteamPingPacket.Write(isReply, 123, 0), out reply, out lobby, out nonce), "Empty nonce accepted");
        }
        Console.WriteLine("PASS ping request/reply identity, malformed payload rejection, truncation, trailing bytes and empty identity");
        return 0;
    }
}

