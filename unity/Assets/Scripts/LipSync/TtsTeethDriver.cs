using System;
using System.Linq;
using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Drives teeth/jaw movement synced to TTS mouth open value.
    /// GUARANTEED to produce visible motion via transform fallback even if no blendshapes exist.
    /// Called from TtsLipSyncRuntime every LateUpdate.
    /// </summary>
    public class TtsTeethDriver : MonoBehaviour
    {
        private static TtsTeethDriver _instance;
        public static TtsTeethDriver Instance => _instance;

        [Header("References")]
        [Tooltip("CharacterModel root")]
        public Transform characterRoot;

        [Tooltip("CC_Base_Teeth SkinnedMeshRenderer (assign in Inspector or auto-finds)")]
        public SkinnedMeshRenderer teethRenderer;

        [Tooltip("Jaw OR lower-teeth transform to move (assign in Inspector or auto-finds)")]
        public Transform jawOrLowerTeethTransform;

        [Header("Runtime")]
        public bool verbose = true;
        public float smooth = 20f;

        [Header("Fallback Transform Motion (GUARANTEED Visible)")]
        [Tooltip("Local position offset when mouth fully open")]
        public Vector3 jawLocalPosOpen = new Vector3(0f, -0.0028f, 0f);

        [Tooltip("Local rotation offset (degrees) when mouth fully open")]
        public Vector3 jawLocalRotOpen = new Vector3(7.5f, 0f, 0f);

        [Header("Blendshape Search Keywords")]
        public string[] openKeywords = new[] { "jaw", "open", "mouth", "teeth" };

        int _openBlendshape = -1;
        Vector3 _jawPosBase;
        Quaternion _jawRotBase;
        bool _hasJawBase;

        float _openSmoothed;
        float _logT;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"[TTS-Teeth] Duplicate instance, destroying {gameObject.name}");
                Destroy(this);
                return;
            }
            _instance = this;

            if (characterRoot == null)
            {
                characterRoot = FindCharacterRoot();
            }

            // Auto-find teeth renderer if not set
            if (teethRenderer == null && characterRoot != null)
            {
                teethRenderer = characterRoot
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .FirstOrDefault(r => r != null &&
                        ((r.name ?? "").ToLowerInvariant().Contains("teeth") ||
                         (r.sharedMesh != null && (r.sharedMesh.name ?? "").ToLowerInvariant().Contains("teeth"))));
            }

            // Auto-find jaw if not set
            if (jawOrLowerTeethTransform == null && characterRoot != null)
            {
                // First try to find explicit jaw bone
                jawOrLowerTeethTransform = characterRoot
                    .GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t != null &&
                        (t.name ?? "").ToLowerInvariant().Contains("jaw"));

                // Fallback to CC_Base_Teeth transform itself (GUARANTEED visible if we move it)
                if (jawOrLowerTeethTransform == null && teethRenderer != null)
                {
                    jawOrLowerTeethTransform = teethRenderer.transform;
                    if (verbose)
                    {
                        Debug.Log($"[TTS-Teeth] No jaw bone found. Using teeth renderer transform as fallback: {teethRenderer.name}");
                    }
                }
            }

            // Cache base transform
            if (jawOrLowerTeethTransform != null)
            {
                _jawPosBase = jawOrLowerTeethTransform.localPosition;
                _jawRotBase = jawOrLowerTeethTransform.localRotation;
                _hasJawBase = true;
            }

            // Find a blendshape on teeth mesh
            _openBlendshape = FindOpenBlendshape(teethRenderer);

            // Ensure teeth renderer updates even when offscreen
            if (teethRenderer != null)
            {
                teethRenderer.updateWhenOffscreen = true;
            }

            if (verbose)
            {
                int blendCount = (teethRenderer != null && teethRenderer.sharedMesh != null)
                    ? teethRenderer.sharedMesh.blendShapeCount : -1;
                string meshName = (teethRenderer != null && teethRenderer.sharedMesh != null)
                    ? teethRenderer.sharedMesh.name : "NULL";

                Debug.Log($"[TTS-Teeth] Awake " +
                          $"teethRenderer={(teethRenderer != null ? teethRenderer.name : "NULL")} " +
                          $"mesh={meshName} " +
                          $"blendCount={blendCount} " +
                          $"openBlendshapeIdx={_openBlendshape} " +
                          $"jawNode={(jawOrLowerTeethTransform != null ? jawOrLowerTeethTransform.name : "NULL")} " +
                          $"hasJawBase={_hasJawBase}");

                // List all blendshapes on teeth mesh for debugging
                if (teethRenderer != null && teethRenderer.sharedMesh != null && blendCount > 0)
                {
                    for (int i = 0; i < blendCount; i++)
                    {
                        string bsName = teethRenderer.sharedMesh.GetBlendShapeName(i);
                        Debug.Log($"[TTS-Teeth] Blendshape[{i}] = {bsName}");
                    }
                }
            }
        }

        Transform FindCharacterRoot()
        {
            string[] paths = { "Avatar/CharacterModel", "CharacterModel", "Avatar" };
            foreach (string path in paths)
            {
                GameObject found = GameObject.Find(path);
                if (found != null) return found.transform;
            }

            var renderers = FindObjectsOfType<SkinnedMeshRenderer>();
            foreach (var smr in renderers)
            {
                if (smr.name == "CC_Base_Body")
                {
                    Transform t = smr.transform;
                    while (t.parent != null)
                    {
                        if (t.name == "CharacterModel" || t.name == "Avatar")
                            return t;
                        t = t.parent;
                    }
                    return smr.transform.root;
                }
            }
            return null;
        }

        int FindOpenBlendshape(SkinnedMeshRenderer r)
        {
            if (r == null || r.sharedMesh == null) return -1;

            var mesh = r.sharedMesh;
            int count = mesh.blendShapeCount;
            if (count <= 0) return -1;

            int bestIdx = -1;
            int bestScore = -1;

            for (int i = 0; i < count; i++)
            {
                string name = (mesh.GetBlendShapeName(i) ?? "").ToLowerInvariant();
                int score = 0;

                foreach (var kw in openKeywords)
                {
                    if (name.Contains(kw.ToLowerInvariant()))
                        score += 1;
                }

                // Prefer explicit "open" keyword
                if (name.Contains("open")) score += 2;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            return (bestScore >= 2) ? bestIdx : -1;
        }

        /// <summary>
        /// Called every frame by the lip driver.
        /// Drives teeth blendshapes AND jaw transform (guaranteed fallback).
        /// </summary>
        public void SetOpen(float open01, bool speaking)
        {
            float target = speaking ? Mathf.Clamp01(open01) : 0f;
            _openSmoothed = Mathf.Lerp(_openSmoothed, target, 1f - Mathf.Exp(-smooth * Time.deltaTime));

            // A) Teeth blendshape (if exists)
            if (teethRenderer != null && _openBlendshape >= 0)
            {
                float w = _openSmoothed * 100f;
                teethRenderer.SetBlendShapeWeight(_openBlendshape, w);
            }

            // B) GUARANTEED visible fallback: move jaw or teeth transform
            if (_hasJawBase && jawOrLowerTeethTransform != null)
            {
                jawOrLowerTeethTransform.localPosition = _jawPosBase + jawLocalPosOpen * _openSmoothed;
                jawOrLowerTeethTransform.localRotation = _jawRotBase * Quaternion.Euler(jawLocalRotOpen * _openSmoothed);
            }

            // Proof logs once per second while speaking
            if (verbose && speaking)
            {
                _logT += Time.deltaTime;
                if (_logT >= 1f)
                {
                    _logT = 0f;

                    string blendW = "N/A";
                    if (teethRenderer != null && _openBlendshape >= 0)
                    {
                        blendW = teethRenderer.GetBlendShapeWeight(_openBlendshape).ToString("F1");
                    }

                    Debug.Log($"[TTS-Teeth] speaking open={_openSmoothed:F2} " +
                              $"blendIdx={_openBlendshape} blendW={blendW} " +
                              $"jawNode={(jawOrLowerTeethTransform != null ? jawOrLowerTeethTransform.name : "NULL")} " +
                              $"jawPos={jawOrLowerTeethTransform?.localPosition}");
                }
            }
        }

        /// <summary>
        /// Force re-scan for teeth objects.
        /// </summary>
        public void Rescan()
        {
            if (characterRoot == null)
                characterRoot = FindCharacterRoot();

            if (characterRoot != null)
            {
                // Re-find teeth renderer
                teethRenderer = characterRoot
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .FirstOrDefault(r => r != null &&
                        ((r.name ?? "").ToLowerInvariant().Contains("teeth") ||
                         (r.sharedMesh != null && (r.sharedMesh.name ?? "").ToLowerInvariant().Contains("teeth"))));

                // Re-find jaw
                jawOrLowerTeethTransform = characterRoot
                    .GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t != null &&
                        (t.name ?? "").ToLowerInvariant().Contains("jaw"));

                if (jawOrLowerTeethTransform == null && teethRenderer != null)
                    jawOrLowerTeethTransform = teethRenderer.transform;

                if (jawOrLowerTeethTransform != null)
                {
                    _jawPosBase = jawOrLowerTeethTransform.localPosition;
                    _jawRotBase = jawOrLowerTeethTransform.localRotation;
                    _hasJawBase = true;
                }

                _openBlendshape = FindOpenBlendshape(teethRenderer);

                if (teethRenderer != null)
                    teethRenderer.updateWhenOffscreen = true;
            }

            if (verbose)
            {
                Debug.Log($"[TTS-Teeth] Rescan complete. " +
                          $"teethRenderer={(teethRenderer != null ? teethRenderer.name : "NULL")} " +
                          $"jawNode={(jawOrLowerTeethTransform != null ? jawOrLowerTeethTransform.name : "NULL")}");
            }
        }

        void OnDestroy()
        {
            // Reset blendshape
            if (teethRenderer != null && _openBlendshape >= 0)
            {
                teethRenderer.SetBlendShapeWeight(_openBlendshape, 0f);
            }

            // Reset transform
            if (_hasJawBase && jawOrLowerTeethTransform != null)
            {
                jawOrLowerTeethTransform.localPosition = _jawPosBase;
                jawOrLowerTeethTransform.localRotation = _jawRotBase;
            }

            if (_instance == this)
                _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (_instance != null) return;

            TtsTeethDriver existing = FindObjectOfType<TtsTeethDriver>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            GameObject go = new GameObject("_TtsTeethDriver");
            _instance = go.AddComponent<TtsTeethDriver>();
            DontDestroyOnLoad(go);
            Debug.Log("[TTS-Teeth] Auto-created runtime teeth driver");
        }

