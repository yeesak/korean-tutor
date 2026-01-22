using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System;
using System.IO;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Build automation script for command-line builds.
    ///
    /// Usage:
    /// /path/to/Unity -quit -batchmode -projectPath /path/to/project \
    ///   -executeMethod ShadowingTutor.Editor.BuildScript.BuildAndroidAPK
    /// </summary>
    public static class BuildScript
    {
        private static readonly string[] Scenes = new string[]
        {
            "Assets/Scenes/TutorRoom.unity",
            "Assets/Scenes/Result.unity"
        };

        private const string BuildFolder = "builds";

        /// <summary>
        /// Build Android APK (Debug)
        /// </summary>
        public static void BuildAndroidAPK()
        {
            BuildAndroid(false);
        }

        /// <summary>
        /// Build Android AAB (Release for Play Store)
        /// </summary>
        public static void BuildAndroidAAB()
        {
            BuildAndroid(true);
        }

        private static void BuildAndroid(bool buildAAB)
        {
            // Ensure build folder exists
            string buildPath = Path.Combine(Application.dataPath, "..", BuildFolder);
            Directory.CreateDirectory(buildPath);

            // Configure build options
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = Scenes,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            if (buildAAB)
            {
                // Release AAB
                options.locationPathName = Path.Combine(buildPath, "ShadowingTutor-release.aab");
                EditorUserBuildSettings.buildAppBundle = true;
                EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Public;
            }
            else
            {
                // Debug APK
                options.locationPathName = Path.Combine(buildPath, "ShadowingTutor-debug.apk");
                options.options = BuildOptions.Development | BuildOptions.AllowDebugging;
                EditorUserBuildSettings.buildAppBundle = false;
            }

            // Set Android settings
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // Build
            Debug.Log($"[BuildScript] Building Android {(buildAAB ? "AAB" : "APK")}...");
            BuildReport report = BuildPipeline.BuildPlayer(options);

            // Check result
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildScript] Build succeeded: {options.locationPathName}");
                Debug.Log($"[BuildScript] Size: {report.summary.totalSize / (1024 * 1024):F2} MB");
            }
            else
            {
                Debug.LogError($"[BuildScript] Build failed with {report.summary.totalErrors} errors");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Build iOS Xcode project
        /// </summary>
        public static void BuildiOS()
        {
            // Ensure build folder exists
            string buildPath = Path.Combine(Application.dataPath, "..", BuildFolder, "iOS");
            Directory.CreateDirectory(buildPath);

            // Configure build options
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = buildPath,
                target = BuildTarget.iOS,
                options = BuildOptions.None
            };

            // Check for development build argument
            string[] args = Environment.GetCommandLineArgs();
            bool developmentBuild = Array.Exists(args, arg => arg == "-development");
            if (developmentBuild)
            {
                options.options = BuildOptions.Development | BuildOptions.AllowDebugging;
            }

            // Set iOS settings
            PlayerSettings.iOS.targetOSVersionString = "13.0";
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;

            // Build
            Debug.Log("[BuildScript] Building iOS Xcode project...");
            BuildReport report = BuildPipeline.BuildPlayer(options);

            // Check result
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildScript] Build succeeded: {buildPath}");
                Debug.Log("[BuildScript] Open Unity-iPhone.xcodeproj in Xcode to complete the build.");
            }
            else
            {
                Debug.LogError($"[BuildScript] Build failed with {report.summary.totalErrors} errors");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Build all platforms
        /// </summary>
        public static void BuildAll()
        {
            Debug.Log("[BuildScript] Building all platforms...");

            // Build Android APK
            BuildAndroidAPK();

            // Build Android AAB
            BuildAndroidAAB();

            // Build iOS (macOS only)
            #if UNITY_EDITOR_OSX
            BuildiOS();
            #else
            Debug.LogWarning("[BuildScript] iOS build skipped (requires macOS)");
            #endif

            Debug.Log("[BuildScript] All builds complete!");
        }

        /// <summary>
        /// Menu item to build Android APK
        /// </summary>
        [MenuItem("Build/Android APK (Debug)")]
        public static void MenuBuildAndroidAPK()
        {
            BuildAndroidAPK();
        }

        /// <summary>
        /// Menu item to build Android AAB
        /// </summary>
        [MenuItem("Build/Android AAB (Release)")]
        public static void MenuBuildAndroidAAB()
        {
            BuildAndroidAAB();
        }

        /// <summary>
        /// Menu item to build iOS
        /// </summary>
        [MenuItem("Build/iOS Xcode Project")]
        public static void MenuBuildiOS()
        {
            BuildiOS();
        }

        /// <summary>
        /// Menu item to open build folder
        /// </summary>
        [MenuItem("Build/Open Build Folder")]
        public static void OpenBuildFolder()
        {
            string buildPath = Path.Combine(Application.dataPath, "..", BuildFolder);
            Directory.CreateDirectory(buildPath);
            EditorUtility.RevealInFinder(buildPath);
        }
    }
}
