using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Runtime cornea fix tool - fixes the active scene character's cornea materials.
    /// Creates a 1x1 transparent texture and assigns it to prevent white overlay.
    /// </summary>
    public class EyeCorneaRuntimeFix
    {
        private const string GENERATED_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Generated/Eyes";
        private const string TRANSPARENT_TEX_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Generated/Eyes/Transparent_1x1.png";
        private const string REPORT_PATH = "Assets/Temp/eye_cornea_fix_report.txt";

        [MenuItem("Tools/Eye Doctor/1) Fix Cornea MainTex + Transparency")]
        public static void FixCorneaMainTexAndTransparency()
        {
            Debug.Log("=== EYE CORNEA RUNTIME FIX ===\n");

            var sb = new StringBuilder();
            sb.AppendLine("=== EYE CORNEA FIX REPORT ===");
            sb.AppendLine($"Timestamp: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Play Mode: {Application.isPlaying}");
            sb.AppendLine();

            // Step 1: Ensure transparent texture exists
            Texture2D transparentTex = EnsureTransparentTexture();
            if (transparentTex == null)
            {
                Debug.LogError("Failed to create/load transparent texture!");
                return;
            }
            sb.AppendLine($"Transparent texture: {TRANSPARENT_TEX_PATH}");
            sb.AppendLine();

            // Step 2: Find the active runtime character
            GameObject character = FindActiveCharacter();
            if (character == null)
            {
                Debug.LogError("Cannot find character in scene! Looking for Avatar/CharacterModel");
                sb.AppendLine("ERROR: Cannot find character in scene!");
                SaveReport(sb.ToString());
                return;
            }
            sb.AppendLine($"Character found: {GetHierarchyPath(character.transform)}");
            sb.AppendLine();

            // Step 3: Find and fix eye-related renderers
            var renderers = character.GetComponentsInChildren<Renderer>(true);
            int fixedCount = 0;
            int totalCorneaMats = 0;

            sb.AppendLine("--- EYE RENDERERS ---");

            foreach (var renderer in renderers)
            {
                string nameLower = renderer.name.ToLowerInvariant();
                if (!nameLower.Contains("eye") && !nameLower.Contains("cornea") &&
                    !nameLower.Contains("tearline") && !nameLower.Contains("occlusion"))
                    continue;

                sb.AppendLine($"\nRenderer: {renderer.name}");
                sb.AppendLine($"  Path: {GetHierarchyPath(renderer.transform)}");

                // Get materials (instance or shared based on play mode)
                Material[] mats = Application.isPlaying ? renderer.materials : renderer.sharedMaterials;
                bool needsReassign = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    Material mat = mats[i];
                    if (mat == null)
                    {
                        sb.AppendLine($"  [{i}] NULL MATERIAL");
                        continue;
                    }

                    string matNameLower = mat.name.ToLowerInvariant();
                    bool isCornea = matNameLower.Contains("cornea");
                    bool isInstance = mat.name.Contains("(Instance)");

                    sb.AppendLine($"  [{i}] {mat.name}");
                    sb.AppendLine($"      IsInstance: {isInstance}");
                    sb.AppendLine($"      Shader: {(mat.shader != null ? mat.shader.name : "NULL")}");

                    // Check _MainTex
                    Texture mainTex = null;
                    if (mat.HasProperty("_MainTex"))
                    {
                        mainTex = mat.GetTexture("_MainTex");
                    }
                    string mainTexStatus = mainTex != null ? mainTex.name : "MISSING";
                    sb.AppendLine($"      _MainTex: {mainTexStatus}");

                    // Get current alpha
                    float currentAlpha = 1f;
                    if (mat.HasProperty("_Color"))
                    {
                        currentAlpha = mat.GetColor("_Color").a;
                    }
                    sb.AppendLine($"      _Color.a: {currentAlpha}");
                    sb.AppendLine($"      RenderQueue: {mat.renderQueue}");

                    // Fix cornea materials
                    if (isCornea)
                    {
                        totalCorneaMats++;
                        bool wasFixed = false;
                        var fixDetails = new List<string>();

                        // Ensure Standard shader
                        Shader standardShader = Shader.Find("Standard");
                        if (standardShader != null && (mat.shader == null || mat.shader.name != "Standard"))
                        {
                            mat.shader = standardShader;
                            fixDetails.Add("Set shader to Standard");
                            wasFixed = true;
                        }

                        // Ensure Fade mode (_Mode=2)
                        if (mat.HasProperty("_Mode"))
                        {
                            float mode = mat.GetFloat("_Mode");
                            if (mode != 2)
                            {
                                mat.SetFloat("_Mode", 2);
                                fixDetails.Add($"Set _Mode from {mode} to 2 (Fade)");
                                wasFixed = true;
                            }
                        }

                        // Set blend mode for Fade
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_ZWrite", 0);
                        mat.EnableKeyword("_ALPHABLEND_ON");
                        mat.DisableKeyword("_ALPHATEST_ON");
                        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

                        // Fix render queue
                        if (mat.renderQueue != 3000)
                        {
                            mat.renderQueue = 3000;
                            fixDetails.Add($"Set renderQueue to 3000");
                            wasFixed = true;
                        }

                        // Fix alpha (set to 0.1 for transparency)
                        if (currentAlpha > 0.2f)
                        {
                            Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                            color.a = 0.1f;
                            mat.SetColor("_Color", color);
                            fixDetails.Add($"Set _Color.a from {currentAlpha} to 0.1");
                            wasFixed = true;
                        }

                        // Fix missing _MainTex - assign transparent texture
                        if (mainTex == null)
                        {
                            mat.SetTexture("_MainTex", transparentTex);
                            fixDetails.Add($"Assigned transparent texture to _MainTex");
                            wasFixed = true;
                        }

                        // Set specular properties for wet eye look
                        mat.SetFloat("_Glossiness", 0.9f);
                        mat.SetFloat("_Metallic", 0f);
                        mat.SetFloat("_SpecularHighlights", 1f);
                        mat.SetFloat("_GlossyReflections", 1f);

                        if (wasFixed)
                        {
                            fixedCount++;
                            needsReassign = true;
                            sb.AppendLine($"      FIXED: {string.Join(", ", fixDetails)}");
                        }
                        else
                        {
                            sb.AppendLine($"      Status: OK (no fix needed)");
                        }
                    }
                }

                // Reassign materials if we modified them
                if (needsReassign && Application.isPlaying)
                {
                    renderer.materials = mats;
                }
            }

            sb.AppendLine();
            sb.AppendLine("=== SUMMARY ===");
            sb.AppendLine($"Total cornea materials found: {totalCorneaMats}");
            sb.AppendLine($"Materials fixed: {fixedCount}");

            // Save report
            SaveReport(sb.ToString());

            Debug.Log(sb.ToString());
            Debug.Log($"\nReport saved to: {REPORT_PATH}");

            if (fixedCount > 0)
            {
                Debug.Log($"\n*** Fixed {fixedCount} cornea material(s). Eyes should now show iris/pupil. ***");
            }
        }

        private static Texture2D EnsureTransparentTexture()
        {
            // Check if texture already exists
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TRANSPARENT_TEX_PATH);
            if (existing != null)
            {
                Debug.Log($"Using existing transparent texture: {TRANSPARENT_TEX_PATH}");
                return existing;
            }

            // Create directory if needed
            if (!Directory.Exists(GENERATED_PATH))
            {
                Directory.CreateDirectory(GENERATED_PATH);
                Debug.Log($"Created directory: {GENERATED_PATH}");
            }

            // Create 1x1 transparent texture
            Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0f)); // Fully transparent white
            tex.Apply();

            // Encode to PNG and save
            byte[] pngData = tex.EncodeToPNG();
            File.WriteAllBytes(TRANSPARENT_TEX_PATH, pngData);
            Object.DestroyImmediate(tex);

            // Import the texture
            AssetDatabase.Refresh();

            // Configure import settings
            TextureImporter importer = AssetImporter.GetAtPath(TRANSPARENT_TEX_PATH) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            Debug.Log($"Created transparent texture: {TRANSPARENT_TEX_PATH}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TRANSPARENT_TEX_PATH);
        }

        private static GameObject FindActiveCharacter()
        {
            // Try to find in active scene
            GameObject avatar = GameObject.Find("Avatar");
            if (avatar != null)
            {
                Transform charModel = avatar.transform.Find("CharacterModel");
                if (charModel != null)
                {
                    return charModel.gameObject;
                }
            }

            // Fallback: search for any object with CC_Base_Eye child
            var allRenderers = Object.FindObjectsOfType<Renderer>(true);
            foreach (var renderer in allRenderers)
            {
                if (renderer.name.Contains("CC_Base_Eye"))
                {
                    // Return the root character (go up to find it)
                    Transform current = renderer.transform;
                    while (current.parent != null)
                    {
                        if (current.name == "CharacterModel" || current.name.Contains("Edumeta"))
                        {
                            return current.gameObject;
                        }
                        current = current.parent;
                    }
                    return renderer.transform.root.gameObject;
                }
            }

            return null;
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

        private static void SaveReport(string content)
        {
            string dir = Path.GetDirectoryName(REPORT_PATH);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(REPORT_PATH, content);
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Eye Doctor/2) Verify Cornea Fix (Current State)")]
        public static void VerifyCorneaFix()
        {
            Debug.Log("=== CORNEA FIX VERIFICATION ===\n");

            GameObject character = FindActiveCharacter();
            if (character == null)
            {
                Debug.LogError("Cannot find character!");
                return;
            }

            Debug.Log($"Character: {GetHierarchyPath(character.transform)}");
            Debug.Log($"Play Mode: {Application.isPlaying}\n");

            var renderers = character.GetComponentsInChildren<Renderer>(true);
            int issues = 0;

            foreach (var renderer in renderers)
            {
                string nameLower = renderer.name.ToLowerInvariant();
                if (!nameLower.Contains("eye") && !nameLower.Contains("cornea"))
                    continue;

                Material[] mats = Application.isPlaying ? renderer.materials : renderer.sharedMaterials;

                foreach (var mat in mats)
                {
                    if (mat == null) continue;

                    string matNameLower = mat.name.ToLowerInvariant();
                    if (!matNameLower.Contains("cornea")) continue;

                    bool isInstance = mat.name.Contains("(Instance)");
                    Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                    float alpha = mat.HasProperty("_Color") ? mat.GetColor("_Color").a : 1f;

                    string status = "OK";
                    var problems = new List<string>();

                    if (mainTex == null)
                    {
                        problems.Add("_MainTex MISSING");
                    }
                    if (alpha > 0.2f)
                    {
                        problems.Add($"alpha too high ({alpha})");
                    }
                    if (mat.renderQueue != 3000)
                    {
                        problems.Add($"wrong renderQueue ({mat.renderQueue})");
                    }

                    if (problems.Count > 0)
                    {
                        status = "PROBLEM: " + string.Join(", ", problems);
                        issues++;
                    }

                    Debug.Log($"{mat.name} (Instance={isInstance}): {status}");
                    Debug.Log($"  _MainTex: {(mainTex != null ? mainTex.name : "NULL")}");
                    Debug.Log($"  _Color.a: {alpha}");
                    Debug.Log($"  RenderQueue: {mat.renderQueue}\n");
                }
            }

            if (issues == 0)
            {
                Debug.Log("*** ALL CORNEA MATERIALS VERIFIED OK ***");
            }
            else
            {
                Debug.LogWarning($"*** {issues} ISSUE(S) FOUND - Run 'Fix Cornea MainTex + Transparency' ***");
            }
        }
    }
}
