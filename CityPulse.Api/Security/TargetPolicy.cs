using System.Net;
using System.Net.Sockets;

namespace CityPulse.Api.Security;

public static class TargetPolicy
{
    public static bool IsAllowed(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 253)
        {
            return false;
        }

        if (address.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || uri.UserInfo.Length > 0)
            {
                return false;
            }

            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            address = uri.Host;
        }

        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        return IsPrivateIpv4(ip)
            || bytes[0] == 127;
    }

    public static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31);
    }
}
