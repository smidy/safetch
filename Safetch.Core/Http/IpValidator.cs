using System.Net;
using System.Net.Sockets;

namespace Safetch.Core.Http;

public static class IpValidator
{
    public static bool IsPrivate(IPAddress ip)
    {
        // Unwrap IPv4-mapped IPv6 (::ffff:192.168.x.x) before range checks
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10
                || b[0] == 127
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)   // link-local / cloud metadata
                || (b[0] == 0);                    // 0.0.0.0/8
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(ip)) return true;        // ::1
            var b = ip.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC    // ULA fc00::/7
                || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80); // link-local fe80::/10
        }

        return true; // block anything else (unknown family)
    }
}