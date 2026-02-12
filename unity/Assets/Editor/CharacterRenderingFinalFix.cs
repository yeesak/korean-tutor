using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Final, definitive character rendering fix tool.
    /// Creates completely fresh materials and updates all prefab references.
    /// </summary>
    public class CharacterRenderingFinalFix : EditorWindow
    {
        private const string CHARACTER_ROOT = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA";
        private const string RUNTIME_PREFAB_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
        private const string CLEAN_MATERIALS_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Materials_Clean";

        #region Data Classes

        [System.Serializable]
        public class SlotReport
        {
            public int index;
            public string materialName;
            public string materialPath;
            public bool isInstance;
            public string shaderName;
            public string renderMode;
            public int renderQueue;
            public string mainTexName;
            public string mainTexPath;
            public bool mainTexExists;
            public string baseMapName;
            public string baseMapPath;
            public bool baseMapExists;
            public List<string> issues = new List<string>();
        }

        [System.Serializable]
        public class RendererReport
        {
            public string path;
            public string meshName;
            public int subMeshCount;
            public List<SlotReport> slots = new List<SlotReport>();
        }

        [System.Serializable]
        public class RuntimeReport
        {
            public string timestamp;
            public bool isPlayMode;
            public string characterRootPath;
            public string sourcePrefabPath;
            public string sourcePrefabGuid;
            public List<RendererReport> renderers = new List<RendererReport>();
            public List<string> criticalIssues = new List<string>();
            public string rootCause;
        }

        #endregion

        #region Menu Items

        [MenuItem("Tools/Character Setup/REPORT: Runtime Materials V2 (Play Mode)")]
        public static void ReportRuntimeMaterials()
        {
            var report = GenerateRuntimeReport();
            SaveRuntimeReport(report);
            PrintRuntimeReport(report);
        }

        [MenuItem("Tools/Character Setup/DIAGNOSTIC V2: Head+Eye+Brow+Hair")]
        public static void RunDiagnostic()
        {
            Debug.Log("=== CHARACTER RENDERING DIAGNOSTIC ===\n");

            var report = GenerateRuntimeReport();

            // Analyze and determine root cause
            DetermineRootCause(report);

            // Save reports
            SaveRuntimeReport(report);
            SaveRootCauseReport(report);

            PrintRuntimeReport(report);

            Debug.Log("\n=== END DIAGNOSTIC ===");
        }

        [MenuItem("Tools/Character Setup/FIX V2: Head+Eye+Brow+Hair (Fresh Materials)")]
        public static void RunCompleteFix()
        {
            Debug.Log("=== CHARACTER RENDERING COMPLETE FIX ===\n");
            Debug.Log("Creating FRESH materials from scratch...\n");

            // Step 1: Build texture index
            var textureIndex = BuildTextureIndex();
            Debug.Log($"Found {textureIndex.Count} textures in character folder");

            // Step 2: Create clean materials folder
            EnsureCleanMaterialsFolder();

            // Step 3: Create all fresh materials
            var materialMap = CreateAllFreshMaterials(textureIndex);
            Debug.Log($"Created {materialMap.Count} fresh materials");

            // Step 4: Update the runtime prefab
            UpdateRuntimePrefab(materialMap);

            // Step 5: Update all other prefabs
            UpdateAllPrefabs(materialMap);

            // Step 6: Save everything
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Step 7: Generate verification report
            var verifyReport = GenerateRuntimeReport();
            DetermineRootCause(verifyReport);
            SaveFixReport(materialMap, verifyReport);

            Debug.Log("\n=== FIX COMPLETE ===");
            Debug.Log($"Created {materialMap.Count} fresh materials in {CLEAN_MATERIALS_PATH}");
            Debug.Log("Run DIAGNOSTIC again to verify, then test in Play Mode.");
        }

        #endregion

        #region Runtime Report Generation

        private static RuntimeReport GenerateRuntimeReport()
        {
            var report = new RuntimeReport
            {
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                isPlayMode = Application.isPlaying
            };

            GameObject characterRoot = null;

            if (Application.isPlaying)
            {
                // Find runtime character
                var avatar = GameObject.Find("Avatar");
                if (avatar != null)
                {
                    var charModel = avatar.transform.Find("CharacterModel");
                    if (charModel != null)
                    {
                        characterRoot = charModel.gameObject;
                        report.characterRootPath = "Avatar/CharacterModel (runtime)";
                    }
                }
            }

            if (characterRoot == null)
            {
                // Load prefab asset
                characterRoot = AssetDatabase.LoadAssetAtPath<GameObject>(RUNTIME_PREFAB_PATH);
                if (characterRoot != null)
                {
                    report.characterRootPath = RUNTIME_PREFAB_PATH;
                    report.sourcePrefabPath = RUNTIME_PREFAB_PATH;
                    report.sourcePrefabGuid = AssetDatabase.AssetPathToGUID(RUNTIME_PREFAB_PATH);
                }
            }

            if (characterRoot == null)
            {
                report.criticalIssues.Add("Cannot find character - neither runtime nor prefab");
                return report;
            }

            // Analyze all renderers
            var renderers = characterRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var rendererReport = AnalyzeRenderer(renderer, characterRoot.transform);
                report.renderers.Add(rendererReport);

                // Collect issues
                foreach (var slot in rendererReport.slots)
                {
                    foreach (var issue in slot.issues)
                    {
                        if (issue.Contains("CRITICAL"))
                            report.criticalIssues.Add($"{rendererReport.path}[{slot.index}]: {issue}");
                    }
                }
            }

            return report;
        }

        private static RendererReport AnalyzeRenderer(Renderer renderer, Transform root)
        {
            var report = new RendererReport
            {
                path = GetHierarchyPath(renderer.gameObject, root)
            };

            if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                report.meshName = smr.sharedMesh.name;
                report.subMeshCount = smr.sharedMesh.subMeshCount;
            }

            // Use sharedMaterials for asset analysis, materials for runtime
            Material[] mats = Application.isPlaying ? renderer.materials : renderer.sharedMaterials;

            for (int i = 0; i < mats.Length; i++)
            {
                var slotReport = AnalyzeMaterialSlot(mats[i], i);
                report.slots.Add(slotReport);
            }

            return report;
        }

        private static SlotReport AnalyzeMaterialSlot(Material mat, int index)
        {
            var report = new SlotReport { index = index };

            if (mat == null)
            {
                report.materialName = "NULL";
                report.issues.Add("CRITICAL: NULL material - Unity will use Default-Material");
                return report;
            }

            report.materialName = mat.name;
            report.isInstance = mat.name.Contains("(Instance)");

            string assetPath = AssetDatabase.GetAssetPath(mat);
            report.materialPath = string.IsNullOrEmpty(assetPath) ? "(runtime instance)" : assetPath;

            // Check for Default-Material
            if (mat.name.Contains("Default-Material") || mat.name.Contains("Default Material"))
            {
                report.issues.Add("CRITICAL: Default-Material - renderer slot has no valid material assigned");
            }

            // Shader analysis
            if (mat.shader == null)
            {
                report.shaderName = "NULL";
                report.issues.Add("CRITICAL: NULL shader");
            }
            else if (mat.shader.name == "Hidden/InternalErrorShader")
            {
                report.shaderName = mat.shader.name;
                report.issues.Add("CRITICAL: Error shader (Broken PPtr in material YAML)");
            }
            else
            {
                report.shaderName = mat.shader.name;
            }

            report.renderQueue = mat.renderQueue;

            // Render mode
            if (mat.HasProperty("_Mode"))
            {
                int mode = (int)mat.GetFloat("_Mode");
                report.renderMode = mode switch
                {
                    0 => "Opaque",
                    1 => "Cutout",
                    2 => "Fade",
                    3 => "Transparent",
                    _ => $"Unknown({mode})"
                };
            }
            else
            {
                report.renderMode = "N/A";
            }

            // Check _MainTex
            if (mat.HasProperty("_MainTex"))
            {
                var tex = mat.GetTexture("_MainTex") as Texture2D;
                if (tex != null)
                {
                    report.mainTexName = tex.name;
                    report.mainTexPath = AssetDatabase.GetAssetPath(tex);
                    report.mainTexExists = !string.IsNullOrEmpty(report.mainTexPath) && File.Exists(report.mainTexPath);
                }
                else
                {
                    report.mainTexName = "null";

                    // Check if this renderer needs a texture
                    string matNameLower = mat.name.ToLowerInvariant();
                    bool needsTexture = matNameLower.Contains("eye") || matNameLower.Contains("skin") ||
                                       matNameLower.Contains("hair") || matNameLower.Contains("brow") ||
                                       matNameLower.Contains("lash") || matNameLower.Contains("head");

                    if (needsTexture)
                    {
                        report.issues.Add("CRITICAL: _MainTex is NULL on material that needs texture");
                    }
                }
            }

            // Check _BaseMap (URP property that might be present)
            if (mat.HasProperty("_BaseMap"))
            {
                var tex = mat.GetTexture("_BaseMap") as Texture2D;
                if (tex != null)
                {
                    report.baseMapName = tex.name;
                    report.baseMapPath = AssetDatabase.GetAssetPath(tex);
                    report.baseMapExists = !string.IsNullOrEmpty(report.baseMapPath);
                }
            }

            return report;
        }

        private static void DetermineRootCause(RuntimeReport report)
        {
            var causes = new List<string>();

            // Check for Default-Material
            int defaultMatCount = 0;
            int missingTexCount = 0;
            int errorShaderCount = 0;
            int instanceMatCount = 0;

            foreach (var renderer in report.renderers)
            {
                foreach (var slot in renderer.slots)
                {
                    if (slot.materialName.Contains("Default-Material") || slot.materialName.Contains("Default Material"))
                        defaultMatCount++;

                    if (slot.mainTexName == "null" && slot.issues.Any(i => i.Contains("_MainTex is NULL")))
                        missingTexCount++;

                    if (slot.shaderName == "Hidden/InternalErrorShader")
                        errorShaderCount++;

                    if (slot.isInstance)
                        instanceMatCount++;
                }
            }

            if (defaultMatCount > 0)
                causes.Add($"A) EMPTY MATERIAL SLOTS: {defaultMatCount} slots using Default-Material");

            if (errorShaderCount > 0)
                causes.Add($"D) BROKEN MATERIAL YAML: {errorShaderCount} materials have error shader (Broken text PPtr)");

            if (missingTexCount > 0)
                causes.Add($"E) MISSING TEXTURES: {missingTexCount} materials have no _MainTex assigned");

            if (instanceMatCount > 0 && report.isPlayMode)
                causes.Add($"B) RUNTIME MATERIAL INSTANCES: {instanceMatCount} materials are runtime instances (may have lost texture references)");

            // Eye-specific checks
            var eyeRenderer = report.renderers.FirstOrDefault(r => r.path.ToLowerInvariant().Contains("cc_base_eye"));
            if (eyeRenderer != null)
            {
                // Check if cornea is before eye base (wrong order causes white eyes)
                bool foundCornea = false;
                bool corneaBeforeEye = false;

                foreach (var slot in eyeRenderer.slots)
                {
                    string matName = slot.materialName.ToLowerInvariant();
                    if (matName.Contains("cornea"))
                    {
                        foundCornea = true;
                    }
                    else if (matName.Contains("eye") && !matName.Contains("cornea") && !matName.Contains("occlusion"))
                    {
                        if (!foundCornea)
                            corneaBeforeEye = false;
                    }
                }

                // Check if eye materials have textures
                foreach (var slot in eyeRenderer.slots)
                {
                    if (slot.mainTexName == "null" && !slot.materialName.ToLowerInvariant().Contains("cornea"))
                    {
                        causes.Add($"C) EYE TEXTURE MISSING: {eyeRenderer.path}[{slot.index}] {slot.materialName} has no texture");
                    }
                }
            }

            if (causes.Count == 0)
            {
                report.rootCause = "No obvious issues detected. Materials appear correctly configured.";
            }
            else
            {
                report.rootCause = string.Join("\n", causes);
            }
        }

        #endregion

        #region Fresh Material Creation

        private static Dictionary<string, Texture2D> BuildTextureIndex()
        {
            var index = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { CHARACTER_ROOT });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;

                // Index by multiple keys for flexible matching
                string fileName = Path.GetFileNameWithoutExtension(path);
                string key = fileName.ToLowerInvariant();

                if (!index.ContainsKey(key))
                    index[key] = tex;

                // Also index normalized version
                string normalized = NormalizeName(fileName);
                if (!index.ContainsKey(normalized))
                    index[normalized] = tex;
            }

            return index;
        }

        private static string NormalizeName(string name)
        {
            string result = name.ToLowerInvariant()
                .Replace("std_", "")
                .Replace("_diffuse", "")
                .Replace("_albedo", "")
                .Replace("_basecolor", "")
                .Replace("_mat", "")
                .Replace("_", "");
            return result;
        }

        private static void EnsureCleanMaterialsFolder()
        {
            if (!AssetDatabase.IsValidFolder(CLEAN_MATERIALS_PATH))
            {
                string parent = Path.GetDirectoryName(CLEAN_MATERIALS_PATH).Replace("\\", "/");
                AssetDatabase.CreateFolder(parent, "Materials_Clean");
                Debug.Log($"Created folder: {CLEAN_MATERIALS_PATH}");
            }
        }

        private static Dictionary<string, Material> CreateAllFreshMaterials(Dictionary<string, Texture2D> textureIndex)
        {
            var materialMap = new Dictionary<string, Material>(System.StringComparer.OrdinalIgnoreCase);

            var shader = Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("Cannot find Standard shader!");
                return materialMap;
            }

            // Define all materials we need to create
            var materialDefs = new[]
            {
                // Eye materials
                new { Name = "Std_Eye_R_Diffuse", Mode = 0, Queue = 2000, Cutoff = 0f, TexKey = "std_eye_r_diffuse" },
                new { Name = "Std_Eye_L_Diffuse", Mode = 0, Queue = 2000, Cutoff = 0f, TexKey = "std_eye_l_diffuse" },
                new { Name = "Std_Cornea_R_Diffuse", Mode = 2, Queue = 3000, Cutoff = 0f, TexKey = "std_cornea_r_diffuse" },
                new { Name = "Std_Cornea_L_Diffuse", Mode = 2, Queue = 3000, Cutoff = 0f, TexKey = "std_cornea_l_diffuse" },

                // Eye overlay materials
                new { Name = "Std_Eye_Occlusion_R_Diffuse", Mode = 2, Queue = 3000, Cutoff = 0f, TexKey = "std_eye_occlusion_r_diffuse" },
                new { Name = "Std_Eye_Occlusion_L_Diffuse", Mode = 2, Queue = 3000, Cutoff = 0f, TexKey = "std_eye_occlusion_l_diffuse" },
                new { Name = "Std_Tearline_R_Diffuse", Mode = 2, Queue = 3000, Cutoff = 0f, TexKey = "std_tearline_r_diffuse" },
                new { Name = "Std_Tearline_L_Diffuse", Mode = 2, Queue = 3000, Cutoff = 0f, TexKey = "std_tearline_l_diffuse" },

                // Skin materials
                new { Name = "Std_Skin_Head_Diffuse", Mode = 0, Queue = 2000, Cutoff = 0f, TexKey = "std_skin_head_diffuse" },
                new { Name = "Std_Skin_Body_Diffuse", Mode = 0, Queue = 2000, Cutoff = 0f, TexKey = "std_skin_body_diffuse" },
                new { Name = "Std_Skin_Arm_Diffuse", Mode = 0, Queue = 2000, Cutoff = 0f, TexKey = "std_skin_arm_diffuse" },
                new { Name = "Std_Skin_Leg_Diffuse", Mode = 0, Queue = 2000, Cutoff = 0f, TexKey = "std_skin_leg_diffuse" },
                new { Name = "Std_Nails_Diffuse", Mode = 0, Queue = 2000, Cutoff = 0f, TexKey = "std_nails_diffuse" },

                // Hair materials
                new { Name = "Hair_Transparency_Diffuse", Mode = 1, Queue = 2450, Cutoff = 0.5f, TexKey = "hair_transparency_diffuse" },
                new { Name = "Hair_Transparency_Diffuse_0001", Mode = 1, Queue = 2450, Cutoff = 0.5f, TexKey = "hair_transparency_diffuse_0001" },
                new { Name = "Hair_Transparency_Diffuse_0002", Mode = 1, Queue = 2450, Cutoff = 0.5f, TexKey = "hair_transparency_diffuse_0002" },
                new { Name = "Hair_Transparency_Diffuse_0003", Mode = 1, Queue = 2450, Cutoff = 0.5f, TexKey = "hair_transparency_diffuse_0003" },
                new { Name = "Scalp_Transparency_Diffuse", Mode = 1, Queue = 2450, Cutoff = 0.5f, TexKey = "scalp_transparency_diffuse" },
                new { Name = "BabyHair_Transparency_Diffuse", Mode = 1, Queue = 2450, Cutoff = 0.5f, TexKey = "babyhair_transparency_diffuse" },

                // Eyelash/Brow
                new { Name = "Std_Eyelash_Diffuse", Mode = 1, Queue = 2450, Cutoff = 0.5f, TexKey = "std_eyelash_diffuse" },

                // Teeth/Tongue
                new { Name = "Std_Upper_Teeth_Diffuse", Mode = 0, Queue = 2000, Cutoff = 0f, TexKey = "std_upper_teeth_diffuse" },
                new { Name = "Std_Lower_Teeth_Diffuse", Mode = 0, Queue = 2000, Cutoff = 0f, TexKey = "std_lower_teeth_diffuse" },
                new { Name = "Std_Tongue_Diffuse", Mode = 0, Queue = 2000, Cutoff = 0f, TexKey = "std_tongue_diffuse" },

                // Outfit
                new { Name = "F_Casual_Outfit_H_Diffuse", Mode = 0, Queue = 2000, Cutoff = 0f, TexKey = "f_casual_outfit_h_diffuse" },
            };

            foreach (var def in materialDefs)
            {
                var mat = new Material(shader);
                mat.name = def.Name;

                // Find and assign texture
                Texture2D mainTex = FindTexture(def.TexKey, textureIndex);
                if (mainTex != null)
                {
                    mat.SetTexture("_MainTex", mainTex);
                }
                else
                {
                    Debug.LogWarning($"  No texture found for {def.Name} (key: {def.TexKey})");
                }

                // Find and assign normal map
                Texture2D normalTex = FindNormalMap(def.TexKey, textureIndex);
                if (normalTex != null)
                {
                    mat.SetTexture("_BumpMap", normalTex);
                    mat.EnableKeyword("_NORMALMAP");
                }

                // Set render mode
                SetStandardShaderMode(mat, def.Mode, def.Cutoff, def.Queue);

                // Save material
                string savePath = $"{CLEAN_MATERIALS_PATH}/{def.Name}.mat";

                // Delete if exists
                if (File.Exists(savePath))
                    AssetDatabase.DeleteAsset(savePath);

                AssetDatabase.CreateAsset(mat, savePath);

                // Load back to get persistent reference
                var savedMat = AssetDatabase.LoadAssetAtPath<Material>(savePath);
                materialMap[def.Name] = savedMat;

                string texStatus = mainTex != null ? mainTex.name : "NO TEXTURE";
                Debug.Log($"  Created: {def.Name} ({GetModeString(def.Mode)}) -> {texStatus}");
            }

            AssetDatabase.SaveAssets();
            return materialMap;
        }

        private static Texture2D FindTexture(string key, Dictionary<string, Texture2D> index)
        {
            // Direct match
            if (index.TryGetValue(key, out var tex))
                return tex;

            // Try normalized
            string normalized = NormalizeName(key);
            if (index.TryGetValue(normalized, out tex))
                return tex;

            // Fuzzy match
            foreach (var kvp in index)
            {
                if (kvp.Key.Contains(normalized) || normalized.Contains(kvp.Key))
                {
                    // Skip normal maps
                    if (kvp.Key.Contains("normal") || kvp.Key.EndsWith("_n"))
                        continue;

                    return kvp.Value;
                }
            }

            return null;
        }

        private static Texture2D FindNormalMap(string key, Dictionary<string, Texture2D> index)
        {
            string baseName = NormalizeName(key);

            string[] tryKeys = {
                baseName + "_normal",
                baseName + "normal",
                key.Replace("_diffuse", "_normal"),
                key.Replace("diffuse", "normal"),
            };

            foreach (var tryKey in tryKeys)
            {
                if (index.TryGetValue(tryKey, out var tex))
                    return tex;
            }

            // Fuzzy match for normal maps
            foreach (var kvp in index)
            {
                if ((kvp.Key.Contains("normal") || kvp.Key.EndsWith("_n")) &&
                    kvp.Key.Contains(baseName.Substring(0, Mathf.Min(5, baseName.Length))))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private static void SetStandardShaderMode(Material mat, int mode, float cutoff, int queue)
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
                    mat.renderQueue = queue;
                    break;

                case 1: // Cutout
                    mat.SetFloat("_Mode", 1);
                    mat.SetFloat("_Cutoff", cutoff);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    mat.SetInt("_ZWrite", 1);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = queue;
                    break;

                case 2: // Fade
                    mat.SetFloat("_Mode", 2);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = queue;
                    break;

                case 3: // Transparent
                    mat.SetFloat("_Mode", 3);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = queue;
                    break;
            }
        }

        private static string GetModeString(int mode)
        {
            return mode switch
            {
                0 => "Opaque",
                1 => "Cutout",
                2 => "Fade",
                3 => "Transparent",
                _ => $"Unknown({mode})"
            };
        }

        #endregion

        #region Prefab Updates

        private static void UpdateRuntimePrefab(Dictionary<string, Material> materialMap)
        {
            Debug.Log($"\nUpdating runtime prefab: {RUNTIME_PREFAB_PATH}");
            UpdatePrefabMaterials(RUNTIME_PREFAB_PATH, materialMap);
        }

        private static void UpdateAllPrefabs(Dictionary<string, Material> materialMap)
        {
            Debug.Log("\nUpdating all character prefabs...");

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { $"{CHARACTER_ROOT}/Prefabs" });
            foreach (var guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == RUNTIME_PREFAB_PATH) continue; // Already updated

                UpdatePrefabMaterials(path, materialMap);
            }
        }

        private static void UpdatePrefabMaterials(string prefabPath, Dictionary<string, Material> materialMap)
        {
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
            {
                Debug.LogWarning($"  Cannot load: {prefabPath}");
                return;
            }

            bool anyChanges = false;

            try
            {
                var renderers = prefabRoot.GetComponentsInChildren<Renderer>(true);

                foreach (var renderer in renderers)
                {
                    var materials = renderer.sharedMaterials;
                    bool changed = false;

                    for (int i = 0; i < materials.Length; i++)
                    {
                        var oldMat = materials[i];
                        string matName = oldMat != null ? oldMat.name : "";

                        // Try to find matching clean material
                        Material newMat = null;

                        // Direct name match
                        if (materialMap.TryGetValue(matName, out newMat))
                        {
                            materials[i] = newMat;
                            changed = true;
                            Debug.Log($"    {renderer.name}[{i}]: {matName} -> {newMat.name}");
                        }
                        // Try without suffixes
                        else
                        {
                            string baseName = matName.Replace("_REBUILT", "").Replace("_Fixed", "");
                            if (materialMap.TryGetValue(baseName, out newMat))
                            {
                                materials[i] = newMat;
                                changed = true;
                                Debug.Log($"    {renderer.name}[{i}]: {matName} -> {newMat.name}");
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
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                    Debug.Log($"  Saved: {Path.GetFileName(prefabPath)}");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        #endregion

        #region Reports

        private static void SaveRuntimeReport(RuntimeReport report)
        {
            string dir = "Assets/Temp";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText($"{dir}/runtime_material_report.json", json);
            AssetDatabase.Refresh();
        }

        private static void SaveRootCauseReport(RuntimeReport report)
        {
            string dir = "Assets/Temp";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("=== HEAD/EYE RENDERING ROOT CAUSE ANALYSIS ===");
            sb.AppendLine($"Timestamp: {report.timestamp}");
            sb.AppendLine($"Play Mode: {report.isPlayMode}");
            sb.AppendLine($"Character: {report.characterRootPath}");
            sb.AppendLine();
            sb.AppendLine("ROOT CAUSE:");
            sb.AppendLine(report.rootCause);
            sb.AppendLine();

            if (report.criticalIssues.Count > 0)
            {
                sb.AppendLine("CRITICAL ISSUES:");
                foreach (var issue in report.criticalIssues)
                {
                    sb.AppendLine($"  - {issue}");
                }
            }

            File.WriteAllText($"{dir}/head_eye_rootcause.txt", sb.ToString());
            AssetDatabase.Refresh();
        }

        private static void SaveFixReport(Dictionary<string, Material> materialMap, RuntimeReport verifyReport)
        {
            string dir = "Assets/Temp";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("=== FIX REPORT ===");
            sb.AppendLine($"Timestamp: {System.DateTime.Now}");
            sb.AppendLine();
            sb.AppendLine($"CREATED {materialMap.Count} FRESH MATERIALS:");
            foreach (var kvp in materialMap)
            {
                var mat = kvp.Value;
                string texName = mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null
                    ? mat.GetTexture("_MainTex").name : "NO TEXTURE";
                sb.AppendLine($"  {kvp.Key} -> {texName}");
            }
            sb.AppendLine();
            sb.AppendLine("VERIFICATION:");
            sb.AppendLine(verifyReport.rootCause);

            File.WriteAllText($"{dir}/fix_report.json", sb.ToString());
            AssetDatabase.Refresh();
        }

        private static void PrintRuntimeReport(RuntimeReport report)
        {
            var sb = new StringBuilder();

            sb.AppendLine("\n========== RUNTIME MATERIAL REPORT ==========");
            sb.AppendLine($"Mode: {(report.isPlayMode ? "Play Mode (Runtime)" : "Edit Mode (Prefab Asset)")}");
            sb.AppendLine($"Character: {report.characterRootPath}");
            sb.AppendLine($"Prefab: {report.sourcePrefabPath}");

            if (report.criticalIssues.Count > 0)
            {
                sb.AppendLine($"\n!!! CRITICAL ISSUES ({report.criticalIssues.Count}) !!!");
                foreach (var issue in report.criticalIssues)
                {
                    sb.AppendLine($"  - {issue}");
                }
            }

            sb.AppendLine("\n--- RENDERER TABLE ---");
            sb.AppendLine("Renderer | Slot | Material | Shader | Mode | Queue | MainTex");
            sb.AppendLine("---------|------|----------|--------|------|-------|--------");

            foreach (var renderer in report.renderers)
            {
                foreach (var slot in renderer.slots)
                {
                    string texStatus = slot.mainTexName ?? "N/A";
                    if (slot.mainTexName == "null")
                        texStatus = "MISSING!";

                    sb.AppendLine($"{renderer.path} | {slot.index} | {slot.materialName} | {slot.shaderName} | {slot.renderMode} | {slot.renderQueue} | {texStatus}");
                }
            }

            if (!string.IsNullOrEmpty(report.rootCause))
            {
                sb.AppendLine("\n--- ROOT CAUSE ---");
                sb.AppendLine(report.rootCause);
            }

            Debug.Log(sb.ToString());
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
    }
}
