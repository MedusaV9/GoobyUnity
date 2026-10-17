using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Gooby.Editor
{
    /// <summary>
    /// Headless build entry points used by CI (.github/workflows/gooby-unity.yml) and locally:
    ///   Unity -batchmode -quit -projectPath GOOBY-UNITY -executeMethod Gooby.Editor.BuildScript.BuildLinux
    ///   Unity -batchmode -quit -projectPath GOOBY-UNITY -executeMethod Gooby.Editor.BuildScript.BuildIOS
    /// Output paths can be overridden with -buildPath &lt;dir&gt;; game-ci/unity-builder's -customBuildPath is
    /// honoured as well, so CI and the local export share this entry point. Fails the process on any build error.
    /// </summary>
    public static class BuildScript
    {
        private const string BundleId = "com.permissionmaxed.gooby";

        private static string[] Scenes => EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        public static void BuildLinux()
        {
            var path = OutputPath("Builds/Linux");
            // GameCI hands over the executable path itself (…/StandaloneLinux64/GOOBY.x86_64), local runs a directory
            var exe = path.EndsWith(".x86_64", StringComparison.OrdinalIgnoreCase) ? path : Path.Combine(path, "GOOBY.x86_64");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(exe)) ?? ".");
            ApplyCommonPlayerSettings();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = exe,
                target = BuildTarget.StandaloneLinux64,
                options = Arg("-dev", "0") == "1" ? BuildOptions.Development : BuildOptions.None,
            });
            Finish(report, "Linux");
        }

        public static void BuildIOS()
        {
            var path = OutputPath("Builds/iOS");
            Directory.CreateDirectory(path);
            ApplyCommonPlayerSettings();
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
            PlayerSettings.iOS.hideHomeButton = true;
            PlayerSettings.iOS.requiresFullScreen = true;
            PlayerSettings.iOS.buildNumber = Arg("-buildNumber", PlayerSettings.iOS.buildNumber);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1); // ARM64
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = path,
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            });
            Finish(report, "iOS (Xcode project)");
        }

        private static void ApplyCommonPlayerSettings()
        {
            PlayerSettings.companyName = "PermissionMaxed";
            PlayerSettings.productName = "GOOBY";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);
            var version = Arg("-appVersion", null);
            if (!string.IsNullOrEmpty(version)) PlayerSettings.bundleVersion = version;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            // own app icon (Blender render + brand palette, tools/blender + docs/concept/01)
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Gooby/UI/AppIcon/gooby_icon_1024.png");
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            else Debug.LogWarning("[build] app icon missing: Assets/Gooby/UI/AppIcon/gooby_icon_1024.png");
            // portrait must be in the plist for the portrait minigames (OrientationLock); at runtime the app autorotates
            // only between the two landscape sides
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.iOS, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
        }

        private static void Finish(BuildReport report, string label)
        {
            var summary = report.summary;
            Debug.Log($"[build] {label}: {summary.result}, {summary.totalSize / (1024 * 1024)} MB, errors={summary.totalErrors}, warnings={summary.totalWarnings}, {summary.totalTime}");
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[build] {label} FAILED");
                EditorApplication.Exit(1);
            }
        }

        private static string OutputPath(string fallback) => Arg("-buildPath", Arg("-customBuildPath", fallback));

        private static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }
    }
}
