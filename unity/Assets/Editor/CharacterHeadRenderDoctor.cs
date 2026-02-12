using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Comprehensive diagnostic and repair tool for character head/eye/hair rendering.
    /// Diagnoses at asset, prefab, and runtime levels with texture import verification.
    /// </summary>
    public class CharacterHeadRenderDoctor : EditorWindow
    {
        private const string CHARACTER_ROOT = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA";
        private const string PREFABS_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs";
        private const string FIXED_MATERIALS_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Materials_Fixed";
        private const string REPORT_PATH = "Assets/Temp/head_render_doctor_report.json";

        // Renderer categories
        private static readonly string[] EYE_RENDERERS = { "cc_base_eye" };
        private static readonly string[] OVERLAY_RENDERERS = { "eyeocclusion", "tearline", "cornea" };
        private static readonly string[] HAIR_RENDERERS = { "hair", "scalp", "babyhair" };
        private static readonly string[] BROW_LASH_RENDERERS = { "brow", "eyebrow", "lash", "eyelash" };
        private static readonly string[] HEAD_BODY_RENDERERS = { "cc_base_body", "cc_base_head", "head", "skin" };

        #region Data Classes

        [System.Serializable]
        public class TextureInfo
        {
            public string propertyName;
            public string textureName;
            public string assetPath;
            public string guid;
            public bool fileExists;
            public bool sRGBTexture;
            public string alphaSource;
            public bool alphaIsTransparency;
            public bool isReadable;
            public bool mipmapsEnabled;
            public string compression;
            public string sanityCheck; // "OK", "SOLID_WHITE", "NO_ALPHA", etc.
            public float alphaMinValue;
            public float alphaMaxValue;
            public float alphaCoverage; // Percentage of pixels with alpha < 1
        }

        [System.Serializable]
        public class MaterialSlotInfo
        {
            public int slotIndex;
            public string materialName;
            public string materialPath;
            public string materialGuid;
            public string shaderName;
            public string renderMode;
            public int renderQueue;
            public List<TextureInfo> textures = new List<TextureInfo>();
            public List<string> issues = new List<string>();
        }

        [System.Serializable]
        public class RendererInfo
        {
            public string gameObjectName;
            public string hierarchyPath;
            public string category;
            public string meshName;
            public int subMeshCount;
            public bool hasInstanceMaterials;
            public bool hasPrefabOverrides;
            public List<MaterialSlotInfo> materialSlots = new List<MaterialSlotInfo>();
        }

        [System.Serializable]
        public class PrefabInfo
        {
            public string name;
            public string path;
            public string guid;
            public bool isRuntimePrefab;
        }

        [System.Serializable]
        public class RuntimeMaterialModifier
        {
            public string filePath;
            public int lineNumber;
            public string matchedCode;
        }

        [System.Serializable]
        public class DiagnosticReport
        {
            public string timestamp;
            public string activeScenePath;
            public string runtimeInstantiatedPrefabPath;
            public string runtimeInstantiatedPrefabGuid;
            public List<PrefabInfo> availablePrefabs = new List<PrefabInfo>();
            public List<RendererInfo> prefabRenderers = new List<RendererInfo>();
            public List<RendererInfo> sceneRenderers = new List<RendererInfo>();
            public List<RuntimeMaterialModifier> runtimeModifiers = new List<RuntimeMaterialModifier>();
            public List<string> criticalIssues = new List<string>();
            public List<string> warnings = new List<string>();
            public string rootCauseAnalysis;
        }

        #endregion

        #region Menu Items

        [MenuItem("Tools/Character Setup/DIAGNOSTIC: Head+Eye+Brow+Hair (Runtime + Asset)")]
        public static void RunDiagnostic()
        {
            Debug.Log("=== CHARACTER HEAD RENDER DOCTOR: DIAGNOSTIC ===\n");

            var report = GenerateComprehensiveReport();
            SaveReport(report);
            PrintReport(report);

            Debug.Log("\n=== END DIAGNOSTIC ===");
        }

        [MenuItem("Tools/Character Setup/FIX: Head+Eye+Brow+Hair (Complete)")]
        public static void RunCompleteFix()
        {
            Debug.Log("=== CHARACTER HEAD RENDER DOCTOR: COMPLETE FIX ===\n");

            // Step 1: Fix texture import settings
            FixTextureImportSettings();

            // Step 2: Create/fix materials
            var textureIndex = BuildTextureIndex();
            FixAllHeadMaterials(textureIndex);

            // Step 3: Update all prefabs
            UpdateAllPrefabs();

            // Step 4: Update scene instance
            UpdateSceneInstance();

            // Step 5: Save and refresh
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("\n=== END COMPLETE FIX ===");
            Debug.Log("Run DIAGNOSTIC again to verify.");
        }

        [MenuItem("Tools/Character Setup/FIX: Reimport Head Textures (Alpha + sRGB)")]
        public static void FixTextureImports()
        {
            Debug.Log("=== FIXING TEXTURE IMPORT SETTINGS ===\n");
            FixTextureImportSettings();
            Debug.Log("\n=== END TEXTURE FIX ===");
        }

        [MenuItem("Tools/Character Setup/FIX: Normalize Eye Material Slot Order")]
        public static void NormalizeEyeSlotOrder()
        {
            Debug.Log("=== NORMALIZING EYE MATERIAL SLOT ORDER ===\n");
            FixEyeMaterialSlotOrder();
            Debug.Log("\n=== END SLOT ORDER FIX ===");
        }

        // Disabled - duplicate of CharacterRenderingFinalFix.ReportRuntimeMaterials
        // [MenuItem("Tools/Character Setup/REPORT: Active Runtime Materials (Play Mode)")]
        public static void ReportRuntimeMaterials_DISABLED()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("This report requires Play Mode. Enter Play Mode first.");
                return;
            }

            Debug.Log("=== RUNTIME MATERIAL REPORT ===\n");
            GenerateRuntimeReport();
            Debug.Log("\n=== END RUNTIME REPORT ===");
        }

        #endregion

        #region Comprehensive Diagnostic

        private static DiagnosticReport GenerateComprehensiveReport()
        {
            var report = new DiagnosticReport
            {
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                activeScenePath = EditorSceneManager.GetActiveScene().path
            };

            // Find runtime prefab
            FindRuntimePrefab(report);

            // List all available prefabs
            ListAvailablePrefabs(report);

            // Analyze prefab renderers
            AnalyzePrefabRenderers(report);

            // Analyze scene renderers
            AnalyzeSceneRenderers(report);

            // Find runtime material modifiers
            FindRuntimeMaterialModifiers(report);

            // Perform root cause analysis
            PerformRootCauseAnalysis(report);

            return report;
        }

        private static void FindRuntimePrefab(DiagnosticReport report)
        {
            // Search for common character loading patterns
            string[] searchPatterns = {
                "CharacterModel", "Avatar", "Edumeta_CharacterGirl"
            };

            // Check TutorRoomController or similar
            var guids = AssetDatabase.FindAssets("t:Script TutorRoomController");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string content = File.ReadAllText(path);

                // Look for prefab references
                var prefabMatch = Regex.Match(content, @"characterPrefab.*?=.*?""([^""]+)""");
                if (prefabMatch.Success)
                {
                    report.runtimeInstantiatedPrefabPath = prefabMatch.Groups[1].Value;
                }

                // Look for serialized field references
                var fieldMatch = Regex.Match(content, @"\[SerializeField\].*?GameObject.*?[Cc]haracter");
                if (fieldMatch.Success)
                {
                    report.warnings.Add($"Found character prefab field in {path}");
                }
            }

            // Default to main prefab if not found
            if (string.IsNullOrEmpty(report.runtimeInstantiatedPrefabPath))
            {
                string defaultPrefab = $"{PREFABS_PATH}/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
                if (File.Exists(defaultPrefab))
                {
                    report.runtimeInstantiatedPrefabPath = defaultPrefab;
                    report.runtimeInstantiatedPrefabGuid = AssetDatabase.AssetPathToGUID(defaultPrefab);
                }
            }
            else
            {
                report.runtimeInstantiatedPrefabGuid = AssetDatabase.AssetPathToGUID(report.runtimeInstantiatedPrefabPath);
            }
        }

        private static void ListAvailablePrefabs(DiagnosticReport report)
        {
            if (!AssetDatabase.IsValidFolder(PREFABS_PATH)) return;

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PREFABS_PATH });
            foreach (var guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                report.availablePrefabs.Add(new PrefabInfo
                {
                    name = Path.GetFileNameWithoutExtension(path),
                    path = path,
                    guid = guid,
                    isRuntimePrefab = path == report.runtimeInstantiatedPrefabPath
                });
            }
        }

        private static void AnalyzePrefabRenderers(DiagnosticReport report)
        {
            if (string.IsNullOrEmpty(report.runtimeInstantiatedPrefabPath)) return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(report.runtimeInstantiatedPrefabPath);
            if (prefab == null)
            {
                report.criticalIssues.Add($"Cannot load runtime prefab: {report.runtimeInstantiatedPrefabPath}");
                return;
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (!IsHeadRelatedRenderer(renderer.gameObject.name)) continue;

                var info = AnalyzeRenderer(renderer, prefab.transform, "prefab");
                report.prefabRenderers.Add(info);

                // Add issues to report
                foreach (var slot in info.materialSlots)
                {
                    foreach (var issue in slot.issues)
                    {
                        if (issue.Contains("CRITICAL"))
                            report.criticalIssues.Add($"{info.hierarchyPath}[{slot.slotIndex}]: {issue}");
                        else
                            report.warnings.Add($"{info.hierarchyPath}[{slot.slotIndex}]: {issue}");
                    }
                }
            }
        }

        private static void AnalyzeSceneRenderers(DiagnosticReport report)
        {
            // Find character in scene
            var avatar = GameObject.Find("Avatar");
            if (avatar == null)
            {
                avatar = GameObject.Find("CharacterModel");
            }

            if (avatar == null)
            {
                report.warnings.Add("No Avatar/CharacterModel found in scene");
                return;
            }

            var renderers = avatar.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (!IsHeadRelatedRenderer(renderer.gameObject.name)) continue;

                var info = AnalyzeRenderer(renderer, avatar.transform, "scene");

                // Check for prefab overrides
                if (PrefabUtility.IsPartOfPrefabInstance(renderer.gameObject))
                {
                    var mods = PrefabUtility.GetPropertyModifications(renderer.gameObject);
                    info.hasPrefabOverrides = mods != null && mods.Any(m =>
                        m.propertyPath.Contains("m_Materials") ||
                        m.propertyPath.Contains("sharedMaterial"));
                }

                report.sceneRenderers.Add(info);
            }
        }

        private static RendererInfo AnalyzeRenderer(Renderer renderer, Transform root, string context)
        {
            var info = new RendererInfo
            {
                gameObjectName = renderer.gameObject.name,
                hierarchyPath = GetHierarchyPath(renderer.gameObject, root),
                category = CategorizeRenderer(renderer.gameObject.name)
            };

            // Get mesh info
            if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                info.meshName = smr.sharedMesh.name;
                info.subMeshCount = smr.sharedMesh.subMeshCount;
            }

            // Check for instance materials
            info.hasInstanceMaterials = renderer.sharedMaterials.Any(m =>
                m != null && m.name.Contains("(Instance)"));

            // Analyze each material slot
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var slotInfo = AnalyzeMaterialSlot(materials[i], i, info.category);
                info.materialSlots.Add(slotInfo);
            }

            return info;
        }

        private static MaterialSlotInfo AnalyzeMaterialSlot(Material mat, int index, string rendererCategory)
        {
            var slot = new MaterialSlotInfo { slotIndex = index };

            if (mat == null)
            {
                slot.materialName = "NULL";
                slot.issues.Add("CRITICAL: NULL material");
                return slot;
            }

            slot.materialName = mat.name;
            slot.materialPath = AssetDatabase.GetAssetPath(mat);
            slot.materialGuid = AssetDatabase.AssetPathToGUID(slot.materialPath);

            // Shader info
            if (mat.shader == null)
            {
                slot.shaderName = "NULL";
                slot.issues.Add("CRITICAL: NULL shader");
            }
            else if (mat.shader.name == "Hidden/InternalErrorShader")
            {
                slot.shaderName = mat.shader.name;
                slot.issues.Add("CRITICAL: Error shader (Broken PPtr)");
            }
            else
            {
                slot.shaderName = mat.shader.name;
            }

            slot.renderQueue = mat.renderQueue;

            // Standard render mode
            if (mat.HasProperty("_Mode"))
            {
                float mode = mat.GetFloat("_Mode");
                slot.renderMode = GetRenderModeString((int)mode);

                // Check if mode is appropriate for category
                ValidateRenderMode(slot, rendererCategory, (int)mode);
            }
            else
            {
                slot.renderMode = "N/A";
            }

            // Analyze textures
            slot.textures.Add(AnalyzeTexture(mat, "_MainTex"));
            slot.textures.Add(AnalyzeTexture(mat, "_BaseMap"));

            // Check for texture issues
            bool hasValidTexture = slot.textures.Any(t => t.fileExists && t.textureName != "null");
            if (!hasValidTexture && IsTextureRequiredForCategory(rendererCategory))
            {
                slot.issues.Add($"CRITICAL: No valid texture for {rendererCategory}");
            }

            return slot;
        }

        private static TextureInfo AnalyzeTexture(Material mat, string propertyName)
        {
            var info = new TextureInfo { propertyName = propertyName };

            if (!mat.HasProperty(propertyName))
            {
                info.textureName = "N/A (no property)";
                return info;
            }

            var tex = mat.GetTexture(propertyName) as Texture2D;
            if (tex == null)
            {
                info.textureName = "null";
                return info;
            }

            info.textureName = tex.name;
            info.assetPath = AssetDatabase.GetAssetPath(tex);
            info.guid = AssetDatabase.AssetPathToGUID(info.assetPath);
            info.fileExists = File.Exists(info.assetPath);

            // Get import settings
            var importer = AssetImporter.GetAtPath(info.assetPath) as TextureImporter;
            if (importer != null)
            {
                info.sRGBTexture = importer.sRGBTexture;
                info.alphaSource = importer.alphaSource.ToString();
                info.alphaIsTransparency = importer.alphaIsTransparency;
                info.isReadable = importer.isReadable;
                info.mipmapsEnabled = importer.mipmapEnabled;
                info.compression = importer.textureCompression.ToString();

                // Analyze alpha coverage if readable
                AnalyzeTextureAlpha(tex, info, importer);
            }

            return info;
        }

        private static void AnalyzeTextureAlpha(Texture2D tex, TextureInfo info, TextureImporter importer)
        {
            // Try to read texture data
            bool wasReadable = importer.isReadable;
            string path = info.assetPath;

            try
            {
                // Make temporarily readable if needed
                if (!wasReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }

                if (tex != null && tex.isReadable)
                {
                    var pixels = tex.GetPixels32();
                    int totalPixels = pixels.Length;
                    int transparentPixels = 0;
                    int minAlpha = 255;
                    int maxAlpha = 0;
                    int sumR = 0, sumG = 0, sumB = 0;

                    foreach (var p in pixels)
                    {
                        if (p.a < 255) transparentPixels++;
                        if (p.a < minAlpha) minAlpha = p.a;
                        if (p.a > maxAlpha) maxAlpha = p.a;
                        sumR += p.r;
                        sumG += p.g;
                        sumB += p.b;
                    }

                    info.alphaMinValue = minAlpha / 255f;
                    info.alphaMaxValue = maxAlpha / 255f;
                    info.alphaCoverage = (float)transparentPixels / totalPixels;

                    // Sanity checks
                    float avgR = (float)sumR / totalPixels / 255f;
                    float avgG = (float)sumG / totalPixels / 255f;
                    float avgB = (float)sumB / totalPixels / 255f;

                    if (avgR > 0.95f && avgG > 0.95f && avgB > 0.95f)
                    {
                        info.sanityCheck = "SOLID_WHITE (may indicate missing texture data)";
                    }
                    else if (minAlpha == maxAlpha && maxAlpha == 255)
                    {
                        info.sanityCheck = "NO_ALPHA (all pixels fully opaque)";
                    }
                    else if (info.alphaCoverage < 0.01f)
                    {
                        info.sanityCheck = "MINIMAL_ALPHA (< 1% transparent pixels)";
                    }
                    else
                    {
                        info.sanityCheck = "OK";
                    }
                }
            }
            catch (System.Exception e)
            {
                info.sanityCheck = $"UNREADABLE: {e.Message}";
            }
            finally
            {
                // Restore original readable state
                if (!wasReadable && importer != null)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
            }
        }

        private static void ValidateRenderMode(MaterialSlotInfo slot, string category, int mode)
        {
            bool isOverlay = category == "overlay";
            bool isHairBrow = category == "hair" || category == "brow_lash";
            bool isEyeBase = category == "eye_base";

            if (isOverlay && mode == 0) // Opaque
            {
                slot.issues.Add("CRITICAL: Overlay material is OPAQUE (should be FADE)");
            }

            if (isHairBrow && mode == 0) // Opaque
            {
                slot.issues.Add("Hair/brow material is OPAQUE (should be CUTOUT)");
            }
        }

        private static void FindRuntimeMaterialModifiers(DiagnosticReport report)
        {
            string[] searchPatterns = {
                @"\.material\s*=",
                @"\.sharedMaterial\s*=",
                @"\.materials\s*=",
                @"\.sharedMaterials\s*=",
                @"SetTexture\s*\(",
                @"_MainTex",
                @"_BaseMap",
                @"FixInstanceMaterials"
            };

            var scriptGuids = AssetDatabase.FindAssets("t:Script", new[] { "Assets/Scripts" });

            foreach (var guid in scriptGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!File.Exists(path)) continue;

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (var pattern in searchPatterns)
                    {
                        if (Regex.IsMatch(lines[i], pattern))
                        {
                            report.runtimeModifiers.Add(new RuntimeMaterialModifier
                            {
                                filePath = path,
                                lineNumber = i + 1,
                                matchedCode = lines[i].Trim()
                            });
                            break;
                        }
                    }
                }
            }
        }

        private static void PerformRootCauseAnalysis(DiagnosticReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== ROOT CAUSE ANALYSIS ===");

            // Check for critical issues
            if (report.criticalIssues.Count > 0)
            {
                sb.AppendLine("\nCRITICAL ISSUES DETECTED:");
                foreach (var issue in report.criticalIssues)
                {
                    sb.AppendLine($"  - {issue}");
                }
            }

            // Analyze eye renderers specifically
            var eyeRenderer = report.prefabRenderers.FirstOrDefault(r => r.category == "eye_base");
            if (eyeRenderer != null)
            {
                sb.AppendLine($"\nEYE BASE ANALYSIS ({eyeRenderer.gameObjectName}):");

                foreach (var slot in eyeRenderer.materialSlots)
                {
                    var mainTex = slot.textures.FirstOrDefault(t => t.propertyName == "_MainTex");
                    if (mainTex != null)
                    {
                        sb.AppendLine($"  Slot {slot.slotIndex} ({slot.materialName}):");
                        sb.AppendLine($"    Texture: {mainTex.textureName}");
                        sb.AppendLine($"    Exists: {mainTex.fileExists}");
                        sb.AppendLine($"    Sanity: {mainTex.sanityCheck}");
                        sb.AppendLine($"    Mode: {slot.renderMode}");
                    }
                }
            }

            // Check overlay issues
            var overlayRenderers = report.prefabRenderers.Where(r => r.category == "overlay");
            foreach (var overlay in overlayRenderers)
            {
                foreach (var slot in overlay.materialSlots)
                {
                    if (slot.renderMode == "Opaque")
                    {
                        sb.AppendLine($"\nOVERLAY BLOCKING ISSUE: {overlay.gameObjectName}[{slot.slotIndex}]");
                        sb.AppendLine($"  Material {slot.materialName} is OPAQUE - will block eye underneath");
                    }

                    var tex = slot.textures.FirstOrDefault(t => t.propertyName == "_MainTex");
                    if (tex != null && tex.sanityCheck == "NO_ALPHA")
                    {
                        sb.AppendLine($"\nOVERLAY ALPHA ISSUE: {overlay.gameObjectName}[{slot.slotIndex}]");
                        sb.AppendLine($"  Texture has no alpha channel - overlay will be fully opaque");
                    }
                }
            }

            // Check for scene overrides
            if (report.sceneRenderers.Any(r => r.hasPrefabOverrides))
            {
                sb.AppendLine("\nSCENE OVERRIDE ISSUE:");
                sb.AppendLine("  Scene instance has material overrides that may differ from prefab");
            }

            // Check for runtime modifiers
            if (report.runtimeModifiers.Count > 0)
            {
                sb.AppendLine($"\nRUNTIME MODIFIERS DETECTED ({report.runtimeModifiers.Count} locations):");
                foreach (var mod in report.runtimeModifiers.Take(5))
                {
                    sb.AppendLine($"  {mod.filePath}:{mod.lineNumber}");
                }
            }

            report.rootCauseAnalysis = sb.ToString();
        }

        #endregion

        #region Fix Implementation

        private static void FixTextureImportSettings()
        {
            string[] paths = {
                $"{CHARACTER_ROOT}/Edumeta_CharacterGirl_AAA 1.fbm",
                $"{CHARACTER_ROOT}/Edumeta_CharacterGirl_AAA.fbm"
            };

            int fixed_count = 0;

            foreach (var basePath in paths)
            {
                if (!Directory.Exists(basePath)) continue;

                var texFiles = Directory.GetFiles(basePath, "*.png", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(basePath, "*.tga", SearchOption.AllDirectories));

                foreach (var filePath in texFiles)
                {
                    string assetPath = filePath.Replace("\\", "/");
                    var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

                    if (importer == null) continue;

                    string fileName = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
                    bool needsAlpha = fileName.Contains("hair") || fileName.Contains("lash") ||
                                     fileName.Contains("brow") || fileName.Contains("scalp") ||
                                     fileName.Contains("occlusion") || fileName.Contains("tearline") ||
                                     fileName.Contains("cornea") || fileName.Contains("transparency") ||
                                     fileName.Contains("opacity");

                    bool changed = false;

                    // Ensure proper alpha settings for transparency textures
                    if (needsAlpha)
                    {
                        if (importer.alphaSource != TextureImporterAlphaSource.FromInput)
                        {
                            importer.alphaSource = TextureImporterAlphaSource.FromInput;
                            changed = true;
                        }
                        if (!importer.alphaIsTransparency)
                        {
                            importer.alphaIsTransparency = true;
                            changed = true;
                        }
                    }

                    // Ensure sRGB for diffuse textures
                    if (fileName.Contains("diffuse") || fileName.Contains("albedo") || fileName.Contains("color"))
                    {
                        if (!importer.sRGBTexture)
                        {
                            importer.sRGBTexture = true;
                            changed = true;
                        }
                    }

                    // Disable sRGB for normal maps
                    if (fileName.Contains("normal") || fileName.Contains("_n"))
                    {
                        if (importer.sRGBTexture)
                        {
                            importer.sRGBTexture = false;
                            changed = true;
                        }
                        if (importer.textureType != TextureImporterType.NormalMap)
                        {
                            importer.textureType = TextureImporterType.NormalMap;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        importer.SaveAndReimport();
                        fixed_count++;
                        Debug.Log($"  Fixed import: {Path.GetFileName(assetPath)}");
                    }
                }
            }

            Debug.Log($"Fixed {fixed_count} texture import settings");
        }

        private static Dictionary<string, Texture2D> BuildTextureIndex()
        {
            var index = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { CHARACTER_ROOT });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                if (tex != null)
                {
                    string key = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    if (!index.ContainsKey(key))
                        index[key] = tex;
                }
            }

            return index;
        }

        private static void FixAllHeadMaterials(Dictionary<string, Texture2D> textureIndex)
        {
            // Ensure fixed materials folder exists
            if (!AssetDatabase.IsValidFolder(FIXED_MATERIALS_PATH))
            {
                AssetDatabase.CreateFolder(Path.GetDirectoryName(FIXED_MATERIALS_PATH), "Materials_Fixed");
            }

            // Find all head-related materials
            string[] materialFolders = {
                $"{CHARACTER_ROOT}/Materials",
                $"{CHARACTER_ROOT}/Materials_Rebuilt"
            };

            foreach (var folder in materialFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;

                var matGuids = AssetDatabase.FindAssets("t:Material", new[] { folder });
                foreach (var guid in matGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

                    if (mat == null) continue;

                    string matName = mat.name.ToLowerInvariant();
                    if (!IsHeadRelatedMaterial(matName)) continue;

                    // Fix the material
                    FixMaterial(mat, matName, textureIndex);
                }
            }

            AssetDatabase.SaveAssets();
        }

        private static void FixMaterial(Material mat, string matNameLower, Dictionary<string, Texture2D> textureIndex)
        {
            bool changed = false;

            // Ensure Standard shader
            var standardShader = Shader.Find("Standard");
            if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
            {
                mat.shader = standardShader;
                changed = true;
                Debug.Log($"  {mat.name}: Fixed broken shader -> Standard");
            }
            else if (!mat.shader.name.Contains("Standard"))
            {
                mat.shader = standardShader;
                changed = true;
            }

            // Ensure texture is assigned
            bool hasMainTex = mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null;
            if (!hasMainTex)
            {
                var tex = FindTextureForMaterial(mat.name, textureIndex);
                if (tex != null)
                {
                    mat.SetTexture("_MainTex", tex);
                    changed = true;
                    Debug.Log($"  {mat.name}: Assigned _MainTex -> {tex.name}");
                }
            }

            // Set correct render mode
            int targetMode = DetermineRenderMode(matNameLower);
            if (mat.HasProperty("_Mode"))
            {
                int currentMode = (int)mat.GetFloat("_Mode");
                if (currentMode != targetMode)
                {
                    SetStandardShaderRenderMode(mat, targetMode);
                    changed = true;
                    Debug.Log($"  {mat.name}: Changed mode {currentMode} -> {targetMode}");
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(mat);
            }
        }

        private static int DetermineRenderMode(string matNameLower)
        {
            // Overlays: Fade (2)
            if (matNameLower.Contains("occlusion") || matNameLower.Contains("tearline") ||
                matNameLower.Contains("cornea"))
            {
                return 2; // Fade
            }

            // Hair/brow/lash: Cutout (1)
            if (matNameLower.Contains("hair") || matNameLower.Contains("scalp") ||
                matNameLower.Contains("brow") || matNameLower.Contains("lash") ||
                matNameLower.Contains("babyhair") || matNameLower.Contains("transparency"))
            {
                return 1; // Cutout
            }

            // Eye base/skin: Opaque (0)
            return 0;
        }

        private static void SetStandardShaderRenderMode(Material mat, int mode)
        {
            switch (mode)
            {
                case 0: // Opaque
                    mat.SetFloat("_Mode", 0);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    mat.SetInt("_ZWrite", 1);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 2000;
                    break;

                case 1: // Cutout
                    mat.SetFloat("_Mode", 1);
                    mat.SetFloat("_Cutoff", 0.5f);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    mat.SetInt("_ZWrite", 1);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 2450;
                    break;

                case 2: // Fade
                    mat.SetFloat("_Mode", 2);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 3000;
                    break;

                case 3: // Transparent
                    mat.SetFloat("_Mode", 3);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 3000;
                    break;
            }
        }

        private static Texture2D FindTextureForMaterial(string materialName, Dictionary<string, Texture2D> textureIndex)
        {
            string key = materialName.ToLowerInvariant();

            // Direct match
            if (textureIndex.TryGetValue(key, out var tex))
                return tex;

            // Remove common prefixes/suffixes
            string normalized = key.Replace("std_", "").Replace("_diffuse", "").Replace("_mat", "");
            if (textureIndex.TryGetValue(normalized, out tex))
                return tex;

            // Try with _diffuse suffix
            if (textureIndex.TryGetValue(normalized + "_diffuse", out tex))
                return tex;

            // Fuzzy match
            foreach (var kvp in textureIndex)
            {
                if (kvp.Key.Contains(normalized) || normalized.Contains(kvp.Key))
                {
                    if (!kvp.Key.Contains("normal") && !kvp.Key.Contains("_n"))
                        return kvp.Value;
                }
            }

            return null;
        }

        private static void UpdateAllPrefabs()
        {
            Debug.Log("\n--- UPDATING PREFABS ---");

            if (!AssetDatabase.IsValidFolder(PREFABS_PATH)) return;

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PREFABS_PATH });
            foreach (var guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UpdatePrefabMaterials(path);
            }
        }

        private static void UpdatePrefabMaterials(string prefabPath)
        {
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null) return;

            bool anyChanges = false;

            try
            {
                var renderers = prefabRoot.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    if (!IsHeadRelatedRenderer(renderer.gameObject.name)) continue;

                    var materials = renderer.sharedMaterials;
                    bool slotChanged = false;

                    for (int i = 0; i < materials.Length; i++)
                    {
                        var mat = materials[i];

                        // Skip valid materials
                        if (mat != null && mat.shader != null &&
                            mat.shader.name != "Hidden/InternalErrorShader")
                        {
                            continue;
                        }

                        // Try to find replacement
                        string matName = mat != null ? mat.name : $"slot{i}";
                        var replacement = FindReplacementMaterial(matName);

                        if (replacement != null)
                        {
                            materials[i] = replacement;
                            slotChanged = true;
                            Debug.Log($"  {prefabPath}: {renderer.name}[{i}] -> {replacement.name}");
                        }
                    }

                    if (slotChanged)
                    {
                        renderer.sharedMaterials = materials;
                        anyChanges = true;
                    }
                }

                if (anyChanges)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static Material FindReplacementMaterial(string matName)
        {
            string[] searchFolders = {
                $"{CHARACTER_ROOT}/Materials_Rebuilt",
                $"{CHARACTER_ROOT}/Materials_Fixed",
                $"{CHARACTER_ROOT}/Materials"
            };

            foreach (var folder in searchFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;

                string path = $"{folder}/{matName}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (mat != null && mat.shader != null &&
                    mat.shader.name != "Hidden/InternalErrorShader")
                {
                    return mat;
                }
            }

            return null;
        }

        private static void UpdateSceneInstance()
        {
            Debug.Log("\n--- UPDATING SCENE INSTANCE ---");

            var avatar = GameObject.Find("Avatar");
            if (avatar == null)
            {
                avatar = GameObject.Find("CharacterModel");
            }

            if (avatar == null)
            {
                Debug.Log("No scene instance found to update");
                return;
            }

            // If it's a prefab instance, revert overrides
            if (PrefabUtility.IsPartOfPrefabInstance(avatar))
            {
                var renderers = avatar.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    if (!IsHeadRelatedRenderer(renderer.gameObject.name)) continue;

                    // Revert material overrides
                    var so = new SerializedObject(renderer);
                    var matProp = so.FindProperty("m_Materials");

                    if (matProp != null)
                    {
                        PrefabUtility.RevertPropertyOverride(matProp, InteractionMode.AutomatedAction);
                        Debug.Log($"  Reverted material overrides on {renderer.name}");
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        private static void FixEyeMaterialSlotOrder()
        {
            // This would reorder material slots on CC_Base_Eye if needed
            // For now, just verify the order is correct
            Debug.Log("Eye slot order verification - no automatic reordering implemented");
            Debug.Log("Manual inspection recommended for CC_Base_Eye submesh assignments");
        }

        #endregion

        #region Runtime Report

        private static void GenerateRuntimeReport()
        {
            var avatar = GameObject.Find("Avatar");
            if (avatar == null)
            {
                avatar = GameObject.Find("CharacterModel");
            }

            if (avatar == null)
            {
                Debug.LogError("No character found in scene during Play Mode");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Character: {avatar.name}");
            sb.AppendLine($"Active: {avatar.activeInHierarchy}");

            var renderers = avatar.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (!IsHeadRelatedRenderer(renderer.gameObject.name)) continue;

                sb.AppendLine($"\n{renderer.gameObject.name}:");

                var materials = renderer.materials; // Runtime instance materials
                for (int i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];
                    if (mat == null)
                    {
                        sb.AppendLine($"  [{i}] NULL");
                        continue;
                    }

                    string texName = "null";
                    if (mat.HasProperty("_MainTex"))
                    {
                        var tex = mat.GetTexture("_MainTex");
                        texName = tex != null ? tex.name : "null";
                    }

                    float mode = mat.HasProperty("_Mode") ? mat.GetFloat("_Mode") : -1;
                    string modeStr = GetRenderModeString((int)mode);

                    sb.AppendLine($"  [{i}] {mat.name} | shader={mat.shader?.name} | mode={modeStr} | tex={texName}");
                }
            }

            Debug.Log(sb.ToString());
        }

        #endregion

        #region Utilities

        private static bool IsHeadRelatedRenderer(string name)
        {
            string nameLower = name.ToLowerInvariant();
            return EYE_RENDERERS.Any(k => nameLower.Contains(k)) ||
                   OVERLAY_RENDERERS.Any(k => nameLower.Contains(k)) ||
                   HAIR_RENDERERS.Any(k => nameLower.Contains(k)) ||
                   BROW_LASH_RENDERERS.Any(k => nameLower.Contains(k)) ||
                   HEAD_BODY_RENDERERS.Any(k => nameLower.Contains(k));
        }

        private static bool IsHeadRelatedMaterial(string name)
        {
            string nameLower = name.ToLowerInvariant();
            string[] keywords = {
                "eye", "iris", "sclera", "cornea", "occlusion", "tearline",
                "brow", "lash", "hair", "scalp", "babyhair", "head", "skin",
                "face", "neck", "body", "arm", "leg"
            };
            return keywords.Any(k => nameLower.Contains(k));
        }

        private static string CategorizeRenderer(string name)
        {
            string nameLower = name.ToLowerInvariant();

            if (nameLower.Contains("cc_base_eye") && !nameLower.Contains("occlusion"))
                return "eye_base";
            if (OVERLAY_RENDERERS.Any(k => nameLower.Contains(k)))
                return "overlay";
            if (HAIR_RENDERERS.Any(k => nameLower.Contains(k)))
                return "hair";
            if (BROW_LASH_RENDERERS.Any(k => nameLower.Contains(k)))
                return "brow_lash";
            if (HEAD_BODY_RENDERERS.Any(k => nameLower.Contains(k)))
                return "head_body";

            return "other";
        }

        private static bool IsTextureRequiredForCategory(string category)
        {
            return category == "eye_base" || category == "hair" || category == "brow_lash";
        }

        private static string GetRenderModeString(int mode)
        {
            switch (mode)
            {
                case 0: return "Opaque";
                case 1: return "Cutout";
                case 2: return "Fade";
                case 3: return "Transparent";
                default: return $"Unknown({mode})";
            }
        }

        private static string GetHierarchyPath(GameObject obj, Transform root)
        {
            var path = new List<string>();
            var current = obj.transform;

            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }

        private static void SaveReport(DiagnosticReport report)
        {
            string dir = Path.GetDirectoryName(REPORT_PATH);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(REPORT_PATH, json);
            AssetDatabase.Refresh();

            Debug.Log($"Report saved: {REPORT_PATH}");
        }

        private static void PrintReport(DiagnosticReport report)
        {
            var sb = new StringBuilder();

            sb.AppendLine("\n========== HEAD RENDER DOCTOR REPORT ==========");
            sb.AppendLine($"Scene: {report.activeScenePath}");
            sb.AppendLine($"Runtime Prefab: {report.runtimeInstantiatedPrefabPath}");
            sb.AppendLine($"Prefabs Available: {report.availablePrefabs.Count}");

            if (report.criticalIssues.Count > 0)
            {
                sb.AppendLine($"\n!!! CRITICAL ISSUES ({report.criticalIssues.Count}) !!!");
                foreach (var issue in report.criticalIssues)
                {
                    sb.AppendLine($"  - {issue}");
                }
            }

            if (report.warnings.Count > 0)
            {
                sb.AppendLine($"\nWARNINGS ({report.warnings.Count}):");
                foreach (var warning in report.warnings.Take(10))
                {
                    sb.AppendLine($"  - {warning}");
                }
                if (report.warnings.Count > 10)
                    sb.AppendLine($"  ... and {report.warnings.Count - 10} more");
            }

            sb.AppendLine("\n--- PREFAB RENDERERS ---");
            foreach (var r in report.prefabRenderers)
            {
                sb.AppendLine($"\n{r.hierarchyPath} [{r.category}]");
                foreach (var slot in r.materialSlots)
                {
                    string texStatus = "NO_TEX";
                    var mainTex = slot.textures.FirstOrDefault(t => t.propertyName == "_MainTex");
                    if (mainTex != null && mainTex.fileExists)
                    {
                        texStatus = mainTex.sanityCheck ?? "OK";
                    }

                    string issues = slot.issues.Count > 0 ? $" [!{slot.issues.Count}]" : "";
                    sb.AppendLine($"  [{slot.slotIndex}] {slot.materialName} | {slot.renderMode} | tex={texStatus}{issues}");
                }
            }

            if (report.runtimeModifiers.Count > 0)
            {
                sb.AppendLine($"\n--- RUNTIME MODIFIERS ({report.runtimeModifiers.Count}) ---");
                foreach (var mod in report.runtimeModifiers.Take(5))
                {
                    sb.AppendLine($"  {Path.GetFileName(mod.filePath)}:{mod.lineNumber}");
                }
            }

            sb.AppendLine(report.rootCauseAnalysis);

            Debug.Log(sb.ToString());
        }

        #endregion

        #region Batch Mode

        public static void BatchDiagnostic()
        {
            RunDiagnostic();
        }

        public static void BatchFix()
        {
            RunCompleteFix();
        }

        public static void BatchAll()
        {
            RunDiagnostic();
            RunCompleteFix();
            RunDiagnostic(); // Re-run to verify
        }

        #endregion
    }
}
