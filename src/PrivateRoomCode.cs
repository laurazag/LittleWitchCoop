using System;
using System.Security.Cryptography;
namespace LittleWitchCoop
{
    internal sealed class PrivateRoomCode
    {
        private const ulong PublicIndividualBase = 76561197960265728UL;
        public readonly ulong Owner, Lobby;
        public readonly string Text;
        private PrivateRoomCode(ulong owner, ulong lobby, byte[] secret, bool compact)
        {
            Owner=owner; Lobby=lobby;
            byte[] data=new byte[compact?20:32];
            if(compact)
            {
                Array.Copy(BitConverter.GetBytes((uint)(owner-PublicIndividualBase)),0,data,0,4);
                Array.Copy(BitConverter.GetBytes(lobby),0,data,4,8);
                Array.Copy(secret,0,data,12,8);
            }
            else
            {
                Array.Copy(BitConverter.GetBytes(owner),0,data,0,8);
                Array.Copy(BitConverter.GetBytes(lobby),0,data,8,8);
                Array.Copy(secret,0,data,16,16);
            }
            Text=(compact?"LW-":"LW1-")+Convert.ToBase64String(data).TrimEnd('=').Replace('+','-').Replace('/','_');
        }
        public static PrivateRoomCode Create(ulong owner,ulong lobby)
        {
            if(owner==0 || lobby==0)throw new ArgumentException("Invalid Steam room");
            bool compact=owner>PublicIndividualBase && owner-PublicIndividualBase<=uint.MaxValue;
            byte[] secret=new byte[compact?8:16];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(secret);
            return new PrivateRoomCode(owner,lobby,secret,compact);
        }
        public static bool TryParse(string text,out PrivateRoomCode result)
        {
            result=null;text=(text??"").Trim();
            bool compact=text.Length==30 && text.StartsWith("LW-",StringComparison.Ordinal);
            if(!compact && !(text.Length==47 && text.StartsWith("LW1-",StringComparison.Ordinal)))return false;
            try
            {
                byte[] data=Convert.FromBase64String(text.Substring(compact?3:4).Replace('-','+').Replace('_','/')+"=");
                if(data.Length!=(compact?20:32))return false;
                uint account=compact?BitConverter.ToUInt32(data,0):0;
                ulong owner=compact?PublicIndividualBase+account:BitConverter.ToUInt64(data,0);
                ulong lobby=BitConverter.ToUInt64(data,compact?4:8);
                if(owner==0 || lobby==0 || (compact && account==0))return false;
                byte[] secret=new byte[compact?8:16];Array.Copy(data,compact?12:16,secret,0,secret.Length);
                result=new PrivateRoomCode(owner,lobby,secret,compact);
                if(result.Text!=text){result=null;return false;}
                return true;
            }
            catch(FormatException){return false;}
        }
    }
}
