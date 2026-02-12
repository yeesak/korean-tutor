using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ShadowingTutor
{
    /// <summary>
    /// TTS-driven mouth animation controller with robust face mesh selection.
    /// Properly excludes teeth/tongue/eye meshes and finds the correct face renderer.
    /// Drives mouth animation in LateUpdate to override other controllers.
    ///
    /// FRONTEND-ONLY: Does not touch backend/server/API code.
    /// </summary>
    public class TtsMouthController : MonoBehaviour
    {
        [Header("Auto-Wire Settings")]
        [Tooltip("Automatically find and wire components on Start")]
        [SerializeField] private bool _autoWireOnStart = true;

        [Header("Face Renderer (Auto-Resolved)")]
        [SerializeField] private SkinnedMeshRenderer _faceRenderer;
        [SerializeField] private int _mouthBlendshapeIndex = -1;
        [SerializeField] private string _selectedBlendshapeName;

        [Header("Jaw Bone Fallback")]
        [SerializeField] private Transform _jawBone;
        [SerializeField] private Vector3 _jawRotationAxis = Vector3.right;
        [SerializeField] private float _maxJawAngle = 15f;

        [Header("TTS Audio Source (Auto-Resolved)")]
        [SerializeField] private AudioSource _ttsAudioSource;

        [Header("Animation Settings")]
        [SerializeField] private float _maxBlendshapeWeight = 100f;
        [SerializeField] private float _responsiveness = 15f;
        [SerializeField] private float _noiseGate = 0.01f;
        [SerializeField] private float _sensitivity = 5f;

        [Header("Controller Conflict Resolution")]
        [Tooltip("Disable competing LipSync controllers while speaking")]
        [SerializeField] private bool _overrideWhileSpeaking = true;

        // Runtime state
        private float _currentOpenAmount = 0f;
        private float _targetOpenAmount = 0f;
        private Quaternion _jawBaseRotation;
        private bool _isSpeaking = false;
        private bool _wasSpeaking = false;
        private List<MonoBehaviour> _disabledControllers = new List<MonoBehaviour>();
        private bool _hasLoggedWiring = false;
        private float[] _audioSamples = new float[256];

        // Singleton for easy access
        private static TtsMouthController _instance;
        public static TtsMouthController Instance => _instance;

        // Excluded renderer name patterns (case-insensitive)
        private static readonly string[] ExcludedNames = {
            "teeth", "tooth", "tongue", "eye", "eyelash", "brow", "hair",
            "scalp", "cap", "occlusion", "tear"
        };

        // Blendshape name patterns for mouth (in priority order)
        private static readonly string[][] BlendshapePriority = {
            new[] { "viseme_aa", "viseme_a" },
            new[] { "jawopen", "jaw_open" },
            new[] { "mouthopen", "mouth_open" },
            new[] { "lipopen", "open" }
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (_instance != null) return;

            TtsMouthController existing = Object.FindObjectOfType<TtsMouthController>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            GameObject go = new GameObject("_TtsMouthController");
            _instance = go.AddComponent<TtsMouthController>();
            Debug.Log("[TTS-Mouth] Auto-created controller");
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"[TTS-Mouth] Duplicate controller, destroying {gameObject.name}");
                Destroy(this);
                return;
            }
            _instance = this;
        }

        private void Start()
        {
            if (_autoWireOnStart)
            {
                StartCoroutine(DelayedAutoWire());
            }
            else if (_jawBone != null)
            {
                _jawBaseRotation = _jawBone.localRotation;
            }
        }

        private IEnumerator DelayedAutoWire()
        {
            float timeout = 5f;
            float elapsed = 0f;

            while (FindCharacterRoot() == null && elapsed < timeout)
            {
                elapsed += 0.2f;
                yield return new WaitForSeconds(0.2f);
            }

            yield return null; // Extra frame for renderers

            AutoWireAll();

            if (_jawBone != null)
            {
                _jawBaseRotation = _jawBone.localRotation;
            }
        }

        /// <summary>
        /// LateUpdate ensures we override any other controllers that modify blendshapes in Update
        /// </summary>
        private void LateUpdate()
        {
            // Get RMS from TTS audio source
            float rms = GetCurrentRms();

            // Determine speaking state
            bool speaking = _ttsAudioSource != null && _ttsAudioSource.isPlaying && rms > _noiseGate;

            // Handle state transitions
            if (speaking != _wasSpeaking)
            {
                OnSpeakingStateChanged(speaking, rms);
                _wasSpeaking = speaking;
            }

            _isSpeaking = speaking;

            // Calculate target open amount
            if (rms < _noiseGate)
            {
                _targetOpenAmount = 0f;
            }
            else
            {
                _targetOpenAmount = Mathf.Clamp01((rms - _noiseGate) * _sensitivity);
            }

            // Smooth interpolation
            _currentOpenAmount = Mathf.Lerp(_currentOpenAmount, _targetOpenAmount, Time.deltaTime * _responsiveness);

            // Apply to blendshape
            if (_faceRenderer != null && _mouthBlendshapeIndex >= 0)
            {
                float weight = _currentOpenAmount * _maxBlendshapeWeight;
                _faceRenderer.SetBlendShapeWeight(_mouthBlendshapeIndex, weight);
            }

            // Apply to jaw bone (fallback or additional)
            if (_jawBone != null)
            {
                float angle = _currentOpenAmount * _maxJawAngle;
                _jawBone.localRotation = _jawBaseRotation * Quaternion.AngleAxis(angle, _jawRotationAxis);
            }
        }

        private float GetCurrentRms()
        {
            if (_ttsAudioSource == null || !_ttsAudioSource.isPlaying || _ttsAudioSource.clip == null)
                return 0f;

            try
            {
                _ttsAudioSource.GetOutputData(_audioSamples, 0);

                float sum = 0f;
                for (int i = 0; i < _audioSamples.Length; i++)
                {
                    sum += _audioSamples[i] * _audioSamples[i];
                }
                return Mathf.Sqrt(sum / _audioSamples.Length);
            }
            catch
            {
                return 0f;
            }
        }

        private void OnSpeakingStateChanged(bool speaking, float rms)
        {
            if (speaking)
            {
                float weight = Mathf.Clamp01((rms - _noiseGate) * _sensitivity) * _maxBlendshapeWeight;
                Debug.Log($"[TTS-Mouth] Speaking=TRUE rms={rms:F4} weight={weight:F1}");

                if (_overrideWhileSpeaking)
                {
                    DisableCompetingControllers();
                }
            }
            else
            {
                Debug.Log("[TTS-Mouth] Speaking=FALSE -> reset mouth");

                _targetOpenAmount = 0f;
                _currentOpenAmount = 0f;

                if (_faceRenderer != null && _mouthBlendshapeIndex >= 0)
                {
                    _faceRenderer.SetBlendShapeWeight(_mouthBlendshapeIndex, 0f);
                }

                if (_jawBone != null)
                {
                    _jawBone.localRotation = _jawBaseRotation;
                }

                if (_overrideWhileSpeaking)
                {
                    EnableCompetingControllers();
                }
            }
        }

        private void DisableCompetingControllers()
        {
            _disabledControllers.Clear();

            // Find LipSyncController specifically
            LipSyncController lipSync = FindObjectOfType<LipSyncController>();
            if (lipSync != null && lipSync.enabled)
            {
                lipSync.enabled = false;
                _disabledControllers.Add(lipSync);
                Debug.Log("[TTS-Mouth] Disabled LipSyncController while TTS speaking");
            }

            // Also find by name pattern
            MonoBehaviour[] allBehaviors = FindObjectsOfType<MonoBehaviour>();
            foreach (var mb in allBehaviors)
            {
                if (mb == null || mb == this || !mb.enabled) continue;
                if (_disabledControllers.Contains(mb)) continue;

                string typeName = mb.GetType().Name;
                if (typeName.Contains("LipSync") && !(mb is TtsMouthController))
                {
                    mb.enabled = false;
                    _disabledControllers.Add(mb);
                    Debug.Log($"[TTS-Mouth] Disabled {typeName} while TTS speaking");
                }
            }
        }

        private void EnableCompetingControllers()
        {
            foreach (var mb in _disabledControllers)
            {
                if (mb != null)
                {
                    mb.enabled = true;
                    Debug.Log($"[TTS-Mouth] Re-enabled {mb.GetType().Name} after TTS");
                }
            }
            _disabledControllers.Clear();
        }

        public void AutoWireAll()
        {
            Debug.Log("[TTS-Mouth] === Starting Auto-Wire ===");

            WireTtsAudioSource();

            Transform characterRoot = FindCharacterRoot();
            if (characterRoot == null)
            {
                Debug.LogWarning("[TTS-Mouth] Character root not found");
                return;
            }

            WireFaceRenderer(characterRoot);
            WireMouthTarget();

            Debug.Log("[TTS-Mouth] === Auto-Wire Complete ===");
            _hasLoggedWiring = true;
        }

        private void WireTtsAudioSource()
        {
            TtsPlayer ttsPlayer = TtsPlayer.Instance;

            if (ttsPlayer == null)
            {
                ttsPlayer = FindObjectOfType<TtsPlayer>();
            }

            if (ttsPlayer == null)
            {
                GameObject[] allObjects = FindObjectsOfType<GameObject>();
                foreach (var go in allObjects)
                {
                    if (go.name.Contains("TtsPlayer") || go.name.Contains("TTS"))
                    {
                        ttsPlayer = go.GetComponent<TtsPlayer>();
                        if (ttsPlayer != null) break;
                    }
                }
            }

            if (ttsPlayer != null)
            {
                _ttsAudioSource = ttsPlayer.GetComponent<AudioSource>();
                if (_ttsAudioSource == null)
                {
                    _ttsAudioSource = ttsPlayer.GetComponentInChildren<AudioSource>();
                }
            }

            if (_ttsAudioSource != null)
            {
                string clipName = _ttsAudioSource.clip != null ? _ttsAudioSource.clip.name : "null";
                Debug.Log($"[TTS-Mouth] Bound AudioSource={GetFullPath(_ttsAudioSource.transform)} clip={clipName}");
            }
            else
            {
                Debug.LogWarning("[TTS-Mouth] AudioSource: NOT FOUND");
            }
        }

        private Transform FindCharacterRoot()
        {
            string[] paths = { "Avatar/CharacterModel", "CharacterModel", "Avatar" };

            foreach (string path in paths)
            {
                GameObject found = GameObject.Find(path);
                if (found != null)
                {
                    return found.transform;
                }
            }

            SkinnedMeshRenderer[] renderers = FindObjectsOfType<SkinnedMeshRenderer>();
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

        private void WireFaceRenderer(Transform characterRoot)
        {
            SkinnedMeshRenderer[] renderers = characterRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            SkinnedMeshRenderer bestRenderer = null;
            int bestScore = int.MinValue;
            string bestReason = "";

            foreach (var smr in renderers)
            {
                string nameLower = smr.name.ToLower();

                // Check exclusions
                bool excluded = false;
                foreach (string excl in ExcludedNames)
                {
                    if (nameLower.Contains(excl))
                    {
                        excluded = true;
                        break;
                    }
                }

                if (excluded) continue;

                int score = 0;
                string reason = "";

                // PRIORITY: CC_Base_Body gets +1000 (CC characters use this for visemes)
                if (smr.name == "CC_Base_Body")
                {
                    score += 1000;
                    reason += "+1000(CC_Base_Body) ";
                }
                // +100 if name contains body, face, or head
                else if (nameLower.Contains("body"))
                {
                    score += 100;
                    reason += "+100(body) ";
                }
                else if (nameLower.Contains("face"))
                {
                    score += 100;
                    reason += "+100(face) ";
                }
                else if (nameLower.Contains("head"))
                {
                    score += 100;
                    reason += "+100(head) ";
                }

                // +50 if has blendshapes
                if (smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
                {
                    score += 50;
                    reason += $"+50(bs:{smr.sharedMesh.blendShapeCount}) ";

                    // +5 per mouth-related blendshape
                    int mouthShapes = 0;
                    for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                    {
                        string bsName = smr.sharedMesh.GetBlendShapeName(i).ToLower();
                        if (bsName.Contains("viseme") || bsName.Contains("jaw") ||
                            bsName.Contains("mouth") || bsName.Contains("lip"))
                        {
                            mouthShapes++;
                        }
                    }
                    if (mouthShapes > 0)
                    {
                        score += mouthShapes * 5;
                        reason += $"+{mouthShapes * 5}(mouth:{mouthShapes}) ";
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestRenderer = smr;
                    bestReason = reason;
                }
            }

            _faceRenderer = bestRenderer;

            if (_faceRenderer != null)
            {
                string meshName = _faceRenderer.sharedMesh != null ? _faceRenderer.sharedMesh.name : "null";
                int blendCount = _faceRenderer.sharedMesh != null ? _faceRenderer.sharedMesh.blendShapeCount : 0;

                Debug.Log($"[TTS-Mouth] FaceRenderer={GetFullPath(_faceRenderer.transform)} mesh={meshName} blendShapeCount={blendCount}");
                Debug.Log($"[TTS-Mouth] Score={bestScore} ({bestReason.Trim()})");

                // Dump first 50 blendshape names
                if (_faceRenderer.sharedMesh != null && blendCount > 0)
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.Append("[TTS-Mouth] BlendShapes: ");
                    int dumpCount = Mathf.Min(blendCount, 50);
                    for (int i = 0; i < dumpCount; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append($"{i}={_faceRenderer.sharedMesh.GetBlendShapeName(i)}");
                    }
                    if (blendCount > 50)
                    {
                        sb.Append($", ... ({blendCount - 50} more)");
                    }
                    Debug.Log(sb.ToString());
                }
            }
            else
            {
                Debug.LogWarning("[TTS-Mouth] FaceRenderer: NOT FOUND (all excluded or no candidates)");
            }
        }

        private void WireMouthTarget()
        {
            _mouthBlendshapeIndex = -1;
            _selectedBlendshapeName = "";

            if (_faceRenderer != null && _faceRenderer.sharedMesh != null)
            {
                int blendCount = _faceRenderer.sharedMesh.blendShapeCount;

                // Search by priority
                foreach (var patterns in BlendshapePriority)
                {
                    for (int i = 0; i < blendCount; i++)
                    {
                        string bsName = _faceRenderer.sharedMesh.GetBlendShapeName(i).ToLower();

                        foreach (string pattern in patterns)
                        {
                            if (bsName.Contains(pattern))
                            {
                                _mouthBlendshapeIndex = i;
                                _selectedBlendshapeName = _faceRenderer.sharedMesh.GetBlendShapeName(i);
                                Debug.Log($"[TTS-Mouth] Using blendshape index={i} name={_selectedBlendshapeName}");
                                return;
                            }
                        }
                    }
                }

                Debug.LogWarning("[TTS-Mouth] No mouth blendshape found, trying jaw bone");
            }

            WireJawBone();
        }

        private void WireJawBone()
        {
            Transform characterRoot = FindCharacterRoot();
            if (characterRoot == null) return;

            string[] jawNames = { "CC_Base_JawRoot", "JawRoot", "CC_Base_UpperJaw", "Jaw", "jaw" };

            foreach (string name in jawNames)
            {
                Transform found = FindBoneRecursive(characterRoot, name);
                if (found != null)
                {
                    _jawBone = found;
                    _jawBaseRotation = _jawBone.localRotation;
                    Debug.Log($"[TTS-Mouth] Using jaw bone: {GetFullPath(_jawBone)}");
                    return;
                }
            }

            Debug.LogWarning("[TTS-Mouth] Jaw bone: NOT FOUND");
        }

        private Transform FindBoneRecursive(Transform parent, string name)
        {
            if (parent.name == name)
                return parent;

            foreach (Transform child in parent)
            {
                Transform found = FindBoneRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private string GetFullPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private void OnDestroy()
        {
            EnableCompetingControllers();
            if (_instance == this)
                _instance = null;
        }

#if UNITY_EDITOR
        [ContextMenu("Re-Wire All")]
        private void EditorRewire()
        {
            _hasLoggedWiring = false;
            AutoWireAll();
        }

        [ContextMenu("Test Open Mouth")]
        private void TestOpen()
        {
            if (_faceRenderer != null && _mouthBlendshapeIndex >= 0)
            {
                _faceRenderer.SetBlendShapeWeight(_mouthBlendshapeIndex, _maxBlendshapeWeight);
                Debug.Log($"[TTS-Mouth] Test: mouth OPEN weight={_maxBlendshapeWeight}");
            }
            if (_jawBone != null)
            {
                _jawBone.localRotation = _jawBaseRotation * Quaternion.AngleAxis(_maxJawAngle, _jawRotationAxis);
            }
        }

        [ContextMenu("Test Close Mouth")]
        private void TestClose()
        {
            if (_faceRenderer != null && _mouthBlendshapeIndex >= 0)
            {
                _faceRenderer.SetBlendShapeWeight(_mouthBlendshapeIndex, 0f);
                Debug.Log("[TTS-Mouth] Test: mouth CLOSED");
            }
            if (_jawBone != null)
            {
                _jawBone.localRotation = _jawBaseRotation;
            }
        }

        [ContextMenu("List All Blendshapes")]
        private void ListBlendshapes()
        {
            if (_faceRenderer == null || _faceRenderer.sharedMesh == null)
            {
                Debug.Log("[TTS-Mouth] No face renderer or mesh");
                return;
            }

            int count = _faceRenderer.sharedMesh.blendShapeCount;
            Debug.Log($"[TTS-Mouth] {_faceRenderer.name} has {count} blendshapes:");

            for (int i = 0; i < count; i++)
            {
                Debug.Log($"  [{i}] {_faceRenderer.sharedMesh.GetBlendShapeName(i)}");
            }
        }
#endif
    }
}
