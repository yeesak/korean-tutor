using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShadowingTutor.UI
{
    /// <summary>
    /// Press-and-hold button for recording.
    /// Uses pointer events (not Update polling) to start/stop recording.
    /// Respects TutorRoomController's state to prevent recording during AI speech.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class HoldToRecordButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Header("Visual Feedback")]
        [Tooltip("Use the Image's current color as idle color on Awake")]
        [SerializeField] private bool _useImageColorAsIdle = true;
        [SerializeField] private Color _idleColor = new Color(0.9f, 0.2f, 0.2f, 1f);       // Red (normal state)
        [SerializeField] private Color _recordingColor = new Color(1f, 0.5f, 0.5f, 1f);    // Lighter red (pressed/recording)
        [SerializeField] private Color _disabledColor = new Color(0.5f, 0.3f, 0.3f, 1f);   // Darker red (disabled)
        [SerializeField] private Color _thinkingColor = new Color(0.6f, 0.6f, 0.6f, 1f);   // Gray (processing/thinking)

        private Button _button;
        private Image _buttonImage;
        private Text _buttonText;
        private bool _isHolding = false;
        private Color _originalColor;  // Captured from Image on Awake
        private bool _recordingStarted = false;  // True if recording actually started during this hold
        private bool _recordingOccurredThisClick = false;  // Persists until next OnPointerDown - prevents onClick skip

        // Callback for TutorRoomController to check if recording is allowed
        public System.Func<bool> CanStartRecording { get; set; }

        // Callback to check if input is globally locked (blocks ALL input during processing)
        public System.Func<bool> IsInputLocked { get; set; }

        // Events for TutorRoomController to hook into
        public event System.Action OnRecordStart;
        public event System.Action OnRecordStop;

        public bool IsHolding => _isHolding;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _buttonImage = GetComponent<Image>();
            _buttonText = GetComponentInChildren<Text>();

            // Capture original color from Image component
            if (_buttonImage != null)
            {
                _originalColor = _buttonImage.color;
                if (_useImageColorAsIdle)
                {
                    _idleColor = _originalColor;
                    // Auto-generate lighter recording color (increase brightness)
                    _recordingColor = new Color(
                        Mathf.Min(1f, _originalColor.r + 0.3f),
                        Mathf.Min(1f, _originalColor.g + 0.3f),
                        Mathf.Min(1f, _originalColor.b + 0.3f),
                        _originalColor.a
                    );
                }
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // GATE 1: Check if input is globally locked (during STT/Grok/TTS processing)
            if (IsInputLocked != null && IsInputLocked())
            {
                Debug.Log("[HoldToRecord] OnPointerDown BLOCKED - input locked");
                return;
            }

            // GATE 2: Check if button is interactable and recording is allowed
            if (_button != null && !_button.interactable) return;
            if (CanStartRecording != null && !CanStartRecording()) return;

            _isHolding = true;
            _recordingStarted = false;  // Reset - will be set true if recording actually starts
            _recordingOccurredThisClick = false;  // Reset for new click cycle
            UpdateVisual(true);

            // Start recording via MicRecorder
            if (MicRecorder.Instance != null && !MicRecorder.Instance.IsRecording)
            {
                bool started = MicRecorder.Instance.StartRecording();
                if (started)
                {
                    _recordingStarted = true;  // Mark that recording actually started
                    _recordingOccurredThisClick = true;  // Persists until next OnPointerDown - blocks onClick
                    OnRecordStart?.Invoke();
                    Debug.Log("[HoldToRecord] Recording STARTED successfully");
                }
                else
                {
                    Debug.LogWarning("[HoldToRecord] MicRecorder.StartRecording() returned false");
                    _isHolding = false;
                    UpdateVisual(false);
                }
            }
            else
            {
                Debug.Log($"[HoldToRecord] MicRecorder not available or already recording");
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_isHolding) return;
            StopHoldRecording();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Stop recording if pointer exits while holding (finger dragged off button)
            if (!_isHolding) return;
            StopHoldRecording();
        }

        private void OnDisable()
        {
            if (_isHolding) StopHoldRecording();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && _isHolding) StopHoldRecording();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && _isHolding) StopHoldRecording();
        }

        /// <summary>
        /// Stop recording and reset visual state.
        /// </summary>
        private void StopHoldRecording()
        {
            bool wasRecording = _recordingStarted;
            _isHolding = false;
            _recordingStarted = false;
            UpdateVisual(false);

            if (wasRecording && MicRecorder.Instance != null && MicRecorder.Instance.IsRecording)
            {
                Debug.Log("[HoldToRecord] Recording STOPPED on release");
                MicRecorder.Instance.StopRecordingGracefully();
                OnRecordStop?.Invoke();
            }
            else if (!wasRecording)
            {
                Debug.Log("[HoldToRecord] Release without recording start - no action taken");
            }
        }

        /// <summary>
        /// Check if recording was actually started during this click cycle.
        /// Used to prevent onClick from firing when recording occurred.
        /// Returns true until the next OnPointerDown, ensuring onClick is blocked.
        /// </summary>
        public bool WasRecordingStarted => _recordingOccurredThisClick;

        private void UpdateVisual(bool isRecording)
        {
            if (_buttonImage != null)
            {
                _buttonImage.color = isRecording ? _recordingColor : _idleColor;
            }
            if (_buttonText != null)
            {
                _buttonText.text = isRecording ? "놓으면 중지" : "꾹 눌러 녹음";
            }
        }

        /// <summary>
        /// Called by TutorRoomController to update button state
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            if (_button != null)
            {
                _button.interactable = interactable;
            }
            if (_buttonImage != null)
            {
                _buttonImage.color = interactable ? _idleColor : _disabledColor;
            }
            // Reset visual to show proper idle label when becoming interactable
            if (interactable)
            {
                UpdateVisual(false);  // Show "꾹 눌러 녹음" label
            }
        }

        /// <summary>
        /// Reset to idle state (called when recording completes externally)
        /// </summary>
        public void ResetState()
        {
            _isHolding = false;
            _recordingStarted = false;
            // NOTE: Do NOT reset _recordingOccurredThisClick here - it must persist until next OnPointerDown
            UpdateVisual(false);
        }

        /// <summary>
        /// Set button to "thinking" state - gray, disabled, shows "생각중..."
        /// Used during STT/Grok/TTS processing pipeline.
        /// </summary>
        public void SetThinkingState(bool isThinking, string customText = null)
        {
            if (_button != null)
            {
                _button.interactable = !isThinking;
            }
            if (_buttonImage != null)
            {
                _buttonImage.color = isThinking ? _thinkingColor : _idleColor;
            }
            if (_buttonText != null)
            {
                _buttonText.text = isThinking
                    ? (customText ?? "생각중...")
                    : "꾹 눌러 녹음";
            }
            _isHolding = false;
        }
    }
}
