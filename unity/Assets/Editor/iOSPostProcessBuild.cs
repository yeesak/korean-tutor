#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Post-process build script for iOS.
    /// Adds ATS (App Transport Security) exceptions for local network testing.
    /// </summary>
    public static class iOSPostProcessBuild
    {
        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
                return;

            // Path to Info.plist
            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            PlistDocument plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            // Get root dictionary
            PlistElementDict rootDict = plist.root;

            // Add or update NSAppTransportSecurity
            PlistElementDict atsDict;
            if (rootDict.values.ContainsKey("NSAppTransportSecurity"))
            {
                atsDict = rootDict["NSAppTransportSecurity"].AsDict();
            }
            else
            {
                atsDict = rootDict.CreateDict("NSAppTransportSecurity");
            }

            // Allow local networking (for LAN testing with devices)
            atsDict.SetBoolean("NSAllowsLocalNetworking", true);

            // For development builds, add exception domains
            #if DEVELOPMENT_BUILD || UNITY_EDITOR
            AddDevelopmentExceptions(atsDict);
            #endif

            // Write changes
            plist.WriteToFile(plistPath);

            UnityEngine.Debug.Log("[iOSPostProcessBuild] Updated Info.plist with ATS settings");
        }

        private static void AddDevelopmentExceptions(PlistElementDict atsDict)
        {
            // Create or get exception domains
            PlistElementDict exceptionDomains;
            if (atsDict.values.ContainsKey("NSExceptionDomains"))
            {
                exceptionDomains = atsDict["NSExceptionDomains"].AsDict();
            }
            else
            {
                exceptionDomains = atsDict.CreateDict("NSExceptionDomains");
            }

            // Add localhost exception
            var localhostDict = exceptionDomains.CreateDict("localhost");
            localhostDict.SetBoolean("NSExceptionAllowsInsecureHTTPLoads", true);

            // Add common LAN IP ranges
            // 192.168.x.x range
            AddIpRangeException(exceptionDomains, "192.168.1.100");
            AddIpRangeException(exceptionDomains, "192.168.0.100");

            // 10.0.x.x range (corporate networks)
            AddIpRangeException(exceptionDomains, "10.0.0.1");
        }

        private static void AddIpRangeException(PlistElementDict exceptionDomains, string ip)
        {
            if (!exceptionDomains.values.ContainsKey(ip))
            {
                var ipDict = exceptionDomains.CreateDict(ip);
                ipDict.SetBoolean("NSExceptionAllowsInsecureHTTPLoads", true);
                ipDict.SetBoolean("NSIncludesSubdomains", false);
            }
        }
    }
}
#endif
