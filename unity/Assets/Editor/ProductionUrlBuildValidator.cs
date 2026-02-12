using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Pre-build validator that prevents shipping release builds with placeholder production URLs.
    /// Only fails non-development (release) builds; development builds are allowed to proceed
    /// since they have runtime fallback protection.
    /// </summary>
    public class ProductionUrlBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            // Only validate release builds (non-development)
            bool isDevelopmentBuild = (report.summary.options & BuildOptions.Development) != 0;
            if (isDevelopmentBuild)
            {
                Debug.Log("[ProductionUrlBuildValidator] Development build - skipping validation (runtime fallback enabled).");
                return;
            }

            // Load AppConfig from Resources
            var appConfig = Resources.Load<AppConfig>("AppConfig");
            if (appConfig == null)
            {
                throw new BuildFailedException(
                    "[ProductionUrlBuildValidator] FAILED: AppConfig not found in Resources.\n" +
                    "Create AppConfig.asset in Assets/Resources/ using Assets > Create > ShadowingTutor > AppConfig"
                );
            }

            // Check if production URL is ready
            if (!appConfig.IsReadyForDeviceBuild)
            {
                string currentUrl = GetProductionUrl(appConfig);
                throw new BuildFailedException(
                    $"[ProductionUrlBuildValidator] FAILED: Production URL is not configured for release build.\n\n" +
                    $"Current Production URL: {currentUrl}\n\n" +
                    $"To fix this:\n" +
                    $"1. Open Assets/Resources/AppConfig.asset in Inspector\n" +
                    $"2. Set '_productionUrl' to your actual backend URL (e.g., https://your-app.onrender.com)\n" +
                    $"3. Rebuild\n\n" +
                    $"Alternatively, build with 'Development Build' checked to use debug fallback."
                );
            }

            Debug.Log($"[ProductionUrlBuildValidator] Production URL validated: {GetProductionUrl(appConfig)}");
        }

        private string GetProductionUrl(AppConfig config)
        {
            // Use reflection to get private field since it's not exposed publicly
            var field = typeof(AppConfig).GetField("_productionUrl",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(config) as string ?? "unknown";
        }
    }
}
