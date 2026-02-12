using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Comprehensive material diagnostic and repair tool.
/// Handles:
/// - Reporting missing _MainTex
/// - Finding and assigning textures by name matching
/// - Fixing prefab material slots that use Default-Material
/// </summary>
public class MaterialDiagnosticAndRepair : Editor
{
    private const string PREFAB_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
    private const string MATERIAL_FOLDER = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Materials";
    private const string CHARACTER_ROOT = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA";

    // ========== PHASE 1: REPORT MISSING MAINTEX ==========

    [MenuItem("Tools/Character Setup/1. REPORT MISSING MAIN TEX")]
    public static void ReportMissingMainTex()
    {
        Debug.Log("=== REPORT: MISSING _MainTex IN PREFAB ===");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogError($"Cannot load prefab: {PREFAB_PATH}");
            return;
        }

        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        int issueCount = 0;

        foreach (var renderer in renderers)
        {
            string rendererPath = GetHierarchyPath(renderer.transform);
            string meshName = GetMeshName(renderer);

            for (int i = 0; i < renderer.sharedMaterials.Length; i++)
            {
                Material mat = renderer.sharedMaterials[i];

                if (mat == null)
                {
                    Debug.LogError($"[NULL MATERIAL] {rendererPath} | Slot[{i}] | Mesh: {meshName}");
                    issueCount++;
                    continue;
                }

                if (mat.name.Contains("Default-Material") || mat.name.Contains("Default Material"))
                {
                    Debug.LogError($"[DEFAULT-MATERIAL] {rendererPath} | Slot[{i}] | Mesh: {meshName} | Mat: {mat.name}");
                    issueCount++;
                    continue;
                }

                // Check if Standard shader with missing _MainTex
                if (mat.shader != null && mat.shader.name == "Standard")
                {
                    Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                    if (mainTex == null)
                    {
                        string matPath = AssetDatabase.GetAssetPath(mat);
                        Debug.LogWarning($"[MISSING _MainTex] {rendererPath} | Slot[{i}] | Mat: {mat.name} | Path: {matPath}");
                        issueCount++;
                    }
                }
            }
        }

        if (issueCount == 0)
        {
            Debug.Log("[REPORT] All materials OK - no issues found!");
        }
        else
        {
            Debug.LogError($"[REPORT] Found {issueCount} material issues. Run repair tools to fix.");
        }

