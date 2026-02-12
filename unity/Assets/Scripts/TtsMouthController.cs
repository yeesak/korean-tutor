using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ShadowingTutor
{
    /// <summary>
    /// TTS-driven mouth animation controller with robust face mesh selection.
    /// Properly excludes teeth/tongue/eye meshes and finds the correct face renderer.
    /// Drives mouth animation in LateUpdate to override other controllers.
    /// Supports multiple lip viseme blendshapes (AA, EE, OH, IH, OU, MBP, FV, TH, CH, SS)
    /// with audio-driven selection using RMS + ZCR spectral analysis.
    /// Falls back to lip bones if no viseme blendshapes found.
    ///
    /// FRONTEND-ONLY: Does not touch backend/server/API code.
    /// </summary>
    public class TtsMouthController : MonoBehaviour
    {
        [Header("Auto-Wire Settings")]
        [Tooltip("Automatically find and wire components on Start")]
        [SerializeField] private bool _autoWireOnStart = true;

        [Header("Debug")]
        [Tooltip("Print found visemes once on wire")]
        public bool debugPrintVisemes = true;

        [Header("Face Renderer (Auto-Resolved)")]
        [SerializeField] private SkinnedMeshRenderer _faceRenderer;
        [SerializeField] private int _mouthBlendshapeIndex = -1;
        [SerializeField] private string _selectedBlendshapeName;

        [Header("Jaw Bone Fallback")]
        [SerializeField] private Transform _jawBone;
        [SerializeField] private Vector3 _jawRotationAxis = Vector3.right;
        [SerializeField] private float _maxJawAngle = 15f;

        [Header("Lip Bone Fallback")]
        [SerializeField] private Transform _upperLipBone;
        [SerializeField] private Transform _lowerLipBone;
        [SerializeField] private Transform _leftMouthCorner;
        [SerializeField] private Transform _rightMouthCorner;
        [SerializeField] private float _lipBoneMovement = 0.002f;

        [Header("TTS Audio Source (Auto-Resolved)")]
        [SerializeField] private AudioSource _ttsAudioSource;

        [Header("Animation Settings")]
        [SerializeField] private float _maxBlendshapeWeight = 100f;
        [SerializeField] private float _responsiveness = 15f;
        [SerializeField] private float _noiseGate = 0.01f;
        [SerializeField] private float _sensitivity = 5f;

        [Header("Viseme Settings")]
        [Tooltip("Enable multi-viseme lip animation")]
        [SerializeField] private bool _enableVisemes = true;
        [Tooltip("Attack time for viseme transitions (seconds)")]
        [SerializeField] private float _visemeAttack = 0.07f;
        [Tooltip("Release time for viseme transitions (seconds)")]
        [SerializeField] private float _visemeRelease = 0.15f;
        [Tooltip("Min interval for viseme switching (seconds)")]
        [SerializeField] private float _visemeSwitchMin = 0.10f;
        [Tooltip("Max interval for viseme switching (seconds)")]
        [SerializeField] private float _visemeSwitchMax = 0.18f;
        [Tooltip("Min interval for MBP consonant pulses (seconds)")]
        [SerializeField] private float _mbpPulseMin = 0.6f;
        [Tooltip("Max interval for MBP consonant pulses (seconds)")]
        [SerializeField] private float _mbpPulseMax = 1.2f;

        [Header("Viseme Weight Limits")]
        [SerializeField] private float _aaMaxWeight = 80f;
        [SerializeField] private float _eeMaxWeight = 60f;
        [SerializeField] private float _ohMaxWeight = 70f;
        [SerializeField] private float _mbpMaxWeight = 50f;
        [SerializeField] private float _fvMaxWeight = 40f;
        [SerializeField] private float _ssMaxWeight = 35f;

        [Header("Controller Conflict Resolution")]
        [Tooltip("Disable competing LipSync controllers while speaking")]
        [SerializeField] private bool _overrideWhileSpeaking = true;

        // Viseme groups (expanded)
        public enum VisemeType { None, AA, EE, IH, OH, OU, MBP, FV, TH, CH, SS }

        // Viseme blendshape indices (-1 if not found)
        private Dictionary<VisemeType, int> _visemeIndices = new Dictionary<VisemeType, int>();
        private Dictionary<VisemeType, float> _visemeWeights = new Dictionary<VisemeType, float>();
        private Dictionary<VisemeType, float> _visemeMaxWeights = new Dictionary<VisemeType, float>();
        private bool _hasVisemes = false;
        private bool _hasLipBones = false;

        // Runtime state
        private float _currentOpenAmount = 0f;
        private float _targetOpenAmount = 0f;
        private Quaternion _jawBaseRotation;
        private Vector3 _upperLipBasePos;
        private Vector3 _lowerLipBasePos;
        private Vector3 _leftCornerBasePos;
        private Vector3 _rightCornerBasePos;
        private bool _isSpeaking = false;
        private bool _wasSpeaking = false;
        private List<MonoBehaviour> _disabledControllers = new List<MonoBehaviour>();
        private bool _hasLoggedWiring = false;
        private float[] _audioSamples = new float[256];

        // Viseme state
        private VisemeType _currentViseme = VisemeType.None;
        private VisemeType _targetViseme = VisemeType.None;
        private float _lastVisemeSwitchTime = 0f;
        private float _lastMbpPulseTime = 0f;
        private float _nextVisemeSwitchInterval = 0.12f;
        private float _nextMbpPulseInterval = 0.8f;
        private float _lastZcr = 0f;
        private float _lastRms = 0f;
        private float _audioBrightness = 0f;

        // Diagnostic logging (1Hz rate limit)
        private float _lastDiagnosticLogTime = 0f;
        private const float DiagnosticLogInterval = 1f;

        // Singleton for easy access
        private static TtsMouthController _instance;
        public static TtsMouthController Instance => _instance;

        // Excluded renderer name patterns (case-insensitive)
        private static readonly string[] ExcludedNames = {
            "teeth", "tooth", "tongue", "eye", "eyelash", "brow", "hair",
            "scalp", "cap", "occlusion", "tear", "lash"
        };

        // Tokens that indicate viseme/mouth blendshapes (for renderer scoring)
        private static readonly string[] VisemeTokens = {
            "viseme", "mouth", "lip", "jaw", "aa", "ee", "ih", "oh", "ou",
            "fv", "mbp", "th", "ch", "ss"
        };

        // Blendshape name patterns for mouth (in priority order)
        private static readonly string[][] BlendshapePriority = {
            new[] { "viseme_aa", "viseme_a" },
            new[] { "jawopen", "jaw_open" },
            new[] { "mouthopen", "mouth_open" },
            new[] { "lipopen", "open" }
        };

        // Viseme blendshape patterns for each type
        // Supports: Reallusion/CC, Oculus/OVR, and generic naming conventions
        private static readonly Dictionary<VisemeType, string[]> VisemePatterns = new Dictionary<VisemeType, string[]>
        {
            // AA - open mouth (ah, a)
            { VisemeType.AA, new[] { "viseme_aa", "v_aa", "mouth_a", "mouth_open", "a01_jaw_open", "aa" } },
            // EE - wide smile (ee)
            { VisemeType.EE, new[] { "viseme_ee", "v_ee", "mouth_e", "e01_mouth_smile", "ee" } },
            // IH - narrow (i)
            { VisemeType.IH, new[] { "viseme_ih", "viseme_i", "v_ih", "mouth_i", "ih" } },
            // OH - round lips (oh, o)
            { VisemeType.OH, new[] { "viseme_oh", "viseme_o", "v_oh", "mouth_o", "o01_mouth_o", "oh" } },
            // OU - pursed (oo, u)
            { VisemeType.OU, new[] { "viseme_ou", "viseme_u", "v_ou", "mouth_u", "ou" } },
            // MBP - closed lips (m, b, p)
            { VisemeType.MBP, new[] { "viseme_pp", "viseme_mbp", "v_mbp", "v_pp", "v_bb", "lips_mbp", "m01_lips_close", "mbp" } },
            // FV - lower lip bite (f, v)
            { VisemeType.FV, new[] { "viseme_ff", "viseme_fv", "v_fv", "v_f", "lips_fv", "f01_lower_lip_in", "fv" } },
            // TH - tongue between teeth
            { VisemeType.TH, new[] { "viseme_th", "v_th", "lips_th", "th" } },
            // CH - jaw forward (ch, j, sh)
            { VisemeType.CH, new[] { "viseme_ch", "viseme_sh", "v_ch", "ch" } },
            // SS - teeth together (s, z)
            { VisemeType.SS, new[] { "viseme_ss", "viseme_s", "v_ss", "s01_teeth_close", "ss" } }
        };

        // Lip bone name patterns
        private static readonly string[] UpperLipBoneNames = { "upperlip", "upper_lip", "lip_upper", "CC_Base_UpperLipIn", "Lip_Upper" };
        private static readonly string[] LowerLipBoneNames = { "lowerlip", "lower_lip", "lip_lower", "CC_Base_LowerLip", "Lip_Lower" };
        private static readonly string[] LeftCornerBoneNames = { "mouthcorner_l", "mouth_l", "corner_l", "CC_Base_L_Mouth", "Mouth_L" };
        private static readonly string[] RightCornerBoneNames = { "mouthcorner_r", "mouth_r", "corner_r", "CC_Base_R_Mouth", "Mouth_R" };

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

            // Initialize viseme max weights
            _visemeMaxWeights[VisemeType.AA] = _aaMaxWeight;
            _visemeMaxWeights[VisemeType.EE] = _eeMaxWeight;
            _visemeMaxWeights[VisemeType.IH] = _eeMaxWeight;
            _visemeMaxWeights[VisemeType.OH] = _ohMaxWeight;
            _visemeMaxWeights[VisemeType.OU] = _ohMaxWeight;
            _visemeMaxWeights[VisemeType.MBP] = _mbpMaxWeight;
            _visemeMaxWeights[VisemeType.FV] = _fvMaxWeight;
            _visemeMaxWeights[VisemeType.TH] = _fvMaxWeight;
            _visemeMaxWeights[VisemeType.CH] = _ssMaxWeight;
            _visemeMaxWeights[VisemeType.SS] = _ssMaxWeight;
        }

        private void Start()
        {
            if (_autoWireOnStart)
            {
                StartCoroutine(DelayedAutoWire());
            }
            else
            {
                CacheBoneBaseTransforms();
            }
        }

        private void CacheBoneBaseTransforms()
        {
            if (_jawBone != null)
                _jawBaseRotation = _jawBone.localRotation;
            if (_upperLipBone != null)
                _upperLipBasePos = _upperLipBone.localPosition;
            if (_lowerLipBone != null)
                _lowerLipBasePos = _lowerLipBone.localPosition;
            if (_leftMouthCorner != null)
                _leftCornerBasePos = _leftMouthCorner.localPosition;
            if (_rightMouthCorner != null)
                _rightCornerBasePos = _rightMouthCorner.localPosition;
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
            CacheBoneBaseTransforms();
        }

        /// <summary>
        /// LateUpdate ensures we override any other controllers that modify blendshapes in Update
        /// </summary>
        private void LateUpdate()
        {
            // Yield to TtsLipSyncRuntime if it's active and handling lip sync
            if (TtsLipSyncRuntime.Instance != null && TtsLipSyncRuntime.Instance.ttsSource != null &&
                TtsLipSyncRuntime.Instance.ttsSource.isPlaying)
            {
                return;
            }

            // Get RMS from TTS audio source (this also fills _audioSamples for ZCR)
            float rms = GetCurrentRms();
            _lastRms = rms;

            // Calculate ZCR for viseme selection
            float zcr = GetZeroCrossingRate();
            _lastZcr = zcr;

            // Calculate audio brightness (average absolute derivative)
            _audioBrightness = GetAudioBrightness();

            // Determine speaking state - CRITICAL: use isPlaying ONLY, not RMS level
            // RMS can drop to zero during quiet portions but we must keep animating
            bool speaking = _ttsAudioSource != null &&
                            _ttsAudioSource.isPlaying &&
                            _ttsAudioSource.clip != null;

            // 1Hz diagnostic logging while speaking
            if (speaking && Time.time - _lastDiagnosticLogTime >= DiagnosticLogInterval)
            {
                _lastDiagnosticLogTime = Time.time;
                Debug.Log($"[TTS-Lips] t={Time.time:F1} isPlaying={_ttsAudioSource.isPlaying} rms={rms:F4} open={_currentOpenAmount:F2}");
            }

            // Handle state transitions
            if (speaking != _wasSpeaking)
            {
                OnSpeakingStateChanged(speaking, rms);
                _wasSpeaking = speaking;
            }

            _isSpeaking = speaking;

            // Use viseme-based lip animation if available
            if (_enableVisemes && _hasVisemes && speaking)
            {
                // Use procedural fallback RMS when actual RMS is below noise gate but audio is playing
                // This ensures mouth keeps moving during quiet portions
                float effectiveRms = rms;
                if (rms < _noiseGate)
                {
                    // Generate subtle procedural movement based on time
                    float proceduralBase = (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f; // 0-1
                    effectiveRms = _noiseGate + proceduralBase * 0.03f; // Low but above gate
                }

                // Check if enough time has passed for viseme switch
                float timeSinceLastSwitch = Time.time - _lastVisemeSwitchTime;
                if (timeSinceLastSwitch >= _nextVisemeSwitchInterval)
                {
                    _targetViseme = SelectVisemeFromAudio(effectiveRms, zcr);

                    if (_targetViseme != _currentViseme)
                    {
                        _currentViseme = _targetViseme;
                        _lastVisemeSwitchTime = Time.time;
                        // Randomize next switch interval
                        _nextVisemeSwitchInterval = Random.Range(_visemeSwitchMin, _visemeSwitchMax);
                    }
                }

                // Update viseme weights with smooth transitions
                UpdateVisemeWeights(_currentViseme, effectiveRms);
            }
            else if (!speaking && _hasVisemes)
            {
                // Release all visemes when not speaking
                UpdateVisemeWeights(VisemeType.None, 0f);
            }
            else if (_enableVisemes && _hasLipBones && speaking)
            {
                // Use procedural fallback RMS for lip bones too
                float effectiveRms = rms;
                if (rms < _noiseGate)
                {
                    float proceduralBase = (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f;
                    effectiveRms = _noiseGate + proceduralBase * 0.03f;
                }
                // Use lip bone fallback
                UpdateLipBones(effectiveRms, zcr);
            }
            else if (!speaking && _hasLipBones)
            {
                ResetLipBones();
            }

            // Calculate target open amount for jaw
            // When speaking, use effectiveRms to keep jaw moving even during quiet portions
            float jawRms = rms;
            if (speaking && rms < _noiseGate)
            {
                float proceduralBase = (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f;
                jawRms = _noiseGate + proceduralBase * 0.03f;
            }

            if (jawRms < _noiseGate)
            {
                _targetOpenAmount = 0f;
            }
            else
            {
                _targetOpenAmount = Mathf.Clamp01((jawRms - _noiseGate) * _sensitivity);
            }

            // Smooth interpolation
            _currentOpenAmount = Mathf.Lerp(_currentOpenAmount, _targetOpenAmount, Time.deltaTime * _responsiveness);

            // Apply to primary blendshape (only if visemes not active)
            if (_faceRenderer != null && _mouthBlendshapeIndex >= 0 && !(_enableVisemes && _hasVisemes))
            {
                float weight = _currentOpenAmount * _maxBlendshapeWeight;
                _faceRenderer.SetBlendShapeWeight(_mouthBlendshapeIndex, weight);
            }

            // Apply to jaw bone (always active as additional movement)
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

        /// <summary>
        /// Calculate Zero-Crossing Rate from audio samples.
        /// Higher ZCR = more fricatives (s, f, sh) / lower ZCR = more vowels (a, o, e)
        /// </summary>
        private float GetZeroCrossingRate()
        {
            if (_ttsAudioSource == null || !_ttsAudioSource.isPlaying || _ttsAudioSource.clip == null)
                return 0f;

            int crossings = 0;
            for (int i = 1; i < _audioSamples.Length; i++)
            {
                if ((_audioSamples[i] >= 0 && _audioSamples[i - 1] < 0) ||
                    (_audioSamples[i] < 0 && _audioSamples[i - 1] >= 0))
                {
                    crossings++;
                }
            }

            // Normalize to 0-1 range (empirically ~50-150 crossings for speech)
            return Mathf.Clamp01(crossings / 100f);
        }

        /// <summary>
        /// Calculate audio "brightness" as average absolute derivative.
        /// Higher = more high frequency content
        /// </summary>
        private float GetAudioBrightness()
        {
            if (_audioSamples == null || _audioSamples.Length < 2)
                return 0f;

            float sum = 0f;
            for (int i = 1; i < _audioSamples.Length; i++)
            {
                sum += Mathf.Abs(_audioSamples[i] - _audioSamples[i - 1]);
            }
            return Mathf.Clamp01(sum / _audioSamples.Length * 10f);
        }

        private void OnSpeakingStateChanged(bool speaking, float rms)
        {
            if (speaking)
            {
                float weight = Mathf.Clamp01((rms - _noiseGate) * _sensitivity) * _maxBlendshapeWeight;
                string animMode = _hasVisemes ? "VISEMES" : (_hasLipBones ? "LIP_BONES" : "JAW_ONLY");
                Debug.Log($"[TTS-Mouth] Speaking=TRUE rms={rms:F4} weight={weight:F1} mode={animMode}");

                // Reset viseme timing with randomization
                _lastVisemeSwitchTime = Time.time;
                _lastMbpPulseTime = Time.time - Random.Range(_mbpPulseMin * 0.3f, _mbpPulseMin * 0.7f);
                _nextVisemeSwitchInterval = Random.Range(_visemeSwitchMin, _visemeSwitchMax);
                _nextMbpPulseInterval = Random.Range(_mbpPulseMin, _mbpPulseMax);

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

                // Reset visemes
                if (_hasVisemes)
                {
                    ResetVisemeWeights();
                }

                // Reset lip bones
                if (_hasLipBones)
                {
                    ResetLipBones();
                }

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

        /// <summary>
        /// Explicitly set the audio source for lip sync.
        /// Call this before starting playback for reliable binding.
        /// </summary>
        public void SetSource(AudioSource source)
        {
            if (source == null)
            {
                Debug.LogWarning("[TTS-Lips] SetSource called with null source");
                return;
            }

            _ttsAudioSource = source;

            // Ensure face renderer renders even when off-screen (for viseme updates)
            if (_faceRenderer != null)
            {
                _faceRenderer.updateWhenOffscreen = true;
            }

            Debug.Log($"[TTS-Lips] SetSource bound to {source.gameObject.name}");
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

            // Wire viseme blendshapes for lip animation
            if (_enableVisemes)
            {
                WireVisemeBlendshapes();

                // If no visemes, try lip bones
                if (!_hasVisemes)
                {
                    WireLipBones(characterRoot);
                }
            }

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
            int bestVisemeCandidates = 0;
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
                int visemeCandidates = 0;
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

                // Count blendshapes matching viseme tokens
                if (smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
                {
                    score += 50;
                    reason += $"+50(bs:{smr.sharedMesh.blendShapeCount}) ";

                    for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                    {
                        string bsName = smr.sharedMesh.GetBlendShapeName(i).ToLower();
                        foreach (string token in VisemeTokens)
                        {
                            if (bsName.Contains(token))
                            {
                                visemeCandidates++;
                                break;
                            }
                        }
                    }

                    if (visemeCandidates > 0)
                    {
                        score += visemeCandidates * 10; // Heavy weight for viseme candidates
                        reason += $"+{visemeCandidates * 10}(viseme:{visemeCandidates}) ";
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestRenderer = smr;
                    bestVisemeCandidates = visemeCandidates;
                    bestReason = reason;
                }
            }

            _faceRenderer = bestRenderer;

            if (_faceRenderer != null)
            {
                // CRITICAL: Ensure blendshapes update even when character is off-screen
                _faceRenderer.updateWhenOffscreen = true;

                string meshName = _faceRenderer.sharedMesh != null ? _faceRenderer.sharedMesh.name : "null";
                int blendCount = _faceRenderer.sharedMesh != null ? _faceRenderer.sharedMesh.blendShapeCount : 0;

                // Log per spec format
                Debug.Log($"[TTS-Lips] SelectedRenderer={GetFullPath(_faceRenderer.transform)} mesh={meshName} totalBlendShapes={blendCount} visemeCandidates={bestVisemeCandidates}");

                if (debugPrintVisemes)
                {
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

        /// <summary>
        /// Wire multiple viseme blendshapes for lip animation
        /// </summary>
        private void WireVisemeBlendshapes()
        {
            _visemeIndices.Clear();
            _visemeWeights.Clear();
            _hasVisemes = false;

            // Initialize all viseme weights to 0
            foreach (VisemeType v in System.Enum.GetValues(typeof(VisemeType)))
            {
                _visemeIndices[v] = -1;
                _visemeWeights[v] = 0f;
            }

            if (_faceRenderer == null || _faceRenderer.sharedMesh == null)
            {
                Debug.Log("[TTS-Lips] No face renderer, visemes disabled");
                return;
            }

            int blendCount = _faceRenderer.sharedMesh.blendShapeCount;
            int foundCount = 0;

            // For each viseme type, find a matching blendshape
            foreach (var kvp in VisemePatterns)
            {
                VisemeType visemeType = kvp.Key;
                string[] patterns = kvp.Value;

                for (int i = 0; i < blendCount; i++)
                {
                    string bsName = _faceRenderer.sharedMesh.GetBlendShapeName(i).ToLower();

                    foreach (string pattern in patterns)
                    {
                        if (bsName.Contains(pattern.ToLower()))
                        {
                            _visemeIndices[visemeType] = i;
                            foundCount++;
                            goto nextViseme; // Found one, move to next viseme type
                        }
                    }
                }
                nextViseme:;
            }

            // Need at least AA + one other for basic lip movement, or at least 2 core visemes
            bool hasAA = _visemeIndices[VisemeType.AA] >= 0;
            bool hasEE = _visemeIndices[VisemeType.EE] >= 0;
            bool hasOH = _visemeIndices[VisemeType.OH] >= 0;
            bool hasMBP = _visemeIndices[VisemeType.MBP] >= 0;
            bool hasFV = _visemeIndices[VisemeType.FV] >= 0;

            int coreVisemes = (hasAA ? 1 : 0) + (hasEE ? 1 : 0) + (hasOH ? 1 : 0) + (hasMBP ? 1 : 0) + (hasFV ? 1 : 0);
            _hasVisemes = coreVisemes >= 2;

            // Log viseme map per spec format
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[TTS-Lips] VisemeMap: ");
            sb.Append($"AA={_visemeIndices[VisemeType.AA]}, ");
            sb.Append($"EE={_visemeIndices[VisemeType.EE]}, ");
            sb.Append($"IH={_visemeIndices[VisemeType.IH]}, ");
            sb.Append($"OH={_visemeIndices[VisemeType.OH]}, ");
            sb.Append($"OU={_visemeIndices[VisemeType.OU]}, ");
            sb.Append($"MBP={_visemeIndices[VisemeType.MBP]}, ");
            sb.Append($"FV={_visemeIndices[VisemeType.FV]}, ");
            sb.Append($"TH={_visemeIndices[VisemeType.TH]}, ");
            sb.Append($"CH={_visemeIndices[VisemeType.CH]}, ");
            sb.Append($"SS={_visemeIndices[VisemeType.SS]}");
            Debug.Log(sb.ToString());

            if (_hasVisemes)
            {
                Debug.Log($"[TTS-Lips] Viseme lip animation ENABLED ({foundCount} visemes found, {coreVisemes} core)");
            }
            else
            {
                Debug.Log("[TTS-Lips] No viseme blendshapes found. Falling back to jaw-only.");
            }
        }

        /// <summary>
        /// Wire lip bones as fallback when no viseme blendshapes exist
        /// </summary>
        private void WireLipBones(Transform characterRoot)
        {
            _hasLipBones = false;

            // Search for lip bones
            _upperLipBone = FindBoneByPatterns(characterRoot, UpperLipBoneNames);
            _lowerLipBone = FindBoneByPatterns(characterRoot, LowerLipBoneNames);
            _leftMouthCorner = FindBoneByPatterns(characterRoot, LeftCornerBoneNames);
            _rightMouthCorner = FindBoneByPatterns(characterRoot, RightCornerBoneNames);

            // Cache base positions
            if (_upperLipBone != null) _upperLipBasePos = _upperLipBone.localPosition;
            if (_lowerLipBone != null) _lowerLipBasePos = _lowerLipBone.localPosition;
            if (_leftMouthCorner != null) _leftCornerBasePos = _leftMouthCorner.localPosition;
            if (_rightMouthCorner != null) _rightCornerBasePos = _rightMouthCorner.localPosition;

            // Need at least upper or lower lip for bone-based animation
            _hasLipBones = _upperLipBone != null || _lowerLipBone != null;

            if (_hasLipBones)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("[TTS-Lips] LipBones: ");
                sb.Append($"upper={(_upperLipBone != null ? _upperLipBone.name : "null")}, ");
                sb.Append($"lower={(_lowerLipBone != null ? _lowerLipBone.name : "null")}, ");
                sb.Append($"corner_l={(_leftMouthCorner != null ? _leftMouthCorner.name : "null")}, ");
                sb.Append($"corner_r={(_rightMouthCorner != null ? _rightMouthCorner.name : "null")}");
                Debug.Log(sb.ToString());
                Debug.Log("[TTS-Lips] Using lip bone fallback for lip animation");
            }
            else
            {
                Debug.Log("[TTS-Lips] No lip bones found. Using jaw-only animation.");
            }
        }

        private Transform FindBoneByPatterns(Transform root, string[] patterns)
        {
            foreach (string pattern in patterns)
            {
                Transform found = FindBoneRecursivePartial(root, pattern.ToLower());
                if (found != null) return found;
            }
            return null;
        }

        private Transform FindBoneRecursivePartial(Transform parent, string pattern)
        {
            if (parent.name.ToLower().Contains(pattern))
                return parent;

            foreach (Transform child in parent)
            {
                Transform found = FindBoneRecursivePartial(child, pattern);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Update lip bone positions based on audio analysis
        /// </summary>
        private void UpdateLipBones(float rms, float zcr)
        {
            if (!_hasLipBones) return;

            float normalizedRms = Mathf.Clamp01((rms - _noiseGate) * _sensitivity);

            // Simulate lip shapes with bone movement
            // Upper lip moves up slightly for open vowels
            if (_upperLipBone != null)
            {
                float upMove = normalizedRms * _lipBoneMovement * 0.5f;
                _upperLipBone.localPosition = _upperLipBasePos + Vector3.up * upMove;
            }

            // Lower lip moves down for open sounds
            if (_lowerLipBone != null)
            {
                float downMove = normalizedRms * _lipBoneMovement;
                _lowerLipBone.localPosition = _lowerLipBasePos - Vector3.up * downMove;
            }

            // Mouth corners for EE-like (wide) vs OH-like (narrow) based on ZCR
            if (_leftMouthCorner != null && _rightMouthCorner != null)
            {
                // Higher ZCR = wider mouth (EE), lower = narrower (OH)
                float wideAmount = (zcr - 0.3f) * normalizedRms * _lipBoneMovement;
                _leftMouthCorner.localPosition = _leftCornerBasePos + Vector3.left * wideAmount;
                _rightMouthCorner.localPosition = _rightCornerBasePos + Vector3.right * wideAmount;
            }
        }

        /// <summary>
        /// Reset lip bones to base positions
        /// </summary>
        private void ResetLipBones()
        {
            if (_upperLipBone != null)
                _upperLipBone.localPosition = _upperLipBasePos;
            if (_lowerLipBone != null)
                _lowerLipBone.localPosition = _lowerLipBasePos;
            if (_leftMouthCorner != null)
                _leftMouthCorner.localPosition = _leftCornerBasePos;
            if (_rightMouthCorner != null)
                _rightMouthCorner.localPosition = _rightCornerBasePos;
        }

        /// <summary>
        /// Select viseme based on RMS (volume) and ZCR (frequency content)
        /// with randomized variation for natural movement
        /// </summary>
        private VisemeType SelectVisemeFromAudio(float rms, float zcr)
        {
            // Below noise gate = closed mouth
            if (rms < _noiseGate)
                return VisemeType.None;

            float normalizedRms = Mathf.Clamp01((rms - _noiseGate) * _sensitivity);

            // MBP pulse injection
            float timeSinceLastMbp = Time.time - _lastMbpPulseTime;
            if (timeSinceLastMbp > _nextMbpPulseInterval)
            {
                // Check for plosive-like conditions (low-ish RMS burst)
                if (rms > 0.03f && rms < 0.12f && _visemeIndices[VisemeType.MBP] >= 0)
                {
                    _lastMbpPulseTime = Time.time;
                    _nextMbpPulseInterval = Random.Range(_mbpPulseMin, _mbpPulseMax);
                    return VisemeType.MBP;
                }
            }

            // High ZCR + moderate RMS = fricatives (SS group: s, z, ch, sh)
            if (zcr > 0.55f && normalizedRms < 0.5f)
            {
                // Choose between available fricative visemes
                List<VisemeType> fricatives = new List<VisemeType>();
                if (_visemeIndices[VisemeType.SS] >= 0) fricatives.Add(VisemeType.SS);
                if (_visemeIndices[VisemeType.CH] >= 0) fricatives.Add(VisemeType.CH);
                if (_visemeIndices[VisemeType.TH] >= 0) fricatives.Add(VisemeType.TH);

                if (fricatives.Count > 0)
                    return fricatives[Random.Range(0, fricatives.Count)];

                if (_visemeIndices[VisemeType.EE] >= 0)
                    return VisemeType.EE; // EE as fallback for fricatives
            }

            // Moderate ZCR + moderate RMS = FV group (f, v, th)
            if (zcr > 0.4f && zcr <= 0.55f && normalizedRms < 0.4f)
            {
                if (_visemeIndices[VisemeType.FV] >= 0)
                    return VisemeType.FV;
            }

            // Vowel selection based on RMS intensity and ZCR

            // High volume = open mouth vowels
            if (normalizedRms > 0.55f)
            {
                // Mix between AA and OH based on brightness
                if (_audioBrightness > 0.5f && _visemeIndices[VisemeType.AA] >= 0)
                    return VisemeType.AA;
                if (_visemeIndices[VisemeType.OH] >= 0 && Random.value > 0.6f)
                    return VisemeType.OH;
                if (_visemeIndices[VisemeType.AA] >= 0)
                    return VisemeType.AA;
            }

            // Medium volume
            if (normalizedRms > 0.25f)
            {
                // Higher ZCR = brighter vowels (EE, IH)
                if (zcr > 0.35f)
                {
                    List<VisemeType> brightVowels = new List<VisemeType>();
                    if (_visemeIndices[VisemeType.EE] >= 0) brightVowels.Add(VisemeType.EE);
                    if (_visemeIndices[VisemeType.IH] >= 0) brightVowels.Add(VisemeType.IH);

                    if (brightVowels.Count > 0)
                        return brightVowels[Random.Range(0, brightVowels.Count)];
                }

                // Lower ZCR = rounder vowels (OH, OU)
                List<VisemeType> roundVowels = new List<VisemeType>();
                if (_visemeIndices[VisemeType.OH] >= 0) roundVowels.Add(VisemeType.OH);
                if (_visemeIndices[VisemeType.OU] >= 0) roundVowels.Add(VisemeType.OU);

                if (roundVowels.Count > 0)
                    return roundVowels[Random.Range(0, roundVowels.Count)];

                if (_visemeIndices[VisemeType.AA] >= 0)
                    return VisemeType.AA;
            }

            // Low volume = partial open
            if (_visemeIndices[VisemeType.AA] >= 0)
                return VisemeType.AA;

            // If we have EE but not AA, use EE as primary
            if (_visemeIndices[VisemeType.EE] >= 0)
                return VisemeType.EE;

            return VisemeType.None;
        }

        /// <summary>
        /// Smooth viseme weight transitions with attack/release
        /// </summary>
        private void UpdateVisemeWeights(VisemeType target, float rms)
        {
            float normalizedRms = Mathf.Clamp01((rms - _noiseGate) * _sensitivity);

            foreach (VisemeType v in System.Enum.GetValues(typeof(VisemeType)))
            {
                if (v == VisemeType.None) continue;
                if (!_visemeIndices.ContainsKey(v) || _visemeIndices[v] < 0) continue;

                float maxWeight = _visemeMaxWeights.ContainsKey(v) ? _visemeMaxWeights[v] : _maxBlendshapeWeight;
                float targetWeight = 0f;

                if (v == target)
                {
                    // Target viseme gets weight based on RMS, capped by type
                    targetWeight = normalizedRms * maxWeight;
                }

                // Smooth with attack/release
                float currentWeight = _visemeWeights[v];
                if (targetWeight > currentWeight)
                {
                    // Attack
                    float attackRate = 1f / Mathf.Max(_visemeAttack, 0.01f);
                    currentWeight = Mathf.MoveTowards(currentWeight, targetWeight, attackRate * Time.deltaTime * _maxBlendshapeWeight);
                }
                else
                {
                    // Release
                    float releaseRate = 1f / Mathf.Max(_visemeRelease, 0.01f);
                    currentWeight = Mathf.MoveTowards(currentWeight, targetWeight, releaseRate * Time.deltaTime * _maxBlendshapeWeight);
                }

                _visemeWeights[v] = currentWeight;

                // Apply to blendshape
                _faceRenderer.SetBlendShapeWeight(_visemeIndices[v], currentWeight);
            }
        }

        /// <summary>
        /// Reset all viseme weights to zero
        /// </summary>
        private void ResetVisemeWeights()
        {
            if (_faceRenderer == null) return;

            foreach (VisemeType v in System.Enum.GetValues(typeof(VisemeType)))
            {
                if (v == VisemeType.None) continue;

                _visemeWeights[v] = 0f;

                if (_visemeIndices.ContainsKey(v) && _visemeIndices[v] >= 0)
                {
                    _faceRenderer.SetBlendShapeWeight(_visemeIndices[v], 0f);
                }
            }

            _currentViseme = VisemeType.None;
            _targetViseme = VisemeType.None;
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
            if (_hasVisemes)
            {
                ResetVisemeWeights();
            }
            if (_hasLipBones)
            {
                ResetLipBones();
            }
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

        [ContextMenu("List Viseme Map")]
        private void ListVisemeMap()
        {
            Debug.Log("[TTS-Lips] === Viseme Map ===");
            Debug.Log($"  Visemes Enabled: {_enableVisemes}");
            Debug.Log($"  Has Visemes: {_hasVisemes}");
            Debug.Log($"  Has Lip Bones: {_hasLipBones}");

            foreach (VisemeType v in System.Enum.GetValues(typeof(VisemeType)))
            {
                if (v == VisemeType.None) continue;

                int idx = _visemeIndices.ContainsKey(v) ? _visemeIndices[v] : -1;
                string bsName = "";
                if (idx >= 0 && _faceRenderer != null && _faceRenderer.sharedMesh != null)
                {
                    bsName = _faceRenderer.sharedMesh.GetBlendShapeName(idx);
                }

                float maxW = _visemeMaxWeights.ContainsKey(v) ? _visemeMaxWeights[v] : 100f;
                Debug.Log($"  {v}: index={idx} name={bsName} maxWeight={maxW}");
            }
        }

        [ContextMenu("Test Viseme AA (Open)")]
        private void TestVisemeAA()
        {
            if (_visemeIndices.ContainsKey(VisemeType.AA) && _visemeIndices[VisemeType.AA] >= 0)
            {
                ResetVisemeWeights();
                float w = _visemeMaxWeights.ContainsKey(VisemeType.AA) ? _visemeMaxWeights[VisemeType.AA] : _maxBlendshapeWeight;
                _faceRenderer.SetBlendShapeWeight(_visemeIndices[VisemeType.AA], w);
                Debug.Log($"[TTS-Lips] Test: AA (open mouth) weight={w}");
            }
            else
            {
                Debug.LogWarning("[TTS-Lips] Test: AA viseme not found");
            }
        }

        [ContextMenu("Test Viseme EE (Wide)")]
        private void TestVisemeEE()
        {
            if (_visemeIndices.ContainsKey(VisemeType.EE) && _visemeIndices[VisemeType.EE] >= 0)
            {
                ResetVisemeWeights();
                float w = _visemeMaxWeights.ContainsKey(VisemeType.EE) ? _visemeMaxWeights[VisemeType.EE] : _maxBlendshapeWeight;
                _faceRenderer.SetBlendShapeWeight(_visemeIndices[VisemeType.EE], w);
                Debug.Log($"[TTS-Lips] Test: EE (wide smile) weight={w}");
            }
            else
            {
                Debug.LogWarning("[TTS-Lips] Test: EE viseme not found");
            }
        }

        [ContextMenu("Test Viseme OH (Round)")]
        private void TestVisemeOH()
        {
            if (_visemeIndices.ContainsKey(VisemeType.OH) && _visemeIndices[VisemeType.OH] >= 0)
            {
                ResetVisemeWeights();
                float w = _visemeMaxWeights.ContainsKey(VisemeType.OH) ? _visemeMaxWeights[VisemeType.OH] : _maxBlendshapeWeight;
                _faceRenderer.SetBlendShapeWeight(_visemeIndices[VisemeType.OH], w);
                Debug.Log($"[TTS-Lips] Test: OH (round lips) weight={w}");
            }
            else
            {
                Debug.LogWarning("[TTS-Lips] Test: OH viseme not found");
            }
        }

        [ContextMenu("Test Viseme MBP (Closed)")]
        private void TestVisemeMBP()
        {
            if (_visemeIndices.ContainsKey(VisemeType.MBP) && _visemeIndices[VisemeType.MBP] >= 0)
            {
                ResetVisemeWeights();
                float w = _visemeMaxWeights.ContainsKey(VisemeType.MBP) ? _visemeMaxWeights[VisemeType.MBP] : _maxBlendshapeWeight;
                _faceRenderer.SetBlendShapeWeight(_visemeIndices[VisemeType.MBP], w);
                Debug.Log($"[TTS-Lips] Test: MBP (closed lips) weight={w}");
            }
            else
            {
                Debug.LogWarning("[TTS-Lips] Test: MBP viseme not found");
            }
        }

        [ContextMenu("Test Viseme FV (Lip Bite)")]
        private void TestVisemeFV()
        {
            if (_visemeIndices.ContainsKey(VisemeType.FV) && _visemeIndices[VisemeType.FV] >= 0)
            {
                ResetVisemeWeights();
                float w = _visemeMaxWeights.ContainsKey(VisemeType.FV) ? _visemeMaxWeights[VisemeType.FV] : _maxBlendshapeWeight;
                _faceRenderer.SetBlendShapeWeight(_visemeIndices[VisemeType.FV], w);
                Debug.Log($"[TTS-Lips] Test: FV (lip bite) weight={w}");
            }
            else
            {
                Debug.LogWarning("[TTS-Lips] Test: FV viseme not found");
            }
        }

        [ContextMenu("Reset All Visemes")]
        private void TestResetVisemes()
        {
            ResetVisemeWeights();
            ResetLipBones();
            Debug.Log("[TTS-Lips] Test: All visemes and lip bones reset");
        }
#endif
    }
}
