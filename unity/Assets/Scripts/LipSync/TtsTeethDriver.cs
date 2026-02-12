using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Drives teeth/jaw movement VERTICALLY ONLY (no sideways motion).
    /// Uses either local Y translation OR pitch rotation around local X.
    /// Called from TtsLipSyncRuntime every LateUpdate.
    /// </summary>
    public class TtsTeethDriver : MonoBehaviour
    {
        private static TtsTeethDriver _instance;
        public static TtsTeethDriver Instance => _instance;

        [Header("Assign explicitly in Inspector (or auto-finds)")]
        [Tooltip("Drag the LOWER teeth transform if it exists. If not, drag the Jaw bone transform.")]
        public Transform lowerTeethOrJaw;

        [Header("Vertical-only motion (LOCAL Y translation)")]
        [Tooltip("Maximum vertical opening distance in LOCAL Y units. Typical range: 0.0015 ~ 0.004")]
        public float maxLocalYOpen = 0.0030f;

        [Tooltip("If true, opening moves +Y; if false, opening moves -Y (most rigs need -Y).")]
        public bool openMovesPositiveY = false;

        [Tooltip("Smoothing speed (higher = snappier).")]
        public float smooth = 22f;

        [Header("Optional: use pitch-only jaw rotation instead")]
        [Tooltip("If true, use rotation instead of translation")]
        public bool usePitchRotationInstead = false;

        [Tooltip("Maximum jaw pitch in degrees for open. Typical 6~12.")]
        public float maxPitchOpenDeg = 9f;

        [Tooltip("If true, open rotates +X; if false, open rotates -X.")]
        public bool openPitchPositiveX = true;

        [Header("Diagnostics")]
        public bool verbose = true;

        Vector3 _baseLocalPos;
        Quaternion _baseLocalRot;
        bool _hasBase;
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

            // Auto-find if not assigned
            if (lowerTeethOrJaw == null)
            {
                lowerTeethOrJaw = FindLowerTeethOrJaw();
            }

            if (lowerTeethOrJaw == null)
            {
                Debug.LogError("[TTS-Teeth] lowerTeethOrJaw is NULL. Assign LowerTeeth or Jaw transform in Inspector.");
                return;
            }

            _baseLocalPos = lowerTeethOrJaw.localPosition;
            _baseLocalRot = lowerTeethOrJaw.localRotation;
            _hasBase = true;

            if (verbose)
            {
                string mode = usePitchRotationInstead ? "PitchRotationOnly" : "LocalYTranslationOnly";
                Debug.Log($"[TTS-Teeth] Awake node={lowerTeethOrJaw.name} " +
                          $"baseLocalPos={_baseLocalPos} " +
                          $"baseLocalRotEuler={_baseLocalRot.eulerAngles} " +
                          $"mode={mode}");
            }
        }

        Transform FindLowerTeethOrJaw()
        {
            // Try to find character root
            Transform characterRoot = null;
            string[] rootPaths = { "Avatar/CharacterModel", "CharacterModel", "Avatar" };
            foreach (string path in rootPaths)
            {
                GameObject found = GameObject.Find(path);
                if (found != null)
                {
                    characterRoot = found.transform;
                    break;
                }
            }

            if (characterRoot == null)
            {
                // Try to find via CC_Base_Body
                var renderers = FindObjectsOfType<SkinnedMeshRenderer>();
                foreach (var smr in renderers)
                {
                    if (smr.name == "CC_Base_Body")
                    {
                        characterRoot = smr.transform.root;
                        break;
                    }
                }
            }

            if (characterRoot == null)
            {
                Debug.LogWarning("[TTS-Teeth] Could not find character root");
                return null;
            }

            // Search for lower teeth or jaw in priority order
            string[] searchNames = {
                "CC_Base_Teeth02",  // Often lower teeth in CC characters
                "TeethLower",
                "LowerTeeth",
                "Teeth_Lower",
                "CC_Base_JawRoot",
                "JawRoot",
                "Jaw"
            };

            var allTransforms = characterRoot.GetComponentsInChildren<Transform>(true);

            foreach (string searchName in searchNames)
            {
                foreach (var t in allTransforms)
                {
                    if (t.name.Equals(searchName, System.StringComparison.OrdinalIgnoreCase) ||
                        t.name.Contains(searchName))
                    {
                        if (verbose) Debug.Log($"[TTS-Teeth] Auto-found: {t.name}");
                        return t;
                    }
                }
            }

            // Fallback: any transform containing "jaw" (case insensitive)
            foreach (var t in allTransforms)
            {
                if (t.name.ToLowerInvariant().Contains("jaw"))
                {
                    if (verbose) Debug.Log($"[TTS-Teeth] Fallback found: {t.name}");
                    return t;
                }
            }

            Debug.LogWarning("[TTS-Teeth] Could not auto-find lower teeth or jaw transform");
            return null;
        }

        /// <summary>
        /// Call every frame from lip driver (LateUpdate).
        /// Applies VERTICAL-ONLY motion (no sideways drift).
        /// </summary>
        public void SetOpen(float open01, bool speaking)
        {
            if (lowerTeethOrJaw == null || !_hasBase) return;

            float target = speaking ? Mathf.Clamp01(open01) : 0f;
            _openSmoothed = Mathf.Lerp(_openSmoothed, target, 1f - Mathf.Exp(-smooth * Time.deltaTime));

            if (!usePitchRotationInstead)
            {
                // LOCAL Y translation only (NO X/Z movement)
                float dir = openMovesPositiveY ? 1f : -1f;
                float yDelta = dir * maxLocalYOpen * _openSmoothed;

                Vector3 newPos = _baseLocalPos;
                newPos.y = _baseLocalPos.y + yDelta;
                lowerTeethOrJaw.localPosition = newPos;

                // Keep rotation UNCHANGED to avoid any sideways drift
                lowerTeethOrJaw.localRotation = _baseLocalRot;

                if (verbose && speaking)
                {
                    _logT += Time.deltaTime;
                    if (_logT >= 1f)
                    {
                        _logT = 0f;
                        Debug.Log($"[TTS-Teeth] speaking open={_openSmoothed:F2} " +
                                  $"appliedLocalYDelta={yDelta:F6} " +
                                  $"nodeLocalPos={lowerTeethOrJaw.localPosition}");
                    }
                }
            }
            else
            {
                // Pitch rotation ONLY around LOCAL X (no yaw/roll)
                float dir = openPitchPositiveX ? 1f : -1f;
                float pitchDelta = dir * maxPitchOpenDeg * _openSmoothed;

                // Keep position unchanged
                lowerTeethOrJaw.localPosition = _baseLocalPos;

                // ONLY pitch (X) changes; Y/Z rotation stays at base
                Vector3 baseEuler = _baseLocalRot.eulerAngles;
                lowerTeethOrJaw.localRotation = Quaternion.Euler(
                    baseEuler.x + pitchDelta,
                    baseEuler.y,
                    baseEuler.z
                );

                if (verbose && speaking)
                {
                    _logT += Time.deltaTime;
                    if (_logT >= 1f)
                    {
                        _logT = 0f;
                        Debug.Log($"[TTS-Teeth] speaking open={_openSmoothed:F2} " +
                                  $"appliedPitchDeg={pitchDelta:F2} " +
                                  $"nodeLocalRotEuler={lowerTeethOrJaw.localRotation.eulerAngles}");
                    }
                }
            }
        }

        /// <summary>
        /// Force re-scan for teeth/jaw node.
        /// </summary>
        public void Rescan()
        {
            lowerTeethOrJaw = FindLowerTeethOrJaw();

            if (lowerTeethOrJaw != null)
            {
                _baseLocalPos = lowerTeethOrJaw.localPosition;
                _baseLocalRot = lowerTeethOrJaw.localRotation;
                _hasBase = true;
            }

            if (verbose)
            {
                Debug.Log($"[TTS-Teeth] Rescan complete. node={(lowerTeethOrJaw != null ? lowerTeethOrJaw.name : "NULL")}");
            }
        }

        void OnDestroy()
        {
            // Reset transform to base
            if (_hasBase && lowerTeethOrJaw != null)
            {
                lowerTeethOrJaw.localPosition = _baseLocalPos;
                lowerTeethOrJaw.localRotation = _baseLocalRot;
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
        [ContextMenu("Test Open")]
        private void TestOpen()
        {
            if (lowerTeethOrJaw == null) return;
            if (!_hasBase)
            {
                _baseLocalPos = lowerTeethOrJaw.localPosition;
                _baseLocalRot = lowerTeethOrJaw.localRotation;
                _hasBase = true;
            }
            SetOpen(1f, true);
            Debug.Log("[TTS-Teeth] Test: OPEN");
        }

        [ContextMenu("Test Close")]
        private void TestClose()
        {
            if (lowerTeethOrJaw == null) return;
            SetOpen(0f, false);
            Debug.Log("[TTS-Teeth] Test: CLOSED");
        }

        [ContextMenu("Flip Y Direction")]
        private void FlipYDirection()
        {
            openMovesPositiveY = !openMovesPositiveY;
            Debug.Log($"[TTS-Teeth] openMovesPositiveY = {openMovesPositiveY}");
        }

        [ContextMenu("Flip Pitch Direction")]
        private void FlipPitchDirection()
        {
            openPitchPositiveX = !openPitchPositiveX;
            Debug.Log($"[TTS-Teeth] openPitchPositiveX = {openPitchPositiveX}");
        }

        [ContextMenu("Rescan")]
        private void EditorRescan()
        {
            Rescan();
        }
#endif
    }
}
