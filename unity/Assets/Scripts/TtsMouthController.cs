using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ShadowingTutor
{
    /// <summary>
    /// TTS-driven mouth animation controller with robust face mesh selection.
    /// Properly excludes teeth/tongue/eye meshes and finds the correct face renderer.
    /// Drives mouth animation in LateUpdate to override other controllers.
    /// Now supports multiple lip viseme blendshapes (AA, EE, OH, MBP, FV) with audio-driven selection.
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

        [Header("Viseme Settings")]
        [Tooltip("Enable multi-viseme lip animation (AA/EE/OH/MBP/FV)")]
        [SerializeField] private bool _enableVisemes = true;
        [Tooltip("Attack time for viseme transitions (seconds)")]
        [SerializeField] private float _visemeAttack = 0.06f;
        [Tooltip("Release time for viseme transitions (seconds)")]
        [SerializeField] private float _visemeRelease = 0.12f;
        [Tooltip("Interval for viseme switching (seconds)")]
        [SerializeField] private float _visemeSwitchInterval = 0.12f;
        [Tooltip("Interval for MBP consonant pulses (seconds)")]
        [SerializeField] private float _mbpPulseInterval = 0.8f;

        [Header("Controller Conflict Resolution")]
        [Tooltip("Disable competing LipSync controllers while speaking")]
        [SerializeField] private bool _overrideWhileSpeaking = true;

        // Viseme groups
        public enum VisemeType { None, AA, EE, OH, MBP, FV, SS }

        // Viseme blendshape indices (-1 if not found)
        private Dictionary<VisemeType, int> _visemeIndices = new Dictionary<VisemeType, int>();
        private Dictionary<VisemeType, float> _visemeWeights = new Dictionary<VisemeType, float>();
        private bool _hasVisemes = false;

        // Runtime state
        private float _currentOpenAmount = 0f;
        private float _targetOpenAmount = 0f;
        private Quaternion _jawBaseRotation;
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
        private float _lastZcr = 0f;
        private float _lastRms = 0f;

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

        // Viseme blendshape patterns for each type
        // Supports: Reallusion/CC, Oculus/OVR, and generic naming conventions
        private static readonly Dictionary<VisemeType, string[]> VisemePatterns = new Dictionary<VisemeType, string[]>
        {
            // AA - open mouth (ah, a)
            { VisemeType.AA, new[] { "viseme_aa", "viseme_a", "v_aa", "mouth_open", "a01_jaw_open" } },
            // EE - wide smile (ee, i)
            { VisemeType.EE, new[] { "viseme_ee", "viseme_e", "viseme_i", "v_ee", "v_ih", "e01_mouth_smile" } },
            // OH - round lips (oh, o, u)
            { VisemeType.OH, new[] { "viseme_oh", "viseme_o", "viseme_u", "v_oh", "v_ou", "o01_mouth_o" } },
            // MBP - closed lips (m, b, p)
            { VisemeType.MBP, new[] { "viseme_pp", "viseme_ff", "v_mbp", "v_pp", "v_bb", "m01_lips_close" } },
            // FV - lower lip bite (f, v)
            { VisemeType.FV, new[] { "viseme_ff", "viseme_th", "v_fv", "v_f", "f01_lower_lip_in" } },
            // SS - teeth together (s, z, ch, sh)
            { VisemeType.SS, new[] { "viseme_ss", "viseme_ch", "v_ss", "v_ch", "s01_teeth_close" } }
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
            // Get RMS from TTS audio source (this also fills _audioSamples for ZCR)
            float rms = GetCurrentRms();
            _lastRms = rms;

            // Calculate ZCR for viseme selection
            float zcr = GetZeroCrossingRate();
            _lastZcr = zcr;

            // Determine speaking state
            bool speaking = _ttsAudioSource != null && _ttsAudioSource.isPlaying && rms > _noiseGate;

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
                // Check if enough time has passed for viseme switch
                float timeSinceLastSwitch = Time.time - _lastVisemeSwitchTime;
                if (timeSinceLastSwitch >= _visemeSwitchInterval)
                {
                    _targetViseme = SelectVisemeFromAudio(rms, zcr);

                    if (_targetViseme != _currentViseme)
                    {
                        _currentViseme = _targetViseme;
                        _lastVisemeSwitchTime = Time.time;
                    }
                }

                // Update viseme weights with smooth transitions
                UpdateVisemeWeights(_currentViseme, rms);
            }
            else if (!speaking && _hasVisemes)
            {
                // Release all visemes when not speaking
                UpdateVisemeWeights(VisemeType.None, 0f);
            }

            // Fallback jaw-only animation (or additional jaw movement)
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

        private void OnSpeakingStateChanged(bool speaking, float rms)
        {
            if (speaking)
            {
                float weight = Mathf.Clamp01((rms - _noiseGate) * _sensitivity) * _maxBlendshapeWeight;
                string visemeStatus = _hasVisemes ? "ENABLED" : "jaw-only";
                Debug.Log($"[TTS-Mouth] Speaking=TRUE rms={rms:F4} weight={weight:F1} visemes={visemeStatus}");

                // Reset viseme timing
                _lastVisemeSwitchTime = Time.time;
                _lastMbpPulseTime = Time.time - _mbpPulseInterval * 0.5f; // Allow early MBP

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

            // Wire viseme blendshapes for lip animation
            if (_enableVisemes)
            {
                WireVisemeBlendshapes();
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

        /// <summary>
        /// Wire multiple viseme blendshapes for lip animation (AA, EE, OH, MBP, FV, SS)
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

            // Need at least AA + one other for basic lip movement
            _hasVisemes = _visemeIndices[VisemeType.AA] >= 0 && foundCount >= 2;

            // Log viseme map
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[TTS-Lips] VisemeMap: ");
            bool first = true;
            foreach (var kvp in _visemeIndices)
            {
                if (kvp.Key == VisemeType.None) continue;
                if (!first) sb.Append(", ");
                first = false;
                sb.Append($"{kvp.Key}={kvp.Value}");
            }
            Debug.Log(sb.ToString());

            if (_hasVisemes)
            {
                Debug.Log($"[TTS-Lips] Viseme lip animation ENABLED ({foundCount} visemes found)");
            }
            else
            {
                Debug.Log("[TTS-Lips] Viseme lip animation DISABLED (using jaw-only fallback)");
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
        /// Select viseme based on RMS (volume) and ZCR (frequency content)
        /// </summary>
        private VisemeType SelectVisemeFromAudio(float rms, float zcr)
        {
            // Below noise gate = closed mouth
            if (rms < _noiseGate)
                return VisemeType.None;

            // High ZCR + moderate RMS = fricatives (SS group: s, z, ch, sh)
            if (zcr > 0.6f && rms < 0.15f)
            {
                if (_visemeIndices[VisemeType.SS] >= 0)
                    return VisemeType.SS;
                if (_visemeIndices[VisemeType.FV] >= 0)
                    return VisemeType.FV;
            }

            // Moderate ZCR + moderate RMS = FV group (f, v, th)
            if (zcr > 0.4f && zcr <= 0.6f && rms < 0.12f)
            {
                if (_visemeIndices[VisemeType.FV] >= 0)
                    return VisemeType.FV;
            }

            // Low RMS burst could be plosive start (MBP)
            // Also inject MBP pulses periodically for natural consonants
            float timeSinceLastMbp = Time.time - _lastMbpPulseTime;
            if (timeSinceLastMbp > _mbpPulseInterval && rms > 0.05f && rms < 0.1f)
            {
                _lastMbpPulseTime = Time.time;
                if (_visemeIndices[VisemeType.MBP] >= 0)
                    return VisemeType.MBP;
            }

            // Vowel selection based on RMS intensity
            float normalizedRms = Mathf.Clamp01((rms - _noiseGate) * _sensitivity);

            // High volume = open mouth (AA)
            if (normalizedRms > 0.6f)
            {
                if (_visemeIndices[VisemeType.AA] >= 0)
                    return VisemeType.AA;
            }

            // Medium volume with moderate ZCR = EE (brighter vowels)
            if (normalizedRms > 0.3f && zcr > 0.3f)
            {
                if (_visemeIndices[VisemeType.EE] >= 0)
                    return VisemeType.EE;
            }

            // Medium volume with low ZCR = OH (rounder vowels)
            if (normalizedRms > 0.3f && zcr <= 0.3f)
            {
                if (_visemeIndices[VisemeType.OH] >= 0)
                    return VisemeType.OH;
            }

            // Low volume = partial AA or keep previous
            if (_visemeIndices[VisemeType.AA] >= 0)
                return VisemeType.AA;

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
                if (_visemeIndices[v] < 0) continue;

                float targetWeight = 0f;

                if (v == target)
                {
                    // Target viseme gets full weight based on RMS
                    targetWeight = normalizedRms * _maxBlendshapeWeight;
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

            foreach (VisemeType v in System.Enum.GetValues(typeof(VisemeType)))
            {
                if (v == VisemeType.None) continue;

                int idx = _visemeIndices.ContainsKey(v) ? _visemeIndices[v] : -1;
                string bsName = "";
                if (idx >= 0 && _faceRenderer != null && _faceRenderer.sharedMesh != null)
                {
                    bsName = _faceRenderer.sharedMesh.GetBlendShapeName(idx);
                }

                Debug.Log($"  {v}: index={idx} name={bsName}");
            }
        }

        [ContextMenu("Test Viseme AA (Open)")]
        private void TestVisemeAA()
        {
            if (_visemeIndices.ContainsKey(VisemeType.AA) && _visemeIndices[VisemeType.AA] >= 0)
            {
                ResetVisemeWeights();
                _faceRenderer.SetBlendShapeWeight(_visemeIndices[VisemeType.AA], _maxBlendshapeWeight);
                Debug.Log($"[TTS-Lips] Test: AA (open mouth) weight={_maxBlendshapeWeight}");
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
                _faceRenderer.SetBlendShapeWeight(_visemeIndices[VisemeType.EE], _maxBlendshapeWeight);
                Debug.Log($"[TTS-Lips] Test: EE (wide smile) weight={_maxBlendshapeWeight}");
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
                _faceRenderer.SetBlendShapeWeight(_visemeIndices[VisemeType.OH], _maxBlendshapeWeight);
                Debug.Log($"[TTS-Lips] Test: OH (round lips) weight={_maxBlendshapeWeight}");
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
                _faceRenderer.SetBlendShapeWeight(_visemeIndices[VisemeType.MBP], _maxBlendshapeWeight);
                Debug.Log($"[TTS-Lips] Test: MBP (closed lips) weight={_maxBlendshapeWeight}");
            }
            else
            {
                Debug.LogWarning("[TTS-Lips] Test: MBP viseme not found");
            }
        }

        [ContextMenu("Reset All Visemes")]
        private void TestResetVisemes()
        {
            ResetVisemeWeights();
            Debug.Log("[TTS-Lips] Test: All visemes reset to 0");
        }
#endif
    }
}
