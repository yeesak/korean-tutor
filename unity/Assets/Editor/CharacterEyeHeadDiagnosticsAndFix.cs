using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Diagnostic and repair tool for character eye/head materials.
    /// Fixes white-eye issues caused by opaque overlay materials (EyeOcclusion/TearLine/Cornea).
    /// </summary>
    public class CharacterEyeHeadDiagnosticsAndFix : EditorWindow
    {
        private const string CHARACTER_ROOT = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA";
        private const string PREFAB_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
        private const string REPORT_PATH = "Assets/Temp/eye_head_material_report.json";

        // Keywords for identifying head-related renderers
        private static readonly string[] HEAD_KEYWORDS = {
            "eye", "iris", "sclera", "cornea", "tearline", "eyeocclusion", "occlusion",
            "brow", "eyebrow", "lash", "eyelash", "hair", "scalp", "head", "skin", "face"
        };

        // Categories that need TRANSPARENT/FADE mode (overlays)
        private static readonly string[] TRANSPARENT_OVERLAY_KEYWORDS = {
            "tearline", "eyeocclusion", "occlusion", "cornea"
        };

        // Categories that need CUTOUT mode
        private static readonly string[] CUTOUT_KEYWORDS = {
            "hair", "scalp", "brow", "eyebrow", "lash", "eyelash", "babyhair"
        };

        // Categories that should be OPAQUE
        private static readonly string[] OPAQUE_KEYWORDS = {
            "eye_r", "eye_l", "iris", "sclera", "skin", "head", "body", "arm", "leg",
            "teeth", "tongue", "nail", "outfit"
        };

        #region Data Classes

        [System.Serializable]
        public class MaterialSlotInfo
        {
            public int slotIndex;
            public string materialName;
            public string materialPath;
            public string shaderName;
            public int renderQueue;
            public bool hasMainTex;
            public bool mainTexAssigned;
            public bool hasBaseMap;
            public bool baseMapAssigned;
            public float standardMode;
            public string renderModeDescription;
            public List<string> keywords = new List<string>();
        }

        [System.Serializable]
        public class RendererInfo
        {
            public string gameObjectName;
            public string hierarchyPath;
            public string meshName;
            public int subMeshCount;
            public List<MaterialSlotInfo> materialSlots = new List<MaterialSlotInfo>();
        }

        [System.Serializable]
        public class DiagnosticReport
        {
            public string prefabPath;
            public string timestamp;
            public List<RendererInfo> renderers = new List<RendererInfo>();
            public int totalMaterialsMissingTexture;
            public int totalMaterialsWrongShader;
            public List<string> opaqueOverlays = new List<string>();
            public List<string> issues = new List<string>();
        }

        #endregion

        #region Menu Items

        [MenuItem("Tools/Character Setup/DIAGNOSTIC - Eye+Head Material Report")]
        public static void RunDiagnostic()
        {
            Debug.Log("=== CHARACTER EYE/HEAD DIAGNOSTIC ===\n");

            var report = GenerateDiagnosticReport();

            // Save JSON report
            SaveReportToFile(report);

            // Print summary
            PrintDiagnosticSummary(report);

            Debug.Log("=== END DIAGNOSTIC ===\n");
        }

        [MenuItem("Tools/Character Setup/FIX - Eye+Head Materials (Rebuild + Rebind)")]
        public static void RunFix()
        {
            Debug.Log("=== CHARACTER EYE/HEAD FIX ===\n");

            // Step 1: Build texture lookup
            var textureIndex = BuildTextureIndex();
            Debug.Log($"Built texture index with {textureIndex.Count} entries");

            // Step 2: Fix materials
            FixHeadMaterials(textureIndex);

            // Step 3: Update prefab references
            UpdatePrefabMaterialReferences(textureIndex);

            // Step 4: Force save and refresh
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("=== END FIX ===\n");
            Debug.Log("Now run VERIFY to confirm all issues are resolved.");
        }

        [MenuItem("Tools/Character Setup/VERIFY - Eye+Head After Fix")]
        public static bool RunVerify()
        {
            Debug.Log("=== CHARACTER EYE/HEAD VERIFY ===\n");

            var issues = new List<string>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);

            if (prefab == null)
            {
                Debug.LogError($"VERIFY FAILED: Cannot load prefab at {PREFAB_PATH}");
                return false;
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                string goName = renderer.gameObject.name.ToLowerInvariant();
                if (!IsHeadRelated(goName)) continue;

                string rendererPath = GetHierarchyPath(renderer.gameObject, prefab.transform);
                var materials = renderer.sharedMaterials;

                for (int i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];

                    if (mat == null)
                    {
                        issues.Add($"NULL material: {rendererPath}[{i}]");
                        continue;
                    }

                    string matName = mat.name.ToLowerInvariant();

                    // Check shader
                    if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                    {
                        issues.Add($"Bad shader: {rendererPath}[{i}] -> {mat.name}");
                        continue;
                    }

                    // Check texture assignment
                    bool hasMainTex = mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null;
                    bool hasBaseMap = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null;

                    // Eye/skin materials must have texture
                    bool needsTexture = matName.Contains("eye") || matName.Contains("skin") ||
                                       matName.Contains("head") || matName.Contains("iris") ||
                                       matName.Contains("sclera") || matName.Contains("hair") ||
                                       matName.Contains("brow") || matName.Contains("lash");

                    if (needsTexture && !hasMainTex && !hasBaseMap)
                    {
                        issues.Add($"Missing texture: {rendererPath}[{i}] -> {mat.name}");
                    }

                    // Check overlay materials are NOT opaque
                    bool isOverlay = IsTransparentOverlay(matName);
                    if (isOverlay)
                    {
                        float mode = mat.HasProperty("_Mode") ? mat.GetFloat("_Mode") : 0;
                        if (mode == 0) // Opaque
                        {
                            issues.Add($"Overlay is OPAQUE (should be FADE/TRANSPARENT): {rendererPath}[{i}] -> {mat.name}");
                        }
                    }
                }
            }

            // Print results
            Debug.Log("=== VERIFY RESULTS ===");

            if (issues.Count == 0)
            {
                Debug.Log("PASS: All eye/head materials verified OK!");
                return true;
            }
            else
            {
                Debug.LogError($"FAIL: {issues.Count} issues found:");
                foreach (var issue in issues)
                {
                    Debug.LogError($"  - {issue}");
                }
                return false;
            }
        }

        #endregion

        #region Diagnostic Implementation

        private static DiagnosticReport GenerateDiagnosticReport()
        {
            var report = new DiagnosticReport
            {
                prefabPath = PREFAB_PATH,
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            if (prefab == null)
            {
                report.issues.Add($"Cannot load prefab: {PREFAB_PATH}");
                return report;
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                string goName = renderer.gameObject.name.ToLowerInvariant();
                if (!IsHeadRelated(goName)) continue;

                var rendererInfo = new RendererInfo
                {
                    gameObjectName = renderer.gameObject.name,
                    hierarchyPath = GetHierarchyPath(renderer.gameObject, prefab.transform)
                };

                // Get mesh info
                if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    rendererInfo.meshName = smr.sharedMesh.name;
                    rendererInfo.subMeshCount = smr.sharedMesh.subMeshCount;
                }

                // Analyze each material slot
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];
                    var slotInfo = new MaterialSlotInfo { slotIndex = i };

                    if (mat == null)
                    {
                        slotInfo.materialName = "NULL";
                        slotInfo.shaderName = "N/A";
                        report.totalMaterialsMissingTexture++;
                    }
                    else
                    {
                        slotInfo.materialName = mat.name;
                        slotInfo.materialPath = AssetDatabase.GetAssetPath(mat);
                        slotInfo.shaderName = mat.shader != null ? mat.shader.name : "NULL";
                        slotInfo.renderQueue = mat.renderQueue;

                        // Check texture properties
                        slotInfo.hasMainTex = mat.HasProperty("_MainTex");
                        slotInfo.mainTexAssigned = slotInfo.hasMainTex && mat.GetTexture("_MainTex") != null;
                        slotInfo.hasBaseMap = mat.HasProperty("_BaseMap");
                        slotInfo.baseMapAssigned = slotInfo.hasBaseMap && mat.GetTexture("_BaseMap") != null;

                        // Check Standard mode
                        if (mat.HasProperty("_Mode"))
                        {
                            slotInfo.standardMode = mat.GetFloat("_Mode");
                            slotInfo.renderModeDescription = GetRenderModeDescription(slotInfo.standardMode);
                        }
                        else
                        {
                            slotInfo.standardMode = -1;
                            slotInfo.renderModeDescription = "Unknown (no _Mode property)";
                        }

                        // Get keywords
                        slotInfo.keywords = mat.shaderKeywords.ToList();

                        // Count issues
                        if (!slotInfo.mainTexAssigned && !slotInfo.baseMapAssigned)
                        {
                            report.totalMaterialsMissingTexture++;
                        }

                        if (mat.shader == null || !mat.shader.name.Contains("Standard"))
                        {
                            report.totalMaterialsWrongShader++;
                        }

                        // Check for opaque overlays
                        string matNameLower = mat.name.ToLowerInvariant();
                        if (IsTransparentOverlay(matNameLower) && slotInfo.standardMode == 0)
                        {
                            report.opaqueOverlays.Add($"{rendererInfo.hierarchyPath}[{i}] -> {mat.name}");
                        }
                    }

                    rendererInfo.materialSlots.Add(slotInfo);
                }

                report.renderers.Add(rendererInfo);
            }

            return report;
        }

        private static void SaveReportToFile(DiagnosticReport report)
        {
            // Ensure directory exists
            string dir = Path.GetDirectoryName(REPORT_PATH);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(REPORT_PATH, json);
            AssetDatabase.Refresh();

            Debug.Log($"Report saved to: {REPORT_PATH}");
        }

        private static void PrintDiagnosticSummary(DiagnosticReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n=== DIAGNOSTIC SUMMARY ===");
            sb.AppendLine($"Prefab: {report.prefabPath}");
            sb.AppendLine($"Head-related renderers found: {report.renderers.Count}");
            sb.AppendLine($"Materials missing texture: {report.totalMaterialsMissingTexture}");
            sb.AppendLine($"Materials with wrong shader: {report.totalMaterialsWrongShader}");

            if (report.opaqueOverlays.Count > 0)
            {
                sb.AppendLine($"\nOPAQUE OVERLAYS (likely causing white eyes):");
                foreach (var overlay in report.opaqueOverlays)
                {
                    sb.AppendLine($"  - {overlay}");
                }
            }

            sb.AppendLine("\n=== RENDERER DETAILS ===");
            foreach (var renderer in report.renderers)
            {
                sb.AppendLine($"\n{renderer.hierarchyPath} (mesh: {renderer.meshName})");
                foreach (var slot in renderer.materialSlots)
                {
                    string texStatus = slot.mainTexAssigned ? "OK" : (slot.baseMapAssigned ? "BaseMap" : "MISSING");
                    sb.AppendLine($"  [{slot.slotIndex}] {slot.materialName} | shader={slot.shaderName} | mode={slot.renderModeDescription} | tex={texStatus}");
                }
            }

            Debug.Log(sb.ToString());
        }

        private static string GetRenderModeDescription(float mode)
        {
            switch ((int)mode)
            {
                case 0: return "Opaque";
                case 1: return "Cutout";
                case 2: return "Fade";
                case 3: return "Transparent";
                default: return $"Unknown({mode})";
            }
        }

        #endregion

        #region Fix Implementation

        private static Dictionary<string, Texture2D> BuildTextureIndex()
        {
            var index = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

            var texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { CHARACTER_ROOT });

            foreach (var guid in texGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                if (tex != null)
                {
                    // Add multiple key variations for better matching
                    string baseName = Path.GetFileNameWithoutExtension(path);
                    string normalized = NormalizeTextureName(baseName);

                    if (!index.ContainsKey(normalized))
                        index[normalized] = tex;

                    if (!index.ContainsKey(baseName.ToLowerInvariant()))
                        index[baseName.ToLowerInvariant()] = tex;
                }
            }

            return index;
        }

        private static string NormalizeTextureName(string name)
        {
            string result = name.ToLowerInvariant();
            result = result.Replace(" ", "").Replace("_", "");

            // Remove common suffixes
            string[] suffixes = { "diffuse", "albedo", "basecolor", "color", "d", "base" };
            foreach (var suffix in suffixes)
            {
                if (result.EndsWith(suffix))
                    result = result.Substring(0, result.Length - suffix.Length);
            }

            return result;
        }

        private static void FixHeadMaterials(Dictionary<string, Texture2D> textureIndex)
        {
            Debug.Log("\n--- FIXING HEAD MATERIALS ---");

            // Find all materials in the character folder
            var matGuids = AssetDatabase.FindAssets("t:Material", new[] { CHARACTER_ROOT });

            foreach (var guid in matGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string matName = Path.GetFileNameWithoutExtension(path);
                string matNameLower = matName.ToLowerInvariant();

                // Only process head-related materials
                if (!IsHeadRelated(matNameLower)) continue;

                Material mat = null;
                bool needsRebuild = false;

                try
                {
                    mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                }
                catch
                {
                    needsRebuild = true;
                }

                if (mat == null || mat.shader == null ||
                    mat.shader.name == "Hidden/InternalErrorShader")
                {
                    needsRebuild = true;
                }

                if (needsRebuild)
                {
                    Debug.Log($"Rebuilding corrupted material: {matName}");
                    mat = RebuildMaterial(matName, path, textureIndex);
                }
                else
                {
                    // Fix existing material
                    bool changed = false;

                    // Ensure Standard shader
                    if (!mat.shader.name.Contains("Standard"))
                    {
                        var standardShader = Shader.Find("Standard");
                        if (standardShader != null)
                        {
                            mat.shader = standardShader;
                            changed = true;
                            Debug.Log($"  {matName}: Changed shader to Standard");
                        }
                    }

                    // Check and fix texture
                    bool hasMainTex = mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null;
                    bool hasBaseMap = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null;

                    if (!hasMainTex && !hasBaseMap)
                    {
                        var matchedTex = FindMatchingTexture(matName, textureIndex);
                        if (matchedTex != null)
                        {
                            mat.SetTexture("_MainTex", matchedTex);
                            changed = true;
                            Debug.Log($"  {matName}: Assigned _MainTex -> {matchedTex.name}");
                        }
                    }
                    else if (hasBaseMap && !hasMainTex)
                    {
                        // Copy _BaseMap to _MainTex for Standard shader
                        var baseTex = mat.GetTexture("_BaseMap");
                        mat.SetTexture("_MainTex", baseTex);
                        changed = true;
                        Debug.Log($"  {matName}: Copied _BaseMap to _MainTex");
                    }

                    // Fix render mode
                    changed |= SetCorrectRenderMode(mat, matNameLower);

                    if (changed)
                    {
                        EditorUtility.SetDirty(mat);
                    }
                }
            }

            // Also fix materials in Materials_Rebuilt folder
            FixRebuiltMaterials(textureIndex);
        }

        private static void FixRebuiltMaterials(Dictionary<string, Texture2D> textureIndex)
        {
            string rebuiltPath = $"{CHARACTER_ROOT}/Materials_Rebuilt";
            if (!AssetDatabase.IsValidFolder(rebuiltPath)) return;

            Debug.Log("\n--- FIXING REBUILT MATERIALS ---");

            var matGuids = AssetDatabase.FindAssets("t:Material", new[] { rebuiltPath });

            foreach (var guid in matGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (mat == null) continue;

                string matNameLower = mat.name.ToLowerInvariant();
                bool changed = false;

                // Fix render mode for overlays
                changed |= SetCorrectRenderMode(mat, matNameLower);

                // Ensure texture is assigned
                bool hasMainTex = mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null;
                if (!hasMainTex)
                {
                    var matchedTex = FindMatchingTexture(mat.name, textureIndex);
                    if (matchedTex != null)
                    {
                        mat.SetTexture("_MainTex", matchedTex);
                        changed = true;
                        Debug.Log($"  {mat.name}: Assigned _MainTex -> {matchedTex.name}");
                    }
                }

                if (changed)
                {
                    EditorUtility.SetDirty(mat);
                    Debug.Log($"  {mat.name}: Updated render mode");
                }
            }
        }

        private static Material RebuildMaterial(string matName, string originalPath, Dictionary<string, Texture2D> textureIndex)
        {
            var standardShader = Shader.Find("Standard");
            if (standardShader == null)
            {
                Debug.LogError("Cannot find Standard shader!");
                return null;
            }

            var newMat = new Material(standardShader);
            newMat.name = matName;

            // Find and assign texture
            var matchedTex = FindMatchingTexture(matName, textureIndex);
            if (matchedTex != null)
            {
                newMat.SetTexture("_MainTex", matchedTex);
                Debug.Log($"  -> Assigned _MainTex: {matchedTex.name}");

                // Also check for normal map
                var normalTex = FindMatchingNormalMap(matName, textureIndex);
                if (normalTex != null)
                {
                    newMat.SetTexture("_BumpMap", normalTex);
                    newMat.EnableKeyword("_NORMALMAP");
                    Debug.Log($"  -> Assigned _BumpMap: {normalTex.name}");
                }
            }
            else
            {
                Debug.LogWarning($"  -> No matching texture found for {matName}");
            }

            // Set correct render mode
            SetCorrectRenderMode(newMat, matName.ToLowerInvariant());

            // Save the new material
            string newPath = originalPath.Replace(".mat", "_REBUILT.mat");
            if (File.Exists(newPath))
            {
                AssetDatabase.DeleteAsset(newPath);
            }

            AssetDatabase.CreateAsset(newMat, newPath);
            Debug.Log($"  -> Saved: {newPath}");

            return AssetDatabase.LoadAssetAtPath<Material>(newPath);
        }

        private static bool SetCorrectRenderMode(Material mat, string matNameLower)
        {
            bool changed = false;

            if (IsTransparentOverlay(matNameLower))
            {
                // FADE mode for overlays (EyeOcclusion, TearLine, Cornea)
                float currentMode = mat.HasProperty("_Mode") ? mat.GetFloat("_Mode") : 0;
                if (currentMode != 2) // Not already Fade
                {
                    SetMaterialRenderMode(mat, 2); // Fade
                    Debug.Log($"  {mat.name}: Set to FADE mode (overlay)");
                    changed = true;
                }
            }
            else if (IsCutoutMaterial(matNameLower))
            {
                // CUTOUT mode for hair/brow/lash
                float currentMode = mat.HasProperty("_Mode") ? mat.GetFloat("_Mode") : 0;
                if (currentMode != 1) // Not already Cutout
                {
                    SetMaterialRenderMode(mat, 1); // Cutout
                    mat.SetFloat("_Cutoff", 0.5f);
                    Debug.Log($"  {mat.name}: Set to CUTOUT mode");
                    changed = true;
                }
            }
            else if (IsOpaqueMaterial(matNameLower))
            {
                // OPAQUE mode for eyes/skin
                float currentMode = mat.HasProperty("_Mode") ? mat.GetFloat("_Mode") : 0;
                if (currentMode != 0)
                {
                    SetMaterialRenderMode(mat, 0); // Opaque
                    Debug.Log($"  {mat.name}: Set to OPAQUE mode");
                    changed = true;
                }
            }

            return changed;
        }

        private static void SetMaterialRenderMode(Material mat, int mode)
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

        private static void UpdatePrefabMaterialReferences(Dictionary<string, Texture2D> textureIndex)
        {
            Debug.Log("\n--- UPDATING PREFAB MATERIAL REFERENCES ---");

            var prefabRoot = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            if (prefabRoot == null)
            {
                Debug.LogError($"Cannot load prefab: {PREFAB_PATH}");
                return;
            }

            bool anyChanges = false;

            try
            {
                var renderers = prefabRoot.GetComponentsInChildren<Renderer>(true);

                foreach (var renderer in renderers)
                {
                    string goName = renderer.gameObject.name.ToLowerInvariant();
                    if (!IsHeadRelated(goName)) continue;

                    string rendererPath = GetHierarchyPath(renderer.gameObject, prefabRoot.transform);
                    var materials = renderer.sharedMaterials;
                    bool changed = false;

                    for (int i = 0; i < materials.Length; i++)
                    {
                        var mat = materials[i];

                        if (mat == null)
                        {
                            // Find replacement material
                            var replacement = FindReplacementMaterial(renderer, i, textureIndex);
                            if (replacement != null)
                            {
                                materials[i] = replacement;
                                changed = true;
                                Debug.Log($"  {rendererPath}[{i}]: NULL -> {replacement.name}");
                            }
                            continue;
                        }

                        // Check if material needs replacement (corrupted or wrong shader)
                        if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                        {
                            // Try to find rebuilt version
                            string rebuiltPath = $"{CHARACTER_ROOT}/Materials_Rebuilt/{mat.name}.mat";
                            var rebuiltMat = AssetDatabase.LoadAssetAtPath<Material>(rebuiltPath);

                            if (rebuiltMat == null)
                            {
                                rebuiltPath = $"{CHARACTER_ROOT}/Materials/{mat.name}_REBUILT.mat";
                                rebuiltMat = AssetDatabase.LoadAssetAtPath<Material>(rebuiltPath);
                            }

                            if (rebuiltMat != null)
                            {
                                materials[i] = rebuiltMat;
                                changed = true;
                                Debug.Log($"  {rendererPath}[{i}]: {mat.name} -> {rebuiltMat.name} (rebuilt)");
                            }
                        }
                    }

                    if (changed)
                    {
                        renderer.sharedMaterials = materials;
                        anyChanges = true;
                    }
                }

                if (anyChanges)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, PREFAB_PATH);
                    Debug.Log($"Saved prefab: {PREFAB_PATH}");
                }
                else
                {
                    Debug.Log("No prefab changes needed.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static Material FindReplacementMaterial(Renderer renderer, int slotIndex, Dictionary<string, Texture2D> textureIndex)
        {
            string meshName = "";
            if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                meshName = smr.sharedMesh.name.ToLowerInvariant();

            // Try to find material in Materials_Rebuilt folder
            string[] searchPaths = {
                $"{CHARACTER_ROOT}/Materials_Rebuilt",
                $"{CHARACTER_ROOT}/Materials"
            };

            foreach (var searchPath in searchPaths)
            {
                if (!AssetDatabase.IsValidFolder(searchPath)) continue;

                var matGuids = AssetDatabase.FindAssets("t:Material", new[] { searchPath });

                foreach (var guid in matGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string matName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

                    // Match by mesh name keywords
                    if (meshName.Contains("eye") && matName.Contains("eye"))
                    {
                        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (mat != null && mat.shader != null) return mat;
                    }
                    if (meshName.Contains("hair") && matName.Contains("hair"))
                    {
                        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (mat != null && mat.shader != null) return mat;
                    }
                }
            }

            return null;
        }

        #endregion

        #region Texture Matching

        private static Texture2D FindMatchingTexture(string materialName, Dictionary<string, Texture2D> textureIndex)
        {
            string matNameLower = materialName.ToLowerInvariant();
            string normalized = NormalizeTextureName(materialName);

            // Direct match
            if (textureIndex.TryGetValue(matNameLower, out var tex))
                return tex;

            if (textureIndex.TryGetValue(normalized, out tex))
                return tex;

            // Try with _diffuse suffix
            if (textureIndex.TryGetValue(matNameLower + "_diffuse", out tex))
                return tex;

            // Try removing std_ prefix
            string withoutPrefix = matNameLower.Replace("std_", "");
            if (textureIndex.TryGetValue(withoutPrefix, out tex))
                return tex;

            // Fuzzy match - find texture containing material keywords
            foreach (var kvp in textureIndex)
            {
                string texName = kvp.Key;

                // Skip normal maps for main texture
                if (texName.Contains("normal") || texName.Contains("_n_") || texName.EndsWith("_n"))
                    continue;

                // Check if texture name matches material keywords
                string[] matParts = matNameLower.Replace("std_", "").Replace("_diffuse", "").Split('_');
                int matches = matParts.Count(p => p.Length > 2 && texName.Contains(p));

                if (matches >= 2 || (matParts.Length == 1 && texName.Contains(matParts[0])))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private static Texture2D FindMatchingNormalMap(string materialName, Dictionary<string, Texture2D> textureIndex)
        {
            string matNameLower = materialName.ToLowerInvariant();
            string baseName = matNameLower.Replace("std_", "").Replace("_diffuse", "");

            string[] tryNames = {
                baseName + "_normal",
                baseName + "_n",
                baseName + "normal",
                "std_" + baseName + "_normal"
            };

            foreach (var tryName in tryNames)
            {
                if (textureIndex.TryGetValue(tryName, out var tex))
                    return tex;
            }

            // Fuzzy match for normal maps
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

        #endregion

        #region Helper Methods

        private static bool IsHeadRelated(string name)
        {
            string nameLower = name.ToLowerInvariant();
            return HEAD_KEYWORDS.Any(k => nameLower.Contains(k));
        }

        private static bool IsTransparentOverlay(string name)
        {
            string nameLower = name.ToLowerInvariant();
            return TRANSPARENT_OVERLAY_KEYWORDS.Any(k => nameLower.Contains(k));
        }

        private static bool IsCutoutMaterial(string name)
        {
            string nameLower = name.ToLowerInvariant();
            // Cutout if contains hair/brow/lash but NOT overlay keywords
            bool isCutout = CUTOUT_KEYWORDS.Any(k => nameLower.Contains(k));
            bool isOverlay = IsTransparentOverlay(nameLower);
            return isCutout && !isOverlay;
        }

        private static bool IsOpaqueMaterial(string name)
        {
            string nameLower = name.ToLowerInvariant();
            // Opaque for eyes (not occlusion), skin, etc.
            if (IsTransparentOverlay(nameLower)) return false;
            if (IsCutoutMaterial(nameLower)) return false;

            return OPAQUE_KEYWORDS.Any(k => nameLower.Contains(k));
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

        #region Batch Mode Entry Points

        public static void BatchDiagnostic()
        {
            RunDiagnostic();
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
            RunDiagnostic();
            RunFix();
            bool passed = RunVerify();
            EditorApplication.Exit(passed ? 0 : 1);
        }

        #endregion
    }
}
