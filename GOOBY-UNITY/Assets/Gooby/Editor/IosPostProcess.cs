using System.IO;
using Gooby.Core.Net;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Gooby.Editor
{
    /// <summary>
    /// After every iOS build (BuildScript.BuildIOS, CI `ios-xcode`): lets the Xcode project talk plain ws:// / http:// to
    /// the multiplayer server address the player enters (App Transport Security exception + local-network prompt text),
    /// see <see cref="IosNetworkPlist"/>. Plain XML edit — no UnityEditor.iOS module required.
    /// </summary>
    public static class IosPostProcess
    {
        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            var plist = Path.Combine(path, "Info.plist");
            if (!File.Exists(plist))
            {
                Debug.LogWarning("[build] iOS: no Info.plist at " + plist + " — ATS exception not applied");
                return;
            }
            File.WriteAllText(plist, IosNetworkPlist.Patch(File.ReadAllText(plist)));
            Debug.Log("[build] iOS: Info.plist allows ws/http to the multiplayer server (NSAppTransportSecurity, NSLocalNetworkUsageDescription)");
        }
    }
}
