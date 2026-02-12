using System;
using System.Collections;
using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// TTS Player that fetches audio from backend and plays it.
    /// Provides events for playback state and audio data for lip sync.
    ///
    /// IMPORTANT: Does NOT use AudioSource.time to avoid warning:
    /// "Attempting to get 'time' on an audio source that has a resource that is not a clip will always return 0."
    ///
    /// Completion detection uses audioSource.isPlaying flag only.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class TtsPlayer : MonoBehaviour
    {
        private static TtsPlayer _instance;
        public static TtsPlayer Instance => _instance;

        [Header("Playback Settings")]
        [SerializeField] private int _defaultLoopCount = 1;
        [SerializeField] private float _loopDelay = 0.5f;

        [Header("Timeout Settings")]
        [SerializeField] private float _loadTimeoutSeconds = 15f;
        [SerializeField] private float _playbackTimeoutSeconds = 30f;

        private AudioSource _audioSource;
        private AudioClip _currentClip;
        private Coroutine _playbackCoroutine;
        private int _currentLoop = 0;
        private int _loopCount = 1;
        private bool _isPlaying = false;
        private bool _playbackComplete = false;
        private bool _loadComplete = false;
        private bool _hasError = false;
        private string _lastError = null;

        // Generation ID for cancellation tracking
        private int _ttsGenId = 0;

        // DSP-based playback tracking for robust completion detection
        private int _playId = 0;  // Incremented on every play/stop for cancellation
        private double _dspStart = 0;
        private double _expectedEnd = 0;

        // Verbose logging flag
        #if UNITY_EDITOR && DEVELOPMENT_BUILD
        private const bool DEV_VERBOSE = true;
        #else
        private const bool DEV_VERBOSE = false;
        #endif

        // Events
        public event Action OnLoadStart;
        public event Action OnLoadComplete;
        public event Action<string> OnLoadError;
        public event Action<int> OnLoopStart;
        public event Action<int> OnLoopComplete;
        public event Action OnPlaybackComplete;
        public event Action<float> OnAudioData;

        // Properties
        public bool IsPlaying => _isPlaying;
        public bool IsPlaybackComplete => _playbackComplete;
        public bool HasError => _hasError;
        public string LastError => _lastError;
        public int CurrentLoop => _currentLoop;
        public int TotalLoops => _loopCount;
        public AudioClip CurrentClip => _currentClip;
        public float CurrentRms { get; private set; }
        public int CurrentGenId => _ttsGenId;
        public int CurrentPlayId => _playId;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"[TtsPlayer] Duplicate instance detected! Existing={_instance.GetInstanceID()}, Destroying={GetInstanceID()}");
                Destroy(gameObject);
                return;
            }
            _instance = this;
            Debug.Log($"[TtsPlayer] Singleton initialized. InstanceID={GetInstanceID()}");
            _audioSource = GetComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;  // CRITICAL: Prevent infinite loop issues
            _loopCount = _defaultLoopCount;
        }

        private void Update()
        {
            if (_isPlaying && _audioSource.isPlaying)
            {
                CurrentRms = CalculateRms();
                OnAudioData?.Invoke(CurrentRms);
            }
            else if (_isPlaying && !_audioSource.isPlaying)
            {
                CurrentRms = 0f;
            }
        }

        /// <summary>
        /// Calculate RMS (Root Mean Square) of current audio for lip sync.
        /// </summary>
        private float CalculateRms()
        {
            // Guard against non-clip audio sources
            if (_currentClip == null || !_audioSource.isPlaying || _audioSource.clip == null)
                return 0f;

            try
            {
                float[] samples = new float[256];
                _audioSource.GetOutputData(samples, 0);

                float sum = 0f;
                foreach (float s in samples)
                {
                    sum += s * s;
                }

                return Mathf.Sqrt(sum / samples.Length);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Load and play TTS for given text with default settings.
        /// </summary>
        public void PlayTts(string text)
        {
            PlayTts(text, AppConfig.Instance?.SpeedProfile ?? 1, 1);
        }

        /// <summary>
        /// Load and play TTS for given text with specified speed profile.
        /// IMPORTANT: This is now "single-flight" - it will NOT stop currently playing audio.
        /// Call StopTts() explicitly if you need to interrupt.
        /// </summary>
        public void PlayTts(string text, int speedProfile, int loopCount = 1)
        {
            if (string.IsNullOrEmpty(text))
            {
                _hasError = true;
                _lastError = "Empty text";
                OnLoadError?.Invoke(_lastError);
                return;
            }

            // Increment generation ID for cancellation tracking
            _ttsGenId++;
            int myGenId = _ttsGenId;

            // CRITICAL FIX: Do NOT call StopTts() here - it causes race condition
            // where _playbackComplete is set to true before coroutine runs.
            // Instead, stop only if there's no active playback.
            if (_playbackCoroutine != null)
            {
                if (DEV_VERBOSE) Debug.LogWarning("[TtsPlayer] PlayTts called while already playing - stopping previous");
                StopCoroutine(_playbackCoroutine);
                _playbackCoroutine = null;
            }

            // Reset state flags BEFORE starting coroutine to avoid race condition
            _loopCount = Mathf.Max(1, loopCount);
            _hasError = false;
            _lastError = null;
            _playbackComplete = false;  // MUST be false before coroutine starts
            _loadComplete = false;
            _isPlaying = false;  // Will be set true when audio actually starts

            // Log for diagnostics
            string textPreview = text.Length > 30 ? text.Substring(0, 30) + "..." : text;
            Debug.Log($"[TTS] start genId={myGenId} textLen={text.Length} text='{textPreview}'");

            _playbackCoroutine = StartCoroutine(LoadAndPlayCoroutine(text, speedProfile, myGenId));
        }

        /// <summary>
        /// Stop current playback immediately and reset all state.
        /// Safe to call multiple times.
        /// ONLY call this when explicitly stopping (STOP button), not when starting new TTS.
        /// </summary>
        public void StopTts()
        {
            int prevPlayId = _playId;
            _playId++;  // Increment to cancel any waiting coroutines

            // Log call site for diagnostics
            Debug.Log($"[TTS] StopTts called playId={prevPlayId}->{_playId} wasPlaying={_isPlaying}");

            if (_playbackCoroutine != null)
            {
                StopCoroutine(_playbackCoroutine);
                _playbackCoroutine = null;
            }

            if (_audioSource != null)
            {
                _audioSource.Stop();
                _audioSource.clip = null;
            }

            _isPlaying = false;
            _currentLoop = 0;
            CurrentRms = 0f;
            _dspStart = 0;
            _expectedEnd = 0;
            _playbackComplete = true;  // Mark as complete so waiters can proceed
        }

        /// <summary>
        /// Force stop playback immediately. Increments playId to cancel all waiting coroutines.
        /// Call this from ResetSessionToHome() to ensure no stale waits remain.
        /// </summary>
        public void ForceStop()
        {
            Debug.Log($"[TTS] forcedStop playId={_playId} reason=reset");
            StopTts();
        }

        /// <summary>
        /// Alias for StopTts for backward compatibility.
        /// </summary>
        public void Stop() => StopTts();

        /// <summary>
        /// Pause playback.
        /// </summary>
        public void Pause()
        {
            _audioSource?.Pause();
        }

        /// <summary>
        /// Resume playback.
        /// </summary>
        public void Resume()
        {
            _audioSource?.UnPause();
        }

        /// <summary>
        /// Wait for TTS playback to complete with timeout.
        /// Uses dspTime-based completion detection for robustness.
        /// Checks playId to bail out if Stop/ForceStop was called.
        /// </summary>
        /// <param name="timeoutSeconds">Maximum time to wait</param>
        /// <param name="capturedPlayId">Optional: captured playId for cancellation check</param>
        /// <returns>IEnumerator for coroutine</returns>
        public IEnumerator WaitForTTSFinished(float timeoutSeconds = 30f, int capturedPlayId = -1)
        {
            // Capture playId if not provided
            if (capturedPlayId < 0) capturedPlayId = _playId;

            float elapsed = 0f;

            // Wait for load to complete first
            while (!_loadComplete && !_hasError && elapsed < _loadTimeoutSeconds)
            {
                // Check for cancellation
                if (_playId != capturedPlayId)
                {
                    Debug.Log($"[TTS] WaitForTTSFinished cancelled during load (playId {capturedPlayId} -> {_playId})");
                    yield break;
                }
                yield return null;
                elapsed += Time.deltaTime;
            }

            // Check for cancellation after load wait
            if (_playId != capturedPlayId)
            {
                Debug.Log($"[TTS] WaitForTTSFinished cancelled after load (playId {capturedPlayId} -> {_playId})");
                yield break;
            }

            if (_hasError)
            {
                Debug.LogWarning($"[TtsPlayer] Load error: {_lastError}");
                yield break;
            }

            if (elapsed >= _loadTimeoutSeconds)
            {
                Debug.LogWarning($"[TtsPlayer] Load timeout after {elapsed:F1}s");
                StopTts();
                yield break;
            }

            // Wait for playback using DSP time
            float playbackElapsed = 0f;
            double dspNow = AudioSettings.dspTime;

            while (!_playbackComplete && _isPlaying && playbackElapsed < timeoutSeconds)
            {
                // Check for cancellation every frame
                if (_playId != capturedPlayId)
                {
                    Debug.Log($"[TTS] WaitForTTSFinished cancelled during playback (playId {capturedPlayId} -> {_playId})");
                    yield break;
                }

                dspNow = AudioSettings.dspTime;

                // Check 1: DSP time past expected end + buffer -> force complete
                if (_expectedEnd > 0 && dspNow >= _expectedEnd + 0.3)
                {
                    Debug.Log($"[TTS] dspTime overdue playId={capturedPlayId} dsp={dspNow:F2} expected={_expectedEnd:F2}");
                    _isPlaying = false;
                    _playbackComplete = true;
                    if (_audioSource.isPlaying) _audioSource.Stop();
                    break;
                }

                // Check 2: AudioSource stopped playing after start
                if (!_audioSource.isPlaying && _isPlaying && _currentClip != null && dspNow > _dspStart + 0.05)
                {
                    // Wait one more frame to confirm
                    yield return null;
                    playbackElapsed += Time.deltaTime;
                    if (!_audioSource.isPlaying)
                    {
                        Debug.Log($"[TTS] isPlaying false playId={capturedPlayId} dsp={dspNow:F2}");
                        break;
                    }
                    continue;
                }

                yield return null;
                playbackElapsed += Time.deltaTime;
            }

            // Final cancellation check
            if (_playId != capturedPlayId)
            {
                Debug.Log($"[TTS] WaitForTTSFinished cancelled at end (playId {capturedPlayId} -> {_playId})");
                yield break;
            }

            // Absolute timeout hit
            if (playbackElapsed >= timeoutSeconds && !_playbackComplete)
            {
                Debug.LogWarning($"[TTS] forcedStop playId={capturedPlayId} reason=timeout elapsed={playbackElapsed:F1}s clipLen={_currentClip?.length ?? 0:F1}s");
                StopTts();
            }
        }

        private IEnumerator LoadAndPlayCoroutine(string text, int speedProfile, int genId)
        {
            _isPlaying = false;
            _currentLoop = 0;
            _loadComplete = false;
            _playbackComplete = false;  // Redundant but safe - ensures flag is false
            OnLoadStart?.Invoke();

            string speedLabel = speedProfile == 0 ? "slow" : (speedProfile == 2 ? "fast" : "normal");
            string textPreview = text.Substring(0, Math.Min(30, text.Length));
            Debug.Log($"[TTS] loading genId={genId} textLen={text.Length} text='{textPreview}' speed={speedLabel}");

            bool loaded = false;
            string error = null;
            float loadStartTime = Time.realtimeSinceStartup;

            yield return ApiClient.GetTts(
                text,
                speedProfile,
                clip =>
                {
                    _currentClip = clip;
                    loaded = true;
                },
                err =>
                {
                    error = err;
                    _hasError = true;
                    _lastError = err;
                }
            );

            float loadElapsed = Time.realtimeSinceStartup - loadStartTime;

            if (!loaded || _currentClip == null)
            {
                _hasError = true;
                _lastError = error ?? "Failed to load TTS";
                _loadComplete = true;
                _playbackComplete = true;
                OnLoadError?.Invoke(_lastError);
                Debug.LogWarning($"[TtsPlayer] Load failed after {loadElapsed:F1}s: {_lastError}");
                yield break;
            }

            // Staleness check - another TTS call superseded this one
            if (_ttsGenId != genId)
            {
                Debug.Log($"[TTS] genId={genId} stale (current={_ttsGenId}), bailing out");
                yield break;
            }

            _loadComplete = true;
            OnLoadComplete?.Invoke();
            Debug.Log($"[TTS] loaded genId={genId} clip={_currentClip.length:F1}s (loadTime={loadElapsed:F1}s)");

            // Play loop
            _isPlaying = true;
            _playId++;  // New play session
            int myPlayId = _playId;

            for (int i = 0; i < _loopCount; i++)
            {
                _currentLoop = i + 1;
                OnLoopStart?.Invoke(_currentLoop);

                _audioSource.clip = _currentClip;
                _audioSource.loop = false;  // Ensure no looping

                // Set DSP tracking before play
                _dspStart = AudioSettings.dspTime;
                float clipDuration = _currentClip.samples / (float)_currentClip.frequency;
                _expectedEnd = _dspStart + clipDuration;

                Debug.Log($"[TTS] start playId={myPlayId} v={genId} clipLen={clipDuration:F2}s dspStart={_dspStart:F2} expectedEnd={_expectedEnd:F2}");

                // Bind audio source to lip sync controllers before play
                TtsMouthController.Instance?.SetSource(_audioSource);
                TtsLipSyncRuntime.Instance?.AttachAudioSource(_audioSource);

                _audioSource.Play();

                // Wait for clip to finish using DSP time
                float playStartTime = Time.realtimeSinceStartup;
                float maxPlayTime = clipDuration + 2f; // Add 2s buffer

                while (_audioSource.isPlaying)
                {
                    // Staleness check during playback (both genId and playId)
                    if (_ttsGenId != genId || _playId != myPlayId)
                    {
                        Debug.Log($"[TTS] cancelled during playback genId={genId}->{_ttsGenId} playId={myPlayId}->{_playId}");
                        _audioSource.Stop();
                        yield break;
                    }

                    // Check DSP time for overdue playback
                    double dspNow = AudioSettings.dspTime;
                    if (dspNow >= _expectedEnd + 0.5)
                    {
                        Debug.Log($"[TTS] dspTime overdue playId={myPlayId} dsp={dspNow:F2} expected={_expectedEnd:F2}");
                        _audioSource.Stop();
                        break;
                    }

                    float playElapsed = Time.realtimeSinceStartup - playStartTime;
                    if (playElapsed > maxPlayTime)
                    {
                        Debug.LogWarning($"[TTS] forcedStop playId={myPlayId} reason=timeout elapsed={playElapsed:F1}s");
                        _audioSource.Stop();
                        break;
                    }
                    yield return null;
                }

                OnLoopComplete?.Invoke(_currentLoop);

                // Delay between loops (except after last)
                if (i < _loopCount - 1)
                {
                    yield return new WaitForSeconds(_loopDelay);
                }
            }

            _isPlaying = false;
            _playbackComplete = true;
            CurrentRms = 0f;
            OnPlaybackComplete?.Invoke();
            Debug.Log($"[TTS] finish genId={genId} clipLen={_currentClip?.length ?? 0:F1}s");
        }

        // NOTE: GetProgress() removed - it used AudioSource.time which causes warnings
        // for streaming audio sources. Use IsPlaybackComplete instead.
    }
}
