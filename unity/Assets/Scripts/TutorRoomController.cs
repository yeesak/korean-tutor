using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using ShadowingTutor.UI;

namespace ShadowingTutor
{
    /// <summary>
    /// Controller for real-time character conversation flow.
    /// NO Results scene - all feedback via TTS audio.
    ///
    /// State Machine:
    /// - Idle: Ready to start (START button)
    /// - TopicIntroTTS: Playing topic introduction
    /// - PlayingTargetTTS: Playing target expression 3 times
    /// - Recording: User speaking (STOP button)
    /// - STTProcessing: Transcribing speech
    /// - FeedbackProcessing: Getting Grok feedback
    /// - SpeakingFeedbackTTS: Speaking feedback
    /// - Advancing: Moving to next/retry
    /// </summary>
    public class TutorRoomController : MonoBehaviour
    {
        #region State Machine

        /// <summary>
        /// Deterministic FSM states - exact list as specified.
        /// Transitions: Boot -> Home -> SeasonIntro -> TopicPrompt -> Recording -> Scoring -> Feedback -> (advance or retry)
        /// </summary>
        public enum TutorState
        {
            Boot,              // App startup, checking backend
            Home,              // Idle, ready for START button
            SeasonIntro,       // Playing season intro TTS
            TopicPrompt,       // Playing target phrase TTS 3x
            Recording,         // User speaking (STOP button)
            Scoring,           // STT + feedback processing
            Feedback,          // Speaking feedback TTS
            RetryPrompt,       // "다시해볼까?" + guided retry
            SeasonEndPrompt,   // "다음 시즌?" YES/NO parsing
            End                // Session ended
        }

        #endregion

        #region Serialized Fields

        [Header("UI References")]
        [SerializeField] private Text _statusText;
        [SerializeField] private Text _topicLabel;
        [SerializeField] private Text _progressText;
        [SerializeField] private Text _transcriptText;
        [SerializeField] private Text _hintText;
        [SerializeField] private Text _targetSentenceText;  // Shows target sentence for comparison
        [SerializeField] private Text _comparisonText;      // Shows mismatch details

        [Header("Comparison Panel (UGUI - uses OS Korean fonts)")]
        [SerializeField] private ComparisonPanelUGUI _comparisonPanelUGUI;  // UGUI-based panel (preferred)

        [Header("Comparison Panel (Legacy TMP - fallback)")]
        [SerializeField] private GameObject _comparisonPanel;              // Panel container
        [SerializeField] private TextMeshProUGUI _tmpTargetLabel;          // "Target:" label
        [SerializeField] private TextMeshProUGUI _tmpTargetSentence;       // Target sentence (black)
        [SerializeField] private TextMeshProUGUI _tmpUserLabel;            // "Your speech:" label
        [SerializeField] private TextMeshProUGUI _tmpUserSentence;         // User's transcript (red/black diff)
        [SerializeField] private TextMeshProUGUI _tmpAccuracyText;         // Accuracy percentage
        [SerializeField] private TMP_FontAsset _tmpFontAsset;              // Font for dynamic TMP (assign in Inspector)

        [Header("Watchdog Settings")]
        [SerializeField] private float _ttsWatchdogTimeout = 12f;          // Max wait for TTS completion
        [SerializeField] private float _sttWatchdogTimeout = 15f;          // Max wait for STT completion
        [SerializeField] private float _feedbackWatchdogTimeout = 10f;     // Max wait for feedback processing

        [Header("Buttons")]
        [FormerlySerializedAs("_startButton")]
        [SerializeField] private Button _mainButton;
        [SerializeField] private Text _mainButtonText;

        [FormerlySerializedAs("_recordButton")]
        [SerializeField] private Button _stopAiButton;
        private Text _stopAiButtonText;
        private Image _stopAiButtonImage;

        [Header("Recording UI")]
        [SerializeField] private Image _recordingIndicator;
        [SerializeField] private Slider _recordingProgress;
        [SerializeField] private GameObject _loadingIndicator;

        [Header("Colors")]
        [SerializeField] private Color _buttonIdleColor = new Color(0.2f, 0.6f, 1f);
        [SerializeField] private Color _buttonRecordingColor = new Color(1f, 0.3f, 0.3f);
        [SerializeField] private Color _buttonDisabledColor = new Color(0.5f, 0.5f, 0.5f);

        [Header("Settings")]
        [SerializeField] private float _buttonDebounceTime = 0.5f;
        [SerializeField] private int _ttsRepeatCount = 3;
        [SerializeField] private float _delayBeforeRecording = 0.5f;
        [SerializeField] private float _delayAfterFeedback = 1.0f;

        [Header("Accuracy Thresholds")]
        [SerializeField] [Range(0, 100)] private int _accuracyCorrectThreshold = 90;   // >= this = correct
        [SerializeField] [Range(0, 100)] private int _accuracyHintThreshold = 65;      // >= this = partial hint

        #endregion

        #region Private Fields

        private TutorState _currentState = TutorState.Boot;
        private bool _isInitialized = false;
        private bool _isBackendConnected = false;  // Track connectivity for retry logic
        private float _lastButtonPressTime = 0f;
        private Image _mainButtonImage;
        private Coroutine _mainCoroutine;

        // Topic/Expression management
        private TopicData _currentTopic;
        private List<Expression> _shuffledExpressions;
        private int _currentExpressionIndex = -1;
        private Expression _currentExpression;

        // TTS repeat tracking
        private int _ttsRepeatIndex = 0;

        // STT result (raw and normalized)
        private string _rawTranscriptText = "";      // Original STT output for debugging
        private string _transcriptText_value = "";   // Normalized (brackets removed)

        // Feedback result
        private ApiClient.FeedbackResponse _lastFeedback;
        private bool _lastAttemptCorrect = false;
        private int _lastAccuracyPercent = 0;           // 0-100 accuracy score
        private string _lastMismatchParts = "";          // Human-readable mismatch segments

        // Season management
        private TopicData[] _allTopics;
        private int _currentSeasonIndex = 0;

        // Season Progress Tracking (SINGLE SOURCE OF TRUTH)
        // The announced count at season start is authoritative
        private int _announcedTotalTopics = 0;   // N spoken at season start (never changes during season)
        private int _remainingTopics = 0;        // Decrements by 1 per completed topic
        private int _retryCount = 0;             // Retries for current expression
        private const int MAX_RETRIES = 3;       // Skip after this many retries

        // Verbose logging flag - set true for detailed season progress debugging
        #if UNITY_EDITOR && DEVELOPMENT_BUILD
        private const bool DEV_VERBOSE = true;
        #else
        private const bool DEV_VERBOSE = false;
        #endif
        private const string PREFS_SEASON_ID = "SeasonProgress_SeasonId";
        private const string PREFS_ANNOUNCED = "SeasonProgress_AnnouncedTotal";
        private const string PREFS_REMAINING = "SeasonProgress_Remaining";
        private const string PREFS_EXPRESSION_INDEX = "SeasonProgress_ExpressionIndex";

        // YES/NO intent keywords (extended for voice-driven continuation)
        private static readonly string[] YesKeywords = {
            "네", "예", "응", "그래", "좋아", "가자", "넘어가", "계속", "진행", "해", "오케이", "ok",
            "할래", "하고싶", "싶어", "공부", "다음", "해줘", "해 줘", "고고", "ㅇㅇ"
        };
        private static readonly string[] NoKeywords = {
            "아니", "아니요", "싫어", "그만", "끝", "종료", "멈춰", "오늘은 여기까지", "피곤해", "하기 싫어",
            "stop", "quit", "안해", "안 해", "됐어", "스톱", "쉬고싶", "쉬고 싶", "그만할래"
        };

        // Voice decision result enum
        public enum VoiceDecision { Positive, Negative, Unknown }

        // Voice-driven continuation state
        private bool _voiceDecisionInProgress = false;
        private int _voiceDecisionRepromptCount = 0;
        private const int MAX_VOICE_REPROMPTS = 1;

        // Session versioning for safe cancellation
        // Incremented on every ResetSessionToHome() call
        // Coroutines check this to bail out if session changed
        private int _sessionVersion = 0;

        // Idle/Silence termination tracking
        // Tracks last meaningful user activity to detect prolonged silence
        private float _lastActivityTime = 0f;
        private int _consecutiveSilenceCount = 0;
        private const float IDLE_TIMEOUT_SECONDS = 30f;       // End session after 30s of no activity
        private const float SILENCE_WARNING_SECONDS = 15f;    // Show warning after 15s
        private const int MAX_CONSECUTIVE_SILENCES = 2;       // End after 2 consecutive silence events

        // Attempt tracking - monotonically increasing per session
        private int _attemptIdCounter = 0;
        private AttemptContext _currentAttempt = null;

        // Grok context for dynamic tutor line generation
        private GrokClient.TutorMessageContext _grokContext;

        #endregion

        #region Data Classes

        [Serializable]
        public class TopicData
        {
            public string id;
            public int season;
            public string topic;
            public string topicEn;
            public Expression[] expressions;
        }

        [Serializable]
        public class Expression
        {
            public string korean;
            public string english;
        }

        [Serializable]
        private class TopicsFile
        {
            public int version;
            public TopicData[] topics;
        }

        /// <summary>
        /// Immutable context for a single attempt - captured at attempt start.
        /// All scoring/advance decisions use THIS data, not class fields.
        /// </summary>
        public class AttemptContext
        {
            // Immutable - captured at attempt start
            public readonly int sessionVersion;
            public readonly int attemptId;
            public readonly int seasonIndex;
            public readonly int expressionIndex;
            public readonly string targetPhrase;

            // Mutable - filled during attempt
            public string transcript;
            public int accuracyPercent;
            public bool timedOut;
            public bool transcriptEmpty;
            public string mismatchSummary;
            public bool extraGuidedRepeatDone;

            // Computed properties
            public bool IsValid => !string.IsNullOrEmpty(targetPhrase);
            public bool ShouldAdvance => accuracyPercent >= 65 && !timedOut && !transcriptEmpty;
            public bool ShouldRetry => accuracyPercent < 65 && !timedOut && !transcriptEmpty;

            public AttemptContext(int sessionVersion, int attemptId, int seasonIndex, int expressionIndex, string targetPhrase)
            {
                this.sessionVersion = sessionVersion;
                this.attemptId = attemptId;
                this.seasonIndex = seasonIndex;
                this.expressionIndex = expressionIndex;
                this.targetPhrase = targetPhrase ?? "";

                // Initialize mutable fields to safe defaults
                this.transcript = "";
                this.accuracyPercent = 0;  // NEVER default to 100!
                this.timedOut = false;
                this.transcriptEmpty = true;
                this.mismatchSummary = "";
                this.extraGuidedRepeatDone = false;
            }

            public override string ToString()
            {
                return $"[AttemptContext] session={sessionVersion}, attempt={attemptId}, " +
                       $"season={seasonIndex}, expr={expressionIndex}, " +
                       $"accuracy={accuracyPercent}%, timedOut={timedOut}, empty={transcriptEmpty}";
            }
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            Debug.Log("[TutorRoom] Awake");
        }

        private void Start()
        {
            Debug.Log("[TutorRoom] Start");
            Initialize();

            // DEBUG: Uncomment to test comparison panel visibility on startup
            // This will show sample text immediately to verify UI is working
            #if UNITY_EDITOR
            // DebugTestComparisonPanel();  // Uncomment this line to test
            #endif
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        #endregion

        #region Initialization

        private void Initialize()
        {
            if (_isInitialized) return;

            Debug.Log("[TutorRoom] Initialize starting...");

            EnsureManagersExist();
            SetupUI();
            SubscribeToEvents();

            _isInitialized = true;
            SetState(TutorState.Home);

            // Non-blocking startup health check
            StartCoroutine(StartupHealthCheck());

            Debug.Log("[TutorRoom] Initialized - Real-time conversation flow ready");
            Debug.Log($"[API] baseUrl={AppConfig.Instance?.BackendBaseUrl ?? "not configured"}");

            // Log Input System configuration for debugging
            LogInputSystemStatus();
        }

        /// <summary>
        /// Startup health check to verify backend connectivity.
        /// On Android: BLOCKS if localhost detected.
        /// </summary>
        private IEnumerator StartupHealthCheck()
        {
            // Brief delay to let app settle
            yield return new WaitForSeconds(0.5f);

            // Validate configuration first
            string configError;
            if (!AppConfig.Instance.ValidateConfiguration(out configError))
            {
                Debug.LogError($"[TutorRoom] Configuration invalid: {configError}");

                // Show blocking error on UI
                if (_hintText != null)
                {
                    _hintText.text = configError;
                    _hintText.color = Color.red;
                }
                if (_statusText != null)
                {
                    _statusText.text = "설정 오류";
                }

                // Disable main button to prevent lesson start
                if (_mainButton != null)
                {
                    _mainButton.interactable = false;
                }

                yield break;  // Stop here - don't proceed
            }

            string baseUrl = AppConfig.Instance?.BackendBaseUrl ?? "not configured";
            Debug.Log($"[TutorRoom] Checking backend connectivity: {baseUrl}");

            bool isReachable = false;
            string healthError = null;

            yield return ApiClient.CheckHealth(
                response => {
                    isReachable = true;
                    Debug.Log($"[TutorRoom] Backend connected! Mode: {response.mode}, TTS: {response.ttsConfigured}, STT: {response.sttConfigured}");
                },
                error => {
                    healthError = error;
                }
            );

            if (!isReachable)
            {
                // Log clear, actionable error message ONCE
                Debug.LogError(
                    $"[TutorRoom] ═══════════════════════════════════════════════════════════════\n" +
                    $"  BACKEND SERVER UNREACHABLE\n" +
                    $"  URL: {baseUrl}\n" +
                    $"  Error: {healthError}\n" +
                    $"  ───────────────────────────────────────────────────────────────\n" +
                    $"  Checklist:\n" +
                    $"  1. Is the backend server running? (cd backend && npm start)\n" +
                    $"  2. Correct IP address? (localhost only works in Editor)\n" +
                    $"  3. Firewall blocking port 3000?\n" +
                    $"  4. For device: Set LAN IP in Settings panel\n" +
                    $"═══════════════════════════════════════════════════════════════"
                );

                // Show blocking error on UI (same style as config error)
                if (_hintText != null)
                {
                    _hintText.gameObject.SetActive(true);
                    _hintText.text = "서버 연결 실패\n탭하여 재시도";
                    _hintText.color = Color.red;
                }
                if (_statusText != null)
                {
                    _statusText.gameObject.SetActive(true);
                    _statusText.text = "연결 오류";
                }

                // Change button to "재시도" (Retry) mode instead of disabling
                if (_mainButton != null)
                {
                    _mainButton.interactable = true;  // Keep enabled for retry
                }
                if (_mainButtonText != null)
                {
                    _mainButtonText.text = "재시도";
                }

                // Set state to allow retry
                _isBackendConnected = false;
            }
            else
            {
                _isBackendConnected = true;
            }
        }

        private void EnsureManagersExist()
        {
            if (TtsPlayer.Instance == null)
            {
                GameObject go = new GameObject("TtsPlayer");
                go.AddComponent<AudioSource>();
                go.AddComponent<TtsPlayer>();
            }

            if (MicRecorder.Instance == null)
            {
                GameObject go = new GameObject("MicRecorder");
                go.AddComponent<MicRecorder>();
            }
        }

        /// <summary>
        /// Log Input System configuration for debugging UI input issues.
        /// </summary>
        private void LogInputSystemStatus()
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null)
            {
                Debug.LogWarning("[INPUT] No EventSystem found in scene!");
                return;
            }

