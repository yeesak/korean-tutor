using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace ShadowingTutor
{
    /// <summary>
    /// Microphone recorder with permission handling for iOS/Android.
    /// Records audio and provides WAV-encoded bytes.
    ///
    /// State machine: Idle -> Recording -> Finalizing -> Idle
    /// Supports manual stop, silence auto-stop, and max duration auto-stop.
    /// </summary>
    public class MicRecorder : MonoBehaviour
    {
        public enum RecordingState
        {
            Idle,
            Recording,
            Finalizing  // Processing audio data after stop
        }

        private static MicRecorder _instance;
        public static MicRecorder Instance => _instance;

        [Header("Recording Settings")]
        [SerializeField] private int _sampleRate = 16000;
        [SerializeField] private float _maxDuration = 10f;

        [Header("Silence Detection")]
        [SerializeField] private bool _enableSilenceDetection = true;
        [SerializeField] private float _silenceThreshold = 0.01f;
        [SerializeField] private float _silenceDuration = 1.5f;  // Stop after this many seconds of silence
        [SerializeField] private float _minRecordingDuration = 0.5f;  // Don't stop before this

        private AudioClip _recordingClip;
        private string _micDevice;
        private RecordingState _state = RecordingState.Idle;
        private float _recordingStartTime;
        private bool _permissionGranted = false;
        private float _lastSoundTime;
        private bool _hasDetectedSound = false;
        private bool _stopRequested = false;  // Flag for graceful stop
        private readonly object _stateLock = new object();  // Thread safety

        // Events
        public event Action OnRecordingStart;
        public event Action<float> OnRecordingProgress;  // 0-1
        public event Action<byte[]> OnRecordingComplete;
        public event Action<string> OnRecordingError;
        public event Action OnPermissionGranted;
        public event Action OnPermissionDenied;

        // Properties
        public bool IsRecording => _state == RecordingState.Recording;
        public bool IsBusy => _state != RecordingState.Idle;  // Recording or finalizing
        public RecordingState State => _state;
        public bool HasPermission => _permissionGranted;
        public float RecordingDuration => _state == RecordingState.Recording ? Time.time - _recordingStartTime : 0f;
        public float MaxDuration => _maxDuration;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            // Use AppConfig settings if available
            if (AppConfig.Instance != null)
            {
                _sampleRate = AppConfig.Instance.MicSampleRate;
                _maxDuration = AppConfig.Instance.MaxRecordingDuration;
            }
        }

        private void Start()
        {
            CheckPermission();
        }

        private void Update()
        {
            if (_state != RecordingState.Recording) return;

            float elapsed = Time.time - _recordingStartTime;
            float progress = Mathf.Clamp01(elapsed / _maxDuration);
            OnRecordingProgress?.Invoke(progress);

            // Check if stop was requested (manual stop)
            if (_stopRequested)
            {
                Debug.Log("[MicRecorder] Manual stop requested");
                FinalizeRecording();
                return;
            }

            // Check for silence detection
            if (_enableSilenceDetection && elapsed >= _minRecordingDuration)
            {
                float currentLevel = GetCurrentLevel();

                if (currentLevel > _silenceThreshold)
                {
                    _lastSoundTime = Time.time;
                    _hasDetectedSound = true;
                }

                // Stop if silence detected after sound was detected
                if (_hasDetectedSound && (Time.time - _lastSoundTime) >= _silenceDuration)
                {
                    Debug.Log($"[MicRecorder] Silence detected after {elapsed:F1}s, auto-stopping");
                    FinalizeRecording();
                    return;
                }
            }

            // Auto-stop at max duration
            if (elapsed >= _maxDuration)
            {
                Debug.Log($"[MicRecorder] Max duration reached ({_maxDuration}s), auto-stopping");
                FinalizeRecording();
            }
        }

        /// <summary>
        /// Check and request microphone permission
        /// </summary>
        public void CheckPermission()
        {
            StartCoroutine(CheckPermissionCoroutine());
        }

        private IEnumerator CheckPermissionCoroutine()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Debug.Log("[MicRecorder] Requesting microphone permission...");
                Permission.RequestUserPermission(Permission.Microphone);

                // Wait a frame for the dialog to show
                yield return new WaitForSeconds(0.5f);

                // Check again after request
                float timeout = 30f;
                float elapsed = 0f;
                while (!Permission.HasUserAuthorizedPermission(Permission.Microphone) && elapsed < timeout)
                {
                    yield return new WaitForSeconds(0.5f);
                    elapsed += 0.5f;
                }

                _permissionGranted = Permission.HasUserAuthorizedPermission(Permission.Microphone);
            }
            else
            {
                _permissionGranted = true;
            }
