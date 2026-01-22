#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEditor;
using TMPro;
using System.IO;
using System.Linq;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Editor tool to create and configure a Korean TMP font asset.
    /// Saves to Resources folder for runtime loading via Resources.Load("TMP_Korean").
    ///
    /// Usage: Tools > ShadowingTutor > Setup Korean Font
    /// </summary>
    public static class KoreanFontSetup
    {
        // Output path - MUST be in Resources for runtime loading
        private const string TMP_FONT_ASSET_PATH = "Assets/Resources/TMP_Korean.asset";
        public const string RUNTIME_LOAD_PATH = "TMP_Korean";

        // Known Korean font name patterns (case-insensitive)
        private static readonly string[] KOREAN_FONT_PATTERNS = new string[]
        {
            "notosanskr", "noto sans kr", "notosanscjkkr", "noto sans cjk",
            "pretendard",
            "spoqahansans", "spoqa han sans",
            "nanumgothic", "nanum gothic", "nanummyeongjo",
            "malgun", "malgungothic",
            "applesdgothicneo", "apple sd gothic",
            "source han sans", "sourcehansan"
        };

        [MenuItem("Tools/ShadowingTutor/Setup Korean Font")]
        public static void SetupKoreanFont()
        {
            // CRITICAL: Never run during Play Mode
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Cannot Run During Play Mode",
                    "Please exit Play Mode before setting up the Korean font.", "OK");
                return;
            }

            // Step 1: Search for Korean font in project
            Font koreanFont = FindKoreanFontInProject();

            if (koreanFont == null)
            {
                EditorUtility.DisplayDialog("Korean Font Not Found",
                    "No Korean font (.ttf/.otf) found in the project.\n\n" +
                    "Please add a Korean font file to:\n" +
                    "Assets/Fonts/\n\n" +
                    "Recommended fonts (free):\n" +
                    "- Noto Sans KR (Google Fonts)\n" +
                    "- Pretendard\n" +
                    "- Spoqa Han Sans\n\n" +
                    "After adding the font file, run this tool again.",
                    "OK");
                return;
            }

            Debug.Log($"[KoreanFontSetup] Found Korean font: {AssetDatabase.GetAssetPath(koreanFont)}");

            // Step 2: Check if TMP font already exists
            TMP_FontAsset existingFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TMP_FONT_ASSET_PATH);
            if (existingFont != null)
            {
                bool overwrite = EditorUtility.DisplayDialog("Font Asset Already Exists",
                    $"Korean TMP font already exists at:\n{TMP_FONT_ASSET_PATH}\n\n" +
                    "Do you want to recreate it?",
                    "Recreate", "Cancel");
                if (!overwrite) return;
                AssetDatabase.DeleteAsset(TMP_FONT_ASSET_PATH);
            }

            // Step 3: Ensure Resources folder exists
            EnsureFolderExists("Assets/Resources");

            // Step 4: Create TMP Font Asset with Dynamic atlas
            Debug.Log("[KoreanFontSetup] Creating TMP font asset with Dynamic atlas...");

            TMP_FontAsset tmpFont = TMP_FontAsset.CreateFontAsset(
                koreanFont,
                90,                              // Sampling point size
                9,                               // Padding
                GlyphRenderMode.SDFAA,           // SDF with anti-aliasing
                4096,                            // Atlas width
                4096                             // Atlas height
            );

            if (tmpFont == null)
            {
                EditorUtility.DisplayDialog("Creation Failed",
                    "Failed to create TMP font asset.\nCheck Console for details.", "OK");
                return;
            }

            // Configure for Dynamic atlas population (Korean has 11,000+ glyphs)
            tmpFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;

            // Step 5: Save asset
            AssetDatabase.CreateAsset(tmpFont, TMP_FONT_ASSET_PATH);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[KoreanFontSetup] Created: {TMP_FONT_ASSET_PATH}");

            // Step 6: Add to TMP Settings fallback fonts
            bool addedToFallback = AddToTMPFallbackFonts(tmpFont);

            // Step 7: Update AppConfig if it exists
            UpdateAppConfig(tmpFont);

            // Step 8: Show success dialog
            string message = $"Korean TMP font created successfully!\n\n" +
                $"Asset: {TMP_FONT_ASSET_PATH}\n" +
                $"Runtime: Resources.Load(\"{RUNTIME_LOAD_PATH}\")\n\n";

            if (addedToFallback)
                message += "Added to TMP fallback fonts.\n";

            EditorUtility.DisplayDialog("Setup Complete", message, "OK");

            // Ping the asset
            Selection.activeObject = tmpFont;
            EditorGUIUtility.PingObject(tmpFont);
        }

        /// <summary>
        /// Search project for a Korean-capable font file.
        /// Checks known names first, then falls back to any font in Assets/Fonts.
        /// </summary>
        private static Font FindKoreanFontInProject()
        {
            // Find all font files in project
            string[] fontGuids = AssetDatabase.FindAssets("t:Font");

            // First pass: look for known Korean font names
            foreach (string guid in fontGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path).ToLower().Replace(" ", "").Replace("-", "").Replace("_", "");

                foreach (string pattern in KOREAN_FONT_PATTERNS)
                {
                    string normalizedPattern = pattern.Replace(" ", "").Replace("-", "").Replace("_", "");
                    if (fileName.Contains(normalizedPattern))
                    {
                        Font font = AssetDatabase.LoadAssetAtPath<Font>(path);
                        if (font != null)
                        {
                            Debug.Log($"[KoreanFontSetup] Found Korean font by name: {path}");
                            return font;
                        }
                    }
                }
            }

            // Second pass: check Assets/Fonts folder for any .ttf/.otf
            string fontsFolder = "Assets/Fonts";
            if (Directory.Exists(fontsFolder))
            {
                var fontFiles = Directory.GetFiles(fontsFolder, "*.ttf", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(fontsFolder, "*.otf", SearchOption.AllDirectories));

                foreach (string filePath in fontFiles)
                {
                    // Convert to Unity asset path
                    string assetPath = filePath.Replace("\\", "/");
                    Font font = AssetDatabase.LoadAssetAtPath<Font>(assetPath);
                    if (font != null)
                    {
                        Debug.Log($"[KoreanFontSetup] Found font in Assets/Fonts: {assetPath}");
                        return font;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Ensure a folder path exists, creating intermediate folders as needed.
        /// </summary>
        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string[] parts = folderPath.Split('/');
            string currentPath = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string nextPath = currentPath + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, parts[i]);
                }
                currentPath = nextPath;
            }
        }

        /// <summary>
        /// Add font to TMP Settings fallback fonts.
        /// </summary>
        private static bool AddToTMPFallbackFonts(TMP_FontAsset font)
        {
            try
            {
                TMP_Settings settings = TMP_Settings.instance;
                if (settings == null) return false;

                SerializedObject serializedSettings = new SerializedObject(settings);
                SerializedProperty fallbackProp = serializedSettings.FindProperty("m_fallbackFontAssets");

                if (fallbackProp != null && fallbackProp.isArray)
                {
                    // Check if already in fallback list
                    for (int i = 0; i < fallbackProp.arraySize; i++)
                    {
                        if (fallbackProp.GetArrayElementAtIndex(i).objectReferenceValue == font)
                        {
                            Debug.Log("[KoreanFontSetup] Font already in fallback list");
                            return true;
                        }
                    }

                    // Add to fallback list at position 0 (highest priority)
                    fallbackProp.InsertArrayElementAtIndex(0);
                    fallbackProp.GetArrayElementAtIndex(0).objectReferenceValue = font;
                    serializedSettings.ApplyModifiedProperties();
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[KoreanFontSetup] Added to TMP fallback fonts");
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[KoreanFontSetup] Could not add to fallback fonts: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Update AppConfig ScriptableObject with the Korean font.
        /// </summary>
        private static void UpdateAppConfig(TMP_FontAsset font)
        {
            // Find AppConfig in Resources
            AppConfig config = Resources.Load<AppConfig>("AppConfig");
            if (config == null)
            {
                // Try to find in project
                string[] guids = AssetDatabase.FindAssets("t:AppConfig");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    config = AssetDatabase.LoadAssetAtPath<AppConfig>(path);
                }
            }

            if (config != null)
            {
                SerializedObject serializedConfig = new SerializedObject(config);
                SerializedProperty fontProp = serializedConfig.FindProperty("_koreanFont");
                if (fontProp != null)
                {
                    fontProp.objectReferenceValue = font;
                    serializedConfig.ApplyModifiedProperties();
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[KoreanFontSetup] Updated AppConfig with Korean font");
                }
            }
        }

        [MenuItem("Tools/ShadowingTutor/Verify Korean Font")]
        public static void VerifyKoreanFont()
        {
            // Check asset exists
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TMP_FONT_ASSET_PATH);

            if (font == null)
            {
                Debug.LogError($"[KoreanFontSetup] Korean font NOT found at: {TMP_FONT_ASSET_PATH}");
                Debug.LogError("Run: Tools > ShadowingTutor > Setup Korean Font");
                return;
            }

            Debug.Log($"[KoreanFontSetup] Korean font found: {font.name}");
            Debug.Log($"  Asset path: {TMP_FONT_ASSET_PATH}");
            Debug.Log($"  Runtime path: Resources.Load<TMP_FontAsset>(\"{RUNTIME_LOAD_PATH}\")");
            Debug.Log($"  Atlas: {font.atlasWidth}x{font.atlasHeight}");
            Debug.Log($"  Population mode: {font.atlasPopulationMode}");

            // Test runtime loading
            TMP_FontAsset runtimeFont = Resources.Load<TMP_FontAsset>(RUNTIME_LOAD_PATH);
            if (runtimeFont != null)
            {
                Debug.Log("[KoreanFontSetup] Runtime loading: OK");
            }
            else
            {
                Debug.LogError("[KoreanFontSetup] Runtime loading FAILED - font won't work in builds!");
            }

            Selection.activeObject = font;
            EditorGUIUtility.PingObject(font);
        }
    }
}
#endif
