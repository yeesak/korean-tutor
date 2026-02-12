using UnityEngine;
using UnityEditor;
using System.IO;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Disables EyeOcclusion layer in prefab to reveal the actual eye underneath.
    /// Frontend-only modification (prefab/scene).
    /// </summary>
    public class EyeOcclusionDisabler
    {
        private const string PREFAB_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";

        [MenuItem("Tools/Eye Doctor/Disable EyeOcclusion (Prefab)")]
        public static void DisableEyeOcclusionInPrefab()
        {
            Debug.Log("=== DISABLING EYE OCCLUSION IN PREFAB ===\n");

            if (!File.Exists(PREFAB_PATH))
            {
                Debug.LogError($"Prefab not found: {PREFAB_PATH}");
                return;
            }

            // Load prefab contents for editing
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            if (prefabContents == null)
            {
                Debug.LogError("Failed to load prefab contents!");
                return;
            }

            int disabledCount = 0;

            // Find all transforms that contain "EyeOcclusion" in name
            Transform[] allTransforms = prefabContents.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in allTransforms)
            {
                if (t.name.Contains("EyeOcclusion"))
                {
                    // Disable the GameObject
                    if (t.gameObject.activeSelf)
                    {
                        t.gameObject.SetActive(false);
                        Debug.Log($"Disabled: {t.name}");
                        disabledCount++;
                    }
                    else
                    {
                        Debug.Log($"Already disabled: {t.name}");
                    }

                    // Also disable any SkinnedMeshRenderer on it
                    var smr = t.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null && smr.enabled)
                    {
                        smr.enabled = false;
                        Debug.Log($"Disabled SkinnedMeshRenderer on: {t.name}");
                    }
                }
            }

            // Save prefab
            PrefabUtility.SaveAsPrefabAsset(prefabContents, PREFAB_PATH);
            PrefabUtility.UnloadPrefabContents(prefabContents);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"\nDisabled {disabledCount} EyeOcclusion object(s) in prefab.");
            Debug.Log("Prefab saved. Enter Play Mode to test.");
        }

        [MenuItem("Tools/Eye Doctor/Enable EyeOcclusion (Prefab)")]
        public static void EnableEyeOcclusionInPrefab()
        {
            Debug.Log("=== RE-ENABLING EYE OCCLUSION IN PREFAB ===\n");

            if (!File.Exists(PREFAB_PATH))
            {
                Debug.LogError($"Prefab not found: {PREFAB_PATH}");
                return;
            }

            GameObject prefabContents = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            if (prefabContents == null)
            {
                Debug.LogError("Failed to load prefab contents!");
                return;
            }

            int enabledCount = 0;

            Transform[] allTransforms = prefabContents.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in allTransforms)
            {
                if (t.name.Contains("EyeOcclusion"))
                {
                    if (!t.gameObject.activeSelf)
                    {
                        t.gameObject.SetActive(true);
                        Debug.Log($"Enabled: {t.name}");
                        enabledCount++;
                    }

                    var smr = t.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null && !smr.enabled)
                    {
                        smr.enabled = true;
                        Debug.Log($"Enabled SkinnedMeshRenderer on: {t.name}");
                    }
                }
            }

            PrefabUtility.SaveAsPrefabAsset(prefabContents, PREFAB_PATH);
            PrefabUtility.UnloadPrefabContents(prefabContents);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"\nEnabled {enabledCount} EyeOcclusion object(s) in prefab.");
        }

        [MenuItem("Tools/Eye Doctor/Disable EyeOcclusion (Scene Instance)")]
        public static void DisableEyeOcclusionInScene()
        {
            Debug.Log("=== DISABLING EYE OCCLUSION IN SCENE ===\n");

            // Find in scene
            GameObject avatar = GameObject.Find("Avatar");
            if (avatar == null)
            {
                Debug.LogError("Avatar not found in scene!");
                return;
            }

            Transform charModel = avatar.transform.Find("CharacterModel");
            if (charModel == null)
            {
                Debug.LogError("CharacterModel not found!");
                return;
            }

            int disabledCount = 0;

            Transform[] allTransforms = charModel.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in allTransforms)
            {
                if (t.name.Contains("EyeOcclusion"))
                {
                    if (t.gameObject.activeSelf)
                    {
                        t.gameObject.SetActive(false);
                        Debug.Log($"Disabled in scene: {t.name}");
                        disabledCount++;
                    }

                    var smr = t.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null && smr.enabled)
                    {
                        smr.enabled = false;
                    }
                }
            }

            Debug.Log($"\nDisabled {disabledCount} EyeOcclusion object(s) in scene.");
            Debug.Log("This is a runtime change - will reset when scene reloads.");
        }
    }
}
