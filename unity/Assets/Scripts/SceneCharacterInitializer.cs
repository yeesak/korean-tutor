using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ShadowingTutor
{
    /// <summary>
    /// Scene initializer that ensures the Edumeta character is set up when the scene loads.
    /// Uses RuntimeInitializeOnLoadMethod but also supports build-safe loading via RuntimeCharacterLoader.
    /// </summary>
    public static class SceneCharacterInitializer
    {
        private const string PREFAB_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
        private static bool _initialized = false;

        // This can be set by RuntimeCharacterLoader before initialization
        public static GameObject InjectedPrefab { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnSceneLoaded()
        {
            // Only initialize once per session
            if (_initialized) return;
            _initialized = true;

            // Find Avatar object
            GameObject avatar = GameObject.Find("Avatar");
            if (avatar == null)
            {
                Debug.Log("[SceneInit] No Avatar object found, skipping character setup");
                return;
            }

            // Reset Avatar scale (was 1,2,1 for capsule placeholder)
            avatar.transform.localScale = Vector3.one;

            // Disable placeholder renderers first
            DisablePlaceholders(avatar);

            // Check if character already exists
            Transform existingChar = avatar.transform.Find("CharacterModel");
            if (existingChar != null)
            {
                Debug.Log("[SceneInit] Character already set up, fixing transforms");
                FixCharacterTransform(existingChar);
                FixInstanceMaterials(existingChar.gameObject);
                ConfigureLipSync(existingChar.gameObject);
                return;
            }

            // Load and instantiate prefab
            GameObject prefab = LoadPrefab();
            if (prefab == null)
            {
                Debug.LogError("[SceneInit] Failed to load character prefab! In builds, attach RuntimeCharacterLoader to Avatar and assign the prefab.");
                return;
            }

            // Instantiate character
            GameObject character = Object.Instantiate(prefab, avatar.transform);
            character.name = "CharacterModel";

            // Set character transform for portrait framing
            // Character at origin, facing camera (rotated 180° around Y), normal scale
            FixCharacterTransform(character.transform);

            Debug.Log("[SceneInit] Edumeta character instantiated successfully");

            // Fix materials at runtime (safety net)
            FixInstanceMaterials(character);

            // Wire up lip sync
            ConfigureLipSync(character);
        }

        private static GameObject LoadPrefab()
        {
            // Priority 1: Injected prefab from RuntimeCharacterLoader
            if (InjectedPrefab != null) return InjectedPrefab;

#if UNITY_EDITOR
            // Priority 2: AssetDatabase (Editor only)
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            if (prefab != null) return prefab;
#endif
            // Priority 3: Resources fallback
            return Resources.Load<GameObject>("CharacterPrefab");
        }

        /// <summary>
        /// Runtime material fix - ensures materials use Standard shader.
        /// Works on instance materials to avoid modifying shared assets.
        /// </summary>
        public static void FixInstanceMaterials(GameObject character)
        {
            if (character == null) return;

            Shader standardShader = Shader.Find("Standard");
            if (standardShader == null)
            {
                Debug.LogWarning("[SceneInit] Cannot find Standard shader for runtime fix");
                return;
            }

            int fixedCount = 0;
            Renderer[] renderers = character.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                // Access .materials (instance) not .sharedMaterials (asset)
                Material[] mats = renderer.materials;
                bool changed = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    Material mat = mats[i];
                    if (mat == null) continue;

                    bool needsFix = mat.shader == null ||
                                   !mat.shader.isSupported ||
                                   mat.shader.name == "Hidden/InternalErrorShader" ||
                                   mat.shader.name.Contains("Universal") ||
                                   mat.shader.name.Contains("URP");

                    if (needsFix)
                    {
                        // Get textures before changing shader (check both URP and Standard properties)
                        Texture mainTex = null;
                        if (mat.HasProperty("_MainTex")) mainTex = mat.GetTexture("_MainTex");
                        if (mainTex == null && mat.HasProperty("_BaseMap")) mainTex = mat.GetTexture("_BaseMap");

                        Texture normalMap = null;
                        if (mat.HasProperty("_BumpMap")) normalMap = mat.GetTexture("_BumpMap");
                        if (normalMap == null && mat.HasProperty("_NormalMap")) normalMap = mat.GetTexture("_NormalMap");

                        Color color = Color.white;
                        if (mat.HasProperty("_Color")) color = mat.GetColor("_Color");
                        else if (mat.HasProperty("_BaseColor")) color = mat.GetColor("_BaseColor");

                        float smoothness = 0.5f;
                        if (mat.HasProperty("_Smoothness")) smoothness = mat.GetFloat("_Smoothness");
                        else if (mat.HasProperty("_Glossiness")) smoothness = mat.GetFloat("_Glossiness");

                        mat.shader = standardShader;

                        // Apply textures to Standard shader
                        if (mainTex != null) mat.SetTexture("_MainTex", mainTex);
                        if (normalMap != null) mat.SetTexture("_BumpMap", normalMap);
                        mat.SetColor("_Color", color);
                        mat.SetFloat("_Glossiness", smoothness);

                        // Set blend mode based on material name
                        string matName = mat.name.ToLower();
                        if (matName.Contains("cornea") || matName.Contains("tearline"))
                        {
                            // Fade mode for transparent eye parts
                            mat.SetFloat("_Mode", 2);
                            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                            mat.SetInt("_ZWrite", 0);
                            mat.EnableKeyword("_ALPHABLEND_ON");
                            mat.renderQueue = 3000;
                            // Ensure low alpha for transparency
                            color.a = 0.1f;
                            mat.SetColor("_Color", color);
                        }
                        else if (matName.Contains("transparency") || matName.Contains("hair") || matName.Contains("eyelash"))
                        {
                            // Cutout mode for hair/eyelashes
                            mat.SetFloat("_Mode", 1);
                            mat.SetFloat("_Cutoff", 0.5f);
                            mat.EnableKeyword("_ALPHATEST_ON");
                            mat.renderQueue = 2450;
                        }

                        changed = true;
                        fixedCount++;
                    }

                    // Always ensure cornea materials have proper transparency settings
                    // (even if shader didn't need fixing)
                    string matNameCheck = mat.name.ToLower();
                    if (matNameCheck.Contains("cornea"))
                    {
                        EnsureCorneaTransparency(mat);
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.materials = mats;
                }
            }

            if (fixedCount > 0)
            {
                Debug.Log($"[SceneInit] Fixed {fixedCount} instance materials at runtime");
            }
        }

        /// <summary>
        /// Ensures cornea materials are properly transparent so iris/pupil is visible.
        /// </summary>
        private static void EnsureCorneaTransparency(Material mat)
        {
            if (mat == null) return;

            // Set Fade mode
            mat.SetFloat("_Mode", 2);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;

            // Ensure low alpha (0.1 = 90% transparent)
            Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            if (color.a > 0.2f)
            {
                color.a = 0.1f;
                mat.SetColor("_Color", color);
            }

            // Set high glossiness for wet eye look
            mat.SetFloat("_Glossiness", 0.9f);
            mat.SetFloat("_Metallic", 0f);
        }

        private static void DisablePlaceholders(GameObject avatar)
        {
            int disabledCount = 0;

            // Disable MeshRenderer on Avatar (capsule)
            MeshRenderer mr = avatar.GetComponent<MeshRenderer>();
            if (mr != null && mr.enabled)
            {
                mr.enabled = false;
                disabledCount++;
            }

            // Disable MeshRenderer on Jaw child (if it's the placeholder, not a bone)
            Transform jaw = avatar.transform.Find("Jaw");
            if (jaw != null)
            {
                MeshRenderer jawMr = jaw.GetComponent<MeshRenderer>();
                if (jawMr != null && jawMr.enabled)
                {
                    jawMr.enabled = false;
                    disabledCount++;
                }
            }

            if (disabledCount > 0)
            {
                Debug.Log($"[SceneInit] Disabled {disabledCount} placeholder renderer(s)");
            }
        }

        private static void FixCharacterTransform(Transform character)
        {
            // Position character so feet are near Avatar origin
            // Slight Y offset to ground the character properly
            character.localPosition = new Vector3(0f, -0.05f, 0f);

            // Face camera (camera is at negative Z, so rotate 180° around Y)
            character.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Use normal scale (PortraitCameraFramer handles framing via bounds calculation)
            character.localScale = Vector3.one;
        }

        private static void ConfigureLipSync(GameObject character)
        {
            // Find LipSyncController
            LipSyncController lipSync = Object.FindObjectOfType<LipSyncController>();
            if (lipSync == null)
            {
                Debug.LogWarning("[SceneInit] No LipSyncController found");
                return;
            }

            // Find jaw bone in character
            string[] jawBoneNames = { "CC_Base_JawRoot", "JawRoot", "CC_Base_UpperJaw", "Jaw" };

            foreach (string boneName in jawBoneNames)
            {
                Transform jawBone = FindBoneRecursive(character.transform, boneName);
                if (jawBone != null)
                {
                    lipSync.SetJawBone(jawBone);
                    Debug.Log($"[SceneInit] Connected jaw bone: {jawBone.name}");
                    return;
                }
            }

            Debug.LogWarning("[SceneInit] No jaw bone found in character");
        }

        private static Transform FindBoneRecursive(Transform parent, string name)
        {
            string lowerName = name.ToLower();

            if (parent.name.ToLower().Contains(lowerName))
                return parent;

            foreach (Transform child in parent)
            {
                Transform found = FindBoneRecursive(child, name);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// Reset initialization flag (useful for scene reloads in editor).
        /// </summary>
        public static void ResetInitialization()
        {
            _initialized = false;
        }
    }
}
