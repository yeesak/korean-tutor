using UnityEngine;
using UnityEditor;
using System.IO;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Fixes the cornea materials to be properly transparent overlays.
    /// The cornea should be a thin transparent layer over the eyeball for specular highlights,
    /// NOT a copy of the eye texture.
    /// </summary>
    public class EyeCorneaFix
    {
        private const string MATERIALS_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Materials_Clean";

        [MenuItem("Tools/Eye Doctor/FIX: Make Cornea Transparent")]
        public static void FixCorneaMaterials()
        {
            Debug.Log("=== CORNEA TRANSPARENCY FIX ===\n");

            string[] corneaMatPaths = {
                $"{MATERIALS_PATH}/Std_Cornea_R_Diffuse.mat",
                $"{MATERIALS_PATH}/Std_Cornea_L_Diffuse.mat"
            };

            int fixedCount = 0;

            foreach (string matPath in corneaMatPaths)
            {
                if (!File.Exists(matPath))
                {
                    Debug.LogWarning($"Cornea material not found: {matPath}");
                    continue;
                }

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    Debug.LogWarning($"Failed to load material: {matPath}");
                    continue;
                }

                Debug.Log($"Fixing: {mat.name}");
                Debug.Log($"  Before: _MainTex = {(mat.GetTexture("_MainTex")?.name ?? "NULL")}");
                Debug.Log($"  Before: _Color.a = {mat.GetColor("_Color").a}");

                // Remove the main texture - cornea should be transparent overlay
                mat.SetTexture("_MainTex", null);

                // Set color to mostly transparent white with slight glossy tint
                Color corneaColor = new Color(1f, 1f, 1f, 0.05f); // 95% transparent
                mat.SetColor("_Color", corneaColor);

                // Ensure Fade mode settings are correct
                mat.SetFloat("_Mode", 2); // Fade mode
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;

                // High glossiness for wet eye look
                mat.SetFloat("_Glossiness", 0.9f);
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_SpecularHighlights", 1f);
                mat.SetFloat("_GlossyReflections", 1f);

                Debug.Log($"  After: _MainTex = NULL (transparent overlay)");
                Debug.Log($"  After: _Color.a = {corneaColor.a} (95% transparent)");
                Debug.Log($"  After: _Glossiness = 0.9 (wet eye look)");

                EditorUtility.SetDirty(mat);
                fixedCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"\n=== Fixed {fixedCount} cornea materials ===");
            Debug.Log("Cornea materials are now properly transparent overlays.");
            Debug.Log("The eye base texture (with iris/pupil) will now be visible beneath the cornea.");
        }

        [MenuItem("Tools/Eye Doctor/VERIFY: Check Eye Render Stack")]
        public static void VerifyEyeRenderStack()
        {
            Debug.Log("=== EYE RENDER STACK VERIFICATION ===\n");

            // Load the prefab
            string prefabPath = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
            {
                Debug.LogError("Cannot load prefab!");
                return;
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);

            Debug.Log("Eye Render Order (by queue):\n");
            var eyeRenderers = new System.Collections.Generic.List<(string name, int queue, string tex, float alpha)>();

            foreach (var renderer in renderers)
            {
                string nameLower = renderer.name.ToLowerInvariant();
                if (!nameLower.Contains("eye") && !nameLower.Contains("cornea"))
                    continue;

                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null) continue;

                    string texName = "NULL";
                    if (mat.HasProperty("_MainTex"))
                    {
                        var tex = mat.GetTexture("_MainTex");
                        texName = tex != null ? tex.name : "NULL";
                    }

                    float alpha = 1f;
                    if (mat.HasProperty("_Color"))
                    {
                        alpha = mat.GetColor("_Color").a;
                    }

                    eyeRenderers.Add((mat.name, mat.renderQueue, texName, alpha));
                }
            }

            // Sort by render queue
            eyeRenderers.Sort((a, b) => a.queue.CompareTo(b.queue));

            foreach (var (name, queue, tex, alpha) in eyeRenderers)
            {
                string alphaStr = alpha < 1f ? $" (alpha={alpha:F2})" : "";
                Debug.Log($"  Queue {queue}: {name} -> Tex: {tex}{alphaStr}");
            }

            Debug.Log("\nExpected order:");
            Debug.Log("  1. Eye base (queue 2000, opaque) - shows iris/pupil");
            Debug.Log("  2. Cornea (queue 3000, transparent) - glossy overlay");
            Debug.Log("  3. Occlusion (queue 3000, transparent) - shadow effect");
        }
    }
}
