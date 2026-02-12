using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Simple eye diagnostic tool - run from Unity menu while playing.
    /// </summary>
    public class EyeDiagnosticTool
    {
        [MenuItem("Tools/Eye Doctor/Check Eye Materials NOW")]
        public static void CheckEyeMaterials()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== EYE MATERIAL DIAGNOSTIC ===");
            sb.AppendLine($"Time: {System.DateTime.Now}");
            sb.AppendLine($"Play Mode: {Application.isPlaying}");
            sb.AppendLine();

            // Find character
            GameObject character = null;
            string source = "";

            if (Application.isPlaying)
            {
                var avatar = GameObject.Find("Avatar");
                if (avatar != null)
                {
                    var charModel = avatar.transform.Find("CharacterModel");
                    if (charModel != null)
                    {
                        character = charModel.gameObject;
                        source = "Runtime instance (Avatar/CharacterModel)";
                    }
                }
            }

            if (character == null)
            {
                // Load prefab
                character = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab");
                source = "Prefab asset";
            }

            if (character == null)
            {
                Debug.LogError("Cannot find character!");
                return;
            }

            sb.AppendLine($"Source: {source}");
            sb.AppendLine();

            // Find eye renderers
            var renderers = character.GetComponentsInChildren<Renderer>(true);
            int eyeCount = 0;

            foreach (var renderer in renderers)
            {
                string nameLower = renderer.name.ToLowerInvariant();
                if (!nameLower.Contains("eye") && !nameLower.Contains("cornea") &&
                    !nameLower.Contains("occlusion") && !nameLower.Contains("tearline"))
                    continue;

                eyeCount++;
                sb.AppendLine($"--- {renderer.name} ---");

                Material[] mats = Application.isPlaying ? renderer.materials : renderer.sharedMaterials;

                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null)
                    {
                        sb.AppendLine($"  [{i}] NULL MATERIAL!");
                        continue;
                    }

                    sb.AppendLine($"  [{i}] {mat.name}");
                    sb.AppendLine($"      Shader: {(mat.shader != null ? mat.shader.name : "NULL")}");
                    sb.AppendLine($"      IsInstance: {mat.name.Contains("(Instance)")}");

                    string matPath = AssetDatabase.GetAssetPath(mat);
                    sb.AppendLine($"      AssetPath: {(string.IsNullOrEmpty(matPath) ? "(none - runtime)" : matPath)}");

                    // Render mode
                    if (mat.HasProperty("_Mode"))
                    {
                        float mode = mat.GetFloat("_Mode");
                        string modeStr = mode switch { 0 => "Opaque", 1 => "Cutout", 2 => "Fade", 3 => "Transparent", _ => $"Unknown({mode})" };
                        sb.AppendLine($"      Mode: {modeStr}");
                    }

                    sb.AppendLine($"      RenderQueue: {mat.renderQueue}");

                    // Check _MainTex
                    if (mat.HasProperty("_MainTex"))
                    {
                        var tex = mat.GetTexture("_MainTex") as Texture2D;
                        if (tex != null)
                        {
                            string texPath = AssetDatabase.GetAssetPath(tex);
                            sb.AppendLine($"      _MainTex: {tex.name} ({tex.width}x{tex.height})");
                            sb.AppendLine($"      TexPath: {texPath}");
                        }
                        else
                        {
                            sb.AppendLine($"      _MainTex: *** NULL - THIS IS THE PROBLEM! ***");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"      _MainTex: (no property)");
                    }

                    // Check _BaseMap (URP)
                    if (mat.HasProperty("_BaseMap"))
                    {
                        var tex = mat.GetTexture("_BaseMap") as Texture2D;
                        if (tex != null)
                        {
                            sb.AppendLine($"      _BaseMap: {tex.name}");
                        }
                    }

                    // Keywords
                    if (mat.shaderKeywords.Length > 0)
                    {
                        sb.AppendLine($"      Keywords: {string.Join(", ", mat.shaderKeywords)}");
                    }

                    sb.AppendLine();
                }
            }

            sb.AppendLine($"Total eye-related renderers: {eyeCount}");

            // Save and log
            string output = sb.ToString();
            Debug.Log(output);

            string dir = "Assets/Temp";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText($"{dir}/eye_diagnostic.txt", output);
            AssetDatabase.Refresh();

            Debug.Log($"Report saved to: {dir}/eye_diagnostic.txt");
        }

        [MenuItem("Tools/Eye Doctor/Force Refresh Prefab Materials")]
        public static void ForceRefreshPrefab()
        {
            string prefabPath = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError("Cannot load prefab!");
                return;
            }

            Debug.Log("Opening prefab for editing...");
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);

            int refreshed = 0;
            var renderers = contents.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                // Force Unity to re-resolve material references
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null)
                    {
                        // Touch the material to ensure Unity resolves the reference
                        string path = AssetDatabase.GetAssetPath(mats[i]);
                        if (!string.IsNullOrEmpty(path))
                        {
                            var freshMat = AssetDatabase.LoadAssetAtPath<Material>(path);
                            if (freshMat != null)
                            {
                                mats[i] = freshMat;
                                refreshed++;
                            }
                        }
                    }
                }
                renderer.sharedMaterials = mats;
            }

            PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Refreshed {refreshed} material references in prefab");
        }

        [MenuItem("Tools/Eye Doctor/Reimport All Eye Textures")]
        public static void ReimportEyeTextures()
        {
            string[] paths = {
                "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Edumeta_CharacterGirl_AAA 1.fbm/Std_Eye_R_Diffuse.png",
                "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Edumeta_CharacterGirl_AAA 1.fbm/Std_Eye_L_Diffuse.png",
                "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Edumeta_CharacterGirl_AAA.fbm/Std_Eye_R_Diffuse.png",
                "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Edumeta_CharacterGirl_AAA.fbm/Std_Eye_L_Diffuse.png",
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null)
                    {
                        // Ensure correct settings
                        importer.textureType = TextureImporterType.Default;
                        importer.sRGBTexture = true;
                        importer.alphaIsTransparency = false;
                        importer.SaveAndReimport();
                        Debug.Log($"Reimported: {path}");
                    }
                }
            }

            AssetDatabase.Refresh();
            Debug.Log("Eye texture reimport complete");
        }
    }
}
