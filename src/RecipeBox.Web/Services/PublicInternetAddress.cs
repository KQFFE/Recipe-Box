using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace RecipeBox.Web.Services;

public static class PublicInternetAddress
{
    private static readonly (uint Network, int PrefixLength)[] NonPublicIpv4Ranges =
    [
        (0x00000000, 8),
        (0x0A000000, 8),
        (0x64400000, 10),
        (0x7F000000, 8),
        (0xA9FE0000, 16),
        (0xAC100000, 12),
        (0xC0000000, 24),
        (0xC0000200, 24),
        (0xC0586300, 24),
        (0xC0A80000, 16),
        (0xC6120000, 15),
        (0xC6336400, 24),
        (0xCB007100, 24),
        (0xE0000000, 4),
        (0xF0000000, 4)
    ];

    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var value = BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
            return !NonPublicIpv4Ranges.Any(range => IsInIpv4Range(value, range.Network, range.PrefixLength));
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return (bytes[0] & 0xE0) == 0x20
            && !HasIpv6Prefix(bytes, [0x20, 0x01, 0x00], 23)
            && !HasIpv6Prefix(bytes, [0x20, 0x01, 0x0D, 0xB8], 32)
            && !HasIpv6Prefix(bytes, [0x20, 0x02], 16)
            && !HasIpv6Prefix(bytes, [0x3F, 0xFF, 0x00], 20);
    }

    private static bool IsInIpv4Range(uint address, uint network, int prefixLength)
    {
        var mask = uint.MaxValue << (32 - prefixLength);
        return (address & mask) == network;
    }

    private static bool HasIpv6Prefix(byte[] address, byte[] prefix, int prefixLength)
    {
        var completeBytes = prefixLength / 8;
        for (var i = 0; i < completeBytes; i++)
        {
            if (address[i] != prefix[i])
            {
                return false;
            }
        }

        var remainingBits = prefixLength % 8;
        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (address[completeBytes] & mask) == (prefix[completeBytes] & mask);
    }
}
