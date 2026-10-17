using System.Linq;
using System.Xml.Linq;

namespace Gooby.Core.Net
{
    /// <summary>
    /// iOS network permissions for the self-hosted multiplayer server: the player types any address
    /// (<c>192.168.x.y:8080</c> at home, a public IP, a DynDNS name), and GOOBY-SERVER speaks plain <c>ws://</c>/<c>http://</c>.
    /// App Transport Security would block all of it, so the Xcode project's Info.plist gets
    /// <c>NSAppTransportSecurity {NSAllowsArbitraryLoads, NSAllowsLocalNetworking}</c> plus the iOS 14+ local-network
    /// prompt text <c>NSLocalNetworkUsageDescription</c>. Pure XML (no UnityEditor.iOS module needed); the editor hook
    /// <c>Gooby.Editor.IosPostProcess</c> applies it after every iOS build.
    /// </summary>
    public static class IosNetworkPlist
    {
        public const string AtsKey = "NSAppTransportSecurity";
        public const string LocalNetworkKey = "NSLocalNetworkUsageDescription";
        public const string LocalNetworkText =
            "GOOBY verbindet sich mit dem Mehrspieler-Server, den du in den Einstellungen einträgst – zum Beispiel im WLAN zu Hause.";

        /// <summary>Returns the patched plist XML (idempotent: existing entries for these keys are replaced).</summary>
        public static string Patch(string plistXml)
        {
            var doc = XDocument.Parse(plistXml, LoadOptions.PreserveWhitespace);
            var dict = doc.Root?.Element("dict");
            if (dict == null) throw new System.FormatException("Info.plist without a top-level <dict>");
            Remove(dict, AtsKey);
            Remove(dict, LocalNetworkKey);
            dict.Add(new XElement("key", AtsKey),
                new XElement("dict",
                    new XElement("key", "NSAllowsArbitraryLoads"), new XElement("true"),
                    new XElement("key", "NSAllowsLocalNetworking"), new XElement("true")));
            dict.Add(new XElement("key", LocalNetworkKey), new XElement("string", LocalNetworkText));
            var decl = doc.Declaration != null ? doc.Declaration + "\n" : "";
            return decl + doc.ToString(SaveOptions.None);
        }

        /// <summary>True when the plist already allows plain ws/http to any host (what the patch writes).</summary>
        public static bool AllowsPlainNetworking(string plistXml)
        {
            var dict = XDocument.Parse(plistXml).Root?.Element("dict");
            var ats = ValueOf(dict, AtsKey);
            if (ats == null || ats.Name != "dict") return false;
            return ValueOf(ats, "NSAllowsArbitraryLoads")?.Name == "true" && ValueOf(ats, "NSAllowsLocalNetworking")?.Name == "true"
                && ValueOf(dict, LocalNetworkKey) != null;
        }

        private static XElement ValueOf(XElement dict, string key)
        {
            var k = dict?.Elements("key").FirstOrDefault(e => e.Value == key);
            return k?.ElementsAfterSelf().FirstOrDefault();
        }

        private static void Remove(XElement dict, string key)
        {
            foreach (var k in dict.Elements("key").Where(e => e.Value == key).ToList())
            {
                k.ElementsAfterSelf().FirstOrDefault()?.Remove();
                k.Remove();
            }
        }
    }
}
