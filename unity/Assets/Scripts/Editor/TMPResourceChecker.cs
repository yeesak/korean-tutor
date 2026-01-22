#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TMPro;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Checks for TMP Essential Resources at editor startup (NOT during Play Mode).
    /// Displays a warning dialog if resources are missing.
    /// </summary>
    [InitializeOnLoad]
    public static class TMPResourceChecker
    {
        private const string PREFS_KEY_SKIP_CHECK = "ShadowingTutor_SkipTMPCheck";
        private const string TMP_SETTINGS_PATH = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        static TMPResourceChecker()
        {
            // Only run check when NOT in Play Mode
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            // Delay the check to ensure Unity is fully loaded
            EditorApplication.delayCall += CheckTMPResources;
        }

        private static void CheckTMPResources()
        {
            // CRITICAL: Never run during Play Mode
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            // Skip if user dismissed the warning before
            if (EditorPrefs.GetBool(PREFS_KEY_SKIP_CHECK, false))
            {
                return;
            }

            // Check if TMP Settings exists
            bool tmpSettingsExists = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TMP_SETTINGS_PATH) != null;

            // Check if default font asset exists in TMP Settings
            bool defaultFontExists = TMP_Settings.defaultFontAsset != null;

            if (!tmpSettingsExists || !defaultFontExists)
            {
                Debug.LogWarning(
                    "[TMPResourceChecker] TextMeshPro Essential Resources are missing!\n" +
                    "This may cause errors when creating TMP text at runtime.\n" +
                    "To fix: Window > TextMeshPro > Import TMP Essential Resources\n" +
                    "Then restart the Editor."
                );

                // Show dialog (only in Editor, not batch mode)
                if (!Application.isBatchMode)
                {
                    int result = EditorUtility.DisplayDialogComplex(
                        "TextMeshPro Resources Missing",
                        "TextMeshPro Essential Resources are not imported.\n\n" +
                        "This will cause errors when displaying comparison text.\n\n" +
                        "Would you like to import them now?",
                        "Import Now",
                        "Skip (Don't ask again)",
                        "Remind Me Later"
                    );

                    switch (result)
                    {
                        case 0: // Import Now
                            OpenTMPImporter();
                            break;
                        case 1: // Skip
                            EditorPrefs.SetBool(PREFS_KEY_SKIP_CHECK, true);
                            Debug.Log("[TMPResourceChecker] TMP check disabled. Re-enable via Edit > Preferences > ShadowingTutor");
                            break;
                        case 2: // Remind Later
                            // Do nothing, will check again next time
                            break;
                    }
                }
            }
            else
            {
                Debug.Log("[TMPResourceChecker] TMP Essential Resources found.");
            }
        }

        /// <summary>
        /// Opens the TMP Essential Resources importer window.
        /// </summary>
        private static void OpenTMPImporter()
        {
            // NEVER import during Play Mode
            if (Application.isPlaying)
            {
                Debug.LogError("[TMPResourceChecker] Cannot import TMP resources during Play Mode. Exit Play Mode first.");
                return;
            }

            // Try to open TMP importer window
            EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
        }

        /// <summary>
        /// Menu item to manually check TMP resources.
        /// </summary>
        [MenuItem("Tools/ShadowingTutor/Check TMP Resources")]
        public static void MenuCheckTMPResources()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Cannot Check During Play Mode",
                    "Exit Play Mode to check TMP resources.",
                    "OK"
                );
                return;
            }

            // Reset skip flag to force check
            EditorPrefs.SetBool(PREFS_KEY_SKIP_CHECK, false);
            CheckTMPResources();
        }

        /// <summary>
        /// Menu item to import TMP resources.
        /// </summary>
        [MenuItem("Tools/ShadowingTutor/Import TMP Essential Resources")]
        public static void MenuImportTMPResources()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Cannot Import During Play Mode",
                    "Exit Play Mode before importing TMP resources.\n\n" +
                    "1. Press the Play button to stop\n" +
                    "2. Window > TextMeshPro > Import TMP Essential Resources",
                    "OK"
                );
                return;
            }

            OpenTMPImporter();
        }

        /// <summary>
        /// Reset the skip flag (for preferences).
        /// </summary>
        [MenuItem("Tools/ShadowingTutor/Reset TMP Check Warning")]
        public static void MenuResetTMPCheck()
        {
            EditorPrefs.SetBool(PREFS_KEY_SKIP_CHECK, false);
            Debug.Log("[TMPResourceChecker] TMP check warning reset. Will check on next editor start.");
        }
    }
}
#endif
