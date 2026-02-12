using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Comprehensive diagnostic and repair tool for character head/hair/eyes/eyebrows materials.
/// Fixes broken GUIDs, missing textures, wrong shaders, and "Broken text PPtr" errors.
/// </summary>
public class CharacterHeadMaterialRepair : Editor
{
    private const string PREFAB_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
    private const string MATERIAL_FOLDER = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Materials";
    private const string CHARACTER_ROOT = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA";

    // Categories for materials
    private static readonly string[] HAIR_KEYWORDS = { "hair", "scalp", "babyhair" };
    private static readonly string[] EYE_KEYWORDS = { "eye", "cornea", "sclera", "iris", "tearline", "occlusion" };
    private static readonly string[] BROW_KEYWORDS = { "brow", "eyebrow", "eyelash" };
    private static readonly string[] SKIN_KEYWORDS = { "skin", "head", "body", "arm", "leg", "nails" };

    // ========== PHASE A: DIAGNOSTIC REPORT ==========

    [MenuItem("Tools/Character Setup/DIAGNOSTIC: Head-Hair-Eyes Report")]
    public static void DiagnosticReport()
    {
        Debug.Log("=" + new string('=', 70));
        Debug.Log("=== PHASE A: DIAGNOSTIC REPORT - Head/Hair/Eyes/Brows ===");
        Debug.Log("=" + new string('=', 70));

        // Load prefab
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogError($"Cannot load prefab: {PREFAB_PATH}");
            return;
        }

        // Analyze renderers
        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        Debug.Log($"\n[A1] Found {renderers.Length} renderers in prefab");

        var materialIssues = new Dictionary<string, List<string>>();
        var categoryStats = new Dictionary<string, int>
        {
            { "hair", 0 }, { "eyes", 0 }, { "brows", 0 }, { "skin", 0 }, { "other", 0 }
        };

        Debug.Log("\n[A2] RENDERER -> MATERIAL TABLE:");
        Debug.Log("-" + new string('-', 100));
        Debug.Log($"{"Renderer Path",-40} | {"Material",-30} | {"Shader",-20} | Missing Props");
        Debug.Log("-" + new string('-', 100));

        foreach (var renderer in renderers)
        {
            string rendererPath = GetHierarchyPath(renderer.transform);
            string rendererName = renderer.name.ToLower();

            foreach (var mat in renderer.sharedMaterials)
            {
                if (mat == null)
                {
                    Debug.LogError($"[NULL MATERIAL] {rendererPath}");
                    continue;
                }

                string shaderName = mat.shader != null ? mat.shader.name : "NULL";
                bool isDefaultMat = mat.name.Contains("Default-Material") || mat.name.Contains("Default Material");

                List<string> missingProps = GetMissingTextureProps(mat);
                string missingStr = missingProps.Count > 0 ? string.Join(", ", missingProps) : "OK";

                // Categorize
                string category = CategorMaterial(mat.name);
                categoryStats[category]++;

                // Truncate for display
                string shortPath = rendererPath.Length > 38 ? "..." + rendererPath.Substring(rendererPath.Length - 35) : rendererPath;
                string shortMat = mat.name.Length > 28 ? mat.name.Substring(0, 25) + "..." : mat.name;
                string shortShader = shaderName.Length > 18 ? shaderName.Substring(0, 15) + "..." : shaderName;

                string status = isDefaultMat ? "[DEFAULT!]" : (missingProps.Count > 0 ? "[MISSING]" : "");
                Debug.Log($"{shortPath,-40} | {shortMat,-30} | {shortShader,-20} | {missingStr} {status}");

                if (missingProps.Count > 0 || isDefaultMat)
                {
                    if (!materialIssues.ContainsKey(mat.name))
                        materialIssues[mat.name] = new List<string>();
                    materialIssues[mat.name].AddRange(missingProps);
                }
            }
        }

