using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Robust TTS lip sync driver that:
    /// - Continuously drives mouth blendshapes during ENTIRE audio playback
    /// - Runs in LateUpdate to override Animator
    /// - Falls back to procedural animation when RMS is ~0
    /// - Falls back to jaw bone if no blendshapes found
    /// - Prints diagnostics every second
    /// </summary>
    public class TtsLipSyncRuntime : MonoBehaviour
    {
        private static TtsLipSyncRuntime _instance;
        public static TtsLipSyncRuntime Instance => _instance;

        [Header("References")]
        [Tooltip("Root transform containing character meshes. Auto-finds if null.")]
        public Transform characterRoot;

        [Tooltip("The AudioSource that plays TTS. Set via AttachAudioSource().")]
        public AudioSource ttsSource;

        [Header("Tuning")]
        [Range(1f, 50f)] public float gain = 25f;
        [Range(0f, 0.1f)] public float rmsFloor = 0.01f;
        [Range(0f, 100f)] public float maxOpenWeight = 80f;
        [Range(1f, 30f)] public float attack = 14f;
        [Range(1f, 30f)] public float release = 10f;

        [Header("Procedural Fallback")]
        [Tooltip("When RMS is ~0 but audio playing, use this base open amount")]
        [Range(0f, 100f)] public float fallbackOpenWeight = 35f;
        [Range(1f, 20f)] public float fallbackWiggleHz = 6f;

        [Header("Alternating Visemes")]
        [Tooltip("Enable switching between viseme shapes for variety")]
        public bool enableVisemeVariation = true;
        [Range(1f, 10f)] public float visemeSwitchHz = 3.5f;

        [Header("Teeth Driver")]
        [Tooltip("Optional teeth driver. If null, auto-finds TtsTeethDriver.Instance.")]
        [SerializeField] private TtsTeethDriver teethDriver;

        [Header("Animator Control")]
        [Tooltip("Disable Animator during TTS to prevent jaw overwrite. Auto-finds if null.")]
        [SerializeField] private Animator characterAnimator;
        [Tooltip("Whether to disable Animator during speech")]
        public bool disableAnimatorDuringSpeech = true;

        [Header("Diagnostics")]
        public bool verbose = true;

        const int SampleSize = 1024;
        float[] _samples;

        class MouthTarget
        {
            public SkinnedMeshRenderer renderer;
            public int openIndex = -1;
            public int altIndexA = -1;
            public int altIndexB = -1;
            public string openName;
            public string altNameA;
            public string altNameB;
        }

        readonly List<MouthTarget> _targets = new List<MouthTarget>();
        Transform _jawBone;
        Quaternion _jawBaseRotation;

        float _open;        // smoothed current
        float _openTarget;
        float _logTimer;
        bool _wasPlaying;

        // Disabled controllers to re-enable when done
        readonly List<MonoBehaviour> _disabledControllers = new List<MonoBehaviour>();

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"[TTS-LipSync] Duplicate instance, destroying {gameObject.name}");
                Destroy(this);
                return;
            }
            _instance = this;

            _samples = new float[SampleSize];

            // Auto-find character root if not set
            if (characterRoot == null)
            {
                characterRoot = FindCharacterRoot();
            }

            if (characterRoot != null)
            {
                BuildTargets();
                FindJawBone();
            }

            if (verbose)
            {
                Debug.Log($"[TTS-LipSync] Awake. characterRoot={(characterRoot?.name ?? "NULL")} targets={_targets.Count} jawBone={(_jawBone?.name ?? "NULL")}");
            }
        }

        void Start()
        {
            // Try to auto-find TTS AudioSource from TtsPlayer
            if (ttsSource == null && TtsPlayer.Instance != null)
            {
                ttsSource = TtsPlayer.Instance.GetComponent<AudioSource>();
                if (ttsSource != null && verbose)
                {
                    Debug.Log($"[TTS-LipSync] Auto-bound to TtsPlayer AudioSource");
                }
            }

            // Auto-find Animator on character if not set
            if (characterAnimator == null && characterRoot != null)
            {
                characterAnimator = characterRoot.GetComponent<Animator>();
                if (characterAnimator == null)
                {
                    characterAnimator = characterRoot.GetComponentInChildren<Animator>();
                }
                if (characterAnimator != null && verbose)
                {
                    Debug.Log($"[TTS-LipSync] Auto-found Animator on {characterAnimator.gameObject.name}");
                }
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
                    if (verbose) Debug.Log($"[TTS-LipSync] Found character root at {path}");
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
                            if (verbose) Debug.Log($"[TTS-LipSync] Found character root via CC_Base_Body: {t.name}");
                            return t;
                        }
                        t = t.parent;
                    }
                    return smr.transform.root;
                }
            }

            Debug.LogWarning("[TTS-LipSync] Could not auto-find character root");
            return null;
        }

        /// <summary>
        /// Attach the audio source that plays TTS audio.
        /// Call this before starting playback.
        /// </summary>
        public void AttachAudioSource(AudioSource src)
        {
            ttsSource = src;
            if (verbose)
            {
                if (src == null)
                {
                    Debug.LogWarning("[TTS-LipSync] AttachAudioSource(NULL) called!");
                }
                else
                {
                    string clipName = src.clip != null ? src.clip.name : "NULL";
                    float clipLen = src.clip != null ? src.clip.length : 0f;
                    string mixerGroup = src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "NULL";
                    Debug.Log($"[TTS-LipSync] Attached AudioSource={src.gameObject.name} clip={clipName} length={clipLen:F2}s mixer={mixerGroup}");
                }
            }

            // Disable competing controllers when TTS starts
            DisableCompetingControllers();
        }

        void BuildTargets()
        {
            _targets.Clear();

            if (characterRoot == null) return;

            var renderers = characterRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (verbose) Debug.Log($"[TTS-LipSync] Scanning {renderers.Length} SkinnedMeshRenderers...");

            foreach (var r in renderers)
            {
                if (r == null || r.sharedMesh == null) continue;

                // Skip teeth, tongue, eyes
                string rName = r.name.ToLower();
                if (rName.Contains("teeth") || rName.Contains("tongue") ||
                    rName.Contains("eye") || rName.Contains("lash") ||
                    rName.Contains("brow") || rName.Contains("hair"))
                {
                    continue;
                }

                var mesh = r.sharedMesh;
                int count = mesh.blendShapeCount;
                if (count <= 0) continue;

                // Build name->index map
                var names = new List<(string name, string key, int idx)>(count);
                for (int i = 0; i < count; i++)
                {
                    string n = mesh.GetBlendShapeName(i);
                    string k = Normalize(n);
                    names.Add((n, k, i));
                }

                // Pick OPEN shape (primary mouth opening)
                int openIdx = PickBest(names, new[] {
                    "visemeaa", "viseme_aa", "v_aa",
                    "mouthopen", "mouth_open", "jawopen", "jaw_open",
                    "a01_jaw_open", "aa", "open"
                });

                // Pick alternating shapes for variation
                int altA = PickBest(names, new[] {
                    "visemeoh", "viseme_oh", "v_oh", "oh", "ou", "oo", "o01_mouth_o"
                }, exclude: openIdx);

                int altB = PickBest(names, new[] {
                    "visemeee", "viseme_ee", "v_ee", "visemeih", "viseme_ih",
                    "ee", "ih", "fv", "mbp", "e01_mouth_smile"
                }, exclude: openIdx);

                // If no open shape found, skip this renderer
                if (openIdx < 0) continue;

                var t = new MouthTarget
                {
                    renderer = r,
                    openIndex = openIdx,
                    altIndexA = altA,
                    altIndexB = altB,
                    openName = names.First(x => x.idx == openIdx).name,
                    altNameA = altA >= 0 ? names.First(x => x.idx == altA).name : null,
                    altNameB = altB >= 0 ? names.First(x => x.idx == altB).name : null,
                };
                _targets.Add(t);

                // Ensure updates even if offscreen
                r.updateWhenOffscreen = true;

                if (verbose)
                {
                    Debug.Log($"[TTS-LipSync] Target: {r.name} open={t.openName}[{t.openIndex}] altA={(t.altNameA ?? "NONE")}[{t.altIndexA}] altB={(t.altNameB ?? "NONE")}[{t.altIndexB}]");
                }
            }

            if (_targets.Count == 0 && verbose)
            {
                Debug.LogWarning("[TTS-LipSync] No mouth/viseme blendshapes found! Will use jaw bone fallback only.");
            }
        }

        void FindJawBone()
        {
            _jawBone = null;
            if (characterRoot == null) return;

            var allTransforms = characterRoot.GetComponentsInChildren<Transform>(true);
            foreach (var tr in allTransforms)
            {
                if (tr == null) continue;
                string k = Normalize(tr.name);
                if (k.Contains("jawroot") || k.Contains("jaw"))
                {
                    _jawBone = tr;
                    _jawBaseRotation = _jawBone.localRotation;
                    if (verbose)
                    {
                        Debug.Log($"[TTS-LipSync] Found jaw bone: {tr.name}");
                    }
                    break;
                }
            }
        }

        void DisableCompetingControllers()
        {
            _disabledControllers.Clear();

            // Find and disable other lip sync controllers
            var lipSyncControllers = FindObjectsOfType<LipSyncController>();
            foreach (var lsc in lipSyncControllers)
            {
                if (lsc != null && lsc.enabled)
                {
                    lsc.enabled = false;
                    _disabledControllers.Add(lsc);
                    if (verbose) Debug.Log($"[TTS-LipSync] Disabled LipSyncController on {lsc.gameObject.name}");
                }
            }

            // Find TtsMouthController and disable it
            var mouthControllers = FindObjectsOfType<TtsMouthController>();
            foreach (var mc in mouthControllers)
            {
                if (mc != null && mc.enabled)
                {
                    mc.enabled = false;
                    _disabledControllers.Add(mc);
                    if (verbose) Debug.Log($"[TTS-LipSync] Disabled TtsMouthController on {mc.gameObject.name}");
                }
            }
        }

        void EnableCompetingControllers()
        {
            foreach (var mb in _disabledControllers)
            {
                if (mb != null)
                {
                    mb.enabled = true;
                    if (verbose) Debug.Log($"[TTS-LipSync] Re-enabled {mb.GetType().Name} on {mb.gameObject.name}");
                }
            }
            _disabledControllers.Clear();
        }

        void LateUpdate()
        {
            bool playing = ttsSource != null && ttsSource.isPlaying && ttsSource.clip != null;

            // Detect state transitions
            if (playing && !_wasPlaying)
            {
                // Started playing
                if (verbose)
                {
                    Debug.Log($"[TTS-LipSync] Playback STARTED clip={ttsSource.clip.name} length={ttsSource.clip.length:F2}s");
                }
                DisableCompetingControllers();

                // Disable Animator to prevent jaw/face overwrite
                if (disableAnimatorDuringSpeech && characterAnimator != null)
                {
                    characterAnimator.enabled = false;
                    if (verbose) Debug.Log($"[TTS-LipSync] Disabled Animator on {characterAnimator.gameObject.name}");
                }

                _logTimer = 0f;
            }
            else if (!playing && _wasPlaying)
            {
                // Stopped playing
                if (verbose)
                {
                    Debug.Log("[TTS-LipSync] Playback STOPPED");
                }
                EnableCompetingControllers();

                // Re-enable Animator
                if (disableAnimatorDuringSpeech && characterAnimator != null)
                {
                    characterAnimator.enabled = true;
                    if (verbose) Debug.Log($"[TTS-LipSync] Re-enabled Animator on {characterAnimator.gameObject.name}");
                }
            }
            _wasPlaying = playing;

            if (playing)
            {
                float rms = SampleRms(ttsSource);

                // Calculate desired open amount
                float desired = Mathf.Clamp01((rms - rmsFloor) * gain);

                // If RMS is basically zero (common with some routing), use procedural fallback
                if (desired < 0.05f)
                {
                    // Procedural fallback: sine wave wiggle
                    float wiggle = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f * fallbackWiggleHz);
                    desired = (fallbackOpenWeight / 100f) * (0.5f + 0.5f * wiggle);
                }

                _openTarget = Mathf.Clamp01(desired);
                _open = SmoothTo(_open, _openTarget, attack, release);

                // Apply to blendshapes
                ApplyMouth(_open * (maxOpenWeight / 100f), true);

                // Apply to jaw as supplementary motion
                ApplyJaw(_open);

                // Apply to teeth (uses same open value for sync)
                ApplyTeeth(_open, true);

                // 1Hz diagnostics
                _logTimer += Time.deltaTime;
                if (verbose && _logTimer >= 1f)
                {
                    _logTimer = 0f;
                    float time = 0f;
                    float clipLen = 0f;
                    try
                    {
                        // Use timeSamples to avoid warnings with streaming audio
                        if (ttsSource.clip != null)
                        {
                            clipLen = ttsSource.clip.length;
                            time = (float)ttsSource.timeSamples / ttsSource.clip.frequency;
                        }
                    }
                    catch { }

                    Debug.Log($"[TTS-LipSync] playing=true t={time:F2}/{clipLen:F2} rms={rms:F4} open={_open:F2} targets={_targets.Count}");
                }
            }
            else
            {
                // Not playing - smoothly return to neutral
                _openTarget = 0f;
                _open = SmoothTo(_open, _openTarget, attack, release);
                ApplyMouth(_open * (maxOpenWeight / 100f), false);
                ApplyJaw(_open);
                ApplyTeeth(_open, false);
            }
        }

        float SampleRms(AudioSource src)
        {
            if (src == null) return 0f;
            try
            {
                src.GetOutputData(_samples, 0);
                double sum = 0.0;
                for (int i = 0; i < _samples.Length; i++)
                {
                    sum += _samples[i] * _samples[i];
                }
                return (float)Math.Sqrt(sum / _samples.Length);
            }
            catch (Exception e)
            {
                if (verbose) Debug.LogWarning($"[TTS-LipSync] GetOutputData failed: {e.GetType().Name}: {e.Message}");
                return 0f;
            }
        }

        void ApplyMouth(float openNormalized, bool speaking)
        {
            float openWeight = openNormalized * 100f;

            foreach (var t in _targets)
            {
                if (t.renderer == null) continue;

                // Primary open shape
                SetBlendShape(t.renderer, t.openIndex, openWeight);

                // Alternating viseme shapes for variation
                if (speaking && enableVisemeVariation)
                {
                    float phase = Mathf.Repeat(Time.time * visemeSwitchHz, 1f);
                    float a = (phase < 0.5f) ? 1f : 0f;
                    float b = 1f - a;

                    if (t.altIndexA >= 0)
                    {
                        SetBlendShape(t.renderer, t.altIndexA, openWeight * 0.6f * a);
                    }
                    if (t.altIndexB >= 0)
                    {
                        SetBlendShape(t.renderer, t.altIndexB, openWeight * 0.6f * b);
                    }
                }
                else
                {
                    // Release alternating shapes
                    if (t.altIndexA >= 0) SetBlendShape(t.renderer, t.altIndexA, 0f);
                    if (t.altIndexB >= 0) SetBlendShape(t.renderer, t.altIndexB, 0f);
                }
            }
        }

        void ApplyJaw(float openNormalized)
        {
            if (_jawBone == null) return;

            // If we have blendshape targets, use minimal jaw supplementary motion
            // If no blendshapes, use larger jaw motion as primary animation
            float maxAngle = _targets.Count > 0 ? 8f : 15f;
            float angle = openNormalized * maxAngle;

            _jawBone.localRotation = _jawBaseRotation * Quaternion.AngleAxis(angle, Vector3.right);
        }

        void ApplyTeeth(float openNormalized, bool speaking)
        {
            // Use serialized reference or singleton
            TtsTeethDriver driver = teethDriver;
            if (driver == null)
            {
                driver = TtsTeethDriver.Instance;
            }

            if (driver != null)
            {
                driver.SetOpen(openNormalized, speaking);
            }
        }

        static void SetBlendShape(SkinnedMeshRenderer r, int idx, float weight)
        {
            if (r == null || idx < 0) return;
            r.SetBlendShapeWeight(idx, Mathf.Clamp(weight, 0f, 100f));
        }

        static float SmoothTo(float current, float target, float atk, float rel)
        {
            float speed = (target > current) ? atk : rel;
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
        }

        static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        static int PickBest(List<(string name, string key, int idx)> items, IEnumerable<string> keywords, int exclude = -1)
        {
            int best = -1;
            int bestScore = -1;

            foreach (var it in items)
            {
                if (it.idx == exclude) continue;

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

        void OnDestroy()
        {
            EnableCompetingControllers();

            // Reset blendshapes
            foreach (var t in _targets)
            {
                if (t.renderer != null)
                {
                    SetBlendShape(t.renderer, t.openIndex, 0f);
                    SetBlendShape(t.renderer, t.altIndexA, 0f);
                    SetBlendShape(t.renderer, t.altIndexB, 0f);
                }
            }

            // Reset jaw
            if (_jawBone != null)
            {
                _jawBone.localRotation = _jawBaseRotation;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// Force re-scan of character for blendshapes.
        /// Call this after character is loaded/changed.
        /// </summary>
        public void Rescan()
        {
            if (characterRoot == null)
            {
                characterRoot = FindCharacterRoot();
            }

            if (characterRoot != null)
            {
                BuildTargets();
                FindJawBone();
            }

            if (verbose)
            {
                Debug.Log($"[TTS-LipSync] Rescan complete. targets={_targets.Count} jawBone={(_jawBone?.name ?? "NULL")}");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (_instance != null) return;

            // Check if one already exists
            TtsLipSyncRuntime existing = FindObjectOfType<TtsLipSyncRuntime>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            // Create new instance
            GameObject go = new GameObject("_TtsLipSyncRuntime");
            _instance = go.AddComponent<TtsLipSyncRuntime>();
            DontDestroyOnLoad(go);
            Debug.Log("[TTS-LipSync] Auto-created runtime lip sync driver");
        }

#if UNITY_EDITOR
        [ContextMenu("Test Open Mouth")]
        private void TestOpen()
        {
            _open = 1f;
            ApplyMouth(maxOpenWeight / 100f, true);
            ApplyJaw(1f);
            Debug.Log("[TTS-LipSync] Test: Mouth OPEN");
        }

        [ContextMenu("Test Close Mouth")]
        private void TestClose()
        {
            _open = 0f;
            ApplyMouth(0f, false);
            ApplyJaw(0f);
            Debug.Log("[TTS-LipSync] Test: Mouth CLOSED");
        }

        [ContextMenu("Rescan Character")]
        private void EditorRescan()
        {
            Rescan();
        }
#endif
    }
}
