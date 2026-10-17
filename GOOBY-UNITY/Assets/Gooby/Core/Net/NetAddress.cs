using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Gooby.Core.Net
{
    /// <summary>
    /// A resolved GOOBY-SERVER address (one port for REST + WebSocket, WS path <c>/ws</c>).
    /// Immutable; <see cref="Valid"/> is false for unparsable input.
    /// </summary>
    public sealed class NetEndpoint
    {
        public string Host { get; }
        public int Port { get; }
        public bool Tls { get; }
        public bool Valid { get; }

        public NetEndpoint(string host, int port, bool tls)
        {
            Host = host ?? "";
            Port = port;
            Tls = tls;
            Valid = Host.Length > 0 && port > 0 && port <= 65535;
        }

        public static readonly NetEndpoint Invalid = new NetEndpoint("", 0, false);

        /// <summary>Host as it must appear in a URL (IPv6 literals get brackets).</summary>
        public string UrlHost => Host.IndexOf(':') >= 0 ? "[" + Host + "]" : Host;

        /// <summary>ws(s)://host:port/ws</summary>
        public string WsUrl => Valid ? (Tls ? "wss" : "ws") + "://" + UrlHost + ":" + Port.ToString(CultureInfo.InvariantCulture) + "/ws" : "";

        /// <summary>http(s)://host:port (no trailing slash) — base for /api/* calls.</summary>
        public string HttpBase => Valid ? (Tls ? "https" : "http") + "://" + UrlHost + ":" + Port.ToString(CultureInfo.InvariantCulture) : "";

        /// <summary>Short human readable form for the settings UI ("192.168.0.10:8080", "wss://host:443").</summary>
        public string Display => Valid ? (Tls ? "wss://" : "") + UrlHost + ":" + Port.ToString(CultureInfo.InvariantCulture) : "";

        /// <summary>True when the host is a LAN/loopback target (UI hint only — never blocks).</summary>
        public bool IsPrivate => NetAddress.IsPrivateHost(Host);

        public override string ToString() => Valid ? WsUrl : "(invalid)";

        public override bool Equals(object obj) =>
            obj is NetEndpoint o && o.Valid == Valid && o.Port == Port && o.Tls == Tls
            && string.Equals(o.Host, Host, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() => (Host.ToLowerInvariant().GetHashCode() * 397) ^ (Port * 2 + (Tls ? 1 : 0));
    }

    /// <summary>
    /// Parses what a player types into the "Server" field (port of mehrspieler_sektion.gd
    /// parse_server_adresse, extended): bare IPs/hosts, host:port, ws/wss/http/https URLs
    /// (path ignored), bracketed IPv6. Defaults: port 8080 without scheme/port (also for
    /// ws:// and http://), 443 for wss:// and https://.
    /// </summary>
    public static class NetAddress
    {
        public const int DefaultPort = 8080;
        public const int DefaultTlsPort = 443;

        /// <param name="text">Raw user input.</param>
        /// <param name="portOverride">Separate port field; wins over a port in <paramref name="text"/> when 1..65535.</param>
        public static NetEndpoint Parse(string text, int? portOverride = null)
        {
            if (text == null) return NetEndpoint.Invalid;
            var rest = text.Trim();
            if (rest.Length == 0) return NetEndpoint.Invalid;

            var tls = false;
            var schemeIdx = rest.IndexOf("://", StringComparison.Ordinal);
            if (schemeIdx >= 0)
            {
                var scheme = rest.Substring(0, schemeIdx).ToLowerInvariant();
                switch (scheme)
                {
                    case "ws":
                    case "http":
                        tls = false;
                        break;
                    case "wss":
                    case "https":
                        tls = true;
                        break;
                    default:
                        return NetEndpoint.Invalid;
                }
                rest = rest.Substring(schemeIdx + 3);
            }

            // Path / query / fragment are ignored (the WS path is always /ws).
            var cut = rest.IndexOfAny(new[] { '/', '?', '#' });
            if (cut >= 0) rest = rest.Substring(0, cut);
            if (rest.IndexOf('@') >= 0) return NetEndpoint.Invalid; // no userinfo
            if (rest.Length == 0) return NetEndpoint.Invalid;

            string host;
            string portText = null;
            if (rest[0] == '[')
            {
                var close = rest.IndexOf(']');
                if (close < 0) return NetEndpoint.Invalid;
                host = rest.Substring(1, close - 1);
                var after = rest.Substring(close + 1);
                if (after.Length > 0)
                {
                    if (after[0] != ':') return NetEndpoint.Invalid;
                    portText = after.Substring(1);
                }
                if (!IsIPv6(host)) return NetEndpoint.Invalid;
            }
            else
            {
                var colons = Count(rest, ':');
                if (colons == 1)
                {
                    var c = rest.IndexOf(':');
                    host = rest.Substring(0, c);
                    portText = rest.Substring(c + 1);
                }
                else if (colons > 1)
                {
                    host = rest; // bare IPv6 literal without port
                    if (!IsIPv6(host)) return NetEndpoint.Invalid;
                }
                else
                {
                    host = rest;
                }
                if (colons <= 1 && !IsHostName(host)) return NetEndpoint.Invalid;
            }

            var port = tls ? DefaultTlsPort : DefaultPort;
            if (portText != null && portText.Length > 0)
            {
                if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port)
                    || port < 1 || port > 65535)
                    return NetEndpoint.Invalid;
            }
            if (portOverride.HasValue && portOverride.Value >= 1 && portOverride.Value <= 65535)
                port = portOverride.Value;

            return new NetEndpoint(host.ToLowerInvariant(), port, tls);
        }

        /// <summary>
        /// LAN/loopback classification (host_gate.gd): localhost, *.local, *.localhost, 127/8, 10/8,
        /// 172.16/12, 192.168/16, ::1, fc00::/7, fe80::/10. Accepts "host:port" and "[v6]:port".
        /// Only a UI hint — plain ws:// to public hosts is allowed on purpose.
        /// </summary>
        public static bool IsPrivateHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            var h = host.Trim().ToLowerInvariant();
            if (h.StartsWith("[", StringComparison.Ordinal))
            {
                var close = h.IndexOf(']');
                h = close > 0 ? h.Substring(1, close - 1) : "";
            }
            else if (Count(h, ':') == 1)
            {
                h = h.Substring(0, h.IndexOf(':'));
            }
            if (h.Length == 0) return false;
            if (h == "localhost" || h.EndsWith(".local", StringComparison.Ordinal)
                || h.EndsWith(".localhost", StringComparison.Ordinal))
                return true;

            var zone = h.IndexOf('%');
            var addrText = zone >= 0 ? h.Substring(0, zone) : h;
            if (!IPAddress.TryParse(addrText, out var ip)) return false;
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                if (Count(addrText, '.') != 3) return false; // "10" alone parses, but is no dotted quad
                var b = ip.GetAddressBytes();
                if (b[0] == 127 || b[0] == 10) return true;
                if (b[0] == 192 && b[1] == 168) return true;
                return b[0] == 172 && b[1] >= 16 && b[1] <= 31;
            }
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (IPAddress.IsLoopback(ip)) return true;
                var b = ip.GetAddressBytes();
                if ((b[0] & 0xFE) == 0xFC) return true; // ULA fc00::/7
                return b[0] == 0xFE && (b[1] & 0xC0) == 0x80; // link-local fe80::/10
            }
            return false;
        }

        private static int Count(string s, char c)
        {
            var n = 0;
            foreach (var ch in s) if (ch == c) n++;
            return n;
        }

        private static bool IsIPv6(string host)
        {
            if (string.IsNullOrEmpty(host) || host.IndexOf(':') < 0) return false;
            var zone = host.IndexOf('%');
            var addr = zone >= 0 ? host.Substring(0, zone) : host;
            return IPAddress.TryParse(addr, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6;
        }

        // RFC 1123-ish host names and dotted IPv4 (letters, digits, '-', '_', '.'; no empty labels).
        private static bool IsHostName(string host)
        {
            if (string.IsNullOrEmpty(host) || host.Length > 253) return false;
            if (host[0] == '.' || host[0] == '-' || host[host.Length - 1] == '-') return false;
            var prevDot = false;
            foreach (var ch in host)
            {
                var ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')
                         || ch == '-' || ch == '_' || ch == '.';
                if (!ok) return false;
                if (ch == '.' && prevDot) return false;
                prevDot = ch == '.';
            }
            return true;
        }
    }
}
