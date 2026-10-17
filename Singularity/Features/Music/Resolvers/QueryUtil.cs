using System.Net;
using System.Net.Sockets;

namespace Singularity.Features.Music.Resolvers;

internal static class QueryUtil
{
    /// <summary>
    ///     Returns true only for http(s) URLs. <see cref="Uri.TryCreate(string?, UriKind, out Uri?)" /> alone also accepts
    ///     rooted paths such as "/foo" as absolute file URIs on Unix.
    /// </summary>
    internal static bool TryGetWebUri(string query, out Uri uri)
    {
        if (Uri.TryCreate(query.Trim(), UriKind.Absolute, out var parsed) &&
            parsed.Scheme is "http" or "https")
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    /// <summary>
    ///     Guards against SSRF: yt-dlp will fetch whatever URL it is given, so refuse hosts that are (or resolve to)
    ///     loopback, private, link-local or otherwise non-public addresses.
    /// </summary>
    internal static async Task<bool> IsPublicHostAsync(Uri uri)
    {
        var host = uri.DnsSafeHost.Trim('[', ']');
        var scopeIndex = host.IndexOf('%');
        if (scopeIndex >= 0) host = host[..scopeIndex];

        if (IPAddress.TryParse(host, out var literal)) return IsPublicAddress(literal);

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var addresses = await Dns.GetHostAddressesAsync(host, cts.Token);
            return addresses.Length > 0 && addresses.All(IsPublicAddress);
        }
        catch (Exception)
        {
            return false; // unresolvable: nothing to fetch anyway
        }
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;

        var b = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] == 0 ||                                    // 0.0.0.0/8
                     b[0] == 10 ||                                   // private
                     b[0] == 100 && b[1] is >= 64 and <= 127 ||      // CGNAT
                     b[0] == 169 && b[1] == 254 ||                   // link-local / cloud metadata
                     b[0] == 172 && b[1] is >= 16 and <= 31 ||       // private
                     b[0] == 192 && b[1] == 168 ||                   // private
                     b[0] == 192 && b[1] == 0 && b[2] == 0 ||        // IETF protocol assignments
                     b[0] == 198 && b[1] is 18 or 19 ||              // benchmarking
                     b[0] >= 224);                                   // multicast / reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
                     (b[0] & 0xFE) == 0xFC);                         // unique local fc00::/7
        }

        return false;
    }
}
