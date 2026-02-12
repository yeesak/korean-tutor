using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Comprehensive material diagnostic and rebuild tool for Edumeta character.
    /// Does NOT patch .mat YAML files - rebuilds materials via Unity Editor API.
    /// </summary>
    public class EdumetaMaterialDoctor : EditorWindow
    {
        private const string CHARACTER_ROOT = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA";
        private const string PREFAB_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
        private const string MATERIALS_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Materials";
        private const string REBUILT_MATERIALS_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Materials_Rebuilt";

        // Categories for material render modes
        private static readonly string[] CUTOUT_KEYWORDS = { "hair", "scalp", "babyhair", "brow", "eyelash", "lash" };
        private static readonly string[] OPAQUE_KEYWORDS = { "eye", "iris", "sclera", "cornea", "head", "skin", "body", "teeth", "tongue", "nail", "outfit", "shoe" };

        #region Menu Items

        [MenuItem("Tools/Character Setup/DIAGNOSE (Prefab Renderers + Materials)")]
        public static void Diagnose()
        {
            Debug.Log("=== EDUMETA MATERIAL DOCTOR: DIAGNOSE ===\n");

            var corruptedMats = new List<string>();
            var missingTexMats = new List<string>();
            var defaultSlots = new List<string>();
            var nullSlots = new List<string>();

            // Part 1: Diagnose Prefab Renderers
            DiagnosePrefabRenderers(corruptedMats, missingTexMats, defaultSlots, nullSlots);

            // Part 2: Diagnose Material Assets
            DiagnoseMaterialAssets(corruptedMats, missingTexMats);

            // Summary
            Debug.Log("\n=== DIAGNOSIS SUMMARY ===");
            Debug.Log($"CORRUPTED_MATS ({corruptedMats.Count}): {string.Join(", ", corruptedMats)}");
            Debug.Log($"MISSING_TEX_MATS ({missingTexMats.Count}): {string.Join(", ", missingTexMats)}");
            Debug.Log($"DEFAULT_SLOTS ({defaultSlots.Count}): {string.Join(", ", defaultSlots)}");
            Debug.Log($"NULL_SLOTS ({nullSlots.Count}): {string.Join(", ", nullSlots)}");
            Debug.Log("=== END DIAGNOSE ===\n");
        }

        [MenuItem("Tools/Character Setup/FIX (Rebuild Corrupted Head Materials)")]
        public static void Fix()
        {
            Debug.Log("=== EDUMETA MATERIAL DOCTOR: FIX ===\n");

            // Step A: Build texture index
            var textureIndex = BuildTextureIndex();
            Debug.Log($"Built texture index with {textureIndex.Count} entries");

            // Step B: Rebuild corrupted materials
            var rebuiltMaterials = RebuildCorruptedMaterials(textureIndex);
            Debug.Log($"Rebuilt {rebuiltMaterials.Count} materials");

            // Step C: Replace prefab references
            ReplacePrefabMaterialReferences(rebuiltMaterials, textureIndex);

            // Step D: Configure model importers
            ConfigureModelImporters();

            // Step E: Force clean serialization
            ForceCleanSerialization();

            Debug.Log("=== END FIX ===\n");
            Debug.Log("Now run VERIFY to confirm all issues are resolved.");
        }

        [MenuItem("Tools/Character Setup/VERIFY (Fail if anything is still broken)")]
        public static bool Verify()
        {
            Debug.Log("=== EDUMETA MATERIAL DOCTOR: VERIFY ===\n");

            int nullSlotCount = 0;
            int defaultMatCount = 0;
            int missingTexCount = 0;
            int nullShaderCount = 0;
            var issues = new List<string>();

            // Load prefab
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            if (prefab == null)
            {
                Debug.LogError($"VERIFY FAILED: Cannot load prefab at {PREFAB_PATH}");
                return false;
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                string rendererPath = GetGameObjectPath(renderer.gameObject, prefab.transform);
                var materials = renderer.sharedMaterials;

                for (int i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];

                    if (mat == null)
                    {
                        nullSlotCount++;
                        issues.Add($"NULL: {rendererPath} slot[{i}]");
                        continue;
                    }

                    string matName = mat.name.ToLowerInvariant();
                    bool isHeadRelated = IsHeadRelatedMaterial(matName);

                    // Check for Default-Material
                    if (mat.name.Contains("Default-Material") || mat.name.Contains("Default Material"))
                    {
                        defaultMatCount++;
                        issues.Add($"DEFAULT-MAT: {rendererPath} slot[{i}] -> {mat.name}");
                        continue;
                    }

                    // Check shader
                    if (mat.shader == null)
                    {
                        nullShaderCount++;
                        issues.Add($"NULL-SHADER: {rendererPath} slot[{i}] -> {mat.name}");
                        continue;
                    }

                    // Check textures for head-related materials
                    if (isHeadRelated)
                    {
                        bool hasMainTex = mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null;
                        bool hasBaseMap = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null;

                        if (!hasMainTex && !hasBaseMap)
                        {
                            missingTexCount++;
                            issues.Add($"MISSING-TEX: {rendererPath} slot[{i}] -> {mat.name} (shader: {mat.shader.name})");
                        }
                    }
                }
            }

            // Print results
            Debug.Log("=== VERIFY RESULTS ===");
            Debug.Log($"NULL slots: {nullSlotCount}");
            Debug.Log($"Default-Material slots: {defaultMatCount}");
            Debug.Log($"Missing main texture: {missingTexCount}");
            Debug.Log($"Null shaders: {nullShaderCount}");

            if (issues.Count > 0)
            {
                Debug.LogError("=== ISSUES FOUND ===");
                foreach (var issue in issues)
                {
                    Debug.LogError(issue);
                }
                Debug.LogError($"VERIFY FAILED: {issues.Count} issues remain.");
                return false;
            }
            else
            {
                Debug.Log("VERIFY PASSED: 0 issues found!");
                return true;
            }
        }

        #endregion

        #region Diagnose Implementation

        private static void DiagnosePrefabRenderers(List<string> corruptedMats, List<string> missingTexMats,
            List<string> defaultSlots, List<string> nullSlots)
        {
            Debug.Log("--- PREFAB RENDERER TABLE ---");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            if (prefab == null)
            {
                Debug.LogError($"Cannot load prefab: {PREFAB_PATH}");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Renderer Path | Mesh | SubMeshCount | MatCount | Slot Details");
            sb.AppendLine("-------------|------|--------------|----------|-------------");

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                string rendererPath = GetGameObjectPath(renderer.gameObject, prefab.transform);
                string meshName = "N/A";
                int subMeshCount = 0;

                if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    meshName = smr.sharedMesh.name;
                    subMeshCount = smr.sharedMesh.subMeshCount;
                }
                else if (renderer is MeshRenderer mr)
                {
                    var mf = renderer.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        meshName = mf.sharedMesh.name;
                        subMeshCount = mf.sharedMesh.subMeshCount;
                    }
                }

                var materials = renderer.sharedMaterials;
                sb.AppendLine($"{rendererPath} | {meshName} | {subMeshCount} | {materials.Length}");

                for (int i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];
                    if (mat == null)
                    {
                        sb.AppendLine($"  [{i}] NULL");
                        nullSlots.Add($"{rendererPath}[{i}]");
                        continue;
                    }

                    string matPath = AssetDatabase.GetAssetPath(mat);
                    string shaderName = mat.shader != null ? mat.shader.name : "NULL";
                    bool hasMainTex = mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null;
                    bool hasBaseMap = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null;
                    bool isDefault = mat.name.Contains("Default-Material") || mat.name.Contains("Default Material");

                    sb.AppendLine($"  [{i}] {mat.name} | shader={shaderName} | _MainTex={hasMainTex} | _BaseMap={hasBaseMap} | path={matPath}");

                    if (isDefault)
                    {
                        defaultSlots.Add($"{rendererPath}[{i}]={mat.name}");
                    }
                    else if (!hasMainTex && !hasBaseMap && IsHeadRelatedMaterial(mat.name.ToLowerInvariant()))
                    {
                        missingTexMats.Add(mat.name);
                    }
                }
            }

            Debug.Log(sb.ToString());
        }

        private static void DiagnoseMaterialAssets(List<string> corruptedMats, List<string> missingTexMats)
        {
            Debug.Log("\n--- MATERIAL ASSETS TABLE ---");

            if (!AssetDatabase.IsValidFolder(MATERIALS_PATH))
            {
                Debug.LogWarning($"Materials folder not found: {MATERIALS_PATH}");
                return;
            }

            var matGuids = AssetDatabase.FindAssets("t:Material", new[] { MATERIALS_PATH });
            var sb = new StringBuilder();
            sb.AppendLine("Material Name | Shader | _MainTex | _BaseMap | Status");
            sb.AppendLine("-------------|--------|----------|----------|-------");

            foreach (var guid in matGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = null;
                string status = "OK";

                try
                {
                    mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                }
                catch (System.Exception e)
                {
                    status = $"LOAD_ERROR: {e.Message}";
                    corruptedMats.Add(Path.GetFileNameWithoutExtension(path));
                }

                if (mat == null)
                {
                    sb.AppendLine($"{Path.GetFileName(path)} | N/A | N/A | N/A | CORRUPTED (null load)");
                    corruptedMats.Add(Path.GetFileNameWithoutExtension(path));
                    continue;
                }

                string shaderName = mat.shader != null ? mat.shader.name : "NULL";
                bool hasMainTex = mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null;
                bool hasBaseMap = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null;

                if (mat.shader == null)
                {
                    status = "NULL_SHADER";
                    corruptedMats.Add(mat.name);
                }
                else if (!hasMainTex && !hasBaseMap)
                {
                    status = "MISSING_TEX";
                    if (!missingTexMats.Contains(mat.name))
                        missingTexMats.Add(mat.name);
                }

                sb.AppendLine($"{mat.name} | {shaderName} | {hasMainTex} | {hasBaseMap} | {status}");
            }

            Debug.Log(sb.ToString());
        }

        #endregion

        #region Fix Implementation

        private static Dictionary<string, Texture2D> BuildTextureIndex()
        {
            var index = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

            // Find all textures under character root
            var texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { CHARACTER_ROOT });

            foreach (var guid in texGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null)
                {
                    string key = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    if (!index.ContainsKey(key))
                    {
                        index[key] = tex;
                        Debug.Log($"  Indexed texture: {key} -> {path}");
                    }
                }
            }

            return index;
        }

        private static Dictionary<string, Material> RebuildCorruptedMaterials(Dictionary<string, Texture2D> textureIndex)
        {
            var rebuiltMaterials = new Dictionary<string, Material>(System.StringComparer.OrdinalIgnoreCase);

            // Ensure rebuilt folder exists
            if (!AssetDatabase.IsValidFolder(REBUILT_MATERIALS_PATH))
            {
                string parent = Path.GetDirectoryName(REBUILT_MATERIALS_PATH).Replace("\\", "/");
                string folderName = Path.GetFileName(REBUILT_MATERIALS_PATH);
                AssetDatabase.CreateFolder(parent, folderName);
                Debug.Log($"Created folder: {REBUILT_MATERIALS_PATH}");
            }

            // Get Standard shader
            Shader standardShader = Shader.Find("Standard");
            if (standardShader == null)
            {
                Debug.LogError("FATAL: Cannot find Standard shader!");
                return rebuiltMaterials;
            }

            // Find all materials that need rebuilding
            if (!AssetDatabase.IsValidFolder(MATERIALS_PATH))
            {
                Debug.LogWarning($"Original materials folder not found: {MATERIALS_PATH}");
                return rebuiltMaterials;
            }

            var matGuids = AssetDatabase.FindAssets("t:Material", new[] { MATERIALS_PATH });

            foreach (var guid in matGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string matName = Path.GetFileNameWithoutExtension(path);

                Material oldMat = null;
                bool needsRebuild = false;

                try
                {
                    oldMat = AssetDatabase.LoadAssetAtPath<Material>(path);
                }
                catch
                {
                    needsRebuild = true;
                }

                if (oldMat == null)
                {
                    needsRebuild = true;
                }
                else
                {
                    // Check if corrupted or missing textures
                    bool hasMainTex = oldMat.HasProperty("_MainTex") && oldMat.GetTexture("_MainTex") != null;
                    bool hasBaseMap = oldMat.HasProperty("_BaseMap") && oldMat.GetTexture("_BaseMap") != null;

                    if (oldMat.shader == null || (!hasMainTex && !hasBaseMap))
                    {
                        needsRebuild = true;
                    }
                }

                if (needsRebuild)
                {
                    Debug.Log($"Rebuilding material: {matName}");

                    // Create new material
                    Material newMat = new Material(standardShader);
                    newMat.name = matName;

                    // Find matching texture
                    Texture2D mainTex = FindMatchingTexture(matName, textureIndex);
                    Texture2D normalTex = FindMatchingNormalMap(matName, textureIndex);

                    if (mainTex != null)
                    {
                        newMat.SetTexture("_MainTex", mainTex);
                        Debug.Log($"  -> Set _MainTex: {mainTex.name}");
                    }
                    else
                    {
                        Debug.LogWarning($"  -> No matching texture found for {matName}");
                    }

                    if (normalTex != null)
                    {
                        newMat.SetTexture("_BumpMap", normalTex);
                        newMat.EnableKeyword("_NORMALMAP");
                        Debug.Log($"  -> Set _BumpMap: {normalTex.name}");
                    }

                    // Configure render mode based on category
                    ConfigureMaterialRenderMode(newMat, matName);

                    // Save material
                    string newPath = $"{REBUILT_MATERIALS_PATH}/{matName}.mat";
                    AssetDatabase.CreateAsset(newMat, newPath);

                    // Reload to get persistent reference
                    var savedMat = AssetDatabase.LoadAssetAtPath<Material>(newPath);
                    rebuiltMaterials[matName] = savedMat;

                    Debug.Log($"  -> Saved: {newPath}");
                }
            }

            AssetDatabase.SaveAssets();
            return rebuiltMaterials;
        }

        private static Texture2D FindMatchingTexture(string materialName, Dictionary<string, Texture2D> textureIndex)
        {
            string key = materialName.ToLowerInvariant();

            // Direct match
            if (textureIndex.TryGetValue(key, out var tex))
                return tex;

            // Remove common prefixes/suffixes
            string[] prefixes = { "std_", "mat_", "m_" };
            string[] suffixes = { "_mat", "_material", "_diffuse", "_albedo", "_color", "_d" };

            string cleaned = key;
            foreach (var prefix in prefixes)
            {
                if (cleaned.StartsWith(prefix))
                    cleaned = cleaned.Substring(prefix.Length);
            }
            foreach (var suffix in suffixes)
            {
                if (cleaned.EndsWith(suffix))
                    cleaned = cleaned.Substring(0, cleaned.Length - suffix.Length);
            }

            // Try cleaned name + _diffuse
            string[] tryNames = {
                cleaned,
                cleaned + "_diffuse",
                cleaned + "_d",
                cleaned + "_albedo",
                key.Replace("std_", "").Replace("_diffuse", ""),
            };

            foreach (var tryName in tryNames)
            {
                if (textureIndex.TryGetValue(tryName, out tex))
                    return tex;
            }

            // Fuzzy match - find texture containing material base name
            foreach (var kvp in textureIndex)
            {
                if (kvp.Key.Contains(cleaned) || cleaned.Contains(kvp.Key))
                {
                    // Prefer diffuse textures
                    if (kvp.Key.Contains("diffuse") || kvp.Key.Contains("albedo") || kvp.Key.Contains("_d"))
                    {
                        return kvp.Value;
                    }
                }
            }

            // Last resort - any texture containing base name
            foreach (var kvp in textureIndex)
            {
                if (kvp.Key.Contains(cleaned) || cleaned.Contains(kvp.Key))
                {
                    if (!kvp.Key.Contains("normal") && !kvp.Key.Contains("_n") &&
                        !kvp.Key.Contains("specular") && !kvp.Key.Contains("_s") &&
                        !kvp.Key.Contains("metallic") && !kvp.Key.Contains("roughness"))
                    {
                        return kvp.Value;
                    }
                }
            }

            return null;
        }

        private static Texture2D FindMatchingNormalMap(string materialName, Dictionary<string, Texture2D> textureIndex)
        {
            string key = materialName.ToLowerInvariant();
            string cleaned = key.Replace("std_", "").Replace("_diffuse", "").Replace("_mat", "");

            string[] tryNames = {
                cleaned + "_normal",
                cleaned + "_n",
                cleaned + "_normalmap",
                cleaned + "_bump",
            };

            foreach (var tryName in tryNames)
            {
                if (textureIndex.TryGetValue(tryName, out var tex))
                    return tex;
            }

            // Fuzzy match
            foreach (var kvp in textureIndex)
            {
                if ((kvp.Key.Contains(cleaned) || cleaned.Contains(kvp.Key)) &&
                    (kvp.Key.Contains("normal") || kvp.Key.Contains("_n")))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private static void ConfigureMaterialRenderMode(Material mat, string matName)
        {
            string nameLower = matName.ToLowerInvariant();
            bool isCutout = CUTOUT_KEYWORDS.Any(k => nameLower.Contains(k));
            bool isOpaque = OPAQUE_KEYWORDS.Any(k => nameLower.Contains(k));

            if (isCutout && !isOpaque)
            {
                // Cutout mode for hair/eyelash/brow
                mat.SetFloat("_Mode", 1); // Cutout
                mat.SetFloat("_Cutoff", 0.3f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 2450;
                Debug.Log($"  -> Render mode: CUTOUT (queue=2450)");
            }
            else
            {
                // Opaque mode for eyes/skin/body
                mat.SetFloat("_Mode", 0); // Opaque
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = -1;
                Debug.Log($"  -> Render mode: OPAQUE");
            }
        }

        private static void ReplacePrefabMaterialReferences(Dictionary<string, Material> rebuiltMaterials,
            Dictionary<string, Texture2D> textureIndex)
        {
            Debug.Log("\n--- REPLACING PREFAB MATERIAL REFERENCES ---");

            // Load prefab contents for editing
            var prefabPath = PREFAB_PATH;
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
            {
                Debug.LogError($"Cannot load prefab contents: {prefabPath}");
                return;
            }

            try
            {
                var renderers = prefabRoot.GetComponentsInChildren<Renderer>(true);
                bool anyChanges = false;

                foreach (var renderer in renderers)
                {
                    string rendererPath = GetGameObjectPath(renderer.gameObject, prefabRoot.transform);
                    var materials = renderer.sharedMaterials;
                    bool changed = false;

                    for (int i = 0; i < materials.Length; i++)
                    {
                        var mat = materials[i];
                        bool needsReplacement = false;
                        string reason = "";

                        if (mat == null)
                        {
                            needsReplacement = true;
                            reason = "NULL";
                        }
                        else if (mat.name.Contains("Default-Material") || mat.name.Contains("Default Material"))
                        {
                            needsReplacement = true;
                            reason = "DEFAULT-MAT";
                        }
                        else if (mat.shader == null)
                        {
                            needsReplacement = true;
                            reason = "NULL-SHADER";
                        }
                        else
                        {
                            // Check if this material was rebuilt
                            string matName = mat.name;
                            if (rebuiltMaterials.ContainsKey(matName))
                            {
                                needsReplacement = true;
                                reason = "REBUILT";
                            }
                            else
                            {
                                // Check if missing textures
                                bool hasMainTex = mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null;
                                bool hasBaseMap = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null;
                                if (!hasMainTex && !hasBaseMap && IsHeadRelatedMaterial(matName.ToLowerInvariant()))
                                {
                                    needsReplacement = true;
                                    reason = "MISSING-TEX";
                                }
                            }
                        }

                        if (needsReplacement)
                        {
                            Material replacement = FindBestReplacementMaterial(renderer, i, materials[i],
                                rebuiltMaterials, textureIndex);

                            if (replacement != null)
                            {
                                materials[i] = replacement;
                                changed = true;
                                Debug.Log($"  {rendererPath}[{i}]: {reason} -> {replacement.name}");
                            }
                            else
                            {
                                Debug.LogWarning($"  {rendererPath}[{i}]: {reason} -> NO REPLACEMENT FOUND");
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
                    Debug.Log($"Saved prefab: {prefabPath}");
                }
                else
                {
                    Debug.Log("No material replacements needed in prefab.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static Material FindBestReplacementMaterial(Renderer renderer, int slotIndex, Material originalMat,
            Dictionary<string, Material> rebuiltMaterials, Dictionary<string, Texture2D> textureIndex)
        {
            // Get mesh name for context
            string meshName = "";
            if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                meshName = smr.sharedMesh.name.ToLowerInvariant();

            // If original material exists and was rebuilt, use the rebuilt version
            if (originalMat != null && rebuiltMaterials.TryGetValue(originalMat.name, out var rebuiltMat))
                return rebuiltMat;

            // Try to infer material from mesh name
            string[] meshParts = meshName.Split('_');

            // Build list of candidate rebuilt materials
            foreach (var kvp in rebuiltMaterials)
            {
                string matNameLower = kvp.Key.ToLowerInvariant();

                // Check if material name matches mesh context
                if (meshName.Contains("hair") && matNameLower.Contains("hair"))
                    return kvp.Value;
                if (meshName.Contains("scalp") && matNameLower.Contains("scalp"))
                    return kvp.Value;
                if (meshName.Contains("brow") && matNameLower.Contains("brow"))
                    return kvp.Value;
                if (meshName.Contains("lash") && (matNameLower.Contains("lash") || matNameLower.Contains("eyelash")))
                    return kvp.Value;
                if (meshName.Contains("eye") && matNameLower.Contains("eye"))
                    return kvp.Value;
            }

            // Fallback: try to load from rebuilt folder by original name
            if (originalMat != null)
            {
                string rebuiltPath = $"{REBUILT_MATERIALS_PATH}/{originalMat.name}.mat";
                var fromPath = AssetDatabase.LoadAssetAtPath<Material>(rebuiltPath);
                if (fromPath != null)
                    return fromPath;
            }

            // Last resort: search all rebuilt materials
            var allRebuilt = AssetDatabase.FindAssets("t:Material", new[] { REBUILT_MATERIALS_PATH });
            if (allRebuilt.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(allRebuilt[0]);
                return AssetDatabase.LoadAssetAtPath<Material>(path);
            }

            return null;
        }

        private static void ConfigureModelImporters()
        {
            Debug.Log("\n--- CONFIGURING MODEL IMPORTERS ---");

            var fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { CHARACTER_ROOT });

            foreach (var guid in fbxGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;

                if (importer != null)
                {
                    bool changed = false;

                    // Ensure materials are external
                    if (importer.materialLocation != ModelImporterMaterialLocation.External)
                    {
                        importer.materialLocation = ModelImporterMaterialLocation.External;
                        changed = true;
                    }

                    // Don't import materials (use our rebuilt ones)
                    if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
                    {
                        importer.materialImportMode = ModelImporterMaterialImportMode.None;
                        changed = true;
                    }

                    if (changed)
                    {
                        importer.SaveAndReimport();
                        Debug.Log($"  Configured: {path}");
                    }
                }
            }
        }

        private static void ForceCleanSerialization()
        {
            Debug.Log("\n--- FORCE CLEAN SERIALIZATION ---");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Force reserialize rebuilt materials
            if (AssetDatabase.IsValidFolder(REBUILT_MATERIALS_PATH))
            {
                var matGuids = AssetDatabase.FindAssets("t:Material", new[] { REBUILT_MATERIALS_PATH });
                var paths = matGuids.Select(g => AssetDatabase.GUIDToAssetPath(g)).ToArray();

                if (paths.Length > 0)
                {
                    AssetDatabase.ForceReserializeAssets(paths);
                    Debug.Log($"  Reserialized {paths.Length} rebuilt materials");
                }
            }

            // Force reserialize prefab
            AssetDatabase.ForceReserializeAssets(new[] { PREFAB_PATH });
            Debug.Log($"  Reserialized prefab");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        #endregion

        #region Batch Mode Entry Points

        /// <summary>
        /// Entry point for Unity batch mode: -executeMethod ShadowingTutor.Editor.EdumetaMaterialDoctor.BatchDiagnose
        /// </summary>
        public static void BatchDiagnose()
        {
            Debug.Log("=== BATCH MODE: DIAGNOSE ===");
            Diagnose();
        }

        /// <summary>
        /// Entry point for Unity batch mode: -executeMethod ShadowingTutor.Editor.EdumetaMaterialDoctor.BatchFix
        /// </summary>
        public static void BatchFix()
        {
            Debug.Log("=== BATCH MODE: FIX ===");
            Fix();
        }

        /// <summary>
        /// Entry point for Unity batch mode: -executeMethod ShadowingTutor.Editor.EdumetaMaterialDoctor.BatchVerify
        /// </summary>
        public static void BatchVerify()
        {
            Debug.Log("=== BATCH MODE: VERIFY ===");
            bool passed = Verify();
            if (!passed)
            {
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Run full pipeline: DIAGNOSE -> FIX -> VERIFY
        /// Entry point: -executeMethod ShadowingTutor.Editor.EdumetaMaterialDoctor.BatchAll
        /// </summary>
        public static void BatchAll()
        {
            Debug.Log("=== BATCH MODE: FULL PIPELINE ===\n");
            Diagnose();
            Fix();
            bool passed = Verify();

            if (passed)
            {
                Debug.Log("\n*** ALL CHECKS PASSED ***");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("\n*** VERIFY FAILED - ISSUES REMAIN ***");
                EditorApplication.Exit(1);
            }
        }

        #endregion

        #region Utility Methods

        private static string GetGameObjectPath(GameObject obj, Transform root)
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

        private static bool IsHeadRelatedMaterial(string matNameLower)
        {
            string[] headKeywords = { "hair", "scalp", "brow", "lash", "eye", "iris", "sclera", "cornea",
                "head", "skin", "face", "teeth", "tongue", "lip" };
            return headKeywords.Any(k => matNameLower.Contains(k));
        }

        #endregion
    }
}