            // Check which input module is active
            var inputModule = eventSystem.currentInputModule;
            string moduleName = inputModule != null ? inputModule.GetType().Name : "None";

            // Check for legacy StandaloneInputModule (will cause errors with new Input System)
            var standaloneModule = eventSystem.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            bool hasLegacyModule = standaloneModule != null;

            // Log status
            Debug.Log($"[INPUT] EventSystem: {eventSystem.name}, ActiveModule: {moduleName}, HasLegacyModule: {hasLegacyModule}");

            if (hasLegacyModule)
            {
                Debug.LogWarning("[INPUT] StandaloneInputModule detected! This may cause InvalidOperationException with Input System Package. " +
                                "Remove StandaloneInputModule and use InputSystemUIInputModule instead.");
            }

            // Check InputSystemUIInputModule configuration
            #if ENABLE_INPUT_SYSTEM
            var uiInputModule = eventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (uiInputModule != null)
            {
                bool hasActionsAsset = uiInputModule.actionsAsset != null;
                string assetName = hasActionsAsset ? uiInputModule.actionsAsset.name : "None";
                Debug.Log($"[INPUT] InputSystemUIInputModule: ActionsAsset={assetName}, HasAsset={hasActionsAsset}");

                if (!hasActionsAsset)
                {
                    Debug.LogWarning("[INPUT] InputSystemUIInputModule has no ActionsAsset! Touch input may not work on mobile.");
                }
            }
            else
            {
                Debug.LogWarning("[INPUT] No InputSystemUIInputModule found! Touch input will not work.");
            }

            // Log touch support info
            Debug.Log($"[INPUT] TouchSupported={UnityEngine.InputSystem.Touchscreen.current != null}, " +
                      $"MouseSupported={UnityEngine.InputSystem.Mouse.current != null}");
            #endif
        }

        private void SetupUI()
        {
            // Wire main button
            if (_mainButton != null)
            {
                _mainButton.onClick.RemoveAllListeners();
                _mainButton.onClick.AddListener(OnMainButtonClick);
                _mainButtonImage = _mainButton.GetComponent<Image>();
                if (_mainButtonText == null)
                    _mainButtonText = _mainButton.GetComponentInChildren<Text>();
                Debug.Log("[TutorRoom] Main button wired");
            }

            // Wire STOP AI button (replaces old RECORD button)
            if (_stopAiButton != null)
            {
                _stopAiButton.onClick.RemoveAllListeners();
                _stopAiButton.onClick.AddListener(OnStopAiButtonClick);
                _stopAiButtonImage = _stopAiButton.GetComponent<Image>();
                _stopAiButtonText = _stopAiButton.GetComponentInChildren<Text>();
                if (_stopAiButtonText != null)
                    _stopAiButtonText.text = "STOP AI";
                // Initially hidden - only shown during AI speech
                _stopAiButton.gameObject.SetActive(false);
                Debug.Log("[TutorRoom] STOP AI button wired");
            }

            // Hide loading/recording indicators
            if (_loadingIndicator != null) _loadingIndicator.SetActive(false);
            if (_recordingIndicator != null) _recordingIndicator.gameObject.SetActive(false);
            if (_recordingProgress != null) _recordingProgress.gameObject.SetActive(false);

            // Configure transcript text for word wrap
            if (_transcriptText != null)
            {
                _transcriptText.horizontalOverflow = HorizontalWrapMode.Wrap;
                _transcriptText.verticalOverflow = VerticalWrapMode.Overflow;
                _transcriptText.text = "";
            }

            // First screen: Hide all lesson UI overlays (only character + START button visible)
            HideLessonUI();

            UpdateButtonUI();
        }

        /// <summary>
        /// Hide all lesson UI elements for clean first screen (only character visible).
        /// </summary>
        private void HideLessonUI()
        {
            if (_statusText != null) _statusText.gameObject.SetActive(false);
            if (_topicLabel != null) _topicLabel.gameObject.SetActive(false);
            if (_progressText != null) _progressText.gameObject.SetActive(false);
            if (_transcriptText != null) _transcriptText.gameObject.SetActive(false);
            if (_hintText != null) _hintText.gameObject.SetActive(false);
            if (_targetSentenceText != null) _targetSentenceText.gameObject.SetActive(false);
            if (_comparisonText != null) _comparisonText.gameObject.SetActive(false);
            if (_comparisonPanel != null) _comparisonPanel.SetActive(false);
            Debug.Log("[TutorRoom] Lesson UI hidden - first screen clean");
        }

        /// <summary>
        /// Show lesson UI elements when lesson starts.
        /// </summary>
        private void ShowLessonUI()
        {
            if (_statusText != null) _statusText.gameObject.SetActive(true);
            if (_topicLabel != null) _topicLabel.gameObject.SetActive(true);
            if (_progressText != null) _progressText.gameObject.SetActive(true);
            if (_transcriptText != null) _transcriptText.gameObject.SetActive(true);
            if (_hintText != null) _hintText.gameObject.SetActive(true);
            if (_targetSentenceText != null) _targetSentenceText.gameObject.SetActive(true);
            if (_comparisonText != null) _comparisonText.gameObject.SetActive(true);
            // Note: _comparisonPanel is shown/hidden based on STT result, not here
            Debug.Log("[TutorRoom] Lesson UI shown");
        }

        private void SubscribeToEvents()
        {
            if (TtsPlayer.Instance != null)
            {
                TtsPlayer.Instance.OnLoadComplete += OnTtsLoadComplete;
                TtsPlayer.Instance.OnPlaybackComplete += OnTtsPlaybackComplete;
                TtsPlayer.Instance.OnLoadError += OnTtsError;
            }

            if (MicRecorder.Instance != null)
            {
                MicRecorder.Instance.OnRecordingStart += OnRecordingStart;
                MicRecorder.Instance.OnRecordingProgress += OnRecordingProgress;
                MicRecorder.Instance.OnRecordingComplete += OnRecordingComplete;
                MicRecorder.Instance.OnRecordingError += OnRecordingError;
            }
        }

        private void UnsubscribeFromEvents()
        {
            if (TtsPlayer.Instance != null)
            {
                TtsPlayer.Instance.OnLoadComplete -= OnTtsLoadComplete;
                TtsPlayer.Instance.OnPlaybackComplete -= OnTtsPlaybackComplete;
                TtsPlayer.Instance.OnLoadError -= OnTtsError;
            }

            if (MicRecorder.Instance != null)
            {
                MicRecorder.Instance.OnRecordingStart -= OnRecordingStart;
                MicRecorder.Instance.OnRecordingProgress -= OnRecordingProgress;
                MicRecorder.Instance.OnRecordingComplete -= OnRecordingComplete;
                MicRecorder.Instance.OnRecordingError -= OnRecordingError;
            }
        }

        #endregion

        #region State Machine

        private float _stateEnterTime;

        private void SetState(TutorState newState)
        {
            if (_currentState == newState) return;

            float timeInPrevState = Time.time - _stateEnterTime;
            Debug.Log($"[STATE] {_currentState}->{newState} v={_sessionVersion} (prev: {timeInPrevState:F1}s)");

            _currentState = newState;
            _stateEnterTime = Time.time;

            UpdateButtonUI();
            UpdateStatusUI();
            UpdateRecordingUI();

            // Log additional context for key states
            switch (newState)
            {
                case TutorState.SeasonIntro:
                    Debug.Log($"[TutorRoom] Starting season: {_currentTopic?.topic ?? "unknown"}");
                    break;
                case TutorState.TopicPrompt:
                    Debug.Log($"[TutorRoom] Playing target: {_currentExpression?.korean ?? "unknown"}");
                    break;
                case TutorState.Recording:
                    Debug.Log("[TutorRoom] Recording started - waiting for user speech");
                    break;
                case TutorState.Scoring:
                    Debug.Log("[TutorRoom] Processing STT and feedback...");
                    break;
                case TutorState.Feedback:
                    Debug.Log($"[TutorRoom] Speaking feedback - accuracy: {_lastAccuracyPercent}%");
                    break;
                case TutorState.RetryPrompt:
                    Debug.Log("[TutorRoom] Retry coaching - guided repeat");
                    break;
                case TutorState.SeasonEndPrompt:
                    Debug.Log("[TutorRoom] Season end - asking YES/NO");
                    break;
                case TutorState.End:
                    Debug.Log("[TutorRoom] Session ended");
                    break;
            }
        }

        private void UpdateButtonUI()
        {
            if (_mainButton == null) return;

            // Determine if AI is speaking (TTS active)
            bool isAiSpeaking = _currentState == TutorState.SeasonIntro ||
                                _currentState == TutorState.TopicPrompt ||
                                _currentState == TutorState.Feedback ||
                                _currentState == TutorState.RetryPrompt;

            // Show/hide STOP AI button based on whether AI is speaking
            if (_stopAiButton != null)
            {
                _stopAiButton.gameObject.SetActive(isAiSpeaking);
                if (isAiSpeaking && _stopAiButtonImage != null)
                {
                    _stopAiButtonImage.color = _buttonRecordingColor;  // Red for stop
                }
            }

            switch (_currentState)
            {
                case TutorState.Boot:
                case TutorState.Home:
                    SetButtonState("START", _buttonIdleColor, true);
                    break;

                case TutorState.Recording:
                    SetButtonState("STOP", _buttonRecordingColor, true);
                    break;

                default:
                    SetButtonState("...", _buttonDisabledColor, false);
                    break;
            }
        }

        private void SetButtonState(string label, Color color, bool interactable)
        {
            if (_mainButtonText != null) _mainButtonText.text = label;
            if (_mainButtonImage != null) _mainButtonImage.color = color;
            if (_mainButton != null) _mainButton.interactable = interactable;
        }

        private void UpdateStatusUI()
        {
            if (_statusText == null) return;

            switch (_currentState)
            {
                case TutorState.Boot:
                    _statusText.text = "로딩 중...";
                    break;
                case TutorState.Home:
                    _statusText.text = "준비 완료";
                    break;
                case TutorState.SeasonIntro:
                    _statusText.text = "주제 소개 중...";
                    break;
                case TutorState.TopicPrompt:
                    _statusText.text = $"잘 들어봐~ ({_ttsRepeatIndex}/{_ttsRepeatCount})";
                    break;
                case TutorState.Recording:
                    _statusText.text = "따라 말해봐!";
                    break;
                case TutorState.Scoring:
                    _statusText.text = "평가 중...";
                    break;
                case TutorState.Feedback:
                    _statusText.text = "피드백 중...";
                    break;
                case TutorState.RetryPrompt:
                    _statusText.text = "다시 들어봐~";
                    break;
                case TutorState.SeasonEndPrompt:
                    _statusText.text = "다음 시즌?";
                    break;
                case TutorState.End:
                    _statusText.text = "수고했어요!";
                    break;
            }
        }

        private void ShowLoading(bool show)
        {
            if (_loadingIndicator != null) _loadingIndicator.SetActive(show);
        }

        /// <summary>
        /// Centralized recording UI visibility control.
        /// Recording indicator and progress bar ONLY visible in Recording state.
        /// </summary>
        private void UpdateRecordingUI()
        {
            bool showRecording = _currentState == TutorState.Recording;

            if (_recordingIndicator != null)
            {
                _recordingIndicator.gameObject.SetActive(showRecording);
                if (showRecording)
                {
                    _recordingIndicator.color = _buttonRecordingColor;
                }
            }

            if (_recordingProgress != null)
            {
                _recordingProgress.gameObject.SetActive(showRecording);
                if (showRecording)
                {
                    _recordingProgress.value = 0;
                }
            }
        }

        #endregion

        #region Season Progress Management

        /// <summary>
        /// Initialize season progress at the START of a new season.
        /// This is the ONLY place where announcedTotalTopics/remainingTopics are set.
        /// Call this ONCE per season, before announcing the count.
        /// </summary>
        private void InitializeSeasonProgress(int totalExpressions)
        {
            _announcedTotalTopics = totalExpressions;
            _remainingTopics = totalExpressions;
            _retryCount = 0;

            // Save to PlayerPrefs for persistence
            PlayerPrefs.SetInt(PREFS_SEASON_ID, _currentSeasonIndex);
            PlayerPrefs.SetInt(PREFS_ANNOUNCED, _announcedTotalTopics);
            PlayerPrefs.SetInt(PREFS_REMAINING, _remainingTopics);
            PlayerPrefs.SetInt(PREFS_EXPRESSION_INDEX, -1);
            PlayerPrefs.Save();

            if (DEV_VERBOSE) Debug.Log($"[SeasonProgress] INITIALIZED: Season {_currentSeasonIndex}, " +
                      $"announced={_announcedTotalTopics}, remaining={_remainingTopics}");
        }

        /// <summary>
        /// DETERMINISTIC GATING: THE ONLY function that can advance to next topic.
        /// All advance decisions MUST go through this function.
        /// Returns: (shouldAdvance, shouldContinueSeason)
        /// </summary>
        /// <param name="ctx">Immutable attempt context with scoring data</param>
        /// <returns>Tuple: (shouldAdvance to next topic, shouldContinue season)</returns>
        private (bool shouldAdvance, bool shouldContinueSeason) TryAdvanceAfterScoring(AttemptContext ctx)
        {
            // GUARD: Session version must match
            if (ctx.sessionVersion != _sessionVersion)
            {
                Debug.LogWarning($"[ADVANCE] BLOCKED - stale session: ctx.session={ctx.sessionVersion}, current={_sessionVersion}");
                return (false, false);
            }

            // GUARD: Target phrase must be valid
            if (string.IsNullOrEmpty(ctx.targetPhrase))
            {
                Debug.LogError($"[ADVANCE] BLOCKED - empty targetPhrase is internal bug! {ctx}");
                return (false, false);
            }

            // GUARD: Timeout or empty transcript = NO advance, accuracy MUST be 0
            if (ctx.timedOut || ctx.transcriptEmpty)
            {
                Debug.Log($"[ADVANCE] BLOCKED - timeout={ctx.timedOut}, empty={ctx.transcriptEmpty}, accuracy forced to 0. {ctx}");
                return (false, true);  // Don't advance, but continue session (retry)
            }

            // SCORING DECISION
            if (ctx.accuracyPercent >= 65)
            {
                // PASS: Advance to next topic
                Debug.Log($"[ADVANCE] APPROVED - accuracy={ctx.accuracyPercent}% >= 65%. {ctx}");
                bool shouldContinue = CompleteTopicAndDecrementInternal();
                return (true, shouldContinue);
            }
            else
            {
                // FAIL: Stay on same topic, retry
                Debug.Log($"[ADVANCE] DENIED - accuracy={ctx.accuracyPercent}% < 65%. Retry. {ctx}");
                return (false, true);  // Don't advance, continue session
            }
        }

        /// <summary>
        /// Internal decrement - ONLY called by TryAdvanceAfterScoring.
        /// </summary>
        private bool CompleteTopicAndDecrementInternal()
        {
            // Decrement remaining (never go below 0)
            _remainingTopics = Mathf.Max(0, _remainingTopics - 1);
            _retryCount = 0;  // Reset retry count for next expression

            // Save progress
            PlayerPrefs.SetInt(PREFS_REMAINING, _remainingTopics);
            PlayerPrefs.SetInt(PREFS_EXPRESSION_INDEX, _currentExpressionIndex);
            PlayerPrefs.Save();

            Debug.Log($"[ADVANCE] TOPIC COMPLETED. Remaining: {_remainingTopics}/{_announcedTotalTopics}");

            if (_remainingTopics <= 0)
            {
                Debug.Log("[ADVANCE] SEASON COMPLETE! Remaining reached 0.");
                return false;  // Season complete
            }

            return true;  // Continue to next topic
        }

