using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace ProjectBootstrap
{
    // Switches the PC Stream plugin on when it's built (Tools/build_pcstream_native.sh put libpcstream.a in
    // Assets/Plugins/iOS/PcStream): PCSTREAM_NATIVE is defined for the iPhone build, which compiles in
    // NativeStreamSource, and the Xcode project gets the system frameworks and library the plugin
    // calls and the Info.plist text iOS shows when the app first talks to the PC on the local network.
    // Without the library the define is removed again and the demo shows its test picture. Unity
    // recompiles when the define changes, so after building the library, open the Editor (or run any
    // batch build) once before the build that should have the plugin.
    [InitializeOnLoad]
    public class PcStreamNative : IPostprocessBuildWithReport
    {
        public const string Define = "PCSTREAM_NATIVE";
        const string Library = "Assets/Plugins/iOS/PcStream/libpcstream.a";

        static PcStreamNative() => Sync();

        // Makes the define match whether the library is there. Also runnable in batch mode:
        // -executeMethod ProjectBootstrap.PcStreamNative.Sync
        public static void Sync()
        {
            var target = NamedBuildTarget.iOS;
            PlayerSettings.GetScriptingDefineSymbols(target, out var defines);
            var list = new System.Collections.Generic.List<string>(defines);
            var has = list.Contains(Define);
            var want = File.Exists(Library);
            if (has == want)
                return;
            if (want)
                list.Add(Define);
            else
                list.Remove(Define);
            PlayerSettings.SetScriptingDefineSymbols(target, list.ToArray());
            Debug.Log($"[PcStream] {(want ? "Plugin found" : "Plugin gone")}: {Define} {(want ? "defined" : "removed")} for iOS.");
        }

        public int callbackOrder => 10;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS || !File.Exists(Library))
                return;

            var projectPath = PBXProject.GetPBXProjectPath(report.summary.outputPath);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            // Native plugins are linked into UnityFramework, which is where their symbols must resolve.
            var target = project.GetUnityFrameworkTargetGuid();
            foreach (var framework in new[]
                     { "VideoToolbox", "CoreMedia", "CoreVideo", "Metal", "AudioToolbox", "AVFoundation", "Security", "CFNetwork" })
                project.AddFrameworkToProject(target, $"{framework}.framework", false);
            // libxml2 parses the PC's replies.
            project.AddBuildProperty(target, "OTHER_LDFLAGS", "-lxml2");
            project.WriteToFile(projectPath);

            var plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("NSLocalNetworkUsageDescription",
                "PC Stream connects to your PC on the local network to show its screen and play its games.");
            plist.WriteToFile(plistPath);
            Debug.Log("[PcStream] Native streaming plugin linked into the Xcode project.");
        }
    }
}
