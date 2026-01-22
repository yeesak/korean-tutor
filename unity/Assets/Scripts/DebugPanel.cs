using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// In-Editor debug panel for development and testing.
    /// Shows current state, validation overlay with WER score, and mismatched tokens.
    /// Includes Editor test mode for bypassing TTS/STT when backend is in mock mode.
    /// Toggle with F12 key or debug button.
    /// </summary>
    public class DebugPanel : MonoBehaviour
    {
        private static DebugPanel _instance;
        public static DebugPanel Instance => _instance;

        [Header("Settings")]
        [SerializeField] private bool _showOnStart = false;
        [SerializeField] private KeyCode _toggleKey = KeyCode.F12;

        private bool _showPanel = false;
        private bool _showValidation = false;
        private bool _showTestPanel = false;
        private Vector2 _scrollPosition;
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _correctStyle;
        private GUIStyle _wrongStyle;
        private GUIStyle _warningStyle;

        // Validation data
        private float _werScore = 0f;
        private List<string> _mismatchedTokens = new List<string>();
        private string _feedbackString = "";

        // Mock mode detection
        private bool _mockModeChecked = false;
        private bool _isMockMode = false;
        private bool _ttsMock = false;
        private bool _sttMock = false;
        private bool _feedbackMock = false;

        // Editor test mode
        private string _manualTranscript = "";
        private bool _editorTestMode = false;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _showPanel = _showOnStart;
        }

        private void Start()
        {
            // Check backend health on startup
            StartCoroutine(CheckBackendHealth());
        }

        private IEnumerator CheckBackendHealth()
        {
            yield return new WaitForSeconds(0.5f); // Wait for managers to initialize

            ApiClient.HealthResponse healthResult = null;
            string healthError = null;

            yield return ApiClient.CheckHealth(
                response => healthResult = response,
                error => healthError = error
            );

            _mockModeChecked = true;

            if (healthResult != null)
            {
                _isMockMode = healthResult.mode == "mock";
                _ttsMock = !healthResult.ttsConfigured;
                _sttMock = !healthResult.sttConfigured;
                _feedbackMock = !healthResult.llmConfigured;

                if (_isMockMode)
                {
                    Debug.LogWarning("[DebugPanel] Backend is in MOCK MODE - TTS will play beeps, STT will return random phrases. Press F12 to open test panel.");
                    #if UNITY_EDITOR
                    _showTestPanel = true; // Auto-show test panel in Editor when mock mode
                    #endif
                }
                else
                {
                    Debug.Log("[DebugPanel] Backend is LIVE - all APIs configured");
                }
            }
            else
            {
                Debug.LogError($"[DebugPanel] Cannot reach backend: {healthError}");
                _isMockMode = true; // Assume mock if unreachable
            }
        }

        /// <summary>
        /// Whether backend is in mock mode (TTS/STT won't work properly)
        /// </summary>
        public bool IsMockMode => _isMockMode;

        /// <summary>
        /// Whether Editor test mode is enabled (bypass STT with manual input)
        /// </summary>
        public bool EditorTestMode => _editorTestMode;

        private void Update()
        {
            // Keyboard shortcuts (Editor/Standalone only - not available on mobile with new Input System)
#if UNITY_EDITOR || UNITY_STANDALONE
            if (Input.GetKeyDown(_toggleKey))
            {
                _showPanel = !_showPanel;
            }

            // Alt+V toggles validation overlay
            if (Input.GetKey(KeyCode.LeftAlt) && Input.GetKeyDown(KeyCode.V))
            {
                _showValidation = !_showValidation;
            }
#endif
        }

        private void InitStyles()
        {
            if (_boxStyle != null) return;

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 10, 10)
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };

            _correctStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14
            };
            _correctStyle.normal.textColor = Color.green;

            _wrongStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14
            };
            _wrongStyle.normal.textColor = Color.red;

            _warningStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            _warningStyle.normal.textColor = new Color(1f, 0.6f, 0f); // Orange
        }

        private void OnGUI()
        {
            InitStyles();

            // Toggle button (always visible in Editor)
            #if UNITY_EDITOR
            if (GUI.Button(new Rect(10, 10, 100, 30), _showPanel ? "Hide Debug" : "Show Debug"))
            {
                _showPanel = !_showPanel;
            }

            if (GUI.Button(new Rect(120, 10, 100, 30), _showValidation ? "Hide Valid" : "Show Valid"))
            {
                _showValidation = !_showValidation;
            }

            if (GUI.Button(new Rect(230, 10, 100, 30), _showTestPanel ? "Hide Test" : "Test Mode"))
            {
                _showTestPanel = !_showTestPanel;
            }

            // Mock mode warning banner
            if (_mockModeChecked && _isMockMode)
            {
                GUI.color = new Color(1f, 0.3f, 0f, 0.9f);
                GUI.Box(new Rect(0, 45, Screen.width, 25), "");
                GUI.color = Color.white;
                GUI.Label(new Rect(10, 48, Screen.width - 20, 20),
                    "MOCK MODE: Backend missing API keys - TTS plays beeps, STT returns random phrases. Use Test Mode panel to bypass.",
                    _warningStyle);
            }
            #endif

            if (_showPanel)
            {
                DrawDebugPanel();
            }

            if (_showValidation)
            {
                DrawValidationOverlay();
            }

            #if UNITY_EDITOR
            if (_showTestPanel)
            {
                DrawTestPanel();
            }
            #endif
        }

        private void DrawDebugPanel()
        {
            float width = 350;
            float height = 400;
            Rect panelRect = new Rect(Screen.width - width - 10, 50, width, height);

            GUI.Box(panelRect, "", _boxStyle);
            GUILayout.BeginArea(new Rect(panelRect.x + 10, panelRect.y + 10, width - 20, height - 20));

            GUILayout.Label("Developer Panel", _headerStyle);
            GUILayout.Space(10);

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            // State info
            var state = ShadowingState.Instance;
            if (state != null)
            {
                GUILayout.Label($"Phase: {state.CurrentPhase}", _labelStyle);
                GUILayout.Label($"Listen Count: {state.ListenCount}/{state.MaxListens}", _labelStyle);

                if (state.CurrentSentence != null)
                {
                    GUILayout.Space(5);
                    GUILayout.Label("Current Sentence:", _headerStyle);
                    GUILayout.Label($"  ID: {state.CurrentSentence.id}", _labelStyle);
                    GUILayout.Label($"  Korean: {state.CurrentSentence.korean}", _labelStyle);
                    GUILayout.Label($"  Category: {state.CurrentSentence.category}", _labelStyle);
                }

                GUILayout.Space(5);
                GUILayout.Label($"Transcript Length: {state.Transcript?.Length ?? 0}", _labelStyle);
                if (!string.IsNullOrEmpty(state.Transcript))
                {
                    GUILayout.Label($"  Text: {state.Transcript}", _labelStyle);
                }

                GUILayout.Space(5);
                GUILayout.Label($"Feedback: {(string.IsNullOrEmpty(state.Feedback) ? "(none)" : state.Feedback)}", _labelStyle);
            }
            else
            {
                GUILayout.Label("ShadowingState: Not initialized", _wrongStyle);
            }

            GUILayout.Space(10);

            // Sentence Repo info
            var repo = SentenceRepo.Instance;
            if (repo != null)
            {
                GUILayout.Label("Sentence Repository:", _headerStyle);
                GUILayout.Label($"  Total: {repo.TotalSentences}", _labelStyle);
                GUILayout.Label($"  Remaining in shuffle: {repo.RemainingInShuffle}", _labelStyle);
            }

            GUILayout.Space(10);

            // TTS Player info
            var tts = TtsPlayer.Instance;
            if (tts != null)
            {
                GUILayout.Label("TTS Player:", _headerStyle);
                GUILayout.Label($"  Playing: {tts.IsPlaying}", _labelStyle);
                GUILayout.Label($"  Loop: {tts.CurrentLoop}/{tts.TotalLoops}", _labelStyle);
                GUILayout.Label($"  RMS: {tts.CurrentRms:F3}", _labelStyle);
            }

            GUILayout.Space(10);

            // Mic Recorder info
            var mic = MicRecorder.Instance;
            if (mic != null)
            {
                GUILayout.Label("Mic Recorder:", _headerStyle);
                GUILayout.Label($"  Permission: {mic.HasPermission}", _labelStyle);
                GUILayout.Label($"  Recording: {mic.IsRecording}", _labelStyle);
                if (mic.IsRecording)
                {
                    GUILayout.Label($"  Duration: {mic.RecordingDuration:F1}s", _labelStyle);
                }
            }

            GUILayout.Space(10);

            // Config info
            var config = AppConfig.Instance;
            if (config != null)
            {
                GUILayout.Label("Config:", _headerStyle);
                GUILayout.Label($"  Environment: {config.CurrentEnvironment}", _labelStyle);
                GUILayout.Label($"  Backend: {config.BackendBaseUrl}", _labelStyle);
                GUILayout.Label($"  Secure: {(config.IsSecure ? "HTTPS" : "HTTP")}", _labelStyle);
                GUILayout.Label($"  Speed: {config.SpeedProfile}", _labelStyle);
            }

            GUILayout.EndScrollView();

            // Print to Console button
            if (GUILayout.Button("Print State to Console"))
            {
                PrintStateToConsole();
            }

            GUILayout.EndArea();
        }

        private void DrawValidationOverlay()
        {
            // Update validation data
            UpdateValidationData();

            float width = 400;
            float height = 300;
            Rect panelRect = new Rect((Screen.width - width) / 2, Screen.height - height - 50, width, height);

            // Semi-transparent background
            GUI.color = new Color(0, 0, 0, 0.85f);
            GUI.Box(panelRect, "");
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(panelRect.x + 15, panelRect.y + 15, width - 30, height - 30));

            GUILayout.Label("Validation Overlay", _headerStyle);
            GUILayout.Space(10);

            // WER Score
            Color werColor = _werScore >= 0.8f ? Color.green : (_werScore >= 0.5f ? Color.yellow : Color.red);
            GUI.color = werColor;
            GUILayout.Label($"WER Score: {_werScore:F2} ({(_werScore * 100):F0}%)", _headerStyle);
            GUI.color = Color.white;

            GUILayout.Space(10);

            // Mismatched tokens
            GUILayout.Label("Mismatched Tokens:", _labelStyle);
            if (_mismatchedTokens.Count == 0)
            {
                GUILayout.Label("  (none - perfect match!)", _correctStyle);
            }
            else
            {
                foreach (var token in _mismatchedTokens)
                {
                    GUILayout.Label($"  - {token}", _wrongStyle);
                }
            }

            GUILayout.Space(10);

            // Feedback string
            GUILayout.Label("Feedback:", _labelStyle);
            GUILayout.Label(_feedbackString, _labelStyle);

            GUILayout.EndArea();
        }

        private void UpdateValidationData()
        {
            var state = ShadowingState.Instance;
            if (state == null || state.CurrentSentence == null)
            {
                _werScore = 0f;
                _mismatchedTokens.Clear();
                _feedbackString = "(no data)";
                return;
            }

            // Calculate diff
            var diffResult = DiffHighlighter.ComputeDiff(
                state.CurrentSentence.korean,
                state.Transcript ?? ""
            );

            _werScore = diffResult.Similarity;
            _mismatchedTokens.Clear();
            _mismatchedTokens.AddRange(diffResult.MissingWords);
            _mismatchedTokens.AddRange(diffResult.WrongWords);

            _feedbackString = string.IsNullOrEmpty(state.Feedback) ? "(awaiting feedback)" : state.Feedback;
        }

        private void PrintStateToConsole()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========== DEBUG STATE ==========");

            var state = ShadowingState.Instance;
            if (state != null)
            {
                sb.AppendLine($"Phase: {state.CurrentPhase}");
                sb.AppendLine($"Listen Count: {state.ListenCount}/{state.MaxListens}");

                if (state.CurrentSentence != null)
                {
                    sb.AppendLine($"Sentence ID: {state.CurrentSentence.id}");
                    sb.AppendLine($"Sentence: {state.CurrentSentence.korean}");
                    sb.AppendLine($"Category: {state.CurrentSentence.category}");
                }

                sb.AppendLine($"Transcript: {state.Transcript ?? "(null)"}");
                sb.AppendLine($"Transcript Length: {state.Transcript?.Length ?? 0}");
                sb.AppendLine($"Feedback: {state.Feedback ?? "(null)"}");

                // Validation
                if (state.CurrentSentence != null && !string.IsNullOrEmpty(state.Transcript))
                {
                    var diff = DiffHighlighter.ComputeDiff(state.CurrentSentence.korean, state.Transcript);
                    sb.AppendLine($"WER Score: {diff.Similarity:F2}");
                    sb.AppendLine($"Correct Words: {diff.CorrectWords}/{diff.TotalWords}");
                    sb.AppendLine($"Missing: {string.Join(", ", diff.MissingWords)}");
                    sb.AppendLine($"Wrong: {string.Join(", ", diff.WrongWords)}");
                }
            }
            else
            {
                sb.AppendLine("ShadowingState: Not initialized");
            }

            sb.AppendLine("=================================");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Toggle debug panel visibility
        /// </summary>
        public void TogglePanel() => _showPanel = !_showPanel;

        /// <summary>
        /// Toggle validation overlay visibility
        /// </summary>
        public void ToggleValidation() => _showValidation = !_showValidation;

        /// <summary>
        /// Show validation overlay
        /// </summary>
        public void ShowValidation() => _showValidation = true;

        /// <summary>
        /// Hide validation overlay
        /// </summary>
        public void HideValidation() => _showValidation = false;

        #if UNITY_EDITOR
        /// <summary>
        /// Draw the Editor test panel for bypassing mock backend
        /// </summary>
        private void DrawTestPanel()
        {
            float width = 400;
            float height = 320;
            Rect panelRect = new Rect(10, 80, width, height);

            GUI.color = new Color(0.1f, 0.1f, 0.2f, 0.95f);
            GUI.Box(panelRect, "");
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(panelRect.x + 10, panelRect.y + 10, width - 20, height - 20));

            GUILayout.Label("Editor Test Mode", _headerStyle);
            GUILayout.Label("Use these controls to test without real API keys", _labelStyle);
            GUILayout.Space(10);

            // Backend status
            GUILayout.Label("Backend Status:", _headerStyle);
            if (!_mockModeChecked)
            {
                GUILayout.Label("  Checking...", _labelStyle);
            }
            else
            {
                GUILayout.Label($"  TTS: {(_ttsMock ? "MOCK (beeps)" : "LIVE")}", _ttsMock ? _wrongStyle : _correctStyle);
                GUILayout.Label($"  STT: {(_sttMock ? "MOCK (random)" : "LIVE")}", _sttMock ? _wrongStyle : _correctStyle);
                GUILayout.Label($"  Feedback: {(_feedbackMock ? "MOCK" : "LIVE")}", _feedbackMock ? _wrongStyle : _correctStyle);
            }

            GUILayout.Space(10);

            // Editor test mode toggle
            _editorTestMode = GUILayout.Toggle(_editorTestMode, " Enable Manual Transcript Mode");
            GUILayout.Label("When enabled, you can type your transcript instead of speaking", _labelStyle);

            GUILayout.Space(10);

            // Quick actions
            GUILayout.Label("Quick Actions:", _headerStyle);

            // Skip TTS button - advances listen count
            if (GUILayout.Button("Skip TTS (Mark 3 listens complete)"))
            {
                if (ShadowingState.Instance != null)
                {
                    for (int i = ShadowingState.Instance.ListenCount; i < 3; i++)
                    {
                        ShadowingState.Instance.IncrementListenCount();
                    }
                    ShadowingState.Instance.SetPhase(ShadowingState.Phase.Idle);
                    Debug.Log("[DebugPanel] Skipped TTS - listen count set to 3");
                }
            }

            GUILayout.Space(5);

            // Manual transcript entry
            if (_editorTestMode)
            {
                GUILayout.Label("Manual Transcript:", _labelStyle);

                // Pre-fill with current sentence if empty
                if (string.IsNullOrEmpty(_manualTranscript) && ShadowingState.Instance?.CurrentSentence != null)
                {
                    _manualTranscript = ShadowingState.Instance.CurrentSentence.korean;
                }

                _manualTranscript = GUILayout.TextField(_manualTranscript, GUILayout.Height(25));

                if (GUILayout.Button("Submit Manual Transcript"))
                {
                    SubmitManualTranscript();
                }

                GUILayout.Space(5);
                GUILayout.Label("Tip: Copy the Korean sentence above, then modify it to test different scores", _labelStyle);
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// Submit a manual transcript (bypasses STT)
        /// </summary>
        private void SubmitManualTranscript()
        {
            if (string.IsNullOrEmpty(_manualTranscript))
            {
                Debug.LogWarning("[DebugPanel] No transcript entered");
                return;
            }

            var state = ShadowingState.Instance;
            if (state == null)
            {
                Debug.LogError("[DebugPanel] ShadowingState not available");
                return;
            }

            Debug.Log($"[DebugPanel] Submitting manual transcript: {_manualTranscript}");

            // Set the transcript
            state.SetTranscript(_manualTranscript);

            // Get feedback (will use mock if no API key, but that's OK for testing)
            if (FeedbackClient.Instance != null && state.CurrentSentence != null)
            {
                state.SetPhase(ShadowingState.Phase.Processing);
                FeedbackClient.Instance.GetFeedback(
                    state.CurrentSentence.korean,
                    _manualTranscript
                );
            }
            else
            {
                // No feedback client, just go to results with mock feedback
                state.SetFeedback(0, 1f, "Manual test - no feedback API", null, null, null, "");
                state.SetPhase(ShadowingState.Phase.ShowingResult);
            }
        }
        #endif
    }
}