        /// <summary>
        /// Create a new AttemptContext with immutable data captured NOW.
        /// </summary>
        private AttemptContext CreateAttemptContext()
        {
            _attemptIdCounter++;
            var ctx = new AttemptContext(
                sessionVersion: _sessionVersion,
                attemptId: _attemptIdCounter,
                seasonIndex: _currentSeasonIndex,
                expressionIndex: _currentExpressionIndex,
                targetPhrase: _currentExpression?.korean ?? ""
            );
            Debug.Log($"[ATTEMPT] CREATED: {ctx}");
            return ctx;
        }

        /// <summary>
        /// Check if attempt context is still valid for this session.
        /// </summary>
        private bool IsAttemptValid(AttemptContext ctx)
        {
            if (ctx == null) return false;
            if (ctx.sessionVersion != _sessionVersion)
            {
                Debug.LogWarning($"[ATTEMPT] STALE: ctx.session={ctx.sessionVersion}, current={_sessionVersion}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// LEGACY wrapper - deprecated, use TryAdvanceAfterScoring instead.
        /// Kept for any remaining callers but logs warning.
        /// </summary>
        [System.Obsolete("Use TryAdvanceAfterScoring(AttemptContext) instead")]
        private bool CompleteTopicAndDecrement()
        {
            Debug.LogWarning("[ADVANCE] CompleteTopicAndDecrement() called directly - should use TryAdvanceAfterScoring!");
            return CompleteTopicAndDecrementInternal();
        }

        /// <summary>
        /// Check if we can start a new topic.
        /// GATING: Returns false if remaining is 0 (must handle season end first).
        /// </summary>
        private bool CanStartNewTopic()
        {
            if (_remainingTopics <= 0)
            {
                if (DEV_VERBOSE) Debug.Log("[SeasonProgress] GATING: Cannot start new topic - remaining is 0");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Increment retry count for current expression.
        /// Returns true if should skip to next (exceeded max retries).
        /// </summary>
        private bool IncrementRetryAndCheckSkip()
        {
            _retryCount++;
            if (DEV_VERBOSE) Debug.Log($"[SeasonProgress] Retry #{_retryCount}/{MAX_RETRIES} for current expression");

            if (_retryCount >= MAX_RETRIES)
            {
                if (DEV_VERBOSE) Debug.Log("[SeasonProgress] MAX RETRIES reached - will skip to next topic");
                return true;  // Should skip
            }
            return false;  // Continue retrying
        }

        /// <summary>
        /// Try to restore session from PlayerPrefs.
        /// Returns true if valid saved session exists.
        /// </summary>
        private bool TryRestoreSeasonProgress()
        {
            if (!PlayerPrefs.HasKey(PREFS_SEASON_ID))
                return false;

            int savedSeasonId = PlayerPrefs.GetInt(PREFS_SEASON_ID, 0);
            int savedAnnounced = PlayerPrefs.GetInt(PREFS_ANNOUNCED, 0);
            int savedRemaining = PlayerPrefs.GetInt(PREFS_REMAINING, 0);
            int savedExpIndex = PlayerPrefs.GetInt(PREFS_EXPRESSION_INDEX, -1);

            // Only restore if there's meaningful progress
            if (savedAnnounced > 0 && savedRemaining > 0 && savedRemaining < savedAnnounced)
            {
                _currentSeasonIndex = savedSeasonId;
                _announcedTotalTopics = savedAnnounced;
                _remainingTopics = savedRemaining;
                _currentExpressionIndex = savedExpIndex;

                if (DEV_VERBOSE) Debug.Log($"[SeasonProgress] RESTORED: Season {_currentSeasonIndex}, " +
                          $"announced={_announcedTotalTopics}, remaining={_remainingTopics}, expIdx={_currentExpressionIndex}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clear saved progress (on session end or user request).
        /// </summary>
        private void ClearSeasonProgress()
        {
            PlayerPrefs.DeleteKey(PREFS_SEASON_ID);
            PlayerPrefs.DeleteKey(PREFS_ANNOUNCED);
            PlayerPrefs.DeleteKey(PREFS_REMAINING);
            PlayerPrefs.DeleteKey(PREFS_EXPRESSION_INDEX);
            PlayerPrefs.Save();

            _announcedTotalTopics = 0;
            _remainingTopics = 0;
            _retryCount = 0;

            if (DEV_VERBOSE) Debug.Log("[SeasonProgress] CLEARED all progress");
        }

        /// <summary>
        /// Update progress UI - shows ONLY "진행: X/Y" (no remaining count, no attempt count).
        /// Visual only, no TTS.
        /// </summary>
        private void UpdateProgressUI()
        {
            if (_progressText == null) return;

            // Simple progress: current expression / total expressions
            if (_shuffledExpressions != null && _currentExpressionIndex >= 0)
            {
                int done = _currentExpressionIndex + 1;
                int total = _shuffledExpressions.Count;
                _progressText.text = $"진행: {done}/{total}";
            }
            else if (_announcedTotalTopics > 0)
            {
                int done = _announcedTotalTopics - _remainingTopics;
                _progressText.text = $"진행: {done}/{_announcedTotalTopics}";
            }
        }

        #endregion

        #region Topic Loading

        /// <summary>
        /// Load all topics from JSON file into _allTopics array.
        /// Call this once at session start.
        /// </summary>
        private void LoadAllTopics()
        {
            try
            {
                TextAsset jsonFile = Resources.Load<TextAsset>("topics");
                if (jsonFile == null)
                {
                    Debug.LogError("[TutorRoom] topics.json not found!");
                    _allTopics = new TopicData[] { CreateFallbackTopic() };
                    return;
                }

                TopicsFile data = JsonUtility.FromJson<TopicsFile>(jsonFile.text);
                if (data?.topics == null || data.topics.Length == 0)
                {
                    Debug.LogError("[TutorRoom] No topics found in JSON!");
                    _allTopics = new TopicData[] { CreateFallbackTopic() };
                    return;
                }

                _allTopics = data.topics;
                Debug.Log($"[TutorRoom] Loaded {_allTopics.Length} topics/seasons");
            }
            catch (Exception e)
            {
                Debug.LogError($"[TutorRoom] Error loading topics: {e.Message}");
                _allTopics = new TopicData[] { CreateFallbackTopic() };
            }
        }

        /// <summary>
        /// Get the current season's topic.
        /// </summary>
        private TopicData GetCurrentSeasonTopic()
        {
            if (_allTopics == null || _allTopics.Length == 0)
            {
                LoadAllTopics();
            }

            if (_currentSeasonIndex >= _allTopics.Length)
            {
                _currentSeasonIndex = 0;  // Wrap around
            }

            return _allTopics[_currentSeasonIndex];
        }

        /// <summary>
        /// Advance to next season. Returns true if there are more seasons.
        /// </summary>
        private bool AdvanceToNextSeason()
        {
            _currentSeasonIndex++;
            if (_currentSeasonIndex >= _allTopics.Length)
            {
                Debug.Log("[TutorRoom] All seasons completed, wrapping to first");
                _currentSeasonIndex = 0;
                return false;  // Wrapped around
            }
            return true;
        }

        private TopicData CreateFallbackTopic()
        {
            return new TopicData
            {
                id = "fallback",
                topic = "기본 인사",
                topicEn = "Basic Greetings",
                expressions = new Expression[]
                {
                    new Expression { korean = "안녕하세요", english = "Hello" },
                    new Expression { korean = "감사합니다", english = "Thank you" },
                    new Expression { korean = "죄송합니다", english = "I'm sorry" }
                }
            };
        }

        private void ShuffleExpressions()
        {
            if (_currentTopic?.expressions == null) return;

            _shuffledExpressions = new List<Expression>(_currentTopic.expressions);

            // Fisher-Yates shuffle
            System.Random rng = new System.Random();
            int n = _shuffledExpressions.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                var temp = _shuffledExpressions[k];
                _shuffledExpressions[k] = _shuffledExpressions[n];
                _shuffledExpressions[n] = temp;
            }

            _currentExpressionIndex = -1;
            Debug.Log($"[TutorRoom] Shuffled {_shuffledExpressions.Count} expressions");
        }

        private bool AdvanceToNextExpression()
        {
            _currentExpressionIndex++;

            if (_shuffledExpressions == null || _currentExpressionIndex >= _shuffledExpressions.Count)
            {
                Debug.Log("[TutorRoom] All expressions complete, picking new topic");
                return false;
            }

            _currentExpression = _shuffledExpressions[_currentExpressionIndex];
            UpdateProgressUI();
            return true;
        }

        // NOTE: UpdateProgressUI() moved to Season Progress Management region (merged)

        #endregion

        #region Button Handler

        private void OnMainButtonClick()
        {
            // FIRST LOG - confirms onClick listener was invoked
            UnityEngine.Debug.Log("[TutorRoom] *** BUTTON CLICKED *** OnMainButtonClick invoked!");

            // Debounce
            float now = Time.unscaledTime;
            if (now - _lastButtonPressTime < _buttonDebounceTime)
            {
                Debug.Log("[TutorRoom] Button debounced");
                return;
            }
            _lastButtonPressTime = now;

            Debug.Log($"[TutorRoom] Button clicked in state: {_currentState}, connected: {_isBackendConnected}");

            // Handle retry mode when backend is not connected
            if (!_isBackendConnected && (_currentState == TutorState.Boot || _currentState == TutorState.Home))
            {
                Debug.Log("[TutorRoom] Retrying backend connection...");
                StartCoroutine(RetryConnectionCoroutine());
                return;
            }

            switch (_currentState)
            {
                case TutorState.Boot:
                case TutorState.Home:
                    StartSession();
                    break;

                case TutorState.Recording:
                    StopRecording();
                    break;

                default:
                    Debug.Log($"[TutorRoom] Button ignored in state: {_currentState}");
                    break;
            }
        }

        /// <summary>
        /// Retry backend connection when user taps "재시도" button.
        /// </summary>
        private IEnumerator RetryConnectionCoroutine()
        {
            // Show loading state
            if (_mainButtonText != null) _mainButtonText.text = "연결 중...";
            if (_mainButton != null) _mainButton.interactable = false;
            if (_hintText != null) _hintText.text = "서버에 연결하는 중...";

            yield return new WaitForSeconds(0.3f);  // Brief delay for UI feedback

            bool isReachable = false;
            string healthError = null;

            yield return ApiClient.CheckHealth(
                response => {
                    isReachable = true;
                    _isBackendConnected = true;
                    Debug.Log($"[TutorRoom] Retry successful! Mode: {response.mode}");
                },
                error => {
                    healthError = error;
                    _isBackendConnected = false;
                }
            );

            if (isReachable)
            {
                // Success - reset to normal Home state
                if (_hintText != null)
                {
                    _hintText.text = "";
                    _hintText.color = Color.white;
                }
                if (_statusText != null) _statusText.text = "";
                if (_mainButtonText != null) _mainButtonText.text = "시작";
                if (_mainButton != null) _mainButton.interactable = true;
                HideLessonUI();  // Clean first screen
                SetState(TutorState.Home);
            }
            else
            {
                // Still failing - show error
                Debug.LogError($"[TutorRoom] Retry failed: {healthError}");
                if (_hintText != null)
                {
                    _hintText.text = "서버 연결 실패\n탭하여 재시도";
                    _hintText.color = Color.red;
                }
                if (_mainButtonText != null) _mainButtonText.text = "재시도";
                if (_mainButton != null) _mainButton.interactable = true;
            }
        }

        /// <summary>
        /// Handler for STOP AI button click.
        /// Immediately stops AI speech and resets to idle state.
        /// </summary>
        private void OnStopAiButtonClick()
        {
            Debug.Log($"[TutorRoom] STOP AI clicked in state: {_currentState}");
            StopAI();
        }

        /// <summary>
        /// Stop all AI speech immediately and reset to Home state.
        /// Safe to call multiple times. Unblocks any waiting coroutines.
        /// This is the ONLY place that should call ResetSessionToHome().
        /// </summary>
        public void StopAI()
        {
            Debug.Log("[TutorRoom] StopAI() called - resetting to Home state");
            ResetSessionToHome();
        }

        /// <summary>
        /// BULLETPROOF session reset - the SINGLE source of truth for stopping everything.
        /// Guarantees we return to a clean Home state regardless of current state.
        /// Increments session version to invalidate all running coroutines.
        /// </summary>
        public void ResetSessionToHome()
        {
            // 1. INCREMENT SESSION VERSION (causes all coroutines to bail out)
            _sessionVersion++;
            _currentAttempt = null;  // Invalidate current attempt
            Debug.Log($"[ADVANCE] ResetSessionToHome() - session version now {_sessionVersion}");

            // 2. Force TTS completion (unblocks PlayTtsAndWait coroutine)
            ForceTtsComplete();

            // 3. Force stop TTS playback (increments playId to cancel all waiting coroutines)
            if (TtsPlayer.Instance != null)
            {
                TtsPlayer.Instance.ForceStop();
            }

            // 4. Stop recording if active (idempotent - safe to call even if not recording)
            if (MicRecorder.Instance != null && MicRecorder.Instance.IsRecording)
            {
                MicRecorder.Instance.StopRecordingGracefully();
            }

            // 5. Stop the main session coroutine
            if (_mainCoroutine != null)
            {
                StopCoroutine(_mainCoroutine);
                _mainCoroutine = null;
            }

            // 6. Hide loading indicator
            ShowLoading(false);

            // 7. Clear comparison panel
            ClearComparisonPanel();

            // 8. Reset Grok context
            _grokContext = null;

            // 8.5. Reset silence counter
            _consecutiveSilenceCount = 0;

            // 9. Reset to Home state
            SetState(TutorState.Home);

            // 10. Hide lesson UI to return to clean first screen
            HideLessonUI();

            // 11. Recording UI cleanup
            if (_recordingIndicator != null) _recordingIndicator.gameObject.SetActive(false);
            if (_recordingProgress != null) _recordingProgress.gameObject.SetActive(false);

            Debug.Log("[TutorRoom] ResetSessionToHome() complete - clean Home state");
        }

        /// <summary>
        /// Check if the current session is still valid.
        /// Call this at the top of coroutine loops to bail out if session was reset.
        /// </summary>
        /// <param name="capturedVersion">The session version captured at coroutine start</param>
        /// <returns>True if session is still valid, false if should bail out</returns>
        private bool IsSessionValid(int capturedVersion)
        {
            if (capturedVersion != _sessionVersion)
            {
                Debug.Log($"[TutorRoom] Session invalidated (captured={capturedVersion}, current={_sessionVersion}) - bailing out");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Update activity timestamp when user does something meaningful.
        /// Call this when: user speaks valid transcript, clicks buttons, etc.
        /// </summary>
        private void UpdateActivityTime()
        {
            _lastActivityTime = Time.time;
            _consecutiveSilenceCount = 0;  // Reset silence count on any valid activity
        }

        /// <summary>
        /// Record a silence/invalid event. Returns true if session should end.
        /// </summary>
        /// <returns>True if max consecutive silences reached and session should end</returns>
        private bool RecordSilenceEvent()
        {
            _consecutiveSilenceCount++;
            Debug.Log($"[IDLE] Silence event #{_consecutiveSilenceCount}/{MAX_CONSECUTIVE_SILENCES}");

            if (_consecutiveSilenceCount >= MAX_CONSECUTIVE_SILENCES)
            {
                Debug.Log("[IDLE] Max consecutive silences reached - triggering session end");
                return true;  // Should end session
            }
            return false;  // Can continue with retry/warning
        }

        /// <summary>
        /// Check if session has been idle too long.
        /// </summary>
        private bool IsIdleTimeout()
        {
            float idleTime = Time.time - _lastActivityTime;
            return idleTime >= IDLE_TIMEOUT_SECONDS;
        }

        /// <summary>
        /// Handle idle termination - end the session gracefully due to user inactivity.
        /// </summary>
        private IEnumerator HandleIdleTermination(int capturedSessionVersion)
        {
            if (!IsSessionValid(capturedSessionVersion)) yield break;

            Debug.Log("[IDLE] Terminating session due to inactivity");

            // Show goodbye message
            if (_hintText != null)
            {
                _hintText.text = "오늘은 여기까지 할게요. 다음에 또 만나요!";
                _hintText.color = Color.white;
            }

            // Optional: Play goodbye TTS
            string goodbyeMessage = "조용하네요. 오늘은 여기까지 할게요. 다음에 또 만나요!";
            yield return PlayTtsAndWait(goodbyeMessage);

            if (!IsSessionValid(capturedSessionVersion)) yield break;

            // Clean transition to Home
            ResetSessionToHome();
        }

        #endregion

        #region Main Flow

        private void StartSession()
        {
            if (_mainCoroutine != null)
            {
                StopCoroutine(_mainCoroutine);
            }
            // Reset idle tracking on session start
            _lastActivityTime = Time.time;
            _consecutiveSilenceCount = 0;
            _mainCoroutine = StartCoroutine(SessionCoroutine());
        }

        private IEnumerator SessionCoroutine()
        {
            // CAPTURE SESSION VERSION - all checks use this to bail out if reset
            int mySessionVersion = _sessionVersion;

            // PRE-CHECK: Verify microphone permission before starting
            if (MicRecorder.Instance != null && !MicRecorder.Instance.HasPermission)
            {
                Debug.Log("[TutorRoom] Mic permission not granted, requesting...");
                MicRecorder.Instance.CheckPermission();

                // Wait briefly for permission dialog
                float permWaitTime = 0f;
                while (!MicRecorder.Instance.HasPermission && permWaitTime < 5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    permWaitTime += 0.5f;
                }

                // Check if permission was denied
                if (!MicRecorder.Instance.HasPermission)
                {
                    Debug.LogWarning("[TutorRoom] Microphone permission denied - cannot start lesson");
                    if (_hintText != null)
                    {
                        _hintText.gameObject.SetActive(true);
                        _hintText.text = "마이크 권한이 필요합니다\n설정에서 권한을 허용해주세요";
                        _hintText.color = Color.red;
                    }
                    if (_statusText != null)
                    {
                        _statusText.gameObject.SetActive(true);
                        _statusText.text = "권한 오류";
                    }
                    SetState(TutorState.Home);
                    yield break;
                }
            }

            // Show lesson UI when session starts (first screen was clean)
            ShowLessonUI();

            // 1. Load all topics once
            LoadAllTopics();
            _currentSeasonIndex = 0;

            // Main season loop
            while (IsSessionValid(mySessionVersion))
            {
                // Get current season's topic
                _currentTopic = GetCurrentSeasonTopic();
                ShuffleExpressions();

                // SEASON PROGRESS: Initialize the authoritative counter
                int expressionCount = _shuffledExpressions?.Count ?? 0;
                InitializeSeasonProgress(expressionCount);

                // Initialize Grok context for this season
                _grokContext = new GrokClient.TutorMessageContext
                {
                    seasonTitle = _currentTopic.topic,
                    targetPhrase = "",
                    attemptNumber = 1,
                    accuracyPercent = 0,
                    mismatchSummary = "",
                    state = GrokClient.TutorState.INTRO
                };

                // Update topic label with remaining count
                if (_topicLabel != null)
                {
                    _topicLabel.text = $"시즌 {_currentTopic.season}: {_currentTopic.topic}";
                }
                UpdateProgressUI();

                // Clear transcript and comparison UI
                if (_transcriptText != null) _transcriptText.text = "";
                if (_targetSentenceText != null) _targetSentenceText.text = "";
                if (_comparisonText != null) _comparisonText.text = "";
                ClearComparisonPanel();

                // 2. Play season theme intro TTS using Grok for dynamic line
                SetState(TutorState.SeasonIntro);

                // Generate intro line LOCALLY (not via Grok) for reliable TTS
                // Format: "안녕하세요! {greeting} 오늘 배울 주제는 "{seasonTitle}"이고 {topicCount}개의 표현을 배워볼거예요! 시작할게요!"
                string themeIntro = GrokClient.BuildIntroLine(_currentTopic.topic, expressionCount);
                Debug.Log($"[STATE] SeasonIntro v={_sessionVersion}: intro='{themeIntro}'");
                if (DEV_VERBOSE) Debug.Log($"[SeasonProgress] Season start: {_announcedTotalTopics} topics");
                yield return PlayTtsAndWait(themeIntro);
                if (!IsSessionValid(mySessionVersion)) yield break;

                // 3. Expression loop for this season - GATED by remaining counter
                while (IsSessionValid(mySessionVersion) && CanStartNewTopic() && AdvanceToNextExpression())
                {
                    // Clear transcript and comparison for new expression
                    if (_transcriptText != null) _transcriptText.text = "";
                    if (_targetSentenceText != null) _targetSentenceText.text = "";
                    if (_comparisonText != null) _comparisonText.text = "";
                    ClearComparisonPanel();
                    UpdateProgressUI();

                    // Reset retry count for new expression
                    _retryCount = 0;

                    // Update Grok context for new expression
                    if (_grokContext != null)
                    {
                        _grokContext.targetPhrase = _currentExpression.korean;
                        _grokContext.attemptNumber = 1;
                        _grokContext.accuracyPercent = 0;
                        _grokContext.mismatchSummary = "";
                    }

                    // A. Generate PROMPT via Grok (prompts user to listen and repeat)
                    SetState(TutorState.TopicPrompt);
                    string promptLine = null;
                    yield return GenerateGrokLineAsync(GrokClient.TutorState.PROMPT, line => promptLine = line);
                    if (!IsSessionValid(mySessionVersion)) yield break;

                    if (DEV_VERBOSE) Debug.Log($"[SeasonProgress] Expression #{_currentExpressionIndex + 1}, remaining={_remainingTopics}");
                    yield return PlayTtsAndWait(promptLine);
                    if (!IsSessionValid(mySessionVersion)) yield break;

                    // B. Play target TTS 3 times
                    SetState(TutorState.TopicPrompt);
                    if (_hintText != null) _hintText.text = "잘 들어봐~";

                    for (_ttsRepeatIndex = 1; _ttsRepeatIndex <= _ttsRepeatCount; _ttsRepeatIndex++)
                    {
                        if (!IsSessionValid(mySessionVersion)) yield break;
                        UpdateStatusUI();
                        yield return PlayTtsAndWait(_currentExpression.korean);

                        if (_ttsRepeatIndex < _ttsRepeatCount)
                        {
                            yield return new WaitForSeconds(0.3f);
                        }
                    }

                    // Retry loop for this expression
                    bool expressionCompleted = false;
                    while (!expressionCompleted && IsSessionValid(mySessionVersion))
                    {
                        // === CREATE ATTEMPT CONTEXT (immutable snapshot) ===
                        _currentAttempt = CreateAttemptContext();
                        AttemptContext attempt = _currentAttempt;

                        // GUARD: Validate target phrase exists
                        if (string.IsNullOrEmpty(attempt.targetPhrase))
                        {
                            Debug.LogError($"[ATTEMPT] FATAL: Empty targetPhrase is internal bug! {attempt}");
                            yield break;
                        }

                        // C. Start recording
                        yield return new WaitForSeconds(_delayBeforeRecording);
                        if (!IsSessionValid(mySessionVersion)) yield break;

                        if (_hintText != null) _hintText.text = "따라 말해봐!";
                        SetState(TutorState.Recording);

                        bool recordingStarted = MicRecorder.Instance.StartRecording();
                        if (!recordingStarted)
                        {
                            Debug.LogError("[ATTEMPT] Failed to start recording");
                            SetState(TutorState.Home);
                            yield break;
                        }

                        // Wait for recording to complete (via callback) with watchdog
                        float recordingWatchdog = 0f;
                        while (_currentState == TutorState.Recording && recordingWatchdog < 30f && IsSessionValid(mySessionVersion))
                        {
                            yield return null;
                            recordingWatchdog += Time.deltaTime;
                        }
                        if (!IsSessionValid(mySessionVersion)) yield break;

                        if (recordingWatchdog >= 30f)
                        {
                            Debug.LogWarning($"[ATTEMPT] WATCHDOG: Recording timeout - forcing stop. {attempt}");
                            attempt.timedOut = true;
                            attempt.accuracyPercent = 0;  // CRITICAL: Force 0 on timeout
                            MicRecorder.Instance?.StopRecordingGracefully();
                            yield return new WaitForSeconds(0.5f);
                        }

                        // Recording complete - STT processing handled in OnRecordingComplete
                        // Wait for STT to complete with watchdog
                        float sttWatchdog = 0f;
                        while (_currentState == TutorState.Scoring && sttWatchdog < _sttWatchdogTimeout && IsSessionValid(mySessionVersion))
                        {
                            yield return null;
                            sttWatchdog += Time.deltaTime;
                        }
                        if (!IsSessionValid(mySessionVersion)) yield break;

                        if (_currentState == TutorState.Scoring)
                        {
                            Debug.LogWarning($"[STT] WATCHDOG: STT timeout after {sttWatchdog:F1}s. {attempt}");
                            attempt.timedOut = true;
                            attempt.transcriptEmpty = true;
                            attempt.accuracyPercent = 0;  // CRITICAL: Force 0 on timeout
                            _transcriptText_value = "";
                            _lastAttemptCorrect = false;
                            _lastAccuracyPercent = 0;
                            SetState(TutorState.Feedback);
                        }

                        // Wait for feedback processing with watchdog
                        float feedbackWatchdog = 0f;
                        while (_currentState == TutorState.Scoring && feedbackWatchdog < _feedbackWatchdogTimeout && IsSessionValid(mySessionVersion))
                        {
                            yield return null;
                            feedbackWatchdog += Time.deltaTime;
                        }
                        if (!IsSessionValid(mySessionVersion)) yield break;

                        if (_currentState == TutorState.Scoring)
                        {
                            Debug.LogWarning($"[SCORE] WATCHDOG: Feedback timeout after {feedbackWatchdog:F1}s. {attempt}");
                            attempt.timedOut = true;
                            attempt.accuracyPercent = 0;  // CRITICAL: Force 0 on timeout
                            _lastAttemptCorrect = false;
                            _lastAccuracyPercent = 0;
                            SetState(TutorState.Feedback);
                        }

                        // Sync attempt context with class fields (in case callback updated them)
                        if (!attempt.timedOut)
                        {
                            attempt.transcript = _transcriptText_value ?? "";
                            attempt.transcriptEmpty = string.IsNullOrEmpty(_transcriptText_value);
                            attempt.accuracyPercent = _lastAccuracyPercent;
                            attempt.mismatchSummary = _lastMismatchParts ?? "";
                        }

                        Debug.Log($"[SCORE] Final: {attempt}");

                        // D-E. Display comparison UI and speak feedback TTS
                        SetState(TutorState.Feedback);

                        // Show target sentence in UI
                        if (_targetSentenceText != null)
                        {
                            _targetSentenceText.text = $"정답: {_currentExpression.korean}";
                        }

                        // Show comparison/mismatch details with accuracy
                        if (_comparisonText != null)
                        {
                            if (attempt.timedOut || attempt.transcriptEmpty)
                            {
                                _comparisonText.text = "(음성 인식 실패 - 다시 시도해 주세요)";
                            }
                            else if (attempt.accuracyPercent >= _accuracyCorrectThreshold)
                            {
                                _comparisonText.text = $"정확해요! ({attempt.accuracyPercent}%)";
                            }
                            else if (attempt.accuracyPercent >= _accuracyHintThreshold)
                            {
                                // Partial - show mismatch parts
                                string mismatchInfo = !string.IsNullOrEmpty(attempt.mismatchSummary)
                                    ? $"거의 맞았어! ({attempt.accuracyPercent}%) 틀린 부분: {attempt.mismatchSummary}"
                                    : $"거의 맞았어! ({attempt.accuracyPercent}%)";
                                _comparisonText.text = mismatchInfo;
                            }
                            else
                            {
                                // Wrong - show detailed mismatch
                                string mismatchInfo = !string.IsNullOrEmpty(attempt.mismatchSummary)
                                    ? $"틀린 부분: {attempt.mismatchSummary} ({attempt.accuracyPercent}%)"
                                    : $"발음이 다릅니다 ({attempt.accuracyPercent}%)";
                                _comparisonText.text = mismatchInfo;
                            }
                        }

                        // Update TextMeshPro comparison panel with highlighted diff
                        UpdateComparisonPanel(_currentExpression.korean, attempt.transcript, attempt.accuracyPercent);

                        // Update Grok context with results
                        if (_grokContext != null)
                        {
                            _grokContext.accuracyPercent = attempt.accuracyPercent;
                            _grokContext.mismatchSummary = attempt.mismatchSummary;
                            _grokContext.attemptNumber = _retryCount + 1;
                        }

                        // === DETERMINISTIC SCORING DECISION ===
                        Debug.Log($"[SCORE] acc={attempt.accuracyPercent}% timeout={attempt.timedOut} transcriptLen={attempt.transcript?.Length ?? 0} targetLen={attempt.targetPhrase?.Length ?? 0}");

                        var (shouldAdvance, shouldContinueSeason) = TryAdvanceAfterScoring(attempt);

                        if (shouldAdvance)
                        {
                            // PASS or EXCELLENT: accuracy >= 65%
                            bool isExcellent = attempt.accuracyPercent >= _accuracyCorrectThreshold;

                            if (isExcellent)
                            {
                                // === EXCELLENT (>=90%): Strong praise + ask to move on ===
                                Debug.Log($"[ADVANCE] decision=EXCELLENT acc={attempt.accuracyPercent}%");
                                string excellentFeedback = null;
                                yield return GenerateGrokLineAsync(GrokClient.TutorState.FEEDBACK_EXCELLENT, line => excellentFeedback = line);
                                if (!IsSessionValid(mySessionVersion)) yield break;

                                yield return PlayTtsAndWait(excellentFeedback);
                                if (!IsSessionValid(mySessionVersion)) yield break;

                                // Wait for yes/no response with 5s timeout (default: YES)
                                yield return WaitForYesNoResponse(5f);
                                if (!IsSessionValid(mySessionVersion)) yield break;

                                // For excellent, always advance (yes/no just affects messaging)
                                int moveOnIntent = ParseYesNoIntent(_transcriptText_value);
                                Debug.Log($"[ADVANCE] MoveOn intent={moveOnIntent} (1=yes, -1=no, 0=unclear)");

                                // No extra retry for excellent - proceed to advance
                                yield return new WaitForSeconds(_delayAfterFeedback);
                                expressionCompleted = true;
                            }
                            else
                            {
                                // === PASS (65-89%): Praise + coaching + ask for polish ===
                                Debug.Log($"[ADVANCE] decision=PASS acc={attempt.accuracyPercent}%");
                                string passFeedback = null;
                                yield return GenerateGrokLineAsync(GrokClient.TutorState.FEEDBACK_PASS, line => passFeedback = line);
                                if (!IsSessionValid(mySessionVersion)) yield break;

                                yield return PlayTtsAndWait(passFeedback);
                                if (!IsSessionValid(mySessionVersion)) yield break;

                                // Wait for yes/no: "한 번만 더 매끈하게 해볼까요?" (5s timeout, default YES)
                                yield return WaitForYesNoResponse(5f);
                                if (!IsSessionValid(mySessionVersion)) yield break;

                                int polishIntent = ParseYesNoIntent(_transcriptText_value);
                                Debug.Log($"[ADVANCE] Polish intent={polishIntent} (1=yes, -1=no, 0=unclear)");

                                if (polishIntent != -1 && !attempt.extraGuidedRepeatDone)
                                {
                                    // YES or unclear: One extra guided repeat for polish
                                    attempt.extraGuidedRepeatDone = true;
                                    if (_hintText != null) _hintText.text = "한 번 더 들어봐~";
                                    yield return PlayTtsAndWait(_currentExpression.korean);
                                    if (!IsSessionValid(mySessionVersion)) yield break;

                                    // Do one more recording for polish (but advance regardless of result)
                                    yield return new WaitForSeconds(_delayBeforeRecording);
                                    if (!IsSessionValid(mySessionVersion)) yield break;

                                    if (_hintText != null) _hintText.text = "따라 말해봐!";
                                    SetState(TutorState.Recording);
                                    MicRecorder.Instance.StartRecording();

                                    // Wait for polish recording (15s max)
                                    float polishWatchdog = 0f;
                                    while (_currentState == TutorState.Recording && polishWatchdog < 15f && IsSessionValid(mySessionVersion))
                                    {
                                        yield return null;
                                        polishWatchdog += Time.deltaTime;
                                    }
                                    if (!IsSessionValid(mySessionVersion)) yield break;

                                    // Brief pause after polish attempt
                                    yield return new WaitForSeconds(1f);
                                }

                                yield return new WaitForSeconds(_delayAfterFeedback);
                                expressionCompleted = true;
                            }

                            Debug.Log($"[ADVANCE] Topic completed. shouldContinueSeason={shouldContinueSeason}");

                            if (!shouldContinueSeason)
                            {
                                // Season complete - break to season end handling
                                break;
                            }

                            // Voice-driven continuation: ask user if they want to continue
                            // This gives user a natural exit point after each sentence
                            yield return VoiceDrivenContinuation(6f);
                            if (!IsSessionValid(mySessionVersion)) yield break;

                            // If we reach here, user chose POSITIVE (continue)
                            // The loop will naturally advance to next expression
                        }
                        else
                        {
                            // === FAIL: accuracy < 65% OR timeout/empty - RETRY ===
                            Debug.Log($"[ADVANCE] decision=FAIL acc={attempt.accuracyPercent}% timeout={attempt.timedOut} empty={attempt.transcriptEmpty}");

                            // Check if we should skip after max retries
                            bool shouldSkip = IncrementRetryAndCheckSkip();

                            if (shouldSkip)
                            {
                                // MAX RETRIES EXCEEDED - force advance via special handling
                                Debug.Log($"[ADVANCE] MAX RETRIES ({MAX_RETRIES}) exceeded - forcing advance");
                                string skipFeedback = "괜찮아! 다음 문장으로 넘어갈게요.";
                                yield return PlayTtsAndWait(skipFeedback);
                                if (!IsSessionValid(mySessionVersion)) yield break;
                                yield return new WaitForSeconds(_delayAfterFeedback);

                                // Force advance by completing topic
                                expressionCompleted = true;
                                bool skipContinue = CompleteTopicAndDecrementInternal();
                                Debug.Log($"[ADVANCE] SKIP completed. shouldContinue={skipContinue}");

                                if (!skipContinue)
                                {
                                    break;
                                }

                                // Voice-driven continuation after skip
                                yield return VoiceDrivenContinuation(6f);
                                if (!IsSessionValid(mySessionVersion)) yield break;
                            }
                            else
                            {
                                // Continue retrying - use Grok for fail feedback
                                SetState(TutorState.RetryPrompt);

                                string retryFeedback = null;
                                yield return GenerateGrokLineAsync(GrokClient.TutorState.FEEDBACK_FAIL, line => retryFeedback = line);
                                if (!IsSessionValid(mySessionVersion)) yield break;

                                yield return PlayTtsAndWait(retryFeedback);
                                if (!IsSessionValid(mySessionVersion)) yield break;

                                yield return new WaitForSeconds(0.3f);

                                // Play target TTS once for retry
                                if (_hintText != null) _hintText.text = "잘 들어봐~";
                                yield return PlayTtsAndWait(_currentExpression.korean);
                                if (!IsSessionValid(mySessionVersion)) yield break;

                                // Loop back to recording - expressionCompleted stays false
                                Debug.Log($"[ATTEMPT] Looping back for retry #{_retryCount + 1}");
                            }
                        }
                    }

                    // Clear comparison UI for next expression
                    if (_comparisonText != null) _comparisonText.text = "";
                    ClearComparisonPanel();
                    UpdateProgressUI();
                }

                // Check session validity before season end
                if (!IsSessionValid(mySessionVersion)) yield break;

                // 4. Season complete (remaining reached 0) - use Grok for season end prompt
                if (DEV_VERBOSE) Debug.Log($"[SeasonProgress] SEASON COMPLETE! announced={_announcedTotalTopics}, remaining={_remainingTopics}");
                SetState(TutorState.SeasonEndPrompt);

                // Use Grok for season end prompt (must end with "다음으로 넘길까요?")
                string seasonEndLine = null;
                yield return GenerateGrokLineAsync(GrokClient.TutorState.SEASON_END, line => seasonEndLine = line);
                if (!IsSessionValid(mySessionVersion)) yield break;

                if (DEV_VERBOSE) Debug.Log($"[SeasonProgress] Season end prompt: {seasonEndLine}");
                yield return PlayTtsAndWait(seasonEndLine);
                if (!IsSessionValid(mySessionVersion)) yield break;

                // Record user's response for YES/NO intent (with 8s auto-timeout to YES)
                yield return new WaitForSeconds(_delayBeforeRecording);
                if (!IsSessionValid(mySessionVersion)) yield break;

                // Clear current expression so ProcessRecordingCoroutine knows to skip feedback
                _currentExpression = null;

                if (_hintText != null) _hintText.text = "네 또는 아니요~";
                SetState(TutorState.Recording);

                bool yesNoRecordingStarted = MicRecorder.Instance.StartRecording();
                if (!yesNoRecordingStarted)
                {
                    Debug.LogWarning("[TutorRoom] Failed to start recording for YES/NO - defaulting to YES");
                    // Default to YES on failure
                }
                else
                {
                    // Wait for recording with 8 second timeout
                    float yesNoTimeout = 0f;
                    while (_currentState == TutorState.Recording && yesNoTimeout < 8f && IsSessionValid(mySessionVersion))
                    {
                        yield return null;
                        yesNoTimeout += Time.deltaTime;
                    }
                    if (!IsSessionValid(mySessionVersion)) yield break;

                    if (yesNoTimeout >= 8f)
                    {
                        if (DEV_VERBOSE) Debug.Log("[SeasonProgress] YES/NO timeout - defaulting to YES");
                        MicRecorder.Instance?.StopRecordingGracefully();
                    }

                    // Wait for STT with timeout
                    float sttTimeout = 0f;
                    while (_currentState == TutorState.Scoring && sttTimeout < 5f && IsSessionValid(mySessionVersion))
                    {
                        yield return null;
                        sttTimeout += Time.deltaTime;
                    }
                    if (!IsSessionValid(mySessionVersion)) yield break;
                }

                // Parse YES/NO intent (timeout defaults to unclear -> YES)
                int intent = ParseYesNoIntent(_transcriptText_value);
                if (DEV_VERBOSE) Debug.Log($"[SeasonProgress] User response: '{_transcriptText_value}' -> intent={intent} (1=yes, -1=no, 0=unclear)");

                if (intent == -1)
                {
                    // NO - end session cleanly
                    if (DEV_VERBOSE) Debug.Log("[SeasonProgress] USER SAID NO -> ending session");
                    SetState(TutorState.Feedback);
                    string goodbye = "알겠어! 오늘 열심히 했어. 다음에 또 봐!";
                    yield return PlayTtsAndWait(goodbye);
                    if (!IsSessionValid(mySessionVersion)) yield break;

                    // Clear progress on session end
                    ClearSeasonProgress();
                    break;  // Exit main loop
                }
                else
                {
                    // YES or unclear - continue to next season
                    if (DEV_VERBOSE) Debug.Log("[SeasonProgress] USER SAID YES/UNCLEAR -> loading next season");
                    SetState(TutorState.Feedback);

                    if (AdvanceToNextSeason())
                    {
                        if (DEV_VERBOSE) Debug.Log($"[SeasonProgress] NEXT SEASON LOADED: season {_currentSeasonIndex}");
                        string nextSeason = "좋아! 다음 시즌 시작할게!";
                        yield return PlayTtsAndWait(nextSeason);
                        if (!IsSessionValid(mySessionVersion)) yield break;
                        // Note: InitializeSeasonProgress will be called at the top of the loop
                    }
                    else
                    {
                        // Wrapped around to first season
                        if (DEV_VERBOSE) Debug.Log("[SeasonProgress] ALL SEASONS COMPLETE - wrapping to first");
                        string restart = "모든 시즌을 다 끝냈어! 처음부터 다시 시작할게!";
                        yield return PlayTtsAndWait(restart);
                        if (!IsSessionValid(mySessionVersion)) yield break;
                    }
                    // Continue main loop to next season (InitializeSeasonProgress called at top)
                }
            }

            // Session ended (only reached if loop exits normally via NO response)
            if (IsSessionValid(mySessionVersion))
            {
                if (DEV_VERBOSE) Debug.Log("[SeasonProgress] SESSION ENDED normally");
                ClearSeasonProgress();
                SetState(TutorState.End);
                yield return new WaitForSeconds(1.0f);  // Brief pause at End
                SetState(TutorState.Home);
                HideLessonUI();
            }
        }

        private void StopRecording()
        {
            if (_currentState == TutorState.Recording && MicRecorder.Instance != null)
            {
                Debug.Log("[TutorRoom] Manual stop recording");
                MicRecorder.Instance.StopRecordingGracefully();
            }
        }

        #endregion

        #region Grok Integration

        /// <summary>
        /// Generate a tutor line using Grok API with fallback.
        /// Updates _grokContext.lastMessageText to avoid repetition.
        /// </summary>
        /// <param name="branch">The branch type for the tutor line</param>
        /// <param name="onComplete">Callback with generated line</param>
        private IEnumerator GenerateGrokLineAsync(GrokClient.TutorState branch, Action<string> onComplete)
        {
            // Ensure context exists
            if (_grokContext == null)
            {
                _grokContext = new GrokClient.TutorMessageContext
                {
                    seasonTitle = _currentTopic?.topic ?? "",
                    targetPhrase = _currentExpression?.korean ?? "",
                    attemptNumber = 1,
                    accuracyPercent = 0,
                    mismatchSummary = ""
                };
            }

            // Update state
            _grokContext.state = branch;

            string generatedLine = null;
            string grokError = null;

            yield return GrokClient.GenerateTutorLineAsync(
                _grokContext,
                line => {
                    generatedLine = line;
                    Debug.Log($"[TutorRoom] Grok generated ({branch}): {line}");
                },
                error => {
                    grokError = error;
                    Debug.LogWarning($"[TutorRoom] Grok error ({branch}): {error}");
                }
            );

            // Store last message to avoid repetition
            if (!string.IsNullOrEmpty(generatedLine))
            {
                _grokContext.lastMessageText = generatedLine;
            }

            onComplete?.Invoke(generatedLine ?? GetFallbackLine(branch));
        }

        /// <summary>
        /// Get fallback line for branch when Grok fails.
        /// </summary>
        private string GetFallbackLine(GrokClient.TutorState branch)
        {
            return branch switch
            {
                GrokClient.TutorState.INTRO => GrokClient.BuildIntroLine(_currentTopic?.topic ?? "한국어", _shuffledExpressions?.Count ?? 5),
                GrokClient.TutorState.PROMPT => "자, 따라 해봐!",
                GrokClient.TutorState.FEEDBACK_EXCELLENT => "정말 잘했어요! 완전 현지인 같아요! 다음 걸로 넘어가 볼까요?",
                GrokClient.TutorState.FEEDBACK_PASS => "잘했어요! 거의 완벽해요! 한 번만 더 매끈하게 해볼까요?",
                GrokClient.TutorState.FEEDBACK_FAIL => "괜찮아요! 다시 한 번 해봐요. 제가 다시 읽어드릴게요.",
                GrokClient.TutorState.RETRY => "다시 한 번 해봐요! 잘 들어봐요!",
                GrokClient.TutorState.SEASON_END => "이번 시즌 끝! 잘했어요! 다음으로 넘길까요?",
                _ => "계속할까요?"
            };
        }

        #endregion

        #region TTS Playback

        // Flag to force TTS completion (set by StopAI)
        private bool _forceTtsComplete = false;

        /// <summary>
        /// Play TTS and wait for completion with robust timeout handling.
        /// CRITICAL: Must wait for audio to ACTUALLY finish before returning.
        /// Uses callback + isPlaying flag for completion detection.
        /// </summary>
        private IEnumerator PlayTtsAndWait(string text)
        {
            string textPreview = text.Length > 30 ? text.Substring(0, 30) + "..." : text;
            int ttsId = TtsPlayer.Instance?.GetInstanceID() ?? 0;
            Debug.Log($"[TTS] PlayTtsAndWait start id={ttsId} textLen={text.Length} text='{textPreview}' v={_sessionVersion}");

            _forceTtsComplete = false;

            // Skip TTS if backend is unreachable
            if (!ApiClient.IsBackendReachable)
            {
                Debug.LogWarning($"[TTS] skipped (offline) text='{textPreview}'");
                if (_hintText != null) _hintText.text = text;
                yield return new WaitForSeconds(0.5f);
                yield break;
            }

            bool ttsComplete = false;
            bool ttsError = false;
            string errorMessage = null;

            Action onComplete = () => {
                ttsComplete = true;
                Debug.Log($"[TTS] callback complete id={ttsId} text='{textPreview}'");
            };
            Action<string> onError = (err) => {
                ttsError = true;
                errorMessage = err;
                Debug.LogWarning($"[TTS] callback error id={ttsId} err={err}");
            };

            TtsPlayer.Instance.OnPlaybackComplete += onComplete;
            TtsPlayer.Instance.OnLoadError += onError;

            ShowLoading(true);
            TtsPlayer.Instance.PlayTts(text, 1, 1);

            // PHASE 1: Wait for load to complete (up to 10s for network)
            float loadTimeout = 10f;
            float loadElapsed = 0f;
            while (!TtsPlayer.Instance.HasError && !ttsError && !_forceTtsComplete &&
                   loadElapsed < loadTimeout)
            {
                // Check if load is complete (clip loaded and ready to play)
                if (TtsPlayer.Instance.CurrentClip != null)
                {
                    Debug.Log($"[TTS] loaded id={ttsId} clipLen={TtsPlayer.Instance.CurrentClip.length:F1}s");
                    break;
                }
                yield return null;
                loadElapsed += Time.deltaTime;
            }

            if (TtsPlayer.Instance.HasError || ttsError)
            {
                Debug.LogWarning($"[TTS] load failed id={ttsId} err={errorMessage ?? TtsPlayer.Instance.LastError}");
                ShowLoading(false);
                TtsPlayer.Instance.OnPlaybackComplete -= onComplete;
                TtsPlayer.Instance.OnLoadError -= onError;
                if (_hintText != null) _hintText.text = text;
                yield return new WaitForSeconds(0.5f);
                yield break;
            }

            ShowLoading(false);

            // PHASE 2: Wait for playback to complete
            // Timeout = clip length + buffer, minimum 8s for short clips
            float clipLen = TtsPlayer.Instance.CurrentClip?.length ?? 5f;
            float playTimeout = Mathf.Max(clipLen + 3f, 8f);
            float playElapsed = 0f;

            Debug.Log($"[TTS] playing id={ttsId} clipLen={clipLen:F1}s timeout={playTimeout:F1}s");

            while (!ttsComplete && !ttsError && !_forceTtsComplete && playElapsed < playTimeout)
            {
                yield return null;
                playElapsed += Time.deltaTime;

                // CRITICAL: Only check IsPlaybackComplete, NOT !IsPlaying
                // IsPlaying can be false during load or between loops
                if (TtsPlayer.Instance.IsPlaybackComplete)
                {
                    Debug.Log($"[TTS] detected complete id={ttsId} elapsed={playElapsed:F1}s");
                    break;
                }
            }

            TtsPlayer.Instance.OnPlaybackComplete -= onComplete;
            TtsPlayer.Instance.OnLoadError -= onError;

            // Handle exit conditions
            if (_forceTtsComplete)
            {
                Debug.Log($"[TTS] force stopped id={ttsId} (StopAI called)");
                TtsPlayer.Instance?.Stop();
            }
            else if (ttsError)
            {
                Debug.LogWarning($"[TTS] error id={ttsId} err={errorMessage}");
                if (_hintText != null) _hintText.text = text;
                yield return new WaitForSeconds(0.5f);
            }
            else if (playElapsed >= playTimeout)
            {
                Debug.LogWarning($"[TTS] timeout id={ttsId} elapsed={playElapsed:F1}s clipLen={clipLen:F1}s isPlaying={TtsPlayer.Instance?.IsPlaying}");
                // DO NOT stop audio on timeout - it might still be playing correctly
                // Just log and continue, the audio will finish naturally
            }
            else
            {
                Debug.Log($"[TTS] done id={ttsId} elapsed={playElapsed:F1}s");
            }
        }

        /// <summary>
        /// Force TTS to complete immediately (called by StopAI).
        /// </summary>
        private void ForceTtsComplete()
        {
            Debug.Log($"[TTS] ForceTtsComplete called v={_sessionVersion}");
            _forceTtsComplete = true;
            TtsPlayer.Instance?.Stop();
        }

        #endregion

        #region Recording Events

        private void OnRecordingStart()
        {
            // GUARD: Only show recording UI if actually in Recording state
            if (_currentState != TutorState.Recording)
            {
                Debug.LogWarning($"[REC] OnRecordingStart called but state is {_currentState} - ignoring UI update");
                return;
            }

            Debug.Log($"[REC] Recording started v={_sessionVersion}");
            if (_recordingIndicator != null)
            {
                _recordingIndicator.gameObject.SetActive(true);
                _recordingIndicator.color = _buttonRecordingColor;
            }
            if (_recordingProgress != null)
            {
                _recordingProgress.gameObject.SetActive(true);
                _recordingProgress.value = 0;
            }
        }

        private void OnRecordingProgress(float progress)
        {
            // GUARD: Only update recording progress if in Recording state
            if (_currentState != TutorState.Recording)
            {
                return;  // Silent ignore - normal during state transitions
            }

            if (_recordingProgress != null)
            {
                _recordingProgress.value = progress;
            }
        }

        private void OnRecordingComplete(byte[] wavData)
        {
            Debug.Log($"[TutorRoom] Recording complete: {wavData.Length} bytes");

            if (_recordingIndicator != null) _recordingIndicator.gameObject.SetActive(false);
            if (_recordingProgress != null) _recordingProgress.gameObject.SetActive(false);

            if (_currentState != TutorState.Recording)
            {
                Debug.LogWarning("[TutorRoom] Recording complete but not in Recording state");
                return;
            }

            // CAPTURE session version and attempt context BEFORE starting async work
            int capturedSessionVersion = _sessionVersion;
            AttemptContext capturedAttempt = _currentAttempt;

            // Start processing with captured context
            StartCoroutine(ProcessRecordingCoroutine(wavData, capturedSessionVersion, capturedAttempt));
        }

        private void OnRecordingError(string error)
        {
            Debug.LogError($"[ATTEMPT] Recording error: {error}");
            if (_recordingIndicator != null) _recordingIndicator.gameObject.SetActive(false);
            if (_recordingProgress != null) _recordingProgress.gameObject.SetActive(false);

            // CRITICAL: Force accuracy to 0 on error
            _transcriptText_value = "";
            _lastAttemptCorrect = false;
            _lastAccuracyPercent = 0;  // CRITICAL: Force 0, never inherit stale value
            _lastFeedback = null;

            // Update current attempt context if exists
            if (_currentAttempt != null)
            {
                _currentAttempt.transcriptEmpty = true;
                _currentAttempt.accuracyPercent = 0;
                Debug.Log($"[ATTEMPT] Error updated context: {_currentAttempt}");
            }

            SetState(TutorState.Home);
        }

        #endregion

        #region API Processing

        private IEnumerator ProcessRecordingCoroutine(byte[] wavData, int capturedSessionVersion, AttemptContext ctx)
        {
            // GUARD: Check session version FIRST
            if (capturedSessionVersion != _sessionVersion)
            {
                Debug.LogWarning($"[STT] STALE callback - session changed: captured={capturedSessionVersion}, current={_sessionVersion}");
                yield break;
            }

            // C. STT
            SetState(TutorState.Scoring);
            ShowLoading(true);

            string sttError = null;
            _rawTranscriptText = "";
            _transcriptText_value = "";

            // CRITICAL: Reset accuracy to 0 BEFORE STT (never inherit stale values)
            _lastAccuracyPercent = 0;
            _lastAttemptCorrect = false;
            _lastMismatchParts = "";

            yield return ApiClient.PostStt(
                wavData,
                "ko",
                response =>
                {
                    // GUARD: Check session version in callback
                    if (capturedSessionVersion != _sessionVersion)
                    {
                        Debug.LogWarning($"[STT] STALE STT callback ignored");
                        return;
                    }

                    // Store raw transcript for debugging
                    _rawTranscriptText = response.CleanText ?? "";

                    // Normalize: remove bracketed noise annotations like (noise), [music], etc.
                    _transcriptText_value = TranscriptUtils.NormalizeTranscript(_rawTranscriptText);

                    // Update attempt context
                    if (ctx != null)
                    {
                        ctx.transcript = _transcriptText_value;
                        ctx.transcriptEmpty = string.IsNullOrEmpty(_transcriptText_value);
                    }

                    // === STT DEBUG LOGS ===
                    Debug.Log("════════════════════════════════════════════════════════════");
                    Debug.Log($"[STT] session={capturedSessionVersion}, attempt={ctx?.attemptId ?? -1}");
                    Debug.Log($"[STT] raw = '{_rawTranscriptText}'");
                    Debug.Log($"[STT] cleaned = '{_transcriptText_value}'");
                    Debug.Log($"[STT] target = '{ctx?.targetPhrase ?? "(no ctx)"}'");
                    Debug.Log("════════════════════════════════════════════════════════════");
                },
                err =>
                {
                    sttError = err;
                    Debug.LogError($"[STT] ERROR: {err}, session={capturedSessionVersion}");
                    if (ctx != null)
                    {
                        ctx.transcriptEmpty = true;
                        ctx.accuracyPercent = 0;  // CRITICAL: Force 0 on error
                    }
                }
            );

            // GUARD: Check session version after async operation
            if (capturedSessionVersion != _sessionVersion)
            {
                Debug.LogWarning($"[STT] Session changed during STT - aborting");
                ShowLoading(false);
                yield break;
            }

            // Display transcript
            if (_transcriptText != null)
            {
                _transcriptText.text = !string.IsNullOrEmpty(_transcriptText_value)
                    ? _transcriptText_value
                    : "(음성 인식 실패)";
            }

            if (sttError != null || string.IsNullOrEmpty(_transcriptText_value))
            {
                // CRITICAL: Force accuracy to 0 on empty/error
                _lastAttemptCorrect = false;
                _lastAccuracyPercent = 0;
                _lastFeedback = null;
                if (ctx != null)
                {
                    ctx.transcriptEmpty = true;
                    ctx.accuracyPercent = 0;
                }
                Debug.Log($"[STT] Empty/error - forcing accuracy=0, session={capturedSessionVersion}");

                // Track as silence event for idle termination
                bool shouldEndSession = RecordSilenceEvent();
                ShowLoading(false);

                if (shouldEndSession)
                {
                    // End session after max consecutive silences
                    yield return HandleIdleTermination(capturedSessionVersion);
                    yield break;
                }

                SetState(TutorState.Feedback);
                yield break;
            }

            // CRITICAL: Validate transcript is Korean (not Chinese/other language)
            var validation = TranscriptUtils.ValidateKoreanTranscript(_transcriptText_value);
            if (!validation.isValid)
            {
                Debug.LogWarning($"[STT] Invalid transcript detected: '{_transcriptText_value}' - {validation.invalidReason}");
                Debug.LogWarning($"[STT] Korean ratio: {validation.koreanRatio:P0} ({validation.koreanLetters}/{validation.totalLetters})");

                // Treat as invalid - force accuracy to 0 and don't advance
                _lastAttemptCorrect = false;
                _lastAccuracyPercent = 0;
                _lastFeedback = null;
                _lastMismatchParts = validation.invalidReason;

                if (ctx != null)
                {
                    ctx.transcriptEmpty = true;  // Treat as "no valid transcript"
                    ctx.accuracyPercent = 0;
                }

                // Update UI to show the invalid reason
                if (_transcriptText != null)
                {
                    _transcriptText.text = $"({validation.invalidReason})";
                }

                // Track as silence/invalid event for idle termination
                bool shouldEndSession = RecordSilenceEvent();
                ShowLoading(false);

                if (shouldEndSession)
                {
                    // End session after max consecutive invalid attempts
                    yield return HandleIdleTermination(capturedSessionVersion);
                    yield break;
                }

                SetState(TutorState.Feedback);
                yield break;
            }

            // VALID TRANSCRIPT - update activity time (user is engaged)
            UpdateActivityTime();

            // Skip feedback for YES/NO question (SeasonEndQuestion state was set before recording)
            // Check if we have a valid expression to compare against
            if (_currentExpression == null || string.IsNullOrEmpty(_currentExpression.korean))
            {
                Debug.Log("[STT] No expression to compare - likely YES/NO question");
                ShowLoading(false);
                _lastFeedback = null;
                _lastAttemptCorrect = false;
                _lastAccuracyPercent = 0;
                // Don't change state - let the main coroutine handle it
                yield break;
            }

            // D. Get feedback from Grok
            SetState(TutorState.Scoring);

            string feedbackError = null;
            _lastFeedback = null;

            yield return ApiClient.PostFeedback(
                _currentExpression.korean,
                _transcriptText_value,
                response =>
                {
                    // GUARD: Check session version in callback
                    if (capturedSessionVersion != _sessionVersion)
                    {
                        Debug.LogWarning($"[SCORE] STALE feedback callback ignored");
                        return;
                    }
                    _lastFeedback = response;
                    Debug.Log($"[SCORE] Feedback: accuracy={response.AccuracyPercent}%, session={capturedSessionVersion}");
                },
                err =>
                {
                    feedbackError = err;
                    Debug.LogError($"[SCORE] Feedback error: {err}");
                }
            );

            // GUARD: Check session version after feedback
            if (capturedSessionVersion != _sessionVersion)
            {
                Debug.LogWarning($"[SCORE] Session changed during feedback - aborting");
                ShowLoading(false);
                yield break;
            }

            ShowLoading(false);

            // Compute accuracy and mismatch for 3-tier evaluation
            var evalResult = EvaluateAttempt(_currentExpression.korean, _transcriptText_value, _lastFeedback);
            _lastAttemptCorrect = evalResult.isCorrect;
            _lastAccuracyPercent = evalResult.accuracyPercent;
            _lastMismatchParts = evalResult.mismatchParts;

            // Update attempt context with scoring
            if (ctx != null)
            {
                ctx.accuracyPercent = _lastAccuracyPercent;
                ctx.mismatchSummary = _lastMismatchParts ?? "";
                Debug.Log($"[SCORE] Updated context: {ctx}");
            }

            Debug.Log($"[TutorRoom] Evaluation: correct={_lastAttemptCorrect}, accuracy={_lastAccuracyPercent}%, mismatch='{_lastMismatchParts}'");

            SetState(TutorState.Feedback);
        }

        #endregion

        #region Feedback Builder

        /// <summary>
        /// Result of attempt evaluation with accuracy and mismatch details.
        /// </summary>
        private struct EvaluationResult
        {
            public bool isCorrect;
            public int accuracyPercent;
            public string mismatchParts;  // Human-readable mismatch segments
        }

        /// <summary>
        /// Unified evaluation function for 3-tier feedback.
        /// Returns: isCorrect, accuracyPercent (0-100), mismatchParts (readable string)
        /// </summary>
        private EvaluationResult EvaluateAttempt(string target, string transcript, ApiClient.FeedbackResponse feedback)
        {
            var result = new EvaluationResult
            {
                isCorrect = false,
                accuracyPercent = 0,
                mismatchParts = ""
            };

            // Defensive: normalize transcript
            string cleanTranscript = TranscriptUtils.NormalizeTranscript(transcript);

            // Get accuracy from feedback API if available
            if (feedback != null)
            {
                result.accuracyPercent = feedback.AccuracyPercent;
            }
            else
            {
                // Fallback: compute character-level accuracy for Korean
                result.accuracyPercent = ComputeCharacterAccuracy(target, cleanTranscript);
            }

            // Check if correct (>= threshold or exact match)
            string normTarget = NormalizeText(target);
            string normTranscript = NormalizeText(cleanTranscript);
            result.isCorrect = (result.accuracyPercent >= _accuracyCorrectThreshold) || (normTarget == normTranscript);

            // Extract mismatch parts for partial/wrong feedback
            if (!result.isCorrect)
            {
                result.mismatchParts = ExtractMismatchParts(target, cleanTranscript, feedback);
            }

            return result;
        }

        /// <summary>
        /// Compute character-level accuracy for Korean text (fallback when API unavailable).
        /// Uses Levenshtein distance to compute similarity.
        /// </summary>
        private int ComputeCharacterAccuracy(string target, string transcript)
        {
            if (string.IsNullOrEmpty(target)) return 0;
            if (string.IsNullOrEmpty(transcript)) return 0;

            string normTarget = NormalizeText(target);
            string normTranscript = NormalizeText(transcript);

            if (normTarget == normTranscript) return 100;
            if (normTarget.Length == 0) return 0;

            // Compute Levenshtein distance
            int distance = LevenshteinDistance(normTarget, normTranscript);
            int maxLen = Math.Max(normTarget.Length, normTranscript.Length);

            // Convert to accuracy percentage
            int accuracy = (int)((1.0f - (float)distance / maxLen) * 100);
            return Math.Max(0, Math.Min(100, accuracy));
        }

        /// <summary>
        /// Levenshtein distance for character-level diff.
        /// </summary>
        private int LevenshteinDistance(string s1, string s2)
        {
            int[,] dp = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++) dp[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++) dp[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost
                    );
                }
            }

            return dp[s1.Length, s2.Length];
        }

        /// <summary>
        /// Extract human-readable mismatch parts for feedback.
        /// Returns comma-separated list of mismatched segments.
        /// </summary>
        private string ExtractMismatchParts(string target, string transcript, ApiClient.FeedbackResponse feedback)
        {
            // Use diff from feedback API if available
            if (feedback?.diff?.wrongParts != null && feedback.diff.wrongParts.Length > 0)
            {
                // Join wrong parts with comma, limit to 3
                var parts = feedback.diff.wrongParts;
                int count = Math.Min(parts.Length, 3);
                string[] limited = new string[count];
                Array.Copy(parts, limited, count);
                return string.Join(", ", limited);
            }

            // Fallback: character-level diff for Korean
            return ExtractCharacterMismatch(target, transcript);
        }

        /// <summary>
        /// Extract mismatched character spans for Korean text.
        /// Returns human-readable description of differences.
        /// </summary>
        private string ExtractCharacterMismatch(string target, string transcript)
        {
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(transcript))
            {
                return target ?? "";
            }

            string normTarget = NormalizeText(target);
            string normTranscript = NormalizeText(transcript);

            List<string> mismatches = new List<string>();

            // Find contiguous mismatch spans
            int i = 0, j = 0;
            while (i < normTarget.Length && j < normTranscript.Length)
            {
                if (normTarget[i] != normTranscript[j])
                {
                    // Found mismatch - extract span from target
                    int start = i;
                    while (i < normTarget.Length && j < normTranscript.Length && normTarget[i] != normTranscript[j])
                    {
                        i++; j++;
                    }
                    // Get original characters from target (not normalized)
                    if (start < target.Length)
                    {
                        int len = Math.Min(i - start + 1, target.Length - start);
                        string mismatch = target.Substring(start, Math.Min(len, 4));  // Limit span length
                        if (!string.IsNullOrWhiteSpace(mismatch))
                        {
                            mismatches.Add(mismatch);
                        }
                    }
                }
                else
                {
                    i++; j++;
                }

                if (mismatches.Count >= 3) break;  // Limit to 3 mismatches
            }

            // If transcript is shorter, the missing part is a mismatch
            if (i < normTarget.Length && mismatches.Count < 3)
            {
                int remaining = Math.Min(normTarget.Length - i, 4);
                if (i < target.Length)
                {
                    mismatches.Add(target.Substring(i, Math.Min(remaining, target.Length - i)));
                }
            }

            return mismatches.Count > 0 ? string.Join(", ", mismatches) : "";
        }

        /// <summary>
        /// Determine if the attempt is correct using normalized comparison.
        /// Correct if accuracy >= threshold or normalized strings match.
        /// </summary>
        private bool IsCorrect(string target, string transcript, ApiClient.FeedbackResponse feedback)
        {
            // Use accuracy if available
            if (feedback != null && feedback.AccuracyPercent >= _accuracyCorrectThreshold)
            {
                return true;
            }

            // Defensive: ensure transcript has no bracketed noise annotations
            string cleanTranscript = TranscriptUtils.NormalizeTranscript(transcript);

            // Fallback to string comparison
            string normTarget = NormalizeText(target);
            string normTranscript = NormalizeText(cleanTranscript);
            return normTarget == normTranscript;
        }

        /// <summary>
        /// Build feedback speech for partial accuracy (65-89%).
        /// Korean: "거의 맞았어. 틀린 부분은 X야. 그 부분만 고쳐서 다시 말해볼래?"
        /// </summary>
        private string BuildPartialHintFeedback(string mismatchParts)
        {
            if (string.IsNullOrEmpty(mismatchParts))
            {
                // Fallback if no specific mismatch identified
                return "거의 맞았어. 조금만 더 정확하게 다시 말해볼래?";
            }

            // Build the hint speech
            return $"거의 맞았어. 틀린 부분은 '{mismatchParts}' 이야. 그 부분만 고쳐서 다시 말해볼래?";
        }

        /// <summary>
        /// Normalize text for comparison: remove punctuation, whitespace, lowercase.
        /// </summary>
        private string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            // Remove punctuation and whitespace
            text = Regex.Replace(text, @"[.,!?;:'""\s]", "");
            return text.ToLower();
        }

        #endregion

        #region Comparison Panel UI

        private bool _comparisonPanelCreated = false;

        /// <summary>
        /// Update the comparison panel with target and user sentences.
        /// Prefers UGUI panel (uses OS Korean fonts), falls back to TMP.
        /// User sentence has incorrect characters highlighted in red.
        /// </summary>
        private void UpdateComparisonPanel(string targetSentence, string userTranscript, int accuracyPercent)
        {
            Debug.Log($"[ComparisonPanel] UpdateComparisonPanel - target='{targetSentence}', user='{userTranscript}', acc={accuracyPercent}%");

            // PREFER UGUI panel (uses OS Korean fonts - no TMP dependency)
            if (_comparisonPanelUGUI != null)
            {
                _comparisonPanelUGUI.Show(targetSentence, userTranscript, accuracyPercent);
                Debug.Log("[ComparisonPanel] Using UGUI panel (OS Korean font)");
                return;
            }

            // Create UGUI panel dynamically if not assigned
            if (!_comparisonPanelCreated)
            {
                Debug.Log("[ComparisonPanel] Creating UGUI panel dynamically");
                CreateComparisonPanelDynamic();
            }

            // Try UGUI panel again after creation
            if (_comparisonPanelUGUI != null)
            {
                _comparisonPanelUGUI.Show(targetSentence, userTranscript, accuracyPercent);
                Debug.Log("[ComparisonPanel] Using dynamically created UGUI panel");
                return;
            }

            // FALLBACK: Use legacy TMP panel if UGUI failed
            Debug.LogWarning("[ComparisonPanel] UGUI panel not available, using TMP fallback");
            UpdateComparisonPanelTMP(targetSentence, userTranscript, accuracyPercent);
        }

        /// <summary>
        /// Legacy TMP-based comparison panel update (fallback only).
        /// </summary>
        private void UpdateComparisonPanelTMP(string targetSentence, string userTranscript, int accuracyPercent)
        {
            if (_comparisonPanel == null)
            {
                Debug.LogError("[ComparisonPanel] No TMP panel available");
                return;
            }

            _comparisonPanel.SetActive(true);

            var canvasGroup = _comparisonPanel.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.blocksRaycasts = true;
            }

            if (_tmpTargetLabel != null)
            {
                _tmpTargetLabel.text = "정답:";
                _tmpTargetLabel.gameObject.SetActive(true);
            }

            if (_tmpTargetSentence != null)
            {
                _tmpTargetSentence.text = TextDiffHighlighter.BuildTargetRichText(targetSentence);
                _tmpTargetSentence.richText = true;
                _tmpTargetSentence.gameObject.SetActive(true);
            }

            if (_tmpUserLabel != null)
            {
                _tmpUserLabel.text = "내 발음:";
                _tmpUserLabel.gameObject.SetActive(true);
            }

            if (_tmpUserSentence != null)
            {
                string cleanUserTranscript = TranscriptUtils.NormalizeTranscript(userTranscript);
                _tmpUserSentence.text = TextDiffHighlighter.BuildUserRichText(cleanUserTranscript, targetSentence);
                _tmpUserSentence.richText = true;
                _tmpUserSentence.gameObject.SetActive(true);
            }

            if (_tmpAccuracyText != null)
            {
                string accuracyColor = accuracyPercent >= _accuracyCorrectThreshold ? "#00AA00" :
                                       accuracyPercent >= _accuracyHintThreshold ? "#FF8800" : "#FF0000";
                _tmpAccuracyText.text = $"<color={accuracyColor}>정확도: {accuracyPercent}%</color>";
                _tmpAccuracyText.richText = true;
                _tmpAccuracyText.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Create the comparison panel dynamically at runtime.
        /// Uses UGUI Text with OS Korean fonts (no TMP dependency).
        /// </summary>
        private void CreateComparisonPanelDynamic()
        {
            _comparisonPanelCreated = true;

            // Find Canvas in scene
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[ComparisonPanel] No Canvas found in scene!");
                return;
            }

            Debug.Log($"[ComparisonPanel] Creating UGUI panel under Canvas: {canvas.name}");

            // Create UGUI-based panel (uses OS Korean fonts)
            _comparisonPanelUGUI = ComparisonPanelUGUI.CreateDynamic(canvas);

            if (_comparisonPanelUGUI != null)
            {
                Debug.Log("[ComparisonPanel] UGUI panel created successfully (OS Korean font)");
            }
            else
            {
                Debug.LogError("[ComparisonPanel] Failed to create UGUI panel");
            }
        }

        /// <summary>
        /// Assign font to ALL TMP_Text children of a GameObject.
        /// Ensures Korean renders correctly even for dynamically created UI.
        /// </summary>
        private void AssignFontToAllTMPChildren(GameObject root, TMP_FontAsset font)
        {
            if (root == null || font == null) return;

            TMP_Text[] allTmpTexts = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (var tmpText in allTmpTexts)
            {
                if (tmpText.font != font)
                {
                    tmpText.font = font;
                }
                // Ensure rich text is enabled for color tags
                tmpText.richText = true;
            }

            Debug.Log($"[ComparisonPanel] Assigned font '{font.name}' to {allTmpTexts.Length} TMP_Text components");
        }

        /// <summary>
        /// Helper to create a TextMeshProUGUI component.
        /// Uses Korean font for proper glyph rendering.
        /// </summary>
        private TextMeshProUGUI CreateTMPText(Transform parent, string name, string text, int fontSize, FontStyles fontStyle, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            // Add RectTransform
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, fontSize * 1.5f);

            // Add LayoutElement for proper sizing in VerticalLayoutGroup
            var layoutElement = go.AddComponent<UnityEngine.UI.LayoutElement>();
            layoutElement.minHeight = fontSize * 1.2f;
            layoutElement.preferredHeight = fontSize * 1.5f;
            layoutElement.flexibleWidth = 1f;

            // Add TextMeshProUGUI
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();

            // CRITICAL: Assign Korean font BEFORE setting text
            TMP_FontAsset fontToUse = GetTMPFont();
            if (fontToUse != null)
            {
                tmp.font = fontToUse;
                Debug.Log($"[ComparisonPanel] {name} using font: {fontToUse.name}");
            }
            else
            {
                Debug.LogError("[ComparisonPanel] No TMP font available - Korean text will show as squares! " +
                    "Run: Tools > ShadowingTutor > Setup Korean Font");
            }

            // Configure text properties
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = fontStyle;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.richText = true;  // REQUIRED for <color> tags

            // Ensure text is visible
            tmp.enabled = true;
            go.SetActive(true);

            return tmp;
        }

        // Static guard to prevent log spam (logs once per session)
        private static bool _fontNotFoundLogged = false;
        private TMP_FontAsset _cachedKoreanFont;

        /// <summary>
        /// Get TMP font asset with single source of truth.
        /// Resolution order:
        /// 1. Cached font (already resolved)
        /// 2. Inspector-assigned font (_tmpFontAsset)
        /// 3. AppConfig.KoreanFont (single source of truth)
        /// 4. Resources.Load("TMP_Korean")
        /// 5. TMP_Settings.defaultFontAsset (fallback, logs warning ONCE)
        /// </summary>
        private TMP_FontAsset GetTMPFont()
        {
            // 1. Return cached font if already loaded
            if (_cachedKoreanFont != null)
            {
                return _cachedKoreanFont;
            }

            // 2. Use Inspector-assigned font (highest priority for manual override)
            if (_tmpFontAsset != null)
            {
                _cachedKoreanFont = _tmpFontAsset;
                Debug.Log($"[ComparisonPanel] Using Inspector-assigned font: {_tmpFontAsset.name}");
                return _cachedKoreanFont;
            }

            // 3. Use AppConfig.KoreanFont (single source of truth)
            if (AppConfig.Instance != null && AppConfig.Instance.KoreanFont != null)
            {
                _cachedKoreanFont = AppConfig.Instance.KoreanFont;
                Debug.Log($"[ComparisonPanel] Using AppConfig Korean font: {_cachedKoreanFont.name}");
                return _cachedKoreanFont;
            }

            // 4. Try Resources.Load (for builds where AppConfig might not have font assigned)
            _cachedKoreanFont = Resources.Load<TMP_FontAsset>("TMP_Korean");
            if (_cachedKoreanFont != null)
            {
                Debug.Log($"[ComparisonPanel] Loaded Korean font from Resources: {_cachedKoreanFont.name}");
                return _cachedKoreanFont;
            }

            // 5. Fallback to TMP Settings default font (will show squares for Korean)
            if (TMP_Settings.defaultFontAsset != null)
            {
                _cachedKoreanFont = TMP_Settings.defaultFontAsset;
                if (!_fontNotFoundLogged)
                {
                    Debug.LogError(
                        "[ComparisonPanel] Korean font NOT FOUND - using default TMP font.\n" +
                        "Korean text will show as squares!\n" +
                        "To fix: Exit Play Mode, then run Tools > ShadowingTutor > Setup Korean Font"
                    );
                    _fontNotFoundLogged = true;
                }
                return _cachedKoreanFont;
            }

            // 6. Last resort: find any loaded font
            TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (fonts != null && fonts.Length > 0)
            {
                _cachedKoreanFont = fonts[0];
                if (!_fontNotFoundLogged)
                {
                    Debug.LogError($"[ComparisonPanel] Using fallback font: {fonts[0].name} - Korean will show as squares!");
                    _fontNotFoundLogged = true;
                }
                return _cachedKoreanFont;
            }

            if (!_fontNotFoundLogged)
            {
                Debug.LogError("[ComparisonPanel] NO TMP FONT FOUND! Text will not render.");
                _fontNotFoundLogged = true;
            }
            return null;
        }

        /// <summary>
        /// Hide/clear the comparison panel.
        /// </summary>
        private void ClearComparisonPanel()
        {
            // Hide UGUI panel
            if (_comparisonPanelUGUI != null)
            {
                _comparisonPanelUGUI.Hide();
            }

            // Hide legacy TMP panel
            if (_comparisonPanel != null)
            {
                _comparisonPanel.SetActive(false);
            }

            if (_tmpTargetSentence != null) _tmpTargetSentence.text = "";
            if (_tmpUserSentence != null) _tmpUserSentence.text = "";
            if (_tmpAccuracyText != null) _tmpAccuracyText.text = "";
        }

        /// <summary>
        /// Debug: Show sample comparison to test UI visibility.
        /// Call this from Start() to verify panel works before STT.
        /// </summary>
        [ContextMenu("Test Comparison Panel")]
        public void DebugTestComparisonPanel()
        {
            Debug.Log("[ComparisonPanel] === DEBUG TEST ===");
            string sampleTarget = "오늘의 주제는 카페에서 주문하기예요";
            string sampleUser = "오늘의 주제는 카페에서 주문하기요";
            int sampleAccuracy = 85;

            UpdateComparisonPanel(sampleTarget, sampleUser, sampleAccuracy);
            Debug.Log("[ComparisonPanel] === DEBUG TEST COMPLETE ===");
        }

        /// <summary>
        /// Build the spoken feedback script for TTS.
        ///
        /// Rules:
        /// - Correct: "정확해요. 잘했어요. 다음 문장으로 넘어갈게요."
        /// - Wrong Case 1 (전체적으로 틀린 경우): word-level mistakes > 2 OR extra different words
        ///   Detailed feedback: user said X, correct is Y, incorrect part is Z
        /// - Wrong Case 2 (부분적으로 틀린 경우): 1-2 word-level mistakes, no extra words
        ///   Specific feedback about the incorrect part
        /// </summary>
        private string BuildSpokenFeedback(string targetText, string transcriptText, ApiClient.FeedbackResponse feedback)
        {
            // Check for empty transcript
            if (string.IsNullOrEmpty(transcriptText))
            {
                return $"음성이 잘 안 들렸어요. 정확한 문장은 '{targetText}'예요. 다시 해볼까요?";
            }

            // Check if correct
            if (IsCorrect(targetText, transcriptText, feedback))
            {
                return "정확해요. 잘했어요. 다음 문장으로 넘어갈게요.";
            }

            // Analyze mismatch
            var analysis = AnalyzeMismatch(targetText, transcriptText, feedback);

            if (analysis.isSevere)
            {
                // Case 1: 전체적으로 틀린 경우 - Provide detailed feedback
                // "You said X. The correct sentence is Y. Please try again."
                return $"'{transcriptText}'라고 하셨네요. 정확한 문장은 '{targetText}'예요. 다시 해볼까요?";
            }
            else
            {
                // Case 2: 부분적으로 틀린 경우
                if (!string.IsNullOrEmpty(analysis.wrongPart))
                {
                    // "You said X. The correct sentence is Y. The incorrect part is Z. Please try again."
                    string feedbackMsg = $"'{transcriptText}'라고 하셨네요. ";
                    feedbackMsg += $"정확한 문장은 '{targetText}'예요. ";
                    feedbackMsg += $"'{analysis.wrongPart}' 부분이 달랐어요. ";

                    // Try to use Grok's tip if available
                    if (feedback?.tutor != null && !string.IsNullOrEmpty(feedback.tutor.commentKo))
                    {
                        string grokComment = feedback.tutor.commentKo;
                        if (grokComment.Length < 50 && !grokComment.Contains("잘했"))
                        {
                            feedbackMsg += grokComment + " ";
                        }
                    }

                    feedbackMsg += "다시 한 번 해볼까요?";
                    return feedbackMsg;
                }
                else
                {
                    return $"'{transcriptText}'라고 하셨네요. 정확한 문장은 '{targetText}'예요. 다시 해볼까요?";
                }
            }
        }

        /// <summary>
        /// Analyze mismatch between target and transcript.
        /// Returns severity and wrong part for feedback.
        /// </summary>
        private (bool isSevere, string wrongPart) AnalyzeMismatch(string target, string transcript, ApiClient.FeedbackResponse feedback)
        {
            // Use diff from feedback if available
            if (feedback?.diff?.wrongParts != null && feedback.diff.wrongParts.Length > 0)
            {
                int wrongCount = feedback.diff.wrongParts.Length;

                // Severe if more than 2 wrong parts or accuracy < 50%
                bool isSevere = wrongCount > 2 || (feedback.AccuracyPercent < 50);

                // Get first wrong part for partial feedback
                string wrongPart = wrongCount > 0 ? feedback.diff.wrongParts[0] : "";

                return (isSevere, wrongPart);
            }

            // Defensive: ensure transcript has no bracketed noise annotations
            string cleanTranscript = TranscriptUtils.NormalizeTranscript(transcript);

            // Fallback: word-level comparison
            string[] targetWords = target.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] transcriptWords = cleanTranscript.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            int mismatchCount = 0;
            string firstMismatch = "";

            // Check for extra words
            bool hasExtraWords = transcriptWords.Length > targetWords.Length + 1;

            // Count mismatches
            int minLen = Math.Min(targetWords.Length, transcriptWords.Length);
            for (int i = 0; i < minLen; i++)
            {
                string normTarget = NormalizeText(targetWords[i]);
                string normTranscript = NormalizeText(transcriptWords[i]);

                if (normTarget != normTranscript)
                {
                    mismatchCount++;
                    if (string.IsNullOrEmpty(firstMismatch))
                    {
                        firstMismatch = targetWords[i];
                    }
                }
            }

            // Add missing word count
            mismatchCount += Math.Abs(targetWords.Length - transcriptWords.Length);

            // Severe if > 2 mismatches or has extra different words
            bool severe = mismatchCount > 2 || hasExtraWords;

            return (severe, firstMismatch);
        }

        #endregion

        #region Intent Parsing

        /// <summary>
        /// Wait for a yes/no response from the user via recording.
        /// Sets _transcriptText_value with the transcribed response.
        /// </summary>
        /// <param name="timeoutSeconds">Max time to wait for response</param>
        private IEnumerator WaitForYesNoResponse(float timeoutSeconds)
        {
            // Clear previous transcript
            _transcriptText_value = "";

            // Indicate we're waiting for response
            if (_hintText != null) _hintText.text = "네 또는 아니요~";
            SetState(TutorState.Recording);

            bool recordingStarted = MicRecorder.Instance?.StartRecording() ?? false;
            if (!recordingStarted)
            {
                Debug.LogWarning("[TutorRoom] Failed to start recording for YES/NO - defaulting to YES");
                yield break;
            }

            // Wait for recording with timeout
            float elapsed = 0f;
            while (_currentState == TutorState.Recording && elapsed < timeoutSeconds)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            if (elapsed >= timeoutSeconds)
            {
                Debug.Log($"[TutorRoom] YES/NO timeout after {elapsed:F1}s - defaulting to YES");
                MicRecorder.Instance?.StopRecordingGracefully();
            }

            // Wait briefly for STT processing
            float sttWait = 0f;
            while (_currentState == TutorState.Scoring && sttWait < 5f)
            {
                yield return null;
                sttWait += Time.deltaTime;
            }
        }

        /// <summary>
        /// Parse YES/NO intent from transcript.
        /// Returns: 1 = YES, -1 = NO, 0 = unclear
        /// </summary>
        private int ParseYesNoIntent(string transcript)
        {
            // Defensive: ensure transcript has no bracketed noise annotations
            string cleanTranscript = TranscriptUtils.NormalizeTranscript(transcript);

            if (string.IsNullOrEmpty(cleanTranscript))
            {
                return 0;  // Unclear
            }

            string lower = cleanTranscript.ToLower();

            // Check for YES keywords
            foreach (string keyword in YesKeywords)
            {
                if (lower.Contains(keyword))
                {
                    Debug.Log($"[TutorRoom] YES intent detected: '{keyword}' in '{transcript}'");
                    return 1;
                }
            }

            // Check for NO keywords
            foreach (string keyword in NoKeywords)
            {
                if (lower.Contains(keyword))
                {
                    Debug.Log($"[TutorRoom] NO intent detected: '{keyword}' in '{transcript}'");
                    return -1;
                }
            }

            Debug.Log($"[TutorRoom] Intent unclear for: '{transcript}'");
            return 0;
        }

        /// <summary>
        /// Classify transcript into VoiceDecision (Positive/Negative/Unknown).
        /// More explicit than ParseYesNoIntent for voice-driven branching.
        /// </summary>
        private VoiceDecision ClassifyVoiceDecision(string transcript)
        {
            string cleanTranscript = TranscriptUtils.NormalizeTranscript(transcript);

            if (string.IsNullOrEmpty(cleanTranscript))
            {
                Debug.Log("[VoiceDecision] Empty transcript -> Unknown");
                return VoiceDecision.Unknown;
            }

            string lower = cleanTranscript.ToLower().Trim();

            // Check for POSITIVE keywords
            foreach (string keyword in YesKeywords)
            {
                if (lower.Contains(keyword.ToLower()))
                {
                    Debug.Log($"[VoiceDecision] POSITIVE detected: '{keyword}' in '{transcript}'");
                    return VoiceDecision.Positive;
                }
            }

            // Check for NEGATIVE keywords
            foreach (string keyword in NoKeywords)
            {
                if (lower.Contains(keyword.ToLower()))
                {
                    Debug.Log($"[VoiceDecision] NEGATIVE detected: '{keyword}' in '{transcript}'");
                    return VoiceDecision.Negative;
                }
            }

            Debug.Log($"[VoiceDecision] UNKNOWN for: '{transcript}'");
            return VoiceDecision.Unknown;
        }

        /// <summary>
        /// Voice-driven continuation flow after sentence study completes.
        /// Asks "다음으로 넘어가볼까요?" and processes user's spoken response.
        /// </summary>
        /// <param name="timeoutSeconds">Listening timeout (default 6s)</param>
        /// <returns>True if user wants to continue, False if user wants to quit</returns>
        private IEnumerator VoiceDrivenContinuation(float timeoutSeconds = 6f)
        {
            int mySessionVersion = _sessionVersion;
            _voiceDecisionInProgress = true;
            _voiceDecisionRepromptCount = 0;

            Debug.Log("[VoiceDecision] Starting voice-driven continuation flow");

            // Step 1: Play continuation prompt
            string continuationPrompt = "다음으로 넘어가볼까요?";
            Debug.Log($"[VoiceDecision] Speaking prompt: '{continuationPrompt}'");
            yield return PlayTtsAndWait(continuationPrompt);

            if (!IsSessionValid(mySessionVersion))
            {
                _voiceDecisionInProgress = false;
                yield break;
            }

            // Step 2: Listen for response
            VoiceDecision decision = VoiceDecision.Unknown;
            bool decisionMade = false;

            while (!decisionMade && IsSessionValid(mySessionVersion))
            {
                // Clear previous transcript
                _transcriptText_value = "";

                // Indicate listening state
                if (_hintText != null) _hintText.text = "네 또는 아니요~";
                SetState(TutorState.Recording);
                Debug.Log("[VoiceDecision] Listening started...");

                bool recordingStarted = MicRecorder.Instance?.StartRecording() ?? false;
                if (!recordingStarted)
                {
                    Debug.LogWarning("[VoiceDecision] Failed to start recording -> treating as Unknown");
                    decision = VoiceDecision.Unknown;
                    break;
                }

                // Wait for recording with timeout
                float elapsed = 0f;
                while (_currentState == TutorState.Recording && elapsed < timeoutSeconds)
                {
                    yield return null;
                    elapsed += Time.deltaTime;
                }

                if (elapsed >= timeoutSeconds)
                {
                    Debug.Log($"[VoiceDecision] Listening timeout after {elapsed:F1}s");
                    MicRecorder.Instance?.StopRecordingGracefully();
                }

                // Wait for STT processing
                float sttWait = 0f;
                while (_currentState == TutorState.Scoring && sttWait < 5f)
                {
                    yield return null;
                    sttWait += Time.deltaTime;
                }

                if (!IsSessionValid(mySessionVersion))
                {
                    _voiceDecisionInProgress = false;
                    yield break;
                }

                // Step 3: Classify response
                string transcript = _transcriptText_value;
                Debug.Log($"[VoiceDecision] Transcript received: '{transcript}'");
                decision = ClassifyVoiceDecision(transcript);

                // Step 4: Handle decision
                if (decision == VoiceDecision.Positive)
                {
                    Debug.Log("[VoiceDecision] VoiceDecision=POSITIVE -> continuing to next topic");
                    decisionMade = true;
                }
                else if (decision == VoiceDecision.Negative)
                {
                    Debug.Log("[VoiceDecision] VoiceDecision=NEGATIVE -> initiating quit flow");
                    decisionMade = true;
                }
                else // Unknown
                {
                    _voiceDecisionRepromptCount++;
                    Debug.Log($"[VoiceDecision] VoiceDecision=UNKNOWN (reprompt #{_voiceDecisionRepromptCount})");

                    if (_voiceDecisionRepromptCount <= MAX_VOICE_REPROMPTS)
                    {
                        // Reprompt once
                        string reprompt = "네 또는 아니요로 말해 주세요.";
                        Debug.Log($"[VoiceDecision] Reprompting: '{reprompt}'");
                        yield return PlayTtsAndWait(reprompt);

                        if (!IsSessionValid(mySessionVersion))
                        {
                            _voiceDecisionInProgress = false;
                            yield break;
                        }
                        // Loop back to listen again
                    }
                    else
                    {
                        // Max reprompts exceeded -> default to NEGATIVE
                        Debug.Log("[VoiceDecision] Max reprompts exceeded -> defaulting to NEGATIVE");
                        decision = VoiceDecision.Negative;
                        decisionMade = true;
                    }
                }
            }

            _voiceDecisionInProgress = false;

            // Execute the decision
            if (decision == VoiceDecision.Positive)
            {
                // Continue - the calling coroutine handles advancement
                Debug.Log("[VoiceDecision] Flow complete -> CONTINUE");
                SetState(TutorState.Feedback);
            }
            else // Negative (or defaulted to Negative after max reprompts)
            {
                // Return to Start screen (bypass season wrap-up)
                Debug.Log("[VoiceDecision] Branching to Start screen (season wrap-up bypassed)");
                yield return ReturnToStartScreen(mySessionVersion);
            }
        }

        /// <summary>
        /// Return to Start screen when user declines to continue.
        /// Bypasses season wrap-up flow and resets to initial screen with START button.
        /// </summary>
        private IEnumerator ReturnToStartScreen(int sessionVersion)
        {
            Debug.Log("[VoiceDecision] NEGATIVE detected -> Returning to Start screen (bypassing season wrap-up)");

            // Log whether this was called during voice decision or forced
            if (_voiceDecisionInProgress)
            {
                Debug.Log("[VoiceDecision] ReturnToStartScreen called during active voice decision");
            }
            else
            {
                Debug.Log("[VoiceDecision] ReturnToStartScreen FORCED - no active voice decision (allowed)");
            }

            string closingMessage = "알겠어요! 처음 화면으로 돌아갈게요.";
            Debug.Log($"[VoiceDecision] Speaking: '{closingMessage}'");

            // Try to play TTS (brief acknowledgment)
            if (ApiClient.IsBackendReachable && TtsPlayer.Instance != null)
            {
                yield return PlayTtsAndWait(closingMessage);
            }
            else
            {
                Debug.LogWarning("[VoiceDecision] Backend unreachable - skipping closing TTS");
                yield return new WaitForSeconds(0.5f);
            }

            // Check session validity before navigation
            if (!IsSessionValid(sessionVersion))
            {
                Debug.Log("[VoiceDecision] Session invalidated during closing - aborting navigation");
                yield break;
            }

            // Brief pause
            yield return new WaitForSeconds(0.3f);

            // === CLEANUP & RETURN TO START ===
            Debug.Log("[VoiceDecision] Calling ResetSessionToHome() to return to Start screen");

            // ResetSessionToHome handles:
            // - Incrementing session version (stops all coroutines)
            // - Stopping TTS and MicRecorder
            // - Clearing UI, comparison panel
            // - Setting state to Home
            // - Calling HideLessonUI()
            ResetSessionToHome();

            Debug.Log("[VoiceDecision] Successfully returned to Start screen - START button should be visible");
        }

        /// <summary>
        /// Force immediate return to Start screen. Can be called anytime.
        /// Stops all active processes (TTS, STT, coroutines) and shows START button.
        /// </summary>
        public void ForceReturnToStartScreen()
        {
            Debug.Log("[Navigation] ForceReturnToStartScreen() called - forcing immediate return to Start");

            // Reset voice decision state
            _voiceDecisionInProgress = false;
            _voiceDecisionRepromptCount = 0;

            // ResetSessionToHome does all cleanup:
            // - Stops TTS, MicRecorder, coroutines
            // - Clears UI, sets state to Home
            // - Shows START button
            ResetSessionToHome();

            Debug.Log("[Navigation] ForceReturnToStartScreen() complete - START button should be visible and clickable");
        }

        #endregion

        #region TTS Events (for non-main-flow TTS)

        private void OnTtsLoadComplete()
        {
            ShowLoading(false);
        }

        private void OnTtsPlaybackComplete()
        {
            // Handled by PlayTtsAndWait coroutine
        }

        private void OnTtsError(string error)
        {
            ShowLoading(false);
            Debug.LogError($"[TutorRoom] TTS error: {error}");
        }

        #endregion
    }
}
