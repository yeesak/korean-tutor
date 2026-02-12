using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Comprehensive character material diagnostic and repair tool.
/// Handles: broken GUIDs, missing textures, Default-Material slots, Broken text PPtr errors.
/// </summary>
public class CharacterMaterialDoctor : Editor
{
    private const string PREFAB_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
    private const string MATERIAL_FOLDER = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Materials";
    private const string CHARACTER_ROOT = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA";

    private static readonly string[] TEXTURE_PROPS = { "_MainTex", "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" };

    // ==================== PHASE 1: DIAGNOSE ====================

    [MenuItem("Tools/Character Setup/DIAGNOSE (Print Full Renderer-Material Table)")]
    public static void Diagnose()
    {
        Debug.Log("\n" + new string('=', 100));
        Debug.Log("=== PHASE 1: HARD DIAGNOSIS ===");
        Debug.Log(new string('=', 100));

        // Load prefab
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogError($"Cannot load prefab: {PREFAB_PATH}");
            return;
        }

        // === SECTION 1: Renderer/Material Table ===
        Debug.Log("\n--- SECTION 1: RENDERER / MATERIAL TABLE ---\n");

        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        var issues = new List<RendererIssue>();

        Debug.Log($"{"Path",-50} | {"Mesh",-20} | {"Slot",-4} | {"Material",-30} | {"Shader",-20} | Issues");
        Debug.Log(new string('-', 150));

        foreach (var renderer in renderers)
        {
            string rendererPath = GetHierarchyPath(renderer.transform);
            string meshName = GetMeshName(renderer);
            int subMeshCount = GetSubMeshCount(renderer);

            for (int i = 0; i < Mathf.Max(renderer.sharedMaterials.Length, subMeshCount); i++)
            {
                Material mat = i < renderer.sharedMaterials.Length ? renderer.sharedMaterials[i] : null;

                string matName = "(null)";
                string shaderName = "-";
                string matPath = "-";
                var issueFlags = new List<string>();

                if (mat == null)
                {
                    issueFlags.Add("NULL_SLOT");
                }
                else
                {
                    matName = mat.name;
                    matPath = AssetDatabase.GetAssetPath(mat);
                    shaderName = mat.shader != null ? mat.shader.name : "NULL_SHADER";

                    // Check for Default-Material
                    if (mat.name.Contains("Default-Material") || mat.name.Contains("Default Material"))
                    {
                        issueFlags.Add("DEFAULT_MAT");
                    }

                    // Check for missing textures
                    foreach (string prop in TEXTURE_PROPS)
                    {
                        if (mat.HasProperty(prop) && mat.GetTexture(prop) == null)
                        {
                            if (prop == "_MainTex" || prop == "_BaseMap")
                            {
                                issueFlags.Add($"MISSING:{prop}");
                            }
                        }
                    }

                    // Check for bad shader
                    if (mat.shader == null || !mat.shader.isSupported ||
                        mat.shader.name == "Hidden/InternalErrorShader" ||
                        mat.shader.name.Contains("Universal") || mat.shader.name.Contains("URP"))
                    {
                        issueFlags.Add("BAD_SHADER");
                    }
                }

                // Check if slot exceeds materials array
                if (i >= renderer.sharedMaterials.Length)
                {
                    issueFlags.Add("SLOT_MISSING");
                }

                string shortPath = rendererPath.Length > 48 ? "..." + rendererPath.Substring(rendererPath.Length - 45) : rendererPath;
                string shortMat = matName.Length > 28 ? matName.Substring(0, 25) + "..." : matName;
                string shortShader = shaderName.Length > 18 ? shaderName.Substring(0, 15) + "..." : shaderName;
                string issueStr = issueFlags.Count > 0 ? string.Join(", ", issueFlags) : "OK";

                if (issueFlags.Count > 0)
                {
                    Debug.LogWarning($"{shortPath,-50} | {meshName,-20} | {i,-4} | {shortMat,-30} | {shortShader,-20} | {issueStr}");
                    issues.Add(new RendererIssue {
                        RendererPath = rendererPath,
                        SlotIndex = i,
                        MaterialName = matName,
                        Issues = issueFlags
                    });
                }
                else
                {
                    Debug.Log($"{shortPath,-50} | {meshName,-20} | {i,-4} | {shortMat,-30} | {shortShader,-20} | {issueStr}");
                }
            }
        }

