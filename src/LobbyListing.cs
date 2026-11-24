using System.Globalization;
namespace LittleWitchCoop
{
    internal static class LobbyListing
    {
        public static bool TryRead(string protocol, string visibility, string ownerText,
            string capacityText, string membersText, string expectedProtocol,
            out ulong owner, out int capacity, out int members)
        {
            owner = 0; capacity = 0; members = 0;
            return protocol == expectedProtocol && visibility == "1"
                && ulong.TryParse(ownerText, NumberStyles.None, CultureInfo.InvariantCulture, out owner) && owner != 0
                && int.TryParse(capacityText, NumberStyles.None, CultureInfo.InvariantCulture, out capacity) && capacity >= 2 && capacity <= 99
                && int.TryParse(membersText, NumberStyles.None, CultureInfo.InvariantCulture, out members) && members >= 1 && members <= capacity;
        }
    }
}
