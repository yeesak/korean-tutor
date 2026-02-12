using UnityEngine;
using System.Collections;

namespace ShadowingTutor
{
    /// <summary>
    /// Runtime verification of character material textures.
    /// Logs detailed status to help diagnose rendering issues.
    /// </summary>
    public class CharacterTextureVerifier : MonoBehaviour
    {
        private static bool _hasRun = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnSceneLoaded()
        {
            if (_hasRun) return;
            _hasRun = true;

            var go = new GameObject("_CharacterTextureVerifier");
            go.hideFlags = HideFlags.HideAndDontSave;
            var verifier = go.AddComponent<CharacterTextureVerifier>();
            verifier.StartCoroutine(verifier.WaitAndVerify());
        }

        private IEnumerator WaitAndVerify()
        {
            // Wait for character to spawn
            float timeout = 3f;
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

            if (character == null)
            {
                Debug.LogWarning("[Verify] CharacterModel not found within timeout");
                Destroy(gameObject);
                yield break;
            }

            // Wait one more frame for materials to initialize
            yield return null;

            VerifyCharacter(character.gameObject);
            Destroy(gameObject);
        }

        private void VerifyCharacter(GameObject character)
        {
            Debug.Log("=== RUNTIME TEXTURE VERIFICATION ===");

            var renderers = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Debug.Log($"[Verify] Character has {renderers.Length} SkinnedMeshRenderers");

            int okCount = 0;
            int missingCount = 0;

            foreach (var renderer in renderers)
            {
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null)
                    {
                        Debug.LogError($"[Verify] NULL material on {renderer.name}");
                        missingCount++;
                        continue;
                    }

                    string shaderName = mat.shader != null ? mat.shader.name : "NULL";
                    Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;

                    string matNameLower = mat.name.ToLower();
                    bool isHairRelated = matNameLower.Contains("hair") ||
                                        matNameLower.Contains("scalp") ||
                                        matNameLower.Contains("transparency");

                    if (mainTex == null)
                    {
                        string severity = isHairRelated ? "ERROR" : "WARN";
                        Debug.LogWarning($"[Verify] {severity}: {mat.name} | Shader: {shaderName} | MainTex: MISSING");
                        missingCount++;
                    }
                    else
                    {
                        if (isHairRelated)
                        {
                            // Log hair materials specifically for visibility
                            Debug.Log($"[Verify] HAIR OK: {mat.name} | Shader: {shaderName} | Tex: {mainTex.name}");
                        }
                        okCount++;
                    }
                }
            }

            Debug.Log($"=== VERIFICATION SUMMARY ===");
            Debug.Log($"[Verify] OK: {okCount} materials with textures");

            if (missingCount > 0)
            {
                Debug.LogError($"[Verify] MISSING: {missingCount} materials without _MainTex");
                Debug.LogError("[Verify] >> Run: Tools > Character Setup > REPAIR MATERIALS (Complete Fix)");
            }
            else
            {
                Debug.Log("[Verify] All materials have textures assigned!");
            }
        }
    }
}