        Debug.Log(new string('-', 150));
        Debug.Log($"Total renderers: {renderers.Length}");
        Debug.Log($"Total issues found: {issues.Count}");

        // === SECTION 2: Broken PPtr Detection ===
        Debug.Log("\n--- SECTION 2: BROKEN TEXT PPTR DETECTION ---\n");
        DetectBrokenPPtr();

        // === SECTION 3: FBX Importer Settings ===
        Debug.Log("\n--- SECTION 3: FBX IMPORTER SETTINGS ---\n");
        CheckFBXImporterSettings();

        // === SUMMARY ===
        Debug.Log("\n--- DIAGNOSIS SUMMARY ---\n");

        int nullSlots = issues.Count(i => i.Issues.Contains("NULL_SLOT"));
        int defaultMats = issues.Count(i => i.Issues.Contains("DEFAULT_MAT"));
        int missingMainTex = issues.Count(i => i.Issues.Any(x => x.StartsWith("MISSING:")));
        int badShaders = issues.Count(i => i.Issues.Contains("BAD_SHADER"));

        Debug.Log($"NULL material slots: {nullSlots}");
        Debug.Log($"Default-Material slots: {defaultMats}");
        Debug.Log($"Missing _MainTex/_BaseMap: {missingMainTex}");
        Debug.Log($"Bad/URP shaders: {badShaders}");

