using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Precise diagnostic and repair tool for character eye/head materials.
    /// Identifies exact root cause of white-eye issue and fixes via Unity API.
    /// </summary>
    public class CharacterEyeHeadFixTool : EditorWindow
    {
        private const string CHARACTER_ROOT = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA";
        private const string PREFAB_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
        private const string REPORT_PATH = "Assets/Temp/eye_rootcause_report.json";
        private const string FIXED_MATERIALS_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Materials_Fixed";

        // Specific renderer names to check
        private static readonly string[] EYE_RENDERER_KEYWORDS = {
            "cc_base_eye", "eyeocclusion", "tearline", "cornea"
        };

        private static readonly string[] HEAD_RENDERER_KEYWORDS = {
            "brow", "eyebrow", "lash", "eyelash", "hair", "scalp", "head", "skin", "body"
        };

        #region Data Classes

        public enum RootCause
        {
            UNKNOWN,
            EYEBASE_MISSING_TEXTURE,
            OVERLAY_OCCLUDING_EYE,
            WRONG_TEXTURE_MAPPED_TO_EYE,
            MATERIAL_BROKEN_PPTR,
            PREFAB_NOT_USING_FIXED_MATERIALS
        }

        [System.Serializable]
        public class MaterialSlotDiag
        {
            public int slotIndex;
            public string materialName;
            public string materialAssetPath;
            public string shaderName;
            public bool shaderIsNull;
            public bool shaderIsError;
            public int renderQueue;
            public float standardMode;
            public string standardModeDesc;
            public bool hasMainTex;
            public bool mainTexAssigned;
            public string mainTexName;
            public bool hasBaseMap;
            public bool baseMapAssigned;
            public string baseMapName;
            public List<string> keywords = new List<string>();
            public string issue;
        }

        [System.Serializable]
        public class RendererDiag
        {
            public string gameObjectName;
            public string hierarchyPath;
            public string category; // "eye_base", "overlay", "hair", etc.
            public List<MaterialSlotDiag> slots = new List<MaterialSlotDiag>();
        }

        [System.Serializable]
        public class RootCauseReport
        {
            public string prefabPath;
            public string timestamp;
            public string rootCause;
            public string rootCauseDetails;
            public List<RendererDiag> eyeRenderers = new List<RendererDiag>();
            public List<RendererDiag> headRenderers = new List<RendererDiag>();
            public List<string> criticalIssues = new List<string>();
        }

        #endregion

        #region Menu Items

        [MenuItem("Tools/Character Setup/DIAG - Eye Root Cause Report")]
        public static void RunDiag()
        {
            Debug.Log("=== EYE ROOT CAUSE DIAGNOSTIC ===\n");

            var report = GenerateRootCauseReport();
            SaveReport(report);
            PrintReport(report);

            Debug.Log("\n=== END DIAGNOSTIC ===");
        }

        [MenuItem("Tools/Character Setup/FIX - Eye+Head Repair (API Rebuild)")]
        public static void RunFix()
        {
            Debug.Log("=== EYE/HEAD REPAIR ===\n");

            // Step 1: Build texture index
            var textureIndex = BuildTextureIndex();
            Debug.Log($"Texture index: {textureIndex.Count} entries");

            // Step 2: Fix materials and update prefab
            FixMaterialsAndPrefab(textureIndex);

            // Step 3: Save everything
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("\n=== END FIX ===");
            Debug.Log("Run VERIFY to confirm.");
        }

        [MenuItem("Tools/Character Setup/VERIFY - Eye Result Check")]
        public static bool RunVerify()
        {
            Debug.Log("=== EYE RESULT VERIFICATION ===\n");

            var issues = new List<string>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);

            if (prefab == null)
            {
                Debug.LogError("FAIL: Cannot load prefab");
                return false;
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                string goName = renderer.gameObject.name.ToLowerInvariant();
                string path = GetHierarchyPath(renderer.gameObject, prefab.transform);

                // Check CC_Base_Eye
                if (goName.Contains("cc_base_eye"))
                {
                    var mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var mat = mats[i];
                        if (mat == null)
                        {
                            issues.Add($"CC_Base_Eye[{i}]: NULL material");
                            continue;
                        }

                        bool hasTex = (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null) ||
                                     (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null);

                        if (!hasTex)
                        {
                            issues.Add($"CC_Base_Eye[{i}]: {mat.name} has NO texture assigned");
                        }

                        // Check shader
                        if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                        {
                            issues.Add($"CC_Base_Eye[{i}]: {mat.name} has broken shader");
                        }
                    }
                }

                // Check overlays are NOT opaque
                if (goName.Contains("eyeocclusion") || goName.Contains("tearline") || goName.Contains("cornea"))
                {
                    var mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var mat = mats[i];
                        if (mat == null) continue;

                        if (mat.HasProperty("_Mode"))
                        {
                            float mode = mat.GetFloat("_Mode");
                            if (mode == 0) // Opaque
                            {
                                issues.Add($"{goName}[{i}]: {mat.name} is OPAQUE (should be FADE)");
                            }
                        }
                    }
                }

                // Check hair/brow/lash have textures
                if (goName.Contains("hair") || goName.Contains("brow") || goName.Contains("lash") || goName.Contains("scalp"))
                {
                    var mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var mat = mats[i];
                        if (mat == null)
                        {
                            issues.Add($"{goName}[{i}]: NULL material");
                            continue;
                        }

                        bool hasTex = (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null) ||
                                     (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null);

                        if (!hasTex)
                        {
                            issues.Add($"{goName}[{i}]: {mat.name} missing texture");
                        }
                    }
                }
            }

            // Print results
            if (issues.Count == 0)
            {
                Debug.Log("=== PASS: All eye/head materials verified OK! ===");
                return true;
            }
            else
            {
                Debug.LogError($"=== FAIL: {issues.Count} issues ===");
                foreach (var issue in issues)
                {
                    Debug.LogError($"  - {issue}");
                }
                return false;
            }
        }

        #endregion

        #region Diagnostic Implementation

        private static RootCauseReport GenerateRootCauseReport()
        {
            var report = new RootCauseReport
            {
                prefabPath = PREFAB_PATH,
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            if (prefab == null)
            {
                report.rootCause = RootCause.UNKNOWN.ToString();
                report.rootCauseDetails = "Cannot load prefab";
                return report;
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);

            // Analyze eye renderers
            bool eyeBaseHasTexture = false;
            bool overlayIsOpaque = false;
            string eyeBaseMaterialPath = "";
            string eyeBaseTextureName = "";

            foreach (var renderer in renderers)
            {
                string goName = renderer.gameObject.name.ToLowerInvariant();
                string path = GetHierarchyPath(renderer.gameObject, prefab.transform);

                bool isEyeRenderer = EYE_RENDERER_KEYWORDS.Any(k => goName.Contains(k));
                bool isHeadRenderer = HEAD_RENDERER_KEYWORDS.Any(k => goName.Contains(k));

                if (!isEyeRenderer && !isHeadRenderer) continue;

                var rendererDiag = new RendererDiag
                {
                    gameObjectName = renderer.gameObject.name,
                    hierarchyPath = path
                };

                // Categorize
                if (goName.Contains("cc_base_eye"))
                    rendererDiag.category = "eye_base";
                else if (goName.Contains("eyeocclusion") || goName.Contains("tearline") || goName.Contains("cornea"))
                    rendererDiag.category = "overlay";
                else if (goName.Contains("hair") || goName.Contains("scalp"))
                    rendererDiag.category = "hair";
                else if (goName.Contains("brow") || goName.Contains("lash"))
                    rendererDiag.category = "brow_lash";
                else
                    rendererDiag.category = "head_other";

                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var slot = AnalyzeMaterialSlot(mats[i], i);
                    rendererDiag.slots.Add(slot);

                    // Track eye base texture status
                    if (rendererDiag.category == "eye_base")
                    {
                        if (slot.mainTexAssigned || slot.baseMapAssigned)
                        {
                            eyeBaseHasTexture = true;
                            eyeBaseMaterialPath = slot.materialAssetPath;
                            eyeBaseTextureName = slot.mainTexAssigned ? slot.mainTexName : slot.baseMapName;
                        }
                    }

                    // Track overlay opaque status
                    if (rendererDiag.category == "overlay")
                    {
                        if (slot.standardMode == 0 && !slot.shaderIsNull)
                        {
                            overlayIsOpaque = true;
                            report.criticalIssues.Add($"OVERLAY OPAQUE: {path}[{i}] -> {slot.materialName} (mode=Opaque)");
                        }
                    }

                    // Track issues
                    if (slot.shaderIsNull || slot.shaderIsError)
                    {
                        report.criticalIssues.Add($"BROKEN SHADER: {path}[{i}] -> {slot.materialName}");
                    }
                }

                if (isEyeRenderer)
                    report.eyeRenderers.Add(rendererDiag);
                else
                    report.headRenderers.Add(rendererDiag);
            }

            // Determine root cause
            if (report.criticalIssues.Any(i => i.Contains("BROKEN SHADER")))
            {
                report.rootCause = RootCause.MATERIAL_BROKEN_PPTR.ToString();
                report.rootCauseDetails = "One or more materials have broken/null shader references";
            }
            else if (!eyeBaseHasTexture)
            {
                report.rootCause = RootCause.EYEBASE_MISSING_TEXTURE.ToString();
                report.rootCauseDetails = $"CC_Base_Eye material has no _MainTex or _BaseMap assigned";
            }
            else if (overlayIsOpaque)
            {
                report.rootCause = RootCause.OVERLAY_OCCLUDING_EYE.ToString();
                report.rootCauseDetails = "EyeOcclusion/TearLine/Cornea are OPAQUE, blocking the eyeball";
            }
            else if (eyeBaseHasTexture && eyeBaseTextureName != null &&
                    !eyeBaseTextureName.ToLowerInvariant().Contains("eye"))
            {
                report.rootCause = RootCause.WRONG_TEXTURE_MAPPED_TO_EYE.ToString();
                report.rootCauseDetails = $"Eye material has texture '{eyeBaseTextureName}' which may not be the iris/sclera";
            }
            else
            {
                report.rootCause = RootCause.UNKNOWN.ToString();
                report.rootCauseDetails = "Eye materials appear correctly configured. Issue may be elsewhere.";
            }

            return report;
        }

        private static MaterialSlotDiag AnalyzeMaterialSlot(Material mat, int index)
        {
            var slot = new MaterialSlotDiag { slotIndex = index };

            if (mat == null)
            {
                slot.materialName = "NULL";
                slot.shaderIsNull = true;
                slot.issue = "NULL material";
                return slot;
            }

            slot.materialName = mat.name;
            slot.materialAssetPath = AssetDatabase.GetAssetPath(mat);

            // Shader analysis
            if (mat.shader == null)
            {
                slot.shaderIsNull = true;
                slot.shaderName = "NULL";
                slot.issue = "Shader is null";
            }
            else if (mat.shader.name == "Hidden/InternalErrorShader")
            {
                slot.shaderIsError = true;
                slot.shaderName = mat.shader.name;
                slot.issue = "Shader is error shader (Broken PPtr)";
            }
            else
            {
                slot.shaderName = mat.shader.name;
            }

            slot.renderQueue = mat.renderQueue;

            // Standard mode
            if (mat.HasProperty("_Mode"))
            {
                slot.standardMode = mat.GetFloat("_Mode");
                slot.standardModeDesc = GetModeDescription((int)slot.standardMode);
            }
            else
            {
                slot.standardMode = -1;
                slot.standardModeDesc = "N/A";
            }

            // Texture properties
            slot.hasMainTex = mat.HasProperty("_MainTex");
            if (slot.hasMainTex)
            {
                var tex = mat.GetTexture("_MainTex");
                slot.mainTexAssigned = tex != null;
                slot.mainTexName = tex != null ? tex.name : "null";
            }

            slot.hasBaseMap = mat.HasProperty("_BaseMap");
            if (slot.hasBaseMap)
            {
                var tex = mat.GetTexture("_BaseMap");
                slot.baseMapAssigned = tex != null;
                slot.baseMapName = tex != null ? tex.name : "null";
            }

            // Keywords
            slot.keywords = mat.shaderKeywords.ToList();

            // Identify issues
            if (string.IsNullOrEmpty(slot.issue))
            {
                if (!slot.mainTexAssigned && !slot.baseMapAssigned)
                {
                    slot.issue = "No texture assigned";
                }
            }

            return slot;
        }

        private static string GetModeDescription(int mode)
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

        private static void SaveReport(RootCauseReport report)
        {
            string dir = Path.GetDirectoryName(REPORT_PATH);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(REPORT_PATH, json);
            AssetDatabase.Refresh();

            Debug.Log($"Report saved: {REPORT_PATH}");
        }

        private static void PrintReport(RootCauseReport report)
        {
            var sb = new StringBuilder();

            sb.AppendLine("\n========== ROOT CAUSE ANALYSIS ==========");
            sb.AppendLine($"ROOT CAUSE: {report.rootCause}");
            sb.AppendLine($"DETAILS: {report.rootCauseDetails}");

            if (report.criticalIssues.Count > 0)
            {
                sb.AppendLine($"\nCRITICAL ISSUES ({report.criticalIssues.Count}):");
                foreach (var issue in report.criticalIssues)
                {
                    sb.AppendLine($"  ! {issue}");
                }
            }

            sb.AppendLine("\n--- EYE RENDERERS ---");
            foreach (var r in report.eyeRenderers)
            {
                sb.AppendLine($"\n{r.hierarchyPath} [{r.category}]");
                foreach (var s in r.slots)
                {
                    string texStatus = s.mainTexAssigned ? $"_MainTex={s.mainTexName}" :
                                      (s.baseMapAssigned ? $"_BaseMap={s.baseMapName}" : "NO TEXTURE");
                    sb.AppendLine($"  [{s.slotIndex}] {s.materialName}");
                    sb.AppendLine($"      shader={s.shaderName}, mode={s.standardModeDesc}, queue={s.renderQueue}");
                    sb.AppendLine($"      texture: {texStatus}");
                    if (!string.IsNullOrEmpty(s.issue))
                        sb.AppendLine($"      ISSUE: {s.issue}");
                }
            }

            sb.AppendLine("\n--- HEAD RENDERERS ---");
            foreach (var r in report.headRenderers)
            {
                sb.AppendLine($"\n{r.hierarchyPath} [{r.category}]");
                foreach (var s in r.slots)
                {
                    string texStatus = s.mainTexAssigned ? "OK" : (s.baseMapAssigned ? "BaseMap" : "MISSING");
                    sb.AppendLine($"  [{s.slotIndex}] {s.materialName} | {s.shaderName} | {s.standardModeDesc} | tex={texStatus}");
                }
            }

            Debug.Log(sb.ToString());
        }

        #endregion

        #region Fix Implementation

        private static Dictionary<string, Texture2D> BuildTextureIndex()
        {
            var index = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { CHARACTER_ROOT });

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                if (tex == null) continue;

                string fileName = Path.GetFileNameWithoutExtension(path);
                string key = fileName.ToLowerInvariant();

                if (!index.ContainsKey(key))
                {
                    index[key] = tex;
                }

                // Also index without common suffixes
                string normalized = NormalizeName(fileName);
                if (!index.ContainsKey(normalized))
                {
                    index[normalized] = tex;
                }
            }

            return index;
        }

        private static string NormalizeName(string name)
        {
            string result = name.ToLowerInvariant();
            string[] suffixes = { "_diffuse", "_albedo", "_basecolor", "_d", "_color" };
            foreach (var suffix in suffixes)
            {
                if (result.EndsWith(suffix))
                    result = result.Substring(0, result.Length - suffix.Length);
            }
            return result.Replace("std_", "").Replace("_", "");
        }

        private static void FixMaterialsAndPrefab(Dictionary<string, Texture2D> textureIndex)
        {
            // Ensure fixed materials folder
            if (!AssetDatabase.IsValidFolder(FIXED_MATERIALS_PATH))
            {
                string parent = Path.GetDirectoryName(FIXED_MATERIALS_PATH).Replace("\\", "/");
                AssetDatabase.CreateFolder(parent, "Materials_Fixed");
            }

            var prefabRoot = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            if (prefabRoot == null)
            {
                Debug.LogError("Cannot load prefab for editing");
                return;
            }

            bool anyChanges = false;

            try
            {
                var renderers = prefabRoot.GetComponentsInChildren<Renderer>(true);

                foreach (var renderer in renderers)
                {
                    string goName = renderer.gameObject.name.ToLowerInvariant();
                    string path = GetHierarchyPath(renderer.gameObject, prefabRoot.transform);

                    bool isEye = goName.Contains("cc_base_eye");
                    bool isOverlay = goName.Contains("eyeocclusion") || goName.Contains("tearline") || goName.Contains("cornea");
                    bool isHair = goName.Contains("hair") || goName.Contains("scalp") || goName.Contains("babyhair");
                    bool isBrowLash = goName.Contains("brow") || goName.Contains("lash");
                    bool isHead = goName.Contains("head") || goName.Contains("skin") || goName.Contains("body");

                    if (!isEye && !isOverlay && !isHair && !isBrowLash && !isHead) continue;

                    var materials = renderer.sharedMaterials;
                    bool slotChanged = false;

                    for (int i = 0; i < materials.Length; i++)
                    {
                        var mat = materials[i];
                        Material fixedMat = null;

                        // Check if material needs fixing
                        bool needsFix = false;
                        string reason = "";

                        if (mat == null)
                        {
                            needsFix = true;
                            reason = "NULL";
                        }
                        else if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                        {
                            needsFix = true;
                            reason = "broken shader";
                        }
                        else
                        {
                            // Check texture
                            bool hasTex = (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null) ||
                                         (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null);

                            if (!hasTex && (isEye || isHair || isBrowLash))
                            {
                                needsFix = true;
                                reason = "missing texture";
                            }

                            // Check overlay mode
                            if (isOverlay && mat.HasProperty("_Mode") && mat.GetFloat("_Mode") == 0)
                            {
                                needsFix = true;
                                reason = "overlay is opaque";
                            }
                        }

                        if (needsFix)
                        {
                            string baseName = mat != null ? mat.name : $"{renderer.gameObject.name}_slot{i}";
                            fixedMat = CreateOrFixMaterial(baseName, isEye, isOverlay, isHair || isBrowLash, textureIndex);

                            if (fixedMat != null)
                            {
                                materials[i] = fixedMat;
                                slotChanged = true;
                                Debug.Log($"FIXED: {path}[{i}] ({reason}) -> {fixedMat.name}");
                            }
                        }
                        else if (isOverlay && mat != null)
                        {
                            // Even if not broken, ensure overlays are FADE
                            if (mat.HasProperty("_Mode") && mat.GetFloat("_Mode") == 0)
                            {
                                SetMaterialMode(mat, 2); // Fade
                                EditorUtility.SetDirty(mat);
                                Debug.Log($"MODE FIX: {path}[{i}] -> FADE");
                            }
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
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, PREFAB_PATH);
                    Debug.Log($"Prefab saved: {PREFAB_PATH}");
                }
                else
                {
                    Debug.Log("No material slot changes needed in prefab.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static Material CreateOrFixMaterial(string baseName, bool isEye, bool isOverlay, bool isHairBrow,
            Dictionary<string, Texture2D> textureIndex)
        {
            var standardShader = Shader.Find("Standard");
            if (standardShader == null)
            {
                Debug.LogError("Cannot find Standard shader");
                return null;
            }

            var newMat = new Material(standardShader);
            newMat.name = baseName + "_Fixed";

            // Find appropriate texture
            Texture2D mainTex = FindTextureForMaterial(baseName, isEye, isOverlay, isHairBrow, textureIndex);

            if (mainTex != null)
            {
                newMat.SetTexture("_MainTex", mainTex);
                Debug.Log($"  -> Texture: {mainTex.name}");

                // Also try to find normal map
                var normalTex = FindNormalMapForMaterial(baseName, textureIndex);
                if (normalTex != null)
                {
                    newMat.SetTexture("_BumpMap", normalTex);
                    newMat.EnableKeyword("_NORMALMAP");
                }
            }
            else
            {
                Debug.LogWarning($"  -> No texture found for {baseName}");
            }

            // Set render mode
            if (isOverlay)
            {
                SetMaterialMode(newMat, 2); // Fade
                Debug.Log($"  -> Mode: FADE (overlay)");
            }
            else if (isHairBrow)
            {
                SetMaterialMode(newMat, 1); // Cutout
                newMat.SetFloat("_Cutoff", 0.5f);
                Debug.Log($"  -> Mode: CUTOUT (hair/brow)");
            }
            else if (isEye)
            {
                SetMaterialMode(newMat, 0); // Opaque
                Debug.Log($"  -> Mode: OPAQUE (eye base)");
            }
            else
            {
                SetMaterialMode(newMat, 0); // Opaque
            }

            // Save the material
            string savePath = $"{FIXED_MATERIALS_PATH}/{newMat.name}.mat";

            // Delete if exists
            if (File.Exists(savePath))
            {
                AssetDatabase.DeleteAsset(savePath);
            }

            AssetDatabase.CreateAsset(newMat, savePath);

            return AssetDatabase.LoadAssetAtPath<Material>(savePath);
        }

        private static Texture2D FindTextureForMaterial(string materialName, bool isEye, bool isOverlay, bool isHairBrow,
            Dictionary<string, Texture2D> textureIndex)
        {
            string matNameLower = materialName.ToLowerInvariant();
            string normalized = NormalizeName(materialName);

            // Direct match attempts
            string[] tryNames = {
                matNameLower,
                normalized,
                matNameLower + "_diffuse",
                "std_" + normalized + "_diffuse",
            };

            foreach (var name in tryNames)
            {
                if (textureIndex.TryGetValue(name, out var tex))
                    return tex;
            }

            // Keyword-based search for eyes
            if (isEye)
            {
                // Look for eye-specific textures
                foreach (var kvp in textureIndex)
                {
                    string texName = kvp.Key;
                    if (texName.Contains("eye") && texName.Contains("diffuse") &&
                        !texName.Contains("occlusion") && !texName.Contains("lash") &&
                        !texName.Contains("brow"))
                    {
                        // Check if L or R matches material name
                        if ((matNameLower.Contains("_l") || matNameLower.Contains("left")) &&
                            (texName.Contains("_l") || texName.Contains("left")))
                            return kvp.Value;

                        if ((matNameLower.Contains("_r") || matNameLower.Contains("right")) &&
                            (texName.Contains("_r") || texName.Contains("right")))
                            return kvp.Value;
                    }
                }

                // Fallback: any eye texture
                foreach (var kvp in textureIndex)
                {
                    if (kvp.Key.Contains("eye") && kvp.Key.Contains("diffuse") &&
                        !kvp.Key.Contains("occlusion") && !kvp.Key.Contains("lash"))
                    {
                        return kvp.Value;
                    }
                }
            }

            // Keyword-based search for overlays
            if (isOverlay)
            {
                string[] overlayKeywords = { "occlusion", "tearline", "cornea" };
                foreach (var keyword in overlayKeywords)
                {
                    if (matNameLower.Contains(keyword))
                    {
                        foreach (var kvp in textureIndex)
                        {
                            if (kvp.Key.Contains(keyword))
                                return kvp.Value;
                        }
                    }
                }
            }

            // Keyword-based search for hair/brow
            if (isHairBrow)
            {
                string[] hairKeywords = { "hair", "scalp", "brow", "lash", "babyhair" };
                foreach (var keyword in hairKeywords)
                {
                    if (matNameLower.Contains(keyword))
                    {
                        foreach (var kvp in textureIndex)
                        {
                            if (kvp.Key.Contains(keyword) && kvp.Key.Contains("diffuse"))
                                return kvp.Value;
                        }
                    }
                }
            }

            // Fuzzy match: find texture with most matching tokens
            string[] matTokens = matNameLower.Replace("std_", "").Replace("_diffuse", "").Split('_');
            Texture2D bestMatch = null;
            int bestScore = 0;

            foreach (var kvp in textureIndex)
            {
                // Skip normal maps for main texture
                if (kvp.Key.Contains("normal") || kvp.Key.EndsWith("_n"))
                    continue;

                int score = matTokens.Count(t => t.Length > 2 && kvp.Key.Contains(t));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = kvp.Value;
                }
            }

            return bestMatch;
        }

        private static Texture2D FindNormalMapForMaterial(string materialName, Dictionary<string, Texture2D> textureIndex)
        {
            string baseName = NormalizeName(materialName);

            string[] tryNames = {
                baseName + "_normal",
                baseName + "_n",
                "std_" + baseName + "_normal",
            };

            foreach (var name in tryNames)
            {
                if (textureIndex.TryGetValue(name, out var tex))
                    return tex;
            }

            // Fuzzy match
            foreach (var kvp in textureIndex)
            {
                if ((kvp.Key.Contains("normal") || kvp.Key.EndsWith("_n")) &&
                    kvp.Key.Contains(baseName.Split('_')[0]))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private static void SetMaterialMode(Material mat, int mode)
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
                    mat.renderQueue = -1;
                    break;

                case 1: // Cutout
                    mat.SetFloat("_Mode", 1);
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

        #endregion

        #region Utilities

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

        #region Batch Mode

        public static void BatchDiag()
        {
            RunDiag();
        }

        public static void BatchFix()
        {
            RunFix();
        }

        public static void BatchVerify()
        {
            bool passed = RunVerify();
            EditorApplication.Exit(passed ? 0 : 1);
        }

        public static void BatchAll()
        {
            RunDiag();
            RunFix();
            bool passed = RunVerify();
            EditorApplication.Exit(passed ? 0 : 1);
        }

        #endregion
    }
}