        Debug.Log("-" + new string('-', 100));
        Debug.Log($"\n[A3] CATEGORY STATS:");
        foreach (var kvp in categoryStats)
        {
            Debug.Log($"  {kvp.Key}: {kvp.Value} materials");
        }

        Debug.Log($"\n[A4] MATERIALS WITH ISSUES: {materialIssues.Count}");
        foreach (var kvp in materialIssues)
        {
            Debug.Log($"  - {kvp.Key}: missing [{string.Join(", ", kvp.Value.Distinct())}]");
        }

        // Phase B: Check for broken GUIDs
        Debug.Log("\n" + "=" + new string('=', 70));
        Debug.Log("=== PHASE B: BROKEN GUID DETECTION ===");
        Debug.Log("=" + new string('=', 70));

        CheckBrokenGuids();
    }

    private static void CheckBrokenGuids()
    {
        // Build a set of all valid GUIDs in project
        var validGuids = new HashSet<string>();
        string[] allMetaFiles = Directory.GetFiles(Application.dataPath, "*.meta", SearchOption.AllDirectories);
        foreach (string metaPath in allMetaFiles)
        {
            string content = File.ReadAllText(metaPath);
            var match = Regex.Match(content, @"^guid:\s*([a-f0-9]+)", RegexOptions.Multiline);
            if (match.Success)
            {
                validGuids.Add(match.Groups[1].Value);
            }
        }
        Debug.Log($"[B1] Total valid GUIDs in project: {validGuids.Count}");

        // Check material files
        string[] matFiles = Directory.GetFiles(Path.Combine(Application.dataPath, "Art/Characters/Edumeta_CharacterGirl_AAA/Materials"), "*.mat");

        int totalBroken = 0;
        var brokenMaterials = new Dictionary<string, List<string>>();

        foreach (string matPath in matFiles)
        {
            string content = File.ReadAllText(matPath);
            string matName = Path.GetFileNameWithoutExtension(matPath);

            // Find all GUID references
            var guidMatches = Regex.Matches(content, @"guid:\s*([a-f0-9]{32})");
            var brokenGuids = new List<string>();

            foreach (Match m in guidMatches)
            {
                string guid = m.Groups[1].Value;
                if (!validGuids.Contains(guid))
                {
                    brokenGuids.Add(guid);
                    totalBroken++;
                }
            }

            if (brokenGuids.Count > 0)
            {
                brokenMaterials[matName] = brokenGuids;
                string category = CategorMaterial(matName);
                Debug.LogWarning($"[BROKEN GUID] {matName} ({category}): {brokenGuids.Count} invalid references");
            }
        }

        Debug.Log($"\n[B2] SUMMARY:");
        Debug.Log($"  Total materials scanned: {matFiles.Length}");
        Debug.Log($"  Materials with broken GUIDs: {brokenMaterials.Count}");
        Debug.Log($"  Total broken GUID references: {totalBroken}");

        // Categorize broken materials
        var byCategory = new Dictionary<string, List<string>> {
            {"hair/scalp", new List<string>()},
            {"eyes", new List<string>()},
            {"brows/eyelash", new List<string>()},
            {"skin/head", new List<string>()},
            {"other", new List<string>()}
        };

        foreach (var mat in brokenMaterials.Keys)
        {
            string cat = CategorMaterial(mat);
            string catKey = cat == "hair" ? "hair/scalp" :
                           cat == "eyes" ? "eyes" :
                           cat == "brows" ? "brows/eyelash" :
                           cat == "skin" ? "skin/head" : "other";
            byCategory[catKey].Add(mat);
        }

        Debug.Log("\n[B3] BROKEN BY CATEGORY:");
        foreach (var kvp in byCategory)
        {
            if (kvp.Value.Count > 0)
            {
                Debug.Log($"  {kvp.Key}: {string.Join(", ", kvp.Value)}");
            }
        }
    }

    // ========== PHASE C: REPAIR ==========

    [MenuItem("Tools/Character Setup/REPAIR HEAD MATERIALS (Complete Fix)")]
    public static void RepairHeadMaterials()
    {
        Debug.Log("=" + new string('=', 70));
        Debug.Log("=== PHASE C: REPAIR HEAD MATERIALS (Complete Fix) ===");
        Debug.Log("=" + new string('=', 70));

        // Step 1: Build texture lookup
        var textureLookup = BuildComprehensiveTextureLookup();
        Debug.Log($"[C1] Built texture lookup with {textureLookup.Count} textures");

        // Step 2: Get Standard shader
        Shader standardShader = Shader.Find("Standard");
        if (standardShader == null)
        {
            Debug.LogError("[FATAL] Cannot find Standard shader!");
            return;
        }

        // Step 3: Process all materials
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { MATERIAL_FOLDER });
        int fixedTextures = 0;
        int fixedShaders = 0;
        int fixedRenderMode = 0;
        var fixedMaterials = new List<string>();

        foreach (string guid in matGuids)
        {
            string matPath = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) continue;

            bool wasModified = false;
            string matNameLower = mat.name.ToLower();

            // Fix 1: Ensure Standard shader
            if (mat.shader == null || !mat.shader.isSupported ||
                mat.shader.name == "Hidden/InternalErrorShader" ||
                mat.shader.name.Contains("Universal") || mat.shader.name.Contains("URP"))
            {
                // Preserve existing textures before shader change
                Texture mainTex = null;
                if (mat.HasProperty("_MainTex")) mainTex = mat.GetTexture("_MainTex");
                if (mainTex == null && mat.HasProperty("_BaseMap")) mainTex = mat.GetTexture("_BaseMap");

                Color color = Color.white;
                if (mat.HasProperty("_Color")) color = mat.GetColor("_Color");
                else if (mat.HasProperty("_BaseColor")) color = mat.GetColor("_BaseColor");

                mat.shader = standardShader;
                mat.SetColor("_Color", color);
                if (mainTex != null) mat.SetTexture("_MainTex", mainTex);

                fixedShaders++;
                wasModified = true;
                Debug.Log($"[SHADER FIX] {mat.name} -> Standard");
            }

            // Fix 2: Find and assign missing textures
            bool needsMainTex = !mat.HasProperty("_MainTex") || mat.GetTexture("_MainTex") == null;

            if (needsMainTex)
            {
                Texture2D foundTex = FindBestTextureMatch(mat.name, textureLookup);
                if (foundTex != null)
                {
                    mat.SetTexture("_MainTex", foundTex);
                    fixedTextures++;
                    wasModified = true;
                    Debug.Log($"[TEXTURE FIX] {mat.name} -> {foundTex.name}");
                }
                else
                {
                    Debug.LogWarning($"[NO TEXTURE FOUND] {mat.name}");
                }
            }

            // Fix 3: Set correct render mode for hair/scalp/eyebrows
            bool isTransparent = HAIR_KEYWORDS.Any(k => matNameLower.Contains(k)) ||
                                BROW_KEYWORDS.Any(k => matNameLower.Contains(k));
            bool isEyeTransparent = matNameLower.Contains("cornea") || matNameLower.Contains("tearline") ||
                                   matNameLower.Contains("occlusion");

            if (isTransparent)
            {
                SetCutoutMode(mat, 0.3f);
                fixedRenderMode++;
                wasModified = true;
            }
            else if (isEyeTransparent)
            {
                SetFadeMode(mat);
                fixedRenderMode++;
                wasModified = true;
            }

            if (wasModified)
            {
                EditorUtility.SetDirty(mat);
                fixedMaterials.Add(matPath);
            }
        }

        // Save and reserialize
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (fixedMaterials.Count > 0)
        {
            Debug.Log($"\n[C2] Force-reserializing {fixedMaterials.Count} materials...");
            AssetDatabase.ForceReserializeAssets(fixedMaterials, ForceReserializeAssetsOptions.ReserializeAssets);
        }

        Debug.Log("\n" + "=" + new string('=', 70));
        Debug.Log("=== REPAIR COMPLETE ===");
        Debug.Log($"  Fixed shaders: {fixedShaders}");
        Debug.Log($"  Fixed textures: {fixedTextures}");
        Debug.Log($"  Fixed render modes: {fixedRenderMode}");
        Debug.Log($"  Total materials modified: {fixedMaterials.Count}");
        Debug.Log("=" + new string('=', 70));

        // Run verification
        Debug.Log("\n[C3] Running post-repair verification...\n");
        ReportMissingTextures();
    }

    [MenuItem("Tools/Character Setup/REPORT MISSING TEXTURES (Head-Hair-Eyes-Brows)")]
    public static void ReportMissingTextures()
    {
        Debug.Log("=== POST-REPAIR VERIFICATION ===");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogError($"Cannot load prefab: {PREFAB_PATH}");
            return;
        }

        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        int missingCount = 0;
        var missingByCategory = new Dictionary<string, List<string>> {
            {"hair/scalp/babyhair", new List<string>()},
            {"eyebrows/eyelash", new List<string>()},
            {"eyes", new List<string>()},
            {"head/skin", new List<string>()},
            {"other", new List<string>()}
        };

        foreach (var renderer in renderers)
        {
            string rendererPath = GetHierarchyPath(renderer.transform);

            foreach (var mat in renderer.sharedMaterials)
            {
                if (mat == null) continue;

                bool isDefaultMat = mat.name.Contains("Default-Material") || mat.name.Contains("Default Material");
                Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;

                if (mainTex == null || isDefaultMat)
                {
                    string category = CategorMaterial(mat.name);
                    string catKey = category == "hair" ? "hair/scalp/babyhair" :
                                   category == "brows" ? "eyebrows/eyelash" :
                                   category == "eyes" ? "eyes" :
                                   category == "skin" ? "head/skin" : "other";

                    string issue = isDefaultMat ? "DEFAULT-MATERIAL" : "MISSING _MainTex";
                    missingByCategory[catKey].Add($"{mat.name} ({issue}) on {rendererPath}");
                    missingCount++;
                }
            }
        }

        Debug.Log($"\nTotal materials with missing _MainTex or Default-Material: {missingCount}");

        foreach (var kvp in missingByCategory)
        {
            if (kvp.Value.Count > 0)
            {
                Debug.LogError($"\n[MISSING - {kvp.Key}]: {kvp.Value.Count} issues");
                foreach (var issue in kvp.Value)
                {
                    Debug.LogError($"  - {issue}");
                }
            }
            else
            {
                Debug.Log($"[OK - {kvp.Key}]: 0 issues");
            }
        }

        if (missingCount == 0)
        {
            Debug.Log("\n*** ALL HEAD/HAIR/EYES/BROWS MATERIALS VERIFIED OK ***");
        }
    }

    // ========== HELPER METHODS ==========

    private static Dictionary<string, Texture2D> BuildComprehensiveTextureLookup()
    {
        var lookup = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

        // Search paths in priority order
        string[] searchPaths = {
            CHARACTER_ROOT + "/Edumeta_CharacterGirl_AAA 1.fbm",
            CHARACTER_ROOT + "/Edumeta_CharacterGirl_AAA.fbm",
            CHARACTER_ROOT + "/Edumeta_CharacterGirl_AAA-nonhead.fbm",
            CHARACTER_ROOT + "/textures",
            CHARACTER_ROOT + "/Materials", // Sometimes textures are here
            CHARACTER_ROOT
        };

        foreach (string basePath in searchPaths)
        {
            if (!Directory.Exists(basePath)) continue;

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { basePath });
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

    private static Texture2D FindBestTextureMatch(string materialName, Dictionary<string, Texture2D> lookup)
    {
        // Try exact match first
        if (lookup.TryGetValue(materialName, out Texture2D exact))
            return exact;

        // Try without _Diffuse suffix
        string baseName = materialName.Replace("_Diffuse", "").Replace("_diffuse", "");
        if (lookup.TryGetValue(baseName, out Texture2D withoutSuffix))
            return withoutSuffix;

        // Try with _Diffuse added
        if (lookup.TryGetValue(baseName + "_Diffuse", out Texture2D withSuffix))
            return withSuffix;

        // Fuzzy match: find textures that contain the material base name
        foreach (var kvp in lookup)
        {
            string texNameLower = kvp.Key.ToLower();
            string baseNameLower = baseName.ToLower();

            // Prioritize diffuse textures
            if (texNameLower.Contains(baseNameLower) && texNameLower.Contains("diffuse"))
                return kvp.Value;
        }

        // Broader fuzzy match
        foreach (var kvp in lookup)
        {
            if (kvp.Key.ToLower().Contains(baseName.ToLower()))
                return kvp.Value;
        }

        return null;
    }

    private static List<string> GetMissingTextureProps(Material mat)
    {
        var missing = new List<string>();

        // Check main texture properties
        string[] texProps = { "_MainTex", "_BaseMap", "_BumpMap" };
        foreach (string prop in texProps)
        {
            if (mat.HasProperty(prop))
            {
                Texture tex = mat.GetTexture(prop);
                if (tex == null && prop == "_MainTex")
                {
                    missing.Add(prop);
                }
            }
        }

        // If no _MainTex property at all, that's an issue
        if (!mat.HasProperty("_MainTex") && !mat.HasProperty("_BaseMap"))
        {
            missing.Add("NO_TEX_PROP");
        }

        return missing;
    }

    private static string CategorMaterial(string matName)
    {
        string lower = matName.ToLower();

        if (HAIR_KEYWORDS.Any(k => lower.Contains(k)))
            return "hair";
        if (EYE_KEYWORDS.Any(k => lower.Contains(k)))
            return "eyes";
        if (BROW_KEYWORDS.Any(k => lower.Contains(k)))
            return "brows";
        if (SKIN_KEYWORDS.Any(k => lower.Contains(k)))
            return "skin";

        return "other";
    }

    private static void SetCutoutMode(Material mat, float cutoff = 0.3f)
    {
        mat.SetFloat("_Mode", 1); // Cutout
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);
        mat.SetFloat("_Cutoff", cutoff);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 2450;
    }

    private static void SetFadeMode(Material mat)
    {
        mat.SetFloat("_Mode", 2); // Fade
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
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

    // ========== FIX MATERIAL FILES DIRECTLY (FOR BROKEN PPtr) ==========

    [MenuItem("Tools/Character Setup/FIX BROKEN GUIDs IN MAT FILES")]
    public static void FixBrokenGuidsInFiles()
    {
        Debug.Log("=== FIXING BROKEN GUIDs DIRECTLY IN .mat FILES ===");

        // Build texture name -> GUID mapping
        string fbmPath = Path.Combine(Application.dataPath, "Art/Characters/Edumeta_CharacterGirl_AAA/Edumeta_CharacterGirl_AAA 1.fbm");
        var textureGuidMap = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(fbmPath))
        {
            foreach (string metaFile in Directory.GetFiles(fbmPath, "*.png.meta"))
            {
                string content = File.ReadAllText(metaFile);
                var match = Regex.Match(content, @"^guid:\s*([a-f0-9]+)", RegexOptions.Multiline);
                if (match.Success)
                {
                    string texName = Path.GetFileNameWithoutExtension(metaFile).Replace(".png", "");
                    textureGuidMap[texName] = match.Groups[1].Value;
                }
            }
        }

        Debug.Log($"Built texture GUID map with {textureGuidMap.Count} entries");

        // Process each material file
        string matFolder = Path.Combine(Application.dataPath, "Art/Characters/Edumeta_CharacterGirl_AAA/Materials");
        int fixedCount = 0;
        var fixedPaths = new List<string>();

        foreach (string matFile in Directory.GetFiles(matFolder, "*.mat"))
        {
            string content = File.ReadAllText(matFile);
            string originalContent = content;
            string matName = Path.GetFileNameWithoutExtension(matFile);

            // Find the correct texture GUID
            string texName = matName.Replace("_Diffuse", "");
            string correctGuid = null;

            if (textureGuidMap.TryGetValue(texName, out string g1))
                correctGuid = g1;
            else if (textureGuidMap.TryGetValue(matName, out string g2))
                correctGuid = g2;
            else if (textureGuidMap.TryGetValue(texName + "_Diffuse", out string g3))
                correctGuid = g3;

            if (correctGuid == null)
            {
                Debug.LogWarning($"[SKIP] {matName} - no matching texture found");
                continue;
            }

            // Replace _MainTex GUID
            content = Regex.Replace(
                content,
                @"(_MainTex:\s*\n\s*m_Texture:\s*\{[^}]*guid:\s*)([a-f0-9]+)([^}]*\})",
                m => $"{m.Groups[1].Value}{correctGuid}{m.Groups[3].Value}"
            );

            // Replace _BaseMap GUID
            content = Regex.Replace(
                content,
                @"(_BaseMap:\s*\n\s*m_Texture:\s*\{[^}]*guid:\s*)([a-f0-9]+)([^}]*\})",
                m => $"{m.Groups[1].Value}{correctGuid}{m.Groups[3].Value}"
            );

            // Update shader to Standard (fileID: 46)
            content = Regex.Replace(
                content,
                @"m_Shader:\s*\{[^}]+\}",
                "m_Shader: {fileID: 46}"
            );

            // Remove orphaned MonoBehaviour sections (URP material editor scripts)
            content = Regex.Replace(
                content,
                @"--- !u!114 &-?\d+\nMonoBehaviour:.*?version: \d+\n",
                "",
                RegexOptions.Singleline
            );

            if (content != originalContent)
            {
                File.WriteAllText(matFile, content);
                string assetPath = "Assets" + matFile.Substring(Application.dataPath.Length);
                fixedPaths.Add(assetPath);
                fixedCount++;
                Debug.Log($"[FIXED] {matName} -> GUID: {correctGuid}");
            }
        }

        AssetDatabase.Refresh();

        if (fixedPaths.Count > 0)
        {
            Debug.Log($"\nForce-reserializing {fixedPaths.Count} materials...");
            AssetDatabase.ForceReserializeAssets(fixedPaths, ForceReserializeAssetsOptions.ReserializeAssets);
        }

        Debug.Log($"\n=== COMPLETE: Fixed {fixedCount} material files ===");
    }

    // ========== FULL FIX SEQUENCE ==========

    [MenuItem("Tools/Character Setup/RUN COMPLETE FIX SEQUENCE")]
    public static void RunCompleteFixSequence()
    {
        Debug.Log("\n" + new string('*', 80));
        Debug.Log("*** STARTING COMPLETE CHARACTER MATERIAL FIX SEQUENCE ***");
        Debug.Log(new string('*', 80) + "\n");

        Debug.Log("STEP 1: Diagnostic Report...\n");
        DiagnosticReport();

        Debug.Log("\n\nSTEP 2: Fixing Broken GUIDs in .mat files...\n");
        FixBrokenGuidsInFiles();

        Debug.Log("\n\nSTEP 3: Repair Head Materials (API-level fixes)...\n");
        RepairHeadMaterials();

        Debug.Log("\n\nSTEP 4: Final Verification...\n");
        ReportMissingTextures();

        Debug.Log("\n" + new string('*', 80));
        Debug.Log("*** COMPLETE FIX SEQUENCE FINISHED ***");
        Debug.Log(new string('*', 80));
    }
}