        Debug.Log("=== END REPORT ===");
    }

    // ========== PHASE 2: REPAIR MATERIALS ==========

    [MenuItem("Tools/Character Setup/2. REPAIR MATERIALS (Find & Assign Textures)")]
    public static void RepairMaterialsFindAndAssign()
    {
        Debug.Log("=== REPAIR MATERIALS: FIND & ASSIGN TEXTURES ===");

        // Build texture lookup
        var textureLookup = BuildTextureLookup();
        Debug.Log($"[Repair] Found {textureLookup.Count} textures in character folder");

        // Get Standard shader
        Shader standardShader = Shader.Find("Standard");
        if (standardShader == null)
        {
            Debug.LogError("[Repair] FATAL: Cannot find Standard shader!");
            return;
        }

        // Process materials
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { MATERIAL_FOLDER });
        int fixedCount = 0;
        int unresolvedCount = 0;

        foreach (string guid in matGuids)
        {
            string matPath = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) continue;

            bool wasModified = false;

            // Ensure Standard shader
            if (mat.shader != standardShader)
            {
                // Preserve color before shader change
                Color color = Color.white;
                if (mat.HasProperty("_Color")) color = mat.GetColor("_Color");
                else if (mat.HasProperty("_BaseColor")) color = mat.GetColor("_BaseColor");

                mat.shader = standardShader;
                mat.SetColor("_Color", color);
                wasModified = true;
            }

            // Check _MainTex
            Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;

            if (mainTex == null)
            {
                // Try to find matching texture
                var candidates = FindTextureForMaterial(mat.name, textureLookup);

                if (candidates.Count == 1)
                {
                    mat.SetTexture("_MainTex", candidates[0].texture);
                    Debug.Log($"[FIXED] {mat.name} -> {candidates[0].texture.name} (score: {candidates[0].score})");
                    wasModified = true;
                    fixedCount++;
                }
                else if (candidates.Count > 1)
                {
                    Debug.LogWarning($"[AMBIGUOUS] {mat.name} has {candidates.Count} candidates:");
                    foreach (var c in candidates.Take(5))
                    {
                        Debug.LogWarning($"  - {c.texture.name} (score: {c.score})");
                    }
                    unresolvedCount++;
                }
                else
                {
                    Debug.LogError($"[NO TEXTURE FOUND] {mat.name} at {matPath}");
                    unresolvedCount++;
                }
            }

            // Configure transparency for hair/scalp materials
            string matNameLower = mat.name.ToLower();
            if (matNameLower.Contains("hair") || matNameLower.Contains("scalp") ||
                matNameLower.Contains("transparency") || matNameLower.Contains("babyhair"))
            {
                SetCutoutMode(mat);
                wasModified = true;
            }
            else if (matNameLower.Contains("cornea") || matNameLower.Contains("tearline"))
            {
                SetFadeMode(mat);
                wasModified = true;
            }
            else if (matNameLower.Contains("eyelash"))
            {
                SetCutoutMode(mat);
                wasModified = true;
            }

            if (wasModified)
            {
                EditorUtility.SetDirty(mat);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"=== REPAIR COMPLETE ===");
        Debug.Log($"[Repair] Fixed: {fixedCount} materials");
        Debug.Log($"[Repair] Unresolved: {unresolvedCount} materials");
    }

    // ========== PHASE 3: REMAP PREFAB MATERIAL SLOTS ==========

    [MenuItem("Tools/Character Setup/3. REMAP PREFAB MATERIAL SLOTS")]
    public static void RemapPrefabMaterialSlots()
    {
        Debug.Log("=== REMAP PREFAB MATERIAL SLOTS ===");

        // Load all available materials
        var materialLookup = BuildMaterialLookup();
        Debug.Log($"[Remap] Found {materialLookup.Count} materials in folder");

        // Load prefab contents for editing
        string prefabPath = PREFAB_PATH;
        GameObject prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);

        if (prefabContents == null)
        {
            Debug.LogError($"Cannot load prefab contents: {prefabPath}");
            return;
        }

        int remappedCount = 0;
        int failedCount = 0;

        try
        {
            var renderers = prefabContents.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                Material[] mats = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    Material mat = mats[i];

                    bool needsRemap = mat == null ||
                                     mat.name.Contains("Default-Material") ||
                                     mat.name.Contains("Default Material");

                    if (needsRemap)
                    {
                        // Try to find replacement material based on mesh/renderer name
                        string hint = GetMaterialHint(renderer, i);
                        Material replacement = FindReplacementMaterial(hint, materialLookup);

                        if (replacement != null)
                        {
                            mats[i] = replacement;
                            changed = true;
                            remappedCount++;
                            Debug.Log($"[REMAPPED] {GetHierarchyPath(renderer.transform)} slot[{i}] -> {replacement.name}");
                        }
                        else
                        {
                            failedCount++;
                            Debug.LogWarning($"[FAILED] {GetHierarchyPath(renderer.transform)} slot[{i}] - No replacement found for hint: {hint}");
                        }
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = mats;
                }
            }

            // Save prefab
            PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
            Debug.Log($"[Remap] Prefab saved: {prefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabContents);
        }

        Debug.Log($"=== REMAP COMPLETE ===");
        Debug.Log($"[Remap] Remapped: {remappedCount} slots");
        Debug.Log($"[Remap] Failed: {failedCount} slots");
    }

    // ========== HELPER METHODS ==========

    private static Dictionary<string, Texture2D> BuildTextureLookup()
    {
        var lookup = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

        string[] searchPaths = {
            CHARACTER_ROOT + "/Edumeta_CharacterGirl_AAA 1.fbm",
            CHARACTER_ROOT + "/Edumeta_CharacterGirl_AAA.fbm",
            CHARACTER_ROOT + "/Edumeta_CharacterGirl_AAA-nonhead.fbm",
            CHARACTER_ROOT + "/textures"
        };

        foreach (string path in searchPaths)
        {
            if (!Directory.Exists(path)) continue;

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { path });
            foreach (string guid in guids)
            {
                string texPath = AssetDatabase.GUIDToAssetPath(guid);
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex == null) continue;

                string key = Path.GetFileNameWithoutExtension(texPath);
                if (!lookup.ContainsKey(key))
                {
                    lookup[key] = tex;
                }
            }
        }

        return lookup;
    }

    private static Dictionary<string, Material> BuildMaterialLookup()
    {
        var lookup = new Dictionary<string, Material>(System.StringComparer.OrdinalIgnoreCase);

        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { MATERIAL_FOLDER });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null && !lookup.ContainsKey(mat.name))
            {
                lookup[mat.name] = mat;
            }
        }

        return lookup;
    }

    private struct TextureCandidate
    {
        public Texture2D texture;
        public int score;
    }

    private static List<TextureCandidate> FindTextureForMaterial(string materialName, Dictionary<string, Texture2D> lookup)
    {
        var candidates = new List<TextureCandidate>();

        // Clean up material name for matching
        string baseName = materialName
            .Replace("_Diffuse", "")
            .Replace("_diffuse", "")
            .Replace(" (Instance)", "");

        // Score-based matching
        foreach (var kvp in lookup)
        {
            string texName = kvp.Key;
            int score = 0;

            // Exact match (highest priority)
            if (texName.Equals(materialName, System.StringComparison.OrdinalIgnoreCase))
            {
                score = 100;
            }
            else if (texName.Equals(baseName, System.StringComparison.OrdinalIgnoreCase))
            {
                score = 90;
            }
            else if (texName.Equals(baseName + "_Diffuse", System.StringComparison.OrdinalIgnoreCase))
            {
                score = 85;
            }
            // Contains match
            else if (texName.Contains(baseName) || baseName.Contains(texName))
            {
                score = 50;
                // Boost for Diffuse textures
                if (texName.ToLower().Contains("diffuse"))
                    score += 20;
            }

            if (score > 0)
            {
                candidates.Add(new TextureCandidate { texture = kvp.Value, score = score });
            }
        }

        // Sort by score descending
        candidates.Sort((a, b) => b.score.CompareTo(a.score));

        // If top candidate has high score, return just that one
        if (candidates.Count > 0 && candidates[0].score >= 80)
        {
            return new List<TextureCandidate> { candidates[0] };
        }

        // Return top candidates for manual review
        return candidates.Take(5).ToList();
    }

    private static Material FindReplacementMaterial(string hint, Dictionary<string, Material> lookup)
    {
        if (string.IsNullOrEmpty(hint)) return null;

        // Exact match
        if (lookup.TryGetValue(hint, out Material mat))
            return mat;

        // Case-insensitive exact
        foreach (var kvp in lookup)
        {
            if (kvp.Key.Equals(hint, System.StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        // Contains match
        string hintLower = hint.ToLower();
        foreach (var kvp in lookup)
        {
            if (kvp.Key.ToLower().Contains(hintLower) || hintLower.Contains(kvp.Key.ToLower()))
                return kvp.Value;
        }

        return null;
    }

    private static string GetMaterialHint(Renderer renderer, int slotIndex)
    {
        // Try to get hint from mesh submesh names or renderer name
        string rendererName = renderer.name;

        // Common patterns in CC3/CC4 exports
        if (rendererName.Contains("Hair")) return "Hair_Transparency_Diffuse";
        if (rendererName.Contains("Scalp")) return "Scalp_Transparency_Diffuse";
        if (rendererName.Contains("Eyelash")) return "Std_Eyelash_Diffuse";
        if (rendererName.Contains("Eye_L")) return "Std_Eye_L_Diffuse";
        if (rendererName.Contains("Eye_R")) return "Std_Eye_R_Diffuse";
        if (rendererName.Contains("Teeth")) return slotIndex == 0 ? "Std_Upper_Teeth_Diffuse" : "Std_Lower_Teeth_Diffuse";
        if (rendererName.Contains("Tongue")) return "Std_Tongue_Diffuse";
        if (rendererName.Contains("Body")) return "Std_Skin_Body_Diffuse";
        if (rendererName.Contains("Head")) return "Std_Skin_Head_Diffuse";
        if (rendererName.Contains("Arm")) return "Std_Skin_Arm_Diffuse";
        if (rendererName.Contains("Leg")) return "Std_Skin_Leg_Diffuse";

        return rendererName;
    }

    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private static string GetMeshName(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            return smr.sharedMesh.name;
        if (renderer is MeshRenderer mr)
        {
            var mf = renderer.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                return mf.sharedMesh.name;
        }
        return "unknown";
    }

    private static void SetCutoutMode(Material mat)
    {
        mat.SetFloat("_Mode", 1);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);
        mat.SetFloat("_Cutoff", 0.3f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 2450;
    }

    private static void SetFadeMode(Material mat)
    {
        mat.SetFloat("_Mode", 2);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
    }

    // ========== QUICK FIX ALL ==========

    [MenuItem("Tools/Character Setup/4. FIX ALL (Run All Steps)")]
    public static void FixAll()
    {
        Debug.Log("========================================");
        Debug.Log("=== RUNNING COMPLETE MATERIAL FIX ===");
        Debug.Log("========================================");

        Debug.Log("\n--- Step 1: Initial Report ---");
        ReportMissingMainTex();

        Debug.Log("\n--- Step 2: Repair Materials ---");
        RepairMaterialsFindAndAssign();

        Debug.Log("\n--- Step 3: Remap Prefab Slots ---");
        RemapPrefabMaterialSlots();

        Debug.Log("\n--- Step 4: Final Report ---");
        ReportMissingMainTex();

        Debug.Log("\n========================================");
        Debug.Log("=== FIX COMPLETE - Enter Play Mode to verify ===");
        Debug.Log("========================================");
    }
}