#if UNITY_EDITOR
        [ContextMenu("Test Open Teeth")]
        private void TestOpen()
        {
            SetOpen(1f, true);
            Debug.Log("[TTS-Teeth] Test: Teeth OPEN");
        }

        [ContextMenu("Test Close Teeth")]
        private void TestClose()
        {
            SetOpen(0f, false);
            Debug.Log("[TTS-Teeth] Test: Teeth CLOSED");
        }

        [ContextMenu("Rescan Character")]
        private void EditorRescan()
        {
            Rescan();
        }

        [ContextMenu("Dump Teeth Info")]
        private void DumpInfo()
        {
            Debug.Log($"[TTS-Teeth] === TEETH DEBUG INFO ===");
            Debug.Log($"characterRoot: {(characterRoot != null ? characterRoot.name : "NULL")}");
            Debug.Log($"teethRenderer: {(teethRenderer != null ? teethRenderer.name : "NULL")}");
            Debug.Log($"jawOrLowerTeethTransform: {(jawOrLowerTeethTransform != null ? jawOrLowerTeethTransform.name : "NULL")}");
            Debug.Log($"_openBlendshape: {_openBlendshape}");
            Debug.Log($"_hasJawBase: {_hasJawBase}");
            Debug.Log($"_openSmoothed: {_openSmoothed}");

            if (teethRenderer != null && teethRenderer.sharedMesh != null)
            {
                var mesh = teethRenderer.sharedMesh;
                Debug.Log($"Teeth mesh: {mesh.name}, blendshapeCount: {mesh.blendShapeCount}");
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    Debug.Log($"  [{i}] {mesh.GetBlendShapeName(i)}");
                }
            }
        }
#endif
    }
}
