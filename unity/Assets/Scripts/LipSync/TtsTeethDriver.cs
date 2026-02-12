using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Drives teeth movement synced to TTS mouth open value.
    /// Uses blendshapes if available, otherwise moves teeth/jaw transforms.
    /// Called from TtsLipSyncRuntime every LateUpdate.
    /// </summary>
    public class TtsTeethDriver : MonoBehaviour
    {
        private static TtsTeethDriver _instance;
        public static TtsTeethDriver Instance => _instance;

        [Header("References")]
        [Tooltip("Root transform containing character meshes. Auto-finds if null.")]
        public Transform characterRoot;

        [Header("Transform Fallback Tuning")]
        [Tooltip("Local position offset for lower teeth when fully open")]
        public Vector3 lowerTeethLocalPosOpen = new Vector3(0f, -0.002f, 0f);

        [Tooltip("Local rotation offset (degrees) for lower teeth when fully open")]
        public Vector3 lowerTeethLocalRotOpen = new Vector3(5f, 0f, 0f);

        [Tooltip("Smoothing speed for teeth movement")]
        [Range(5f, 30f)] public float smooth = 16f;

        [Header("Blendshape Tuning")]
        [Tooltip("Max blendshape weight for teeth open")]
        [Range(0f, 100f)] public float maxTeethWeight = 80f;

        [Header("Diagnostics")]
        public bool verbose = true;

        class TeethTarget
        {
            public SkinnedMeshRenderer renderer;
            public int openIndex = -1;
            public string openName;
        }

        readonly List<TeethTarget> _teethRenderers = new List<TeethTarget>();
        Transform _lowerTeeth;
        Transform _upperTeeth;
        Transform _jawBone;

        Vector3 _lowerPosBase;
        Quaternion _lowerRotBase;
        bool _hasLowerTeethBase;

        float _openSmoothed;
        float _logTimer;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"[TTS-Teeth] Duplicate instance, destroying {gameObject.name}");
                Destroy(this);
                return;
            }
            _instance = this;

            // Auto-find character root if not set
            if (characterRoot == null)
            {
                characterRoot = FindCharacterRoot();
            }

            if (characterRoot != null)
            {
                ScanTeethRenderers();
                FindTeethTransforms();
            }

            if (verbose)
            {
                Debug.Log($"[TTS-Teeth] Awake. characterRoot={(characterRoot?.name ?? "NULL")} " +
                         $"teethRenderers={_teethRenderers.Count} " +
                         $"lowerTeeth={(_lowerTeeth?.name ?? "NULL")} " +
                         $"upperTeeth={(_upperTeeth?.name ?? "NULL")} " +
                         $"jawBone={(_jawBone?.name ?? "NULL")}");
            }
        }

        Transform FindCharacterRoot()
        {
            // Try common paths
            string[] paths = { "Avatar/CharacterModel", "CharacterModel", "Avatar" };
            foreach (string path in paths)
            {
                GameObject found = GameObject.Find(path);
                if (found != null)
                {
                    return found.transform;
                }
            }

            // Try to find by CC_Base_Body
            var renderers = FindObjectsOfType<SkinnedMeshRenderer>();
            foreach (var smr in renderers)
            {
                if (smr.name == "CC_Base_Body")
                {
                    Transform t = smr.transform;
                    while (t.parent != null)
                    {
                        if (t.name == "CharacterModel" || t.name == "Avatar")
                        {
                            return t;
                        }
                        t = t.parent;
                    }
                    return smr.transform.root;
                }
            }

            return null;
        }

        void ScanTeethRenderers()
        {
            _teethRenderers.Clear();

            if (characterRoot == null) return;

            var renderers = characterRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            foreach (var r in renderers)
            {
                if (r == null || r.sharedMesh == null) continue;

                string rendererName = (r.name ?? "").ToLowerInvariant();
                string meshName = (r.sharedMesh.name ?? "").ToLowerInvariant();

                // Check if this is a teeth renderer
                bool isTeeth = rendererName.Contains("teeth") || meshName.Contains("teeth") ||
                               rendererName.Contains("tooth") || meshName.Contains("tooth");

                if (!isTeeth) continue;

                var mesh = r.sharedMesh;
                int count = mesh.blendShapeCount;

                if (count <= 0)
                {
                    if (verbose)
                    {
                        Debug.Log($"[TTS-Teeth] Found teeth renderer {r.name} but mesh has 0 blendshapes. Will use transform fallback.");
                    }
                    continue;
                }

                // Build name->index map
                var items = new List<(string name, string key, int idx)>(count);
                for (int i = 0; i < count; i++)
                {
                    string n = mesh.GetBlendShapeName(i);
                    items.Add((n, Normalize(n), i));
                }

                // Pick best open shape
                int openIdx = PickBest(items, new[] {
                    "teethopen", "teeth_open",
                    "jawopen", "jaw_open",
                    "mouthopen", "mouth_open",
                    "open", "visemeaa", "viseme_aa"
                });

                if (openIdx >= 0)
                {
                    var target = new TeethTarget
                    {
                        renderer = r,
                        openIndex = openIdx,
                        openName = items.First(x => x.idx == openIdx).name
                    };
                    _teethRenderers.Add(target);

                    // Ensure updates even if offscreen
                    r.updateWhenOffscreen = true;

                    if (verbose)
                    {
                        Debug.Log($"[TTS-Teeth] Using blendshape on {r.name}: {target.openName}[{target.openIndex}]");
                    }
                }
                else
                {
                    if (verbose)
                    {
                        Debug.Log($"[TTS-Teeth] Teeth renderer {r.name} has {count} blendshapes but none match open keywords.");
                    }
                }
            }
        }

        void FindTeethTransforms()
        {
            _lowerTeeth = null;
            _upperTeeth = null;
            _jawBone = null;
            _hasLowerTeethBase = false;

            if (characterRoot == null) return;

            var allTransforms = characterRoot.GetComponentsInChildren<Transform>(true);

            foreach (var t in allTransforms)
            {
                if (t == null) continue;
                string k = Normalize(t.name);

                // Find jaw bone
                if (_jawBone == null && (k.Contains("jawroot") || k.Contains("jaw")))
                {
                    _jawBone = t;
                }

                // Find explicit teeth nodes
                if (_lowerTeeth == null && k.Contains("teeth") && k.Contains("lower"))
                {
                    _lowerTeeth = t;
                }
                if (_upperTeeth == null && k.Contains("teeth") && k.Contains("upper"))
                {
                    _upperTeeth = t;
                }

                // Also check for CC_Base naming convention
                if (_lowerTeeth == null && (t.name.Contains("CC_Base_Teeth") || t.name.Contains("LowerTeeth")))
                {
                    // This might be the teeth mesh, look for its transform
                    _lowerTeeth = t;
                }
            }

            // Fallback: if no explicit lower teeth found but jaw exists, use jaw to move teeth visually
            if (_lowerTeeth == null && _jawBone != null)
            {
                _lowerTeeth = _jawBone;
                if (verbose)
                {
                    Debug.Log($"[TTS-Teeth] No explicit lower teeth transform found. Using jaw bone: {_jawBone.name}");
                }
            }

            // Cache base transform if we have lower teeth
            if (_lowerTeeth != null)
            {
                _lowerPosBase = _lowerTeeth.localPosition;
                _lowerRotBase = _lowerTeeth.localRotation;
                _hasLowerTeethBase = true;

                if (verbose)
                {
                    Debug.Log($"[TTS-Teeth] Cached lower teeth base: pos={_lowerPosBase} rot={_lowerRotBase.eulerAngles}");
                }
            }
        }

        /// <summary>
        /// Set the current mouth open value (0-1) and speaking state.
        /// Called every LateUpdate from TtsLipSyncRuntime.
        /// </summary>
        public void SetOpen(float open01, bool speaking)
        {
            // Smooth the open value to avoid jitter
            float target = speaking ? Mathf.Clamp01(open01) : 0f;
            _openSmoothed = Mathf.Lerp(_openSmoothed, target, 1f - Mathf.Exp(-smooth * Time.deltaTime));

            // 1) Apply to teeth blendshapes (if available)
            if (_teethRenderers.Count > 0)
            {
                foreach (var t in _teethRenderers)
                {
                    if (t.renderer == null || t.openIndex < 0) continue;

                    float weight = Mathf.Clamp(_openSmoothed * maxTeethWeight, 0f, 100f);
                    t.renderer.SetBlendShapeWeight(t.openIndex, weight);
                }
            }

            // 2) Apply transform fallback (move lower teeth/jaw slightly)
            if (_hasLowerTeethBase && _lowerTeeth != null)
            {
                // Only apply if we DON'T have blendshape targets (or as supplementary motion)
                // For teeth with blendshapes, transforms are less needed
                // For teeth without blendshapes, transforms are primary
                if (_teethRenderers.Count == 0)
                {
                    _lowerTeeth.localPosition = _lowerPosBase + lowerTeethLocalPosOpen * _openSmoothed;
                    _lowerTeeth.localRotation = _lowerRotBase * Quaternion.Euler(lowerTeethLocalRotOpen * _openSmoothed);
                }
            }

            // 1Hz diagnostics while speaking
            if (verbose && speaking)
            {
                _logTimer += Time.deltaTime;
                if (_logTimer >= 1f)
                {
                    _logTimer = 0f;
                    Debug.Log($"[TTS-Teeth] speaking=true open={_openSmoothed:F2} " +
                             $"blendTargets={_teethRenderers.Count} " +
                             $"lowerNode={(_lowerTeeth?.name ?? "NULL")}");
                }
            }
        }

        /// <summary>
        /// Force re-scan of character for teeth objects.
        /// </summary>
        public void Rescan()
        {
            if (characterRoot == null)
            {
                characterRoot = FindCharacterRoot();
            }

            if (characterRoot != null)
            {
                ScanTeethRenderers();
                FindTeethTransforms();
            }

            if (verbose)
            {
                Debug.Log($"[TTS-Teeth] Rescan complete. teethRenderers={_teethRenderers.Count}");
            }
        }

        void OnDestroy()
        {
            // Reset blendshapes
            foreach (var t in _teethRenderers)
            {
                if (t.renderer != null && t.openIndex >= 0)
                {
                    t.renderer.SetBlendShapeWeight(t.openIndex, 0f);
                }
            }

            // Reset transform
            if (_hasLowerTeethBase && _lowerTeeth != null)
            {
                _lowerTeeth.localPosition = _lowerPosBase;
                _lowerTeeth.localRotation = _lowerRotBase;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        static int PickBest(List<(string name, string key, int idx)> items, IEnumerable<string> keywords)
        {
            int best = -1;
            int bestScore = -1;

            foreach (var it in items)
            {
                int score = 0;
                foreach (var kw in keywords)
                {
                    var k = Normalize(kw);
                    if (it.key == k) score += 10;
                    else if (it.key.Contains(k)) score += 5;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = it.idx;
                }
            }

            // Require minimal confidence
            return (bestScore >= 5) ? best : -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (_instance != null) return;

            // Check if one already exists
            TtsTeethDriver existing = FindObjectOfType<TtsTeethDriver>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            // Create new instance
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
#endif
    }
}
