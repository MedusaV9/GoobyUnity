using Gooby.Core.Net;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The iOS Info.plist patch that lets the Xcode build reach a self-hosted ws:// server (IosNetworkPlist).</summary>
    public class IosPlistTests
    {
        private const string UnityPlist =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n<dict>\n" +
            "  <key>CFBundleIdentifier</key>\n  <string>com.permissionmaxed.gooby</string>\n" +
            "  <key>NSAppTransportSecurity</key>\n  <dict>\n    <key>NSAllowsArbitraryLoads</key>\n    <false/>\n  </dict>\n" +
            "  <key>UIRequiresFullScreen</key>\n  <true/>\n" +
            "</dict>\n</plist>\n";

        [Test]
        public void ThePatchAllowsPlainWebSocketsAndKeepsTheRest()
        {
            Assert.That(IosNetworkPlist.AllowsPlainNetworking(UnityPlist), Is.False, "Unity's default blocks plain loads");
            var patched = IosNetworkPlist.Patch(UnityPlist);
            Assert.That(IosNetworkPlist.AllowsPlainNetworking(patched), Is.True);
            Assert.That(patched, Does.Contain("<string>com.permissionmaxed.gooby</string>"));
            Assert.That(patched, Does.Contain("<key>UIRequiresFullScreen</key>"));
            Assert.That(patched, Does.Contain("<!DOCTYPE plist"));
            Assert.That(patched, Does.StartWith("<?xml"));
            // exactly one ATS dict (the old <false/> one was replaced), exactly one prompt text
            Assert.That(Count(patched, "<key>NSAppTransportSecurity</key>"), Is.EqualTo(1));
            Assert.That(Count(patched, "<key>NSLocalNetworkUsageDescription</key>"), Is.EqualTo(1));
            Assert.That(patched, Does.Not.Contain("<false />"));
        }

        [Test]
        public void PatchingTwiceChangesNothing()
        {
            var once = IosNetworkPlist.Patch(UnityPlist);
            var twice = IosNetworkPlist.Patch(once);
            Assert.That(Count(twice, "<key>NSAppTransportSecurity</key>"), Is.EqualTo(1));
            Assert.That(Count(twice, "<key>NSLocalNetworkUsageDescription</key>"), Is.EqualTo(1));
            Assert.That(IosNetworkPlist.AllowsPlainNetworking(twice), Is.True);
        }

        private static int Count(string s, string sub)
        {
            var n = 0;
            for (var i = s.IndexOf(sub, System.StringComparison.Ordinal); i >= 0; i = s.IndexOf(sub, i + sub.Length, System.StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