#elif UNITY_IOS && !UNITY_EDITOR
            // iOS handles permission automatically when Microphone.Start is called
            // if NSMicrophoneUsageDescription is set in Info.plist
            _permissionGranted = Application.HasUserAuthorization(UserAuthorization.Microphone);
            if (!_permissionGranted)
            {
                yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
                _permissionGranted = Application.HasUserAuthorization(UserAuthorization.Microphone);
            }
#else
            // Editor/Standalone - assume permission granted
            _permissionGranted = Microphone.devices.Length > 0;
            yield return null;
#endif

            if (_permissionGranted)
            {
                Debug.Log("[MicRecorder] Microphone permission granted");
                SelectMicDevice();
                OnPermissionGranted?.Invoke();
            }
            else
            {
                Debug.LogWarning("[MicRecorder] Microphone permission denied");
                OnPermissionDenied?.Invoke();
            }
        }

        /// <summary>
        /// Select the default microphone device
        /// </summary>
        private void SelectMicDevice()
        {
            string[] devices = Microphone.devices;
            if (devices.Length > 0)
            {
                _micDevice = devices[0];
                Debug.Log($"[MicRecorder] Using microphone: {_micDevice}");
            }
            else
            {
                Debug.LogWarning("[MicRecorder] No microphone devices found");
                _micDevice = null;
            }
        }

        /// <summary>
        /// Start recording audio.
        /// Returns false if recording cannot start (already recording, no permission, etc.)
        /// </summary>
        public bool StartRecording()
        {
            lock (_stateLock)
            {
                // Defensive guard: don't start if already busy
                if (_state != RecordingState.Idle)
                {
                    Debug.LogWarning($"[MicRecorder] Cannot start recording - current state: {_state}");
                    return false;
                }

                if (!_permissionGranted)
                {
                    OnRecordingError?.Invoke("Microphone permission not granted");
                    return false;
                }

                if (string.IsNullOrEmpty(_micDevice))
                {
                    SelectMicDevice();
                    if (string.IsNullOrEmpty(_micDevice))
                    {
                        OnRecordingError?.Invoke("No microphone available");
                        return false;
                    }
                }

                Debug.Log($"[MicRecorder] Starting recording (max {_maxDuration}s, {_sampleRate}Hz, silence={_enableSilenceDetection})");

                try
                {
                    _recordingClip = Microphone.Start(_micDevice, false, Mathf.CeilToInt(_maxDuration) + 1, _sampleRate);

                    if (_recordingClip == null)
                    {
                        OnRecordingError?.Invoke("Failed to start microphone");
                        return false;
                    }

                    _state = RecordingState.Recording;
                    _recordingStartTime = Time.time;
                    _lastSoundTime = Time.time;
                    _hasDetectedSound = false;
                    _stopRequested = false;

                    OnRecordingStart?.Invoke();
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[MicRecorder] Exception starting recording: {e.Message}");
                    OnRecordingError?.Invoke($"Recording error: {e.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Request graceful stop of recording.
        /// This sets a flag that will be processed in Update() on the main thread.
        /// Safe to call from any context. Idempotent - no-op if not recording.
        /// </summary>
        public void StopRecordingGracefully()
        {
            // Idempotent: silently return if not recording (no warning spam)
            if (_state != RecordingState.Recording)
            {
                return;
            }

            Debug.Log("[MicRecorder] Stop requested (graceful)");
            _stopRequested = true;
        }

        /// <summary>
        /// Stop recording immediately and get WAV bytes.
        /// Calls StopRecordingGracefully internally for consistency.
        /// </summary>
        public void StopRecording()
        {
            StopRecordingGracefully();
        }

        /// <summary>
        /// Finalize the recording - must be called from main thread (Update).
        /// Stops microphone, extracts audio data, and invokes completion callback.
        /// </summary>
        private void FinalizeRecording()
        {
            lock (_stateLock)
            {
                if (_state != RecordingState.Recording)
                {
                    Debug.LogWarning($"[MicRecorder] FinalizeRecording called but state is: {_state}");
                    return;
                }

                _state = RecordingState.Finalizing;
                _stopRequested = false;
            }

            try
            {
                // Get position before stopping (must be on main thread)
                int position = 0;
                if (!string.IsNullOrEmpty(_micDevice))
                {
                    position = Microphone.GetPosition(_micDevice);
                    Microphone.End(_micDevice);
                }

                if (position <= 0 || _recordingClip == null)
                {
                    Debug.LogWarning("[MicRecorder] Recording failed - no audio captured");
                    lock (_stateLock) { _state = RecordingState.Idle; }
                    OnRecordingError?.Invoke("Recording failed - no audio captured");
                    return;
                }

                float duration = Time.time - _recordingStartTime;
                Debug.Log($"[MicRecorder] Finalizing recording. Duration: {duration:F1}s, Samples: {position}");

                // Extract samples (must be on main thread)
                float[] samples = new float[position];
                _recordingClip.GetData(samples, 0);

                // Encode to WAV
                byte[] wavData = WavEncoder.Encode(samples, _sampleRate);
                Debug.Log($"[MicRecorder] WAV encoded: {wavData.Length} bytes");

                lock (_stateLock) { _state = RecordingState.Idle; }

                // Invoke completion callback
                OnRecordingComplete?.Invoke(wavData);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MicRecorder] Exception in FinalizeRecording: {e.Message}");
                lock (_stateLock) { _state = RecordingState.Idle; }
                OnRecordingError?.Invoke($"Recording finalization error: {e.Message}");
            }
        }

        /// <summary>
        /// Cancel recording without callback.
        /// Use this to abort a recording without processing.
        /// </summary>
        public void CancelRecording()
        {
            lock (_stateLock)
            {
                if (_state == RecordingState.Idle)
                {
                    return;
                }

                Debug.Log("[MicRecorder] Recording cancelled");
                _state = RecordingState.Idle;
                _stopRequested = false;
            }

            try
            {
                if (!string.IsNullOrEmpty(_micDevice) && Microphone.IsRecording(_micDevice))
                {
                    Microphone.End(_micDevice);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MicRecorder] Exception in CancelRecording: {e.Message}");
            }
        }

        /// <summary>
        /// Get current audio level (0-1) for visualization
        /// </summary>
        public float GetCurrentLevel()
        {
            if (_state != RecordingState.Recording || _recordingClip == null) return 0f;

            try
            {
                int position = Microphone.GetPosition(_micDevice);
                if (position <= 0) return 0f;

                int sampleWindow = Mathf.Min(256, position);
                float[] samples = new float[sampleWindow];

                int startPosition = Mathf.Max(0, position - sampleWindow);
                _recordingClip.GetData(samples, startPosition);

                float sum = 0f;
                foreach (float s in samples)
                {
                    sum += Mathf.Abs(s);
                }

                return Mathf.Clamp01(sum / sampleWindow * 4f);  // Scale up for visibility
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Force reset to idle state (use with caution - for error recovery)
        /// </summary>
        public void ForceReset()
        {
            Debug.LogWarning("[MicRecorder] Force reset called");
            CancelRecording();
        }

        private void OnDestroy()
        {
            CancelRecording();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && _state == RecordingState.Recording)
            {
                Debug.Log("[MicRecorder] App paused during recording, cancelling");
                CancelRecording();
            }
        }
    }
}
