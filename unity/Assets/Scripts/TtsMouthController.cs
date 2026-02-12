using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ShadowingTutor
{
    /// <summary>
    /// TTS-driven mouth animation controller with robust face mesh selection.
    /// Properly excludes teeth/tongue/eye meshes and finds the correct face renderer.
    /// Disables competing LipSync controllers while TTS is speaking.
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

        // Singleton for easy access
        private static TtsMouthController _instance;
        public static TtsMouthController Instance => _instance;

        // Excluded renderer name patterns (case-insensitive)
        private static readonly string[] ExcludedNames = {
            "teeth", "tooth", "tongue", "eye", "eyelash", "brow", "hair", "scalp", "cap"
        };

        // Preferred renderer name patterns (case-insensitive)
        private static readonly string[] PreferredNames = { "face", "head", "body" };

        // Blendshape name patterns for mouth (in priority order)
        private static readonly string[][] BlendshapePriority = {
            new[] { "viseme_aa", "viseme_a" },
            new[] { "jawopen", "jaw_open" },
            new[] { "mouthopen", "mouth_open" },
            new[] { "lipopen", "open" }
        };

        /// <summary>
        /// Auto-create TtsMouthController at runtime if none exists.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            // Check if one already exists in scene
            if (_instance != null) return;

            TtsMouthController existing = Object.FindObjectOfType<TtsMouthController>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            // Create new instance
            GameObject go = new GameObject("_TtsMouthController");
            _instance = go.AddComponent<TtsMouthController>();
            Debug.Log("[Mouth] Auto-created TtsMouthController at runtime");
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"[Mouth] Duplicate TtsMouthController, destroying {gameObject.name}");
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
            else
            {
                // Store jaw base rotation if already assigned
                if (_jawBone != null)
                {
                    _jawBaseRotation = _jawBone.localRotation;
                }
            }
        }

        /// <summary>
        /// Wait for character to be loaded before auto-wiring.
        /// </summary>
        private IEnumerator DelayedAutoWire()
        {
            float timeout = 5f;
            float elapsed = 0f;

            // Wait for character root to be available
            while (FindCharacterRoot() == null && elapsed < timeout)
            {
                elapsed += 0.2f;
                yield return new WaitForSeconds(0.2f);
            }

            // Additional frame for renderers to initialize
            yield return null;

            AutoWireAll();

            // Store jaw base rotation after wiring
            if (_jawBone != null)
            {
                _jawBaseRotation = _jawBone.localRotation;
            }
        }

        private void Update()
        {
            // Get RMS from TTS audio source
            float rms = GetCurrentRms();

            // Determine speaking state
            bool speaking = _ttsAudioSource != null && _ttsAudioSource.isPlaying && rms > _noiseGate;

            // Handle state transitions
            if (speaking != _wasSpeaking)
            {
                OnSpeakingStateChanged(speaking);
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
                float[] samples = new float[256];
                _ttsAudioSource.GetOutputData(samples, 0);

                float sum = 0f;
                for (int i = 0; i < samples.Length; i++)
                {
                    sum += samples[i] * samples[i];
                }
                return Mathf.Sqrt(sum / samples.Length);
            }
            catch
            {
                return 0f;
            }
        }

        private void OnSpeakingStateChanged(bool speaking)
        {
            if (speaking)
            {
                Debug.Log($"[Mouth] Speaking=TRUE");
                if (_overrideWhileSpeaking)
                {
                    DisableCompetingControllers();
                }
            }
            else
            {
                Debug.Log($"[Mouth] Speaking=FALSE");
                // Return mouth to neutral
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

            // Find all MonoBehaviours that might conflict
            MonoBehaviour[] allBehaviors = FindObjectsOfType<MonoBehaviour>();

            foreach (var mb in allBehaviors)
            {
                if (mb == null || mb == this || !mb.enabled) continue;

                string typeName = mb.GetType().Name;

                // Check if this is a competing lip sync controller
                if (typeName.Contains("LipSync") || typeName.Contains("Mouth"))
                {
                    if (mb is TtsMouthController) continue; // Don't disable ourselves

                    mb.enabled = false;
                    _disabledControllers.Add(mb);
                    Debug.Log($"[Mouth] Speaking=TRUE -> disabling {typeName} on {mb.gameObject.name}");
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
                    Debug.Log($"[Mouth] Speaking=FALSE -> enabling {mb.GetType().Name} on {mb.gameObject.name}");
                }
            }
            _disabledControllers.Clear();
        }

        /// <summary>
        /// Auto-wire all components: TTS AudioSource, Face Renderer, Blendshape/Jaw
        /// </summary>
        public void AutoWireAll()
        {
            Debug.Log("[Mouth] === Auto-wiring TTS Mouth Controller ===");

            // 1. Find TTS AudioSource
            WireTtsAudioSource();

            // 2. Find character root
            Transform characterRoot = FindCharacterRoot();
            if (characterRoot == null)
            {
                Debug.LogWarning("[Mouth] Character root not found");
                return;
            }

            // 3. Find face renderer with scoring
            WireFaceRenderer(characterRoot);

            // 4. Find mouth blendshape or jaw bone
            WireMouthTarget();

            Debug.Log("[Mouth] === Auto-wiring complete ===");
        }

        private void WireTtsAudioSource()
        {
            // Try to find TtsPlayer
            TtsPlayer ttsPlayer = TtsPlayer.Instance;

            if (ttsPlayer == null)
            {
                // Search by type
                ttsPlayer = FindObjectOfType<TtsPlayer>();
            }

            if (ttsPlayer == null)
            {
                // Search by name
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
                // Get AudioSource on same GameObject
                _ttsAudioSource = ttsPlayer.GetComponent<AudioSource>();

                if (_ttsAudioSource == null)
                {
                    // Search children
                    _ttsAudioSource = ttsPlayer.GetComponentInChildren<AudioSource>();
                }
            }

            if (_ttsAudioSource != null)
            {
                string clipName = _ttsAudioSource.clip != null ? _ttsAudioSource.clip.name : "null";
                Debug.Log($"[Mouth] TTS AudioSource: {GetFullPath(_ttsAudioSource.transform)} clip={clipName}");
            }
            else
            {
                Debug.LogWarning("[Mouth] TTS AudioSource: NOT FOUND");
            }
        }

        private Transform FindCharacterRoot()
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

            // Score each renderer
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

                if (excluded)
                {
                    continue;
                }

                int score = 0;
                string reason = "";

                // +100 if name contains face or head
                foreach (string pref in PreferredNames)
                {
                    if (nameLower.Contains(pref))
                    {
                        score += 100;
                        reason += $"+100(name:{pref}) ";
                        break;
                    }
                }

                // +50 if has blendshapes
                if (smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
                {
                    score += 50;
                    reason += $"+50(blendshapes:{smr.sharedMesh.blendShapeCount}) ";

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
                        reason += $"+{mouthShapes * 5}(mouthShapes:{mouthShapes}) ";
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
                int blendCount = _faceRenderer.sharedMesh != null ? _faceRenderer.sharedMesh.blendShapeCount : 0;
                Debug.Log($"[Mouth] FaceRenderer selected: {GetFullPath(_faceRenderer.transform)} blendShapeCount={blendCount} score={bestScore} ({bestReason.Trim()})");
            }
            else
            {
                Debug.LogWarning("[Mouth] FaceRenderer: NOT FOUND (all renderers excluded or no valid candidates)");
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
                                Debug.Log($"[Mouth] Blendshape selected: {_selectedBlendshapeName} index={i}");
                                return;
                            }
                        }
                    }
                }

                Debug.LogWarning("[Mouth] No suitable mouth blendshape found, trying jaw bone fallback");
            }

            // Fallback: find jaw bone
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
                    Debug.Log($"[Mouth] Jaw bone selected: {GetFullPath(_jawBone)}");
                    return;
                }
            }

            Debug.LogWarning("[Mouth] Jaw bone: NOT FOUND");
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
            // Re-enable any controllers we disabled
            EnableCompetingControllers();

            if (_instance == this)
                _instance = null;
        }

#if UNITY_EDITOR
        [ContextMenu("Re-Wire All")]
        private void EditorRewire()
        {
            AutoWireAll();
        }

        [ContextMenu("Test Open Mouth")]
        private void TestOpen()
        {
            if (_faceRenderer != null && _mouthBlendshapeIndex >= 0)
            {
                _faceRenderer.SetBlendShapeWeight(_mouthBlendshapeIndex, _maxBlendshapeWeight);
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
                Debug.Log("No face renderer or mesh");
                return;
            }

            int count = _faceRenderer.sharedMesh.blendShapeCount;
            Debug.Log($"[{_faceRenderer.name}] has {count} blendshapes:");

            for (int i = 0; i < count; i++)
            {
                string name = _faceRenderer.sharedMesh.GetBlendShapeName(i);
                Debug.Log($"  [{i}] {name}");
            }
        }
#endif
    }
}