        if (issues.Count > 0)
        {
            Debug.LogError($"\n*** {issues.Count} ISSUES FOUND - Run FIX to repair ***");
        }
        else
        {
            Debug.Log("\n*** ALL CLEAR - No issues detected ***");
        }
    }

    private static void DetectBrokenPPtr()
    {
        // Scan material files for potential broken references
        string matFolder = Path.Combine(Application.dataPath, "Art/Characters/Edumeta_CharacterGirl_AAA/Materials");
        if (!Directory.Exists(matFolder))
        {
            Debug.LogWarning("Material folder not found");
            return;
        }

        // Build valid GUID set
        var validGuids = new HashSet<string>();
        string[] allMetaFiles = Directory.GetFiles(Application.dataPath, "*.meta", SearchOption.AllDirectories);
        foreach (string metaPath in allMetaFiles)
        {
            try
            {
                string content = File.ReadAllText(metaPath);
                var match = Regex.Match(content, @"^guid:\s*([a-f0-9]+)", RegexOptions.Multiline);
                if (match.Success)
                {
                    validGuids.Add(match.Groups[1].Value);
                }
            }
            catch { }
        }

        Debug.Log($"Valid GUIDs in project: {validGuids.Count}");

        int brokenCount = 0;
        foreach (string matPath in Directory.GetFiles(matFolder, "*.mat"))
        {
            string content = File.ReadAllText(matPath);
            string matName = Path.GetFileNameWithoutExtension(matPath);

            // Find all GUID references
            var guidMatches = Regex.Matches(content, @"guid:\s*([a-f0-9]{32})");
            var invalidGuids = new List<string>();

            foreach (Match m in guidMatches)
            {
                string guid = m.Groups[1].Value;
                if (!validGuids.Contains(guid))
                {
                    invalidGuids.Add(guid);
                }
            }

            // Check for orphaned MonoBehaviour
            bool hasOrphanedMono = content.Contains("--- !u!114 &") && content.Contains("MonoBehaviour:");

            if (invalidGuids.Count > 0 || hasOrphanedMono)
            {
                brokenCount++;
                string issues = "";
                if (invalidGuids.Count > 0) issues += $"INVALID_GUIDS:{invalidGuids.Count}";
                if (hasOrphanedMono) issues += " ORPHANED_MONO";
                Debug.LogWarning($"[BROKEN PPTR] {matName}: {issues}");
            }
        }

        Debug.Log($"\nMaterials with potential Broken PPtr: {brokenCount}");
    }

    private static void CheckFBXImporterSettings()
    {
        string[] fbxFiles = AssetDatabase.FindAssets("t:Model", new[] { CHARACTER_ROOT });

        Debug.Log($"{"Model Path",-70} | {"Mat Location",-15} | {"Extract Mats"}");
        Debug.Log(new string('-', 110));

        foreach (string guid in fbxFiles)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;

            string shortPath = path.Length > 68 ? "..." + path.Substring(path.Length - 65) : path;
            string matLocation = importer.materialLocation.ToString();
            string extractMats = importer.materialImportMode.ToString();

            Debug.Log($"{shortPath,-70} | {matLocation,-15} | {extractMats}");
        }
    }

    // ==================== PHASE 2: FIX ====================

    [MenuItem("Tools/Character Setup/FIX (Complete Head-Hair-Eyes-Brows Repair)")]
    public static void Fix()
    {
        Debug.Log("\n" + new string('=', 100));
        Debug.Log("=== PHASE 2: FIX ===");
        Debug.Log(new string('=', 100));

        // Step A: Fix FBX importer settings
        Debug.Log("\n--- STEP A: FIX FBX IMPORTER SETTINGS ---\n");
        FixFBXImporterSettings();

        // Step B: Rebuild/repair materials
        Debug.Log("\n--- STEP B: REPAIR MATERIALS ---\n");
        RepairMaterials();

        // Step C: Fix prefab renderer slots
        Debug.Log("\n--- STEP C: FIX PREFAB RENDERER SLOTS ---\n");
        FixPrefabRendererSlots();

        // Step D: Force refresh
        Debug.Log("\n--- STEP D: FORCE REFRESH ---\n");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("\n=== FIX COMPLETE - Running VERIFY ===\n");
        Verify();
    }

    private static void FixFBXImporterSettings()
    {
        string[] fbxFiles = AssetDatabase.FindAssets("t:Model", new[] { CHARACTER_ROOT });
        int fixedCount = 0;

        foreach (string guid in fbxFiles)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;

            bool needsFix = false;

            // Set to use external materials
            if (importer.materialLocation != ModelImporterMaterialLocation.External)
            {
                importer.materialLocation = ModelImporterMaterialLocation.External;
                needsFix = true;
            }

            // Don't import materials (use existing external ones)
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                needsFix = true;
            }

            if (needsFix)
            {
                importer.SaveAndReimport();
                fixedCount++;
                Debug.Log($"[FIXED IMPORTER] {path}");
            }
        }

        Debug.Log($"Fixed {fixedCount} model importers");
    }

    private static void RepairMaterials()
    {
        // Build texture lookup
        var textureLookup = BuildTextureLookup();
        Debug.Log($"Texture lookup contains {textureLookup.Count} textures");

        // Get Standard shader
        Shader standardShader = Shader.Find("Standard");
        if (standardShader == null)
        {
            Debug.LogError("Cannot find Standard shader!");
            return;
        }

        // Process materials
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { MATERIAL_FOLDER });
        int fixedCount = 0;
        var fixedPaths = new List<string>();

        foreach (string guid in matGuids)
        {
            string matPath = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) continue;

            bool wasFixed = false;
            string matNameLower = mat.name.ToLower();

            // Fix shader
            if (mat.shader == null || !mat.shader.isSupported ||
                mat.shader.name == "Hidden/InternalErrorShader" ||
                mat.shader.name.Contains("Universal") || mat.shader.name.Contains("URP"))
            {
                // Preserve textures
                Texture mainTex = null;
                if (mat.HasProperty("_MainTex")) mainTex = mat.GetTexture("_MainTex");
                if (mainTex == null && mat.HasProperty("_BaseMap")) mainTex = mat.GetTexture("_BaseMap");

                Color color = Color.white;
                if (mat.HasProperty("_Color")) color = mat.GetColor("_Color");
                else if (mat.HasProperty("_BaseColor")) color = mat.GetColor("_BaseColor");

                mat.shader = standardShader;
                mat.SetColor("_Color", color);
                if (mainTex != null) mat.SetTexture("_MainTex", mainTex);

                wasFixed = true;
                Debug.Log($"[FIXED SHADER] {mat.name}");
            }

            // Fix missing _MainTex
            if (!mat.HasProperty("_MainTex") || mat.GetTexture("_MainTex") == null)
            {
                Texture2D foundTex = FindTextureForMaterial(mat.name, textureLookup);
                if (foundTex != null)
                {
                    mat.SetTexture("_MainTex", foundTex);
                    wasFixed = true;
                    Debug.Log($"[FIXED TEXTURE] {mat.name} -> {foundTex.name}");
                }
                else
                {
                    Debug.LogWarning($"[NO TEXTURE] {mat.name}");
                }
            }

            // Set render mode for transparent materials
            bool isTransparent = matNameLower.Contains("hair") || matNameLower.Contains("scalp") ||
                                matNameLower.Contains("babyhair") || matNameLower.Contains("eyelash") ||
                                matNameLower.Contains("brow");
            bool isFade = matNameLower.Contains("cornea") || matNameLower.Contains("tearline") ||
                         matNameLower.Contains("occlusion");

            if (isTransparent)
            {
                SetCutoutMode(mat, 0.3f);
                wasFixed = true;
            }
            else if (isFade)
            {
                SetFadeMode(mat);
                wasFixed = true;
            }

            if (wasFixed)
            {
                EditorUtility.SetDirty(mat);
                fixedPaths.Add(matPath);
                fixedCount++;
            }
        }

        AssetDatabase.SaveAssets();

        if (fixedPaths.Count > 0)
        {
            AssetDatabase.ForceReserializeAssets(fixedPaths, ForceReserializeAssetsOptions.ReserializeAssets);
        }

        Debug.Log($"Repaired {fixedCount} materials");
    }

    private static void FixPrefabRendererSlots()
    {
        // Build material lookup
        var materialLookup = new Dictionary<string, Material>(System.StringComparer.OrdinalIgnoreCase);
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { MATERIAL_FOLDER });
        foreach (string guid in matGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null && !materialLookup.ContainsKey(mat.name))
            {
                materialLookup[mat.name] = mat;
            }
        }

        // Load prefab for editing
        GameObject prefabContents = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (prefabContents == null)
        {
            Debug.LogError($"Cannot load prefab contents: {PREFAB_PATH}");
            return;
        }

        int fixedSlots = 0;

        try
        {
            var renderers = prefabContents.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                Material[] mats = renderer.sharedMaterials;
                bool changed = false;
                int subMeshCount = GetSubMeshCount(renderer);

                // Ensure we have enough material slots
                if (mats.Length < subMeshCount)
                {
                    var newMats = new Material[subMeshCount];
                    for (int i = 0; i < newMats.Length; i++)
                    {
                        newMats[i] = i < mats.Length ? mats[i] : null;
                    }
                    mats = newMats;
                    changed = true;
                }

                for (int i = 0; i < mats.Length; i++)
                {
                    Material mat = mats[i];

                    bool needsReplacement = mat == null ||
                                           mat.name.Contains("Default-Material") ||
                                           mat.name.Contains("Default Material");

                    if (needsReplacement)
                    {
                        // Find best replacement material based on renderer name
                        Material replacement = FindReplacementMaterial(renderer.name, i, materialLookup);
                        if (replacement != null)
                        {
                            mats[i] = replacement;
                            changed = true;
                            fixedSlots++;
                            Debug.Log($"[FIXED SLOT] {renderer.name}[{i}] -> {replacement.name}");
                        }
                        else
                        {
                            Debug.LogWarning($"[NO REPLACEMENT] {renderer.name}[{i}]");
                        }
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = mats;
                }
            }

            // Save prefab
            PrefabUtility.SaveAsPrefabAsset(prefabContents, PREFAB_PATH);
            Debug.Log($"Fixed {fixedSlots} material slots in prefab");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabContents);
        }
    }

    // ==================== VERIFY ====================

    [MenuItem("Tools/Character Setup/VERIFY (Fail if ANY Issues Remain)")]
    public static bool Verify()
    {
        Debug.Log("\n" + new string('=', 100));
        Debug.Log("=== VERIFICATION ===");
        Debug.Log(new string('=', 100));

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogError($"Cannot load prefab: {PREFAB_PATH}");
            return false;
        }

        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        int nullSlots = 0;
        int defaultMats = 0;
        int missingMainTex = 0;
        int badShaders = 0;

        foreach (var renderer in renderers)
        {
            int subMeshCount = GetSubMeshCount(renderer);

            for (int i = 0; i < Mathf.Max(renderer.sharedMaterials.Length, subMeshCount); i++)
            {
                if (i >= renderer.sharedMaterials.Length)
                {
                    nullSlots++;
                    Debug.LogError($"[VERIFY FAIL] {renderer.name}[{i}]: SLOT_MISSING");
                    continue;
                }

                Material mat = renderer.sharedMaterials[i];

                if (mat == null)
                {
                    nullSlots++;
                    Debug.LogError($"[VERIFY FAIL] {renderer.name}[{i}]: NULL_SLOT");
                    continue;
                }

                if (mat.name.Contains("Default-Material") || mat.name.Contains("Default Material"))
                {
                    defaultMats++;
                    Debug.LogError($"[VERIFY FAIL] {renderer.name}[{i}]: DEFAULT_MATERIAL ({mat.name})");
                }

                if (mat.shader == null || !mat.shader.isSupported ||
                    mat.shader.name == "Hidden/InternalErrorShader")
                {
                    badShaders++;
                    Debug.LogError($"[VERIFY FAIL] {renderer.name}[{i}]: BAD_SHADER ({mat.shader?.name ?? "null"})");
                }

                bool hasMainTex = (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null) ||
                                 (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null);

                if (!hasMainTex)
                {
                    missingMainTex++;
                    Debug.LogError($"[VERIFY FAIL] {renderer.name}[{i}]: MISSING_MAINTEX ({mat.name})");
                }
            }
        }

        // Check for broken PPtr in material files
        int brokenPPtr = CountBrokenPPtr();

        Debug.Log("\n--- VERIFICATION RESULTS ---\n");
        Debug.Log($"NULL slots: {nullSlots}");
        Debug.Log($"Default-Material slots: {defaultMats}");
        Debug.Log($"Missing _MainTex: {missingMainTex}");
        Debug.Log($"Bad shaders: {badShaders}");
        Debug.Log($"Broken PPtr materials: {brokenPPtr}");

        bool passed = nullSlots == 0 && defaultMats == 0 && missingMainTex == 0 && badShaders == 0 && brokenPPtr == 0;

        if (passed)
        {
            Debug.Log("\n*** VERIFICATION PASSED - ALL CLEAR ***");
        }
        else
        {
            Debug.LogError($"\n*** VERIFICATION FAILED - {nullSlots + defaultMats + missingMainTex + badShaders + brokenPPtr} issues remain ***");
        }

        return passed;
    }

    private static int CountBrokenPPtr()
    {
        string matFolder = Path.Combine(Application.dataPath, "Art/Characters/Edumeta_CharacterGirl_AAA/Materials");
        if (!Directory.Exists(matFolder)) return 0;

        int count = 0;
        foreach (string matPath in Directory.GetFiles(matFolder, "*.mat"))
        {
            string content = File.ReadAllText(matPath);
            if (content.Contains("--- !u!114 &") && content.Contains("MonoBehaviour:"))
            {
                count++;
            }
        }
        return count;
    }

    // ==================== HELPER METHODS ====================

    private static Dictionary<string, Texture2D> BuildTextureLookup()
    {
        var lookup = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

        string[] searchPaths = {
            CHARACTER_ROOT + "/Edumeta_CharacterGirl_AAA 1.fbm",
            CHARACTER_ROOT + "/Edumeta_CharacterGirl_AAA.fbm",
            CHARACTER_ROOT + "/Edumeta_CharacterGirl_AAA-nonhead.fbm",
            CHARACTER_ROOT + "/textures",
            CHARACTER_ROOT
        };

        foreach (string path in searchPaths)
        {
            if (!AssetDatabase.IsValidFolder(path)) continue;

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

    private static Texture2D FindTextureForMaterial(string materialName, Dictionary<string, Texture2D> lookup)
    {
        // Try exact match
        if (lookup.TryGetValue(materialName, out Texture2D exact))
            return exact;

        // Try without _Diffuse
        string baseName = materialName.Replace("_Diffuse", "");
        if (lookup.TryGetValue(baseName, out Texture2D noSuffix))
            return noSuffix;

        // Try with _Diffuse added
        if (lookup.TryGetValue(baseName + "_Diffuse", out Texture2D withSuffix))
            return withSuffix;

        // Fuzzy match
        foreach (var kvp in lookup)
        {
            if (kvp.Key.ToLower().Contains(baseName.ToLower()) &&
                kvp.Key.ToLower().Contains("diffuse"))
            {
                return kvp.Value;
            }
        }

        return null;
    }

    private static Material FindReplacementMaterial(string rendererName, int slotIndex, Dictionary<string, Material> lookup)
    {
        string nameLower = rendererName.ToLower();

        // Hair meshes
        if (nameLower.Contains("hair") && !nameLower.Contains("baby"))
        {
            if (lookup.TryGetValue("Hair_Transparency_Diffuse", out Material hairMat))
                return hairMat;
        }

        if (nameLower.Contains("babyhair") || (nameLower.Contains("hair") && nameLower.Contains("baby")))
        {
            if (lookup.TryGetValue("BabyHair_Transparency_Diffuse", out Material babyHairMat))
                return babyHairMat;
        }

        // Scalp
        if (nameLower.Contains("scalp"))
        {
            if (lookup.TryGetValue("Scalp_Transparency_Diffuse", out Material scalpMat))
                return scalpMat;
        }

        // Eyes
        if (nameLower.Contains("eye") && nameLower.Contains("_l"))
        {
            if (lookup.TryGetValue("Std_Eye_L_Diffuse", out Material eyeLMat))
                return eyeLMat;
        }
        if (nameLower.Contains("eye") && nameLower.Contains("_r"))
        {
            if (lookup.TryGetValue("Std_Eye_R_Diffuse", out Material eyeRMat))
                return eyeRMat;
        }

        // Cornea
        if (nameLower.Contains("cornea") && nameLower.Contains("_l"))
        {
            if (lookup.TryGetValue("Std_Cornea_L_Diffuse", out Material corneaLMat))
                return corneaLMat;
        }
        if (nameLower.Contains("cornea") && nameLower.Contains("_r"))
        {
            if (lookup.TryGetValue("Std_Cornea_R_Diffuse", out Material corneaRMat))
                return corneaRMat;
        }

        // Eyelash
        if (nameLower.Contains("eyelash"))
        {
            if (lookup.TryGetValue("Std_Eyelash_Diffuse", out Material eyelashMat))
                return eyelashMat;
        }

        // Head
        if (nameLower.Contains("head"))
        {
            if (lookup.TryGetValue("Std_Skin_Head_Diffuse", out Material headMat))
                return headMat;
        }

        // Body
        if (nameLower.Contains("body"))
        {
            if (lookup.TryGetValue("Std_Skin_Body_Diffuse", out Material bodyMat))
                return bodyMat;
        }

        // Teeth
        if (nameLower.Contains("teeth"))
        {
            if (slotIndex == 0 && lookup.TryGetValue("Std_Upper_Teeth_Diffuse", out Material upperTeethMat))
                return upperTeethMat;
            if (slotIndex == 1 && lookup.TryGetValue("Std_Lower_Teeth_Diffuse", out Material lowerTeethMat))
                return lowerTeethMat;
        }

        // Tongue
        if (nameLower.Contains("tongue"))
        {
            if (lookup.TryGetValue("Std_Tongue_Diffuse", out Material tongueMat))
                return tongueMat;
        }

        // Eye occlusion
        if (nameLower.Contains("occlusion"))
        {
            if (lookup.TryGetValue("Std_Eye_Occlusion_L_Diffuse", out Material occLMat))
                return occLMat;
        }

        // Tearline
        if (nameLower.Contains("tearline"))
        {
            if (lookup.TryGetValue("Std_Tearline_L_Diffuse", out Material tearMat))
                return tearMat;
        }

        return null;
    }

    private static void SetCutoutMode(Material mat, float cutoff)
    {
        mat.SetFloat("_Mode", 1);
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
        mat.SetFloat("_Mode", 2);
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

    private static int GetSubMeshCount(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            return smr.sharedMesh.subMeshCount;
        if (renderer is MeshRenderer mr)
        {
            var mf = renderer.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                return mf.sharedMesh.subMeshCount;
        }
        return 1;
    }

    private class RendererIssue
    {
        public string RendererPath;
        public int SlotIndex;
        public string MaterialName;
        public List<string> Issues;
    }
}
