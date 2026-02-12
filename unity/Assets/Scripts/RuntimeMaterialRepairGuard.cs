using UnityEngine;
using System.Collections.Generic;

namespace ShadowingTutor
{
    /// <summary>
    /// Runtime safety guard that ensures character materials are properly assigned.
    /// Attaches to CharacterModel and verifies/fixes materials on Awake.
    /// This is a final guard - should not be needed if Editor repair was successful.
    /// </summary>
    public class RuntimeMaterialRepairGuard : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Log all material checks (verbose mode)")]
        [SerializeField] private bool _verboseLogging = false;

        [Tooltip("Attempt to fix Default-Material references")]
        [SerializeField] private bool _fixDefaultMaterials = true;

        private static bool _hasRun = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoAttach()
        {
            if (_hasRun) return;
            _hasRun = true;

            // Wait for character to spawn then attach guard
            var go = new GameObject("_RuntimeMaterialGuard");
            go.hideFlags = HideFlags.HideAndDontSave;
            var helper = go.AddComponent<RuntimeGuardHelper>();
        }

        private class RuntimeGuardHelper : MonoBehaviour
        {
            private void Start()
            {
                StartCoroutine(WaitAndAttach());
            }

            private System.Collections.IEnumerator WaitAndAttach()
            {
                float timeout = 5f;
                float elapsed = 0f;
                Transform character = null;

                while (elapsed < timeout)
                {
                    GameObject avatar = GameObject.Find("Avatar");
                    if (avatar != null)
                    {
                        character = avatar.transform.Find("CharacterModel");
                        if (character != null) break;
                    }
                    elapsed += 0.1f;
                    yield return new WaitForSeconds(0.1f);
                }

                if (character != null)
                {
                    // Add guard if not present
                    if (character.GetComponent<RuntimeMaterialRepairGuard>() == null)
                    {
                        var guard = character.gameObject.AddComponent<RuntimeMaterialRepairGuard>();
                        guard.VerifyAndFixMaterials();
                    }
                }

                Destroy(gameObject);
            }
        }

        private void Awake()
        {
            VerifyAndFixMaterials();
        }

        public void VerifyAndFixMaterials()
        {
            if (_verboseLogging)
                Debug.Log("[RuntimeGuard] Starting material verification...");

            var renderers = GetComponentsInChildren<Renderer>(true);
            int issuesFound = 0;
            int issuesFixed = 0;

            foreach (var renderer in renderers)
            {
                Material[] mats = renderer.materials; // Get instance materials
                bool changed = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    Material mat = mats[i];

                    if (mat == null)
                    {
                        Debug.LogWarning($"[RuntimeGuard] NULL material on {renderer.name} slot {i}");
                        issuesFound++;
                        continue;
                    }

                    bool isDefaultMaterial = mat.name.Contains("Default-Material") ||
                                            mat.name.Contains("Default Material");
                    bool hasMissingMainTex = mat.HasProperty("_MainTex") &&
                                            mat.GetTexture("_MainTex") == null;
                    bool isCornea = mat.name.ToLower().Contains("cornea");

                    // Cornea materials are special - they should be transparent overlays
                    // Missing _MainTex is OK for cornea as long as it's properly transparent
                    if (isCornea)
                    {
                        EnsureCorneaTransparency(mat);
                        changed = true;
                        issuesFixed++;
                        if (_verboseLogging)
                            Debug.Log($"[RuntimeGuard] Ensured cornea transparency: {mat.name}");
                    }
                    else if (isDefaultMaterial || hasMissingMainTex)
                    {
                        issuesFound++;

                        if (_verboseLogging)
                        {
                            string issue = isDefaultMaterial ? "DEFAULT-MATERIAL" : "MISSING _MainTex";
                            Debug.LogWarning($"[RuntimeGuard] {issue}: {renderer.name} -> {mat.name}");
                        }

                        // Attempt runtime fix using Standard shader
                        if (_fixDefaultMaterials && hasMissingMainTex)
                        {
                            // Try to find texture from _BaseMap (URP property)
                            if (mat.HasProperty("_BaseMap"))
                            {
                                Texture tex = mat.GetTexture("_BaseMap");
                                if (tex != null)
                                {
                                    mat.SetTexture("_MainTex", tex);
                                    issuesFixed++;
                                    if (_verboseLogging)
                                        Debug.Log($"[RuntimeGuard] Fixed {mat.name}: copied _BaseMap to _MainTex");
                                }
                            }
                        }

                        changed = true;
                    }

                    // Ensure shader is valid (not pink/error)
                    if (mat.shader == null || !mat.shader.isSupported ||
                        mat.shader.name == "Hidden/InternalErrorShader")
                    {
                        Shader standardShader = Shader.Find("Standard");
                        if (standardShader != null)
                        {
                            mat.shader = standardShader;
                            issuesFixed++;
                            if (_verboseLogging)
                                Debug.Log($"[RuntimeGuard] Fixed shader for {mat.name} -> Standard");
                        }
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.materials = mats;
                }
            }

            if (issuesFound > 0)
            {
                Debug.Log($"[RuntimeGuard] Found {issuesFound} material issues, fixed {issuesFixed}");
            }
            else if (_verboseLogging)
            {
                Debug.Log("[RuntimeGuard] All materials verified OK");
            }
        }

        /// <summary>
        /// Force re-verify materials (call from external script if needed).
        /// </summary>
        public void Revalidate()
        {
            VerifyAndFixMaterials();
        }

        /// <summary>
        /// Ensures cornea materials are properly transparent so iris/pupil is visible.
        /// Cornea should be a thin transparent layer for specular highlights, not an opaque overlay.
        /// </summary>
        private void EnsureCorneaTransparency(Material mat)
        {
            if (mat == null) return;

            // Ensure Standard shader
            Shader standardShader = Shader.Find("Standard");
            if (standardShader != null && (mat.shader == null || mat.shader.name != "Standard"))
            {
                mat.shader = standardShader;
            }

            // Set Fade mode for transparency
            mat.SetFloat("_Mode", 2);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;

            // Ensure low alpha (0.1 = 90% transparent) so iris/pupil shows through
            Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            if (color.a > 0.2f)
            {
                color.a = 0.1f;
                mat.SetColor("_Color", color);
            }

            // High glossiness for wet eye look
            mat.SetFloat("_Glossiness", 0.9f);
            mat.SetFloat("_Metallic", 0f);
        }
    }
}
