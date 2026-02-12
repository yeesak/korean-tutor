using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Comprehensive character render audit tool.
    /// Generates detailed JSON report for diagnosing eye/hair/skin rendering issues.
    /// </summary>
    public class CharacterRenderAudit : EditorWindow
    {
        private const string CHARACTER_ROOT = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA";
        private const string PREFAB_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
        private const string OUTPUT_PATH = "Assets/Temp/character_render_audit.json";

        #region Data Classes

        [System.Serializable]
        public class TextureInfo
        {
            public string name;
            public string assetPath;
            public string guid;
            public int width;
            public int height;
            public string format;
            public string importType;
            public bool sRGB;
            public string alphaSource;
            public bool alphaIsTransparency;
            public bool mipmaps;
            public string compression;
            public bool isSuspicious;
            public string suspiciousReason;
        }

        [System.Serializable]
        public class MaterialSlotInfo
        {
            public int slotIndex;
            public string materialName;
            public string materialAssetPath;
            public string materialGuid;
            public bool isInstanceMaterial;
            public string shaderName;
            public bool shaderSupported;
            public int renderQueue;
            public string renderMode;
            public float modeValue;
            public string mainTexProperty;
            public TextureInfo mainTex;
            public string baseMapProperty;
            public TextureInfo baseMap;
            public TextureInfo bumpMap;
            public string colorHex;
            public float cutoff;
            public float metallic;
            public float smoothness;
            public bool zWrite;
            public int srcBlend;
            public int dstBlend;
            public List<string> keywords = new List<string>();
            public List<string> issues = new List<string>();
        }

        [System.Serializable]
        public class RendererInfo
        {
            public string gameObjectName;
            public string hierarchyPath;
            public string meshName;
            public int subMeshCount;
            public string category;
            public List<MaterialSlotInfo> slots = new List<MaterialSlotInfo>();
        }

        [System.Serializable]
        public class AuditReport
        {
            public string timestamp;
            public string mode;
            public string characterPath;
            public string prefabPath;
            public string prefabGuid;
            public List<RendererInfo> allRenderers = new List<RendererInfo>();
            public List<RendererInfo> eyeRenderers = new List<RendererInfo>();
            public List<RendererInfo> eyeOverlayRenderers = new List<RendererInfo>();
            public List<RendererInfo> hairRenderers = new List<RendererInfo>();
            public List<RendererInfo> skinRenderers = new List<RendererInfo>();
            public List<TextureInfo> suspiciousTextures = new List<TextureInfo>();
            public List<string> criticalIssues = new List<string>();
            public string rootCauseAnalysis;
            public string recommendedFix;
        }

        #endregion

        #region Menu Items

        [MenuItem("Tools/Character Audit/FULL AUDIT (Generate JSON Report)")]
        public static void RunFullAudit()
        {
            Debug.Log("=== CHARACTER RENDER AUDIT ===\n");

            var report = GenerateAuditReport(false);
            SaveReport(report);
            AnalyzeRootCause(report);
            SaveReport(report); // Save again with root cause
            PrintSummary(report);

            Debug.Log($"\nFull report saved to: {OUTPUT_PATH}");
            Debug.Log("\n=== END AUDIT ===");
        }

        [MenuItem("Tools/Character Audit/AUDIT RUNTIME (Play Mode Only)")]
        public static void RunRuntimeAudit()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("This audit requires Play Mode. Enter Play Mode first.");
                return;
            }

            Debug.Log("=== RUNTIME CHARACTER AUDIT ===\n");

            var report = GenerateAuditReport(true);
            SaveReport(report);
            AnalyzeRootCause(report);
            SaveReport(report);
            PrintSummary(report);

            Debug.Log($"\nFull report saved to: {OUTPUT_PATH}");
            Debug.Log("\n=== END RUNTIME AUDIT ===");
        }

        #endregion

        #region Report Generation

        private static AuditReport GenerateAuditReport(bool runtimeMode)
        {
            var report = new AuditReport
            {
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                mode = runtimeMode ? "Runtime (Play Mode)" : "Asset (Edit Mode)",
                prefabPath = PREFAB_PATH,
                prefabGuid = AssetDatabase.AssetPathToGUID(PREFAB_PATH)
            };

            GameObject characterRoot = null;

            if (runtimeMode && Application.isPlaying)
            {
                // Find runtime instance
                var avatar = GameObject.Find("Avatar");
                if (avatar != null)
                {
                    var charModel = avatar.transform.Find("CharacterModel");
                    if (charModel != null)
                    {
                        characterRoot = charModel.gameObject;
                        report.characterPath = "Avatar/CharacterModel (runtime instance)";
                    }
                }
            }

            if (characterRoot == null)
            {
                // Load prefab asset
                characterRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
                if (characterRoot != null)
                {
                    report.characterPath = PREFAB_PATH;
                }
            }

            if (characterRoot == null)
            {
                report.criticalIssues.Add("CRITICAL: Cannot find character - neither runtime instance nor prefab asset");
                return report;
            }

            // Analyze all renderers
            var renderers = characterRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var info = AnalyzeRenderer(renderer, characterRoot.transform, runtimeMode);
                report.allRenderers.Add(info);

                // Categorize
                string nameLower = info.gameObjectName.ToLowerInvariant();
                if (nameLower.Contains("cc_base_eye") && !nameLower.Contains("occlusion"))
                {
                    report.eyeRenderers.Add(info);
                }
                else if (nameLower.Contains("occlusion") || nameLower.Contains("tearline") || nameLower.Contains("cornea"))
                {
                    report.eyeOverlayRenderers.Add(info);
                }
                else if (nameLower.Contains("hair") || nameLower.Contains("scalp") || nameLower.Contains("babyhair"))
                {
                    report.hairRenderers.Add(info);
                }
                else if (nameLower.Contains("skin") || nameLower.Contains("body") || nameLower.Contains("head"))
                {
                    report.skinRenderers.Add(info);
                }

                // Collect critical issues
                foreach (var slot in info.slots)
                {
                    foreach (var issue in slot.issues)
                    {
                        if (issue.StartsWith("CRITICAL"))
                        {
                            report.criticalIssues.Add($"{info.hierarchyPath}[{slot.slotIndex}]: {issue}");
                        }
                    }
                }
            }

            // Scan for suspicious textures in character folder
            ScanSuspiciousTextures(report);

            return report;
        }

        private static RendererInfo AnalyzeRenderer(Renderer renderer, Transform root, bool runtimeMode)
        {
            var info = new RendererInfo
            {
                gameObjectName = renderer.gameObject.name,
                hierarchyPath = GetHierarchyPath(renderer.gameObject, root)
            };

            // Categorize by name
            string nameLower = renderer.name.ToLowerInvariant();
            if (nameLower.Contains("cc_base_eye") && !nameLower.Contains("occlusion"))
                info.category = "EYE_BASE";
            else if (nameLower.Contains("occlusion"))
                info.category = "EYE_OCCLUSION";
            else if (nameLower.Contains("tearline"))
                info.category = "EYE_TEARLINE";
            else if (nameLower.Contains("cornea"))
                info.category = "EYE_CORNEA";
            else if (nameLower.Contains("eyelash"))
                info.category = "EYELASH";
            else if (nameLower.Contains("hair") || nameLower.Contains("scalp"))
                info.category = "HAIR";
            else if (nameLower.Contains("skin") || nameLower.Contains("body"))
                info.category = "SKIN";
            else if (nameLower.Contains("teeth") || nameLower.Contains("tongue"))
                info.category = "MOUTH";
            else
                info.category = "OTHER";

            if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                info.meshName = smr.sharedMesh.name;
                info.subMeshCount = smr.sharedMesh.subMeshCount;
            }
            else if (renderer is MeshRenderer mr)
            {
                var mf = renderer.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    info.meshName = mf.sharedMesh.name;
                    info.subMeshCount = mf.sharedMesh.subMeshCount;
                }
            }

            // Analyze materials
            Material[] mats = runtimeMode ? renderer.materials : renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var slotInfo = AnalyzeMaterialSlot(mats[i], i, runtimeMode, info.category);
                info.slots.Add(slotInfo);
            }

            return info;
        }

        private static MaterialSlotInfo AnalyzeMaterialSlot(Material mat, int index, bool runtimeMode, string category)
        {
            var info = new MaterialSlotInfo { slotIndex = index };

            if (mat == null)
            {
                info.materialName = "NULL";
                info.issues.Add("CRITICAL: NULL material reference");
                return info;
            }

            info.materialName = mat.name;
            info.isInstanceMaterial = mat.name.Contains("(Instance)");

            string assetPath = AssetDatabase.GetAssetPath(mat);
            info.materialAssetPath = string.IsNullOrEmpty(assetPath) ? "(runtime/embedded)" : assetPath;
            if (!string.IsNullOrEmpty(assetPath))
                info.materialGuid = AssetDatabase.AssetPathToGUID(assetPath);

            // Check for Default-Material
            if (mat.name.Contains("Default-Material") || mat.name.Contains("Default Material"))
            {
                info.issues.Add("CRITICAL: Default-Material - slot has no valid material assigned");
            }

            // Shader analysis
            if (mat.shader == null)
            {
                info.shaderName = "NULL";
                info.shaderSupported = false;
                info.issues.Add("CRITICAL: NULL shader");
            }
            else
            {
                info.shaderName = mat.shader.name;
                info.shaderSupported = mat.shader.isSupported;

                if (mat.shader.name == "Hidden/InternalErrorShader")
                {
                    info.issues.Add("CRITICAL: Error shader (Broken PPtr / missing shader)");
                }
                else if (!mat.shader.isSupported)
                {
                    info.issues.Add("WARNING: Shader not supported on this platform");
                }
            }

            info.renderQueue = mat.renderQueue;

            // Render mode detection
            if (mat.HasProperty("_Mode"))
            {
                info.modeValue = mat.GetFloat("_Mode");
                info.renderMode = info.modeValue switch
                {
                    0 => "Opaque",
                    1 => "Cutout",
                    2 => "Fade",
                    3 => "Transparent",
                    _ => $"Unknown({info.modeValue})"
                };
            }
            else
            {
                info.renderMode = "N/A (no _Mode property)";
            }

            // ZWrite
            if (mat.HasProperty("_ZWrite"))
                info.zWrite = mat.GetInt("_ZWrite") == 1;
            else
                info.zWrite = true; // Default assumption

            // Blend modes
            if (mat.HasProperty("_SrcBlend"))
                info.srcBlend = mat.GetInt("_SrcBlend");
            if (mat.HasProperty("_DstBlend"))
                info.dstBlend = mat.GetInt("_DstBlend");

            // Color
            if (mat.HasProperty("_Color"))
            {
                Color c = mat.GetColor("_Color");
                info.colorHex = ColorUtility.ToHtmlStringRGBA(c);
            }
            else if (mat.HasProperty("_BaseColor"))
            {
                Color c = mat.GetColor("_BaseColor");
                info.colorHex = ColorUtility.ToHtmlStringRGBA(c);
            }

            // Cutoff
            if (mat.HasProperty("_Cutoff"))
                info.cutoff = mat.GetFloat("_Cutoff");

            // Metallic/Smoothness
            if (mat.HasProperty("_Metallic"))
                info.metallic = mat.GetFloat("_Metallic");
            if (mat.HasProperty("_Glossiness"))
                info.smoothness = mat.GetFloat("_Glossiness");
            else if (mat.HasProperty("_Smoothness"))
                info.smoothness = mat.GetFloat("_Smoothness");

            // Keywords
            foreach (var kw in mat.shaderKeywords)
            {
                info.keywords.Add(kw);
            }

            // Texture analysis - _MainTex
            info.mainTexProperty = "_MainTex";
            if (mat.HasProperty("_MainTex"))
            {
                var tex = mat.GetTexture("_MainTex") as Texture2D;
                if (tex != null)
                {
                    info.mainTex = AnalyzeTexture(tex);
                }
                else
                {
                    // Check if this category needs a texture
                    bool needsTexture = category == "EYE_BASE" || category == "SKIN" ||
                                       category == "HAIR" || category == "EYELASH";
                    if (needsTexture)
                    {
                        info.issues.Add("CRITICAL: _MainTex is NULL - eye/skin/hair needs diffuse texture");
                    }
                }
            }

            // Texture analysis - _BaseMap (URP)
            info.baseMapProperty = "_BaseMap";
            if (mat.HasProperty("_BaseMap"))
            {
                var tex = mat.GetTexture("_BaseMap") as Texture2D;
                if (tex != null)
                {
                    info.baseMap = AnalyzeTexture(tex);
                }
            }

            // Normal map
            if (mat.HasProperty("_BumpMap"))
            {
                var tex = mat.GetTexture("_BumpMap") as Texture2D;
                if (tex != null)
                {
                    info.bumpMap = AnalyzeTexture(tex);
                }
            }

            // Category-specific validation
            ValidateByCategory(info, category);

            return info;
        }

        private static void ValidateByCategory(MaterialSlotInfo info, string category)
        {
            switch (category)
            {
                case "EYE_BASE":
                    // Eye base (iris/sclera) should be Opaque with valid texture
                    if (info.renderMode == "Fade" || info.renderMode == "Transparent")
                    {
                        info.issues.Add("WARNING: Eye base material is transparent - should typically be Opaque");
                    }
                    if (info.mainTex == null && info.baseMap == null)
                    {
                        info.issues.Add("CRITICAL: Eye base has no diffuse texture - will appear white");
                    }
                    break;

                case "EYE_CORNEA":
                case "EYE_OCCLUSION":
                case "EYE_TEARLINE":
                    // Overlays should be Fade/Transparent, NOT Opaque
                    if (info.renderMode == "Opaque")
                    {
                        info.issues.Add("CRITICAL: Eye overlay is OPAQUE - will block iris/pupil visibility");
                    }
                    if (info.zWrite && info.renderMode != "Opaque")
                    {
                        info.issues.Add("WARNING: Transparent overlay has ZWrite ON - may cause sorting issues");
                    }
                    if (info.renderQueue < 2500)
                    {
                        info.issues.Add("WARNING: Eye overlay renderQueue too low - may render before eye base");
                    }
                    break;

                case "HAIR":
                case "EYELASH":
                    // Hair/eyelash should be Cutout or Transparent
                    if (info.renderMode == "Opaque")
                    {
                        info.issues.Add("WARNING: Hair/eyelash is Opaque - should use Cutout or Fade for transparency");
                    }
                    break;
            }
        }

        private static TextureInfo AnalyzeTexture(Texture2D tex)
        {
            if (tex == null) return null;

            var info = new TextureInfo
            {
                name = tex.name,
                width = tex.width,
                height = tex.height,
                format = tex.format.ToString()
            };

            string assetPath = AssetDatabase.GetAssetPath(tex);
            info.assetPath = assetPath;
            if (!string.IsNullOrEmpty(assetPath))
            {
                info.guid = AssetDatabase.AssetPathToGUID(assetPath);

                // Get import settings
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    info.importType = importer.textureType.ToString();
                    info.sRGB = importer.sRGBTexture;
                    info.alphaSource = importer.alphaSource.ToString();
                    info.alphaIsTransparency = importer.alphaIsTransparency;
                    info.mipmaps = importer.mipmapEnabled;
                    info.compression = importer.textureCompression.ToString();

                    // Check for suspicious import settings
                    string nameLower = tex.name.ToLowerInvariant();
                    bool isDiffuse = nameLower.Contains("diffuse") || nameLower.Contains("albedo") ||
                                    nameLower.Contains("basecolor") || nameLower.Contains("color");

                    if (isDiffuse && importer.textureType == TextureImporterType.NormalMap)
                    {
                        info.isSuspicious = true;
                        info.suspiciousReason = "Diffuse texture imported as NormalMap";
                    }
                }
            }

            // Check for suspicious dimensions
            if (tex.width <= 8 || tex.height <= 8)
            {
                info.isSuspicious = true;
                info.suspiciousReason = $"Extremely small texture ({tex.width}x{tex.height})";
            }

            return info;
        }

        private static void ScanSuspiciousTextures(AuditReport report)
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { CHARACTER_ROOT });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;

                var info = AnalyzeTexture(tex);
                if (info != null && info.isSuspicious)
                {
                    report.suspiciousTextures.Add(info);
                }
            }
        }

        #endregion

        #region Root Cause Analysis

        private static void AnalyzeRootCause(AuditReport report)
        {
            var causes = new List<string>();
            string primaryCause = "UNKNOWN";
            string recommendedFix = "";

            // Check 1: Missing/blank eye textures
            foreach (var renderer in report.eyeRenderers)
            {
                foreach (var slot in renderer.slots)
                {
                    if (slot.mainTex == null && slot.baseMap == null)
                    {
                        causes.Add($"CAUSE 1: Eye texture missing on {renderer.gameObjectName}[{slot.slotIndex}] ({slot.materialName})");
                        primaryCause = "MISSING_EYE_TEXTURE";
                    }
                    else if (slot.mainTex != null && slot.mainTex.isSuspicious)
                    {
                        causes.Add($"CAUSE 2: Eye texture suspicious: {slot.mainTex.suspiciousReason}");
                        if (primaryCause == "UNKNOWN") primaryCause = "BAD_TEXTURE_IMPORT";
                    }
                }
            }

            // Check 2: Opaque overlays blocking eyes
            foreach (var renderer in report.eyeOverlayRenderers)
            {
                foreach (var slot in renderer.slots)
                {
                    if (slot.renderMode == "Opaque")
                    {
                        causes.Add($"CAUSE 3: Opaque overlay blocking eye: {renderer.gameObjectName}[{slot.slotIndex}] ({slot.materialName}) is OPAQUE");
                        if (primaryCause == "UNKNOWN" || primaryCause == "MISSING_EYE_TEXTURE")
                        {
                            primaryCause = "OPAQUE_OVERLAY_BLOCKING";
                        }
                    }
                }
            }

            // Check 3: Wrong render queue
            int eyeBaseQueue = -1;
            int overlayQueue = -1;
            foreach (var renderer in report.eyeRenderers)
            {
                foreach (var slot in renderer.slots)
                {
                    if (!slot.materialName.ToLower().Contains("cornea"))
                        eyeBaseQueue = Mathf.Max(eyeBaseQueue, slot.renderQueue);
                }
            }
            foreach (var renderer in report.eyeOverlayRenderers)
            {
                foreach (var slot in renderer.slots)
                {
                    overlayQueue = Mathf.Max(overlayQueue, slot.renderQueue);
                }
            }
            if (eyeBaseQueue > 0 && overlayQueue > 0 && overlayQueue <= eyeBaseQueue)
            {
                causes.Add($"CAUSE 4: Overlay renderQueue ({overlayQueue}) <= eye base ({eyeBaseQueue}) - wrong render order");
            }

            // Check 4: Runtime material instance issues
            foreach (var renderer in report.allRenderers)
            {
                foreach (var slot in renderer.slots)
                {
                    if (slot.materialName.Contains("Default-Material"))
                    {
                        causes.Add($"CAUSE 5: Runtime material override created Default-Material on {renderer.gameObjectName}[{slot.slotIndex}]");
                        if (primaryCause == "UNKNOWN") primaryCause = "RUNTIME_MATERIAL_OVERRIDE";
                    }
                }
            }

            // Determine recommended fix
            switch (primaryCause)
            {
                case "MISSING_EYE_TEXTURE":
                    recommendedFix = "Assign correct eye diffuse textures (Std_Eye_L_Diffuse, Std_Eye_R_Diffuse) to eye materials' _MainTex property.";
                    break;
                case "BAD_TEXTURE_IMPORT":
                    recommendedFix = "Fix texture import settings: set eye diffuse textures to TextureType=Default, sRGB=true.";
                    break;
                case "OPAQUE_OVERLAY_BLOCKING":
                    recommendedFix = "Change Cornea/EyeOcclusion/TearLine materials from Opaque to Fade mode, set ZWrite=OFF, renderQueue>=3000.";
                    break;
                case "RUNTIME_MATERIAL_OVERRIDE":
                    recommendedFix = "Fix runtime material scripts (SceneCharacterInitializer, RuntimeMaterialRepairGuard) to preserve original material references.";
                    break;
                default:
                    recommendedFix = "Manual inspection required - check prefab material assignments in Unity Inspector.";
                    break;
            }

            report.rootCauseAnalysis = $"PRIMARY CAUSE: {primaryCause}\n\nEvidence:\n" + string.Join("\n", causes);
            report.recommendedFix = recommendedFix;
        }

        #endregion

        #region Output

        private static void SaveReport(AuditReport report)
        {
            string dir = Path.GetDirectoryName(OUTPUT_PATH);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(OUTPUT_PATH, json);
            AssetDatabase.Refresh();
        }

        private static void PrintSummary(AuditReport report)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("\n========== AUDIT SUMMARY ==========");
            sb.AppendLine($"Mode: {report.mode}");
            sb.AppendLine($"Character: {report.characterPath}");
            sb.AppendLine($"Total Renderers: {report.allRenderers.Count}");
            sb.AppendLine($"Eye Renderers: {report.eyeRenderers.Count}");
            sb.AppendLine($"Eye Overlay Renderers: {report.eyeOverlayRenderers.Count}");
            sb.AppendLine($"Hair Renderers: {report.hairRenderers.Count}");

            if (report.criticalIssues.Count > 0)
            {
                sb.AppendLine($"\n!!! CRITICAL ISSUES ({report.criticalIssues.Count}) !!!");
                foreach (var issue in report.criticalIssues.Take(10))
                {
                    sb.AppendLine($"  - {issue}");
                }
                if (report.criticalIssues.Count > 10)
                    sb.AppendLine($"  ... and {report.criticalIssues.Count - 10} more");
            }

            sb.AppendLine("\n--- EYE MATERIALS ---");
            foreach (var renderer in report.eyeRenderers)
            {
                foreach (var slot in renderer.slots)
                {
                    string texName = slot.mainTex?.name ?? slot.baseMap?.name ?? "NO TEXTURE";
                    string texSize = slot.mainTex != null ? $"{slot.mainTex.width}x{slot.mainTex.height}" : "N/A";
                    sb.AppendLine($"  {renderer.gameObjectName}[{slot.slotIndex}]: {slot.materialName}");
                    sb.AppendLine($"    Shader: {slot.shaderName}, Mode: {slot.renderMode}, Queue: {slot.renderQueue}");
                    sb.AppendLine($"    Texture: {texName} ({texSize})");
                    if (slot.issues.Count > 0)
                    {
                        foreach (var issue in slot.issues)
                            sb.AppendLine($"    >> {issue}");
                    }
                }
            }

            sb.AppendLine("\n--- EYE OVERLAY MATERIALS ---");
            foreach (var renderer in report.eyeOverlayRenderers)
            {
                foreach (var slot in renderer.slots)
                {
                    sb.AppendLine($"  {renderer.gameObjectName}[{slot.slotIndex}]: {slot.materialName}");
                    sb.AppendLine($"    Shader: {slot.shaderName}, Mode: {slot.renderMode}, Queue: {slot.renderQueue}, ZWrite: {slot.zWrite}");
                    if (slot.issues.Count > 0)
                    {
                        foreach (var issue in slot.issues)
                            sb.AppendLine($"    >> {issue}");
                    }
                }
            }

            sb.AppendLine("\n--- ROOT CAUSE ANALYSIS ---");
            sb.AppendLine(report.rootCauseAnalysis);

            sb.AppendLine("\n--- RECOMMENDED FIX ---");
            sb.AppendLine(report.recommendedFix);

            Debug.Log(sb.ToString());
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

        #endregion
    }
}
