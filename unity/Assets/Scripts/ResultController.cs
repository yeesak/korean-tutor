using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using System.Text;

namespace ShadowingTutor
{
    /// <summary>
    /// Controller for the Result scene.
    /// Displays:
    /// - Attempt progress (1/3, 2/3, 3/3)
    /// - Transcript (clean)
    /// - Score (0-100)
    /// - Feedback sections (scrollable)
    ///
    /// NEXT button behavior:
    /// - If attempt < 3: Go to next attempt (same sentence)
    /// - If attempt == 3: Go to next sentence
    ///
    /// RETRY button: Retry current attempt
    /// </summary>
    public class ResultController : MonoBehaviour
    {
        [Header("UI References - Attempt Info")]
        [SerializeField] private Text _attemptText;
        [SerializeField] private Text _sentenceText;

        [Header("UI References - Transcript")]
        [Tooltip("For word wrap: Horizontal Overflow = Wrap")]
        [SerializeField] private Text _transcriptText;

        [Header("UI References - Score (Main)")]
        [SerializeField] private Text _scoreText;           // Big score display "85%"
        [SerializeField] private Text _scoreDescriptionText;
        [SerializeField] private Slider _scoreSlider;
        [SerializeField] private Image _scoreFill;

        [Header("UI References - Metrics Breakdown")]
        [SerializeField] private Text _accuracyLabelText;   // "Text Accuracy"
        [SerializeField] private Text _accuracyValueText;   // "85%"
        [SerializeField] private Text _mistakeLabelText;    // "Mistakes"
        [SerializeField] private Text _mistakeValueText;    // "15%"
        [SerializeField] private Text _wrongPartsText;      // Shows wrong characters/words

        [Header("UI References - Feedback (Scrollable)")]
        [Tooltip("Place inside a ScrollView for long feedback")]
        [SerializeField] private Text _diffText;
        [SerializeField] private Text _grammarText;
        [SerializeField] private Text _commentText;

        [Header("UI References - Pronunciation (from xAI Realtime)")]
        [SerializeField] private Text _pronunciationHeaderText;
        [SerializeField] private Text _weakPronunciationText;
        [SerializeField] private Text _strongPronunciationText;
        [SerializeField] private Text _pronunciationCommentText;
        [SerializeField] private GameObject _pronunciationSection;

        [Header("UI References - Buttons")]
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _nextButton;
        [SerializeField] private Text _nextButtonText;

        [Header("Scene Navigation")]
        [SerializeField] private string _tutorRoomSceneName = "TutorRoom";

        [Header("Score Colors")]
        [SerializeField] private Color _scoreLowColor = new Color(1f, 0.3f, 0.3f);
        [SerializeField] private Color _scoreMediumColor = new Color(1f, 0.8f, 0.2f);
        [SerializeField] private Color _scoreHighColor = new Color(0.3f, 0.9f, 0.4f);

        private const string LABEL_NEXT_ATTEMPT = "NEXT ATTEMPT";
        private const string LABEL_NEXT_SENTENCE = "NEXT SENTENCE";

        private bool _isTransitioning = false;
        private float _transitionStartTime = 0f;
        private const float TRANSITION_TIMEOUT = 3f; // Reset after 3 seconds if stuck

        private void Awake()
        {
            Debug.Log("[ResultController] Awake");
            _isTransitioning = false;
        }

        private void Start()
        {
            Debug.Log("[ResultController] Start");
            _isTransitioning = false;
            Initialize();
        }

        private void OnEnable()
        {
            Debug.Log("[ResultController] OnEnable - resetting transition state");
            _isTransitioning = false;
            _transitionStartTime = 0f;

            // Wire buttons once on enable
            WireButtons();
        }

        private void OnDisable()
        {
            Debug.Log("[ResultController] OnDisable - resetting transition state");
            _isTransitioning = false;
        }

        private void WireButtons()
        {
            // RETRY button - simple, robust wiring (NO EventTrigger to avoid double-fire)
            if (_retryButton != null)
            {
                _retryButton.onClick.RemoveAllListeners();
                _retryButton.onClick.AddListener(OnRetryButtonClick);
                _retryButton.interactable = true;
                Debug.Log("[ResultController] RETRY button wired");
            }
            else
            {
                Debug.LogError("[ResultController] RETRY button is NULL!");
            }

            // NEXT button - simple, robust wiring (NO EventTrigger to avoid double-fire)
            if (_nextButton != null)
            {
                _nextButton.onClick.RemoveAllListeners();
                _nextButton.onClick.AddListener(OnNextButtonClick);
                _nextButton.interactable = true;
                Debug.Log("[ResultController] NEXT button wired");
            }
            else
            {
                Debug.LogError("[ResultController] NEXT button is NULL!");
            }
        }

        private void Initialize()
        {
            // Buttons already wired in OnEnable, just configure UI

            // Configure text components for word wrap
            ConfigureTextWordWrap(_transcriptText);
            ConfigureTextWordWrap(_diffText);
            ConfigureTextWordWrap(_grammarText);
            ConfigureTextWordWrap(_commentText);
            ConfigureTextWordWrap(_weakPronunciationText);
            ConfigureTextWordWrap(_strongPronunciationText);
            ConfigureTextWordWrap(_pronunciationCommentText);

            // Display results
            DisplayResults();

            // Log button state for debugging
            LogButtonState();
        }

        private void LogButtonState()
        {
            Debug.Log("=== Button State Check ===");
            if (_retryButton != null)
            {
                int runtimeListeners = _retryButton.onClick.GetPersistentEventCount();
                Debug.Log($"[ResultController] RETRY: active={_retryButton.gameObject.activeInHierarchy}, " +
                          $"interactable={_retryButton.interactable}, persistentListeners={runtimeListeners}");

                // Test if click event fires by checking listener attachment
                var img = _retryButton.GetComponent<Image>();
                if (img != null)
                {
                    Debug.Log($"[ResultController] RETRY Image: raycastTarget={img.raycastTarget}, enabled={img.enabled}");
                }
            }
            else
            {
                Debug.LogError("[ResultController] RETRY button is NULL!");
            }

            if (_nextButton != null)
            {
                int runtimeListeners = _nextButton.onClick.GetPersistentEventCount();
                Debug.Log($"[ResultController] NEXT: active={_nextButton.gameObject.activeInHierarchy}, " +
                          $"interactable={_nextButton.interactable}, persistentListeners={runtimeListeners}");
            }
            else
            {
                Debug.LogError("[ResultController] NEXT button is NULL!");
            }

            // Check scene exists
            Debug.Log($"[ResultController] Target scene: '{_tutorRoomSceneName}'");
            Debug.Log($"[ResultController] ShadowingState.Instance: {(ShadowingState.Instance != null ? "OK" : "NULL")}");
            Debug.Log("=== End Button State ===");
        }

        private void ConfigureTextWordWrap(Text textComponent)
        {
            if (textComponent == null) return;
            textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            textComponent.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void DisplayResults()
        {
            if (ShadowingState.Instance == null)
            {
                Debug.LogError("[ResultController] No ShadowingState found!");
                DisplayPlaceholder();
                return;
            }

            var state = ShadowingState.Instance;
            var sentence = state.CurrentSentence;

            if (sentence == null)
            {
                Debug.LogError("[ResultController] No current sentence!");
                DisplayPlaceholder();
                return;
            }

            // Display attempt info
            DisplayAttemptInfo(state);

            // Display sentence
            if (_sentenceText != null)
            {
                _sentenceText.text = $"{sentence.korean}\n{sentence.english}";
            }

            // Display transcript (clean, no formatting)
            if (_transcriptText != null)
            {
                string transcript = state.Transcript;
                _transcriptText.text = !string.IsNullOrEmpty(transcript)
                    ? $"네가 말한 것: {transcript}"
                    : "네가 말한 것: (음성 없음)";

                // DEBUG: Verify transcript is clean text, not diff
                Debug.Log($"[ResultController] Transcript display: \"{transcript}\"");
                if (transcript != null && transcript.Contains("<color"))
                {
                    Debug.LogError("[ResultController] ERROR: Transcript contains rich text tags! This should be clean text.");
                }
            }

            // Display score
            DisplayScore(state);

            // Display diff
            if (_diffText != null && !string.IsNullOrEmpty(state.DiffRichText))
            {
                _diffText.text = state.DiffRichText;
                _diffText.supportRichText = true;
            }
            else if (_diffText != null)
            {
                _diffText.text = "";
            }

            // Display pronunciation (from xAI Realtime)
            DisplayPronunciation(state);

            // Display grammar
            DisplayGrammar(state);

            // Display comment
            DisplayComment(state);

            // Update NEXT button text
            UpdateNextButton(state);

            Debug.Log($"[ResultController] Displayed results: attempt={state.AttemptIndex}, accuracy={state.AccuracyPercent}%");
        }

        private void DisplayAttemptInfo(ShadowingState state)
        {
            if (_attemptText != null)
            {
                _attemptText.text = state.GetAttemptLabel();
            }
        }

        private void DisplayScore(ShadowingState state)
        {
            int accuracy = state.AccuracyPercent;
            int mistakes = state.WrongPercent;
            Color scoreColor = GetScoreColor(accuracy);

            // Main score display (big)
            if (_scoreText != null)
            {
                _scoreText.text = $"{accuracy}%";
                _scoreText.color = scoreColor;
            }

            // Score description
            if (_scoreDescriptionText != null)
            {
                _scoreDescriptionText.text = GetScoreDescription(accuracy);
            }

            // Slider
            if (_scoreSlider != null)
            {
                _scoreSlider.value = accuracy / 100f;
            }

            // Slider fill color
            if (_scoreFill != null)
            {
                _scoreFill.color = scoreColor;
            }

            // Metrics breakdown - Text Accuracy
            if (_accuracyLabelText != null)
            {
                _accuracyLabelText.text = "정확도";
            }
            if (_accuracyValueText != null)
            {
                _accuracyValueText.text = $"{accuracy}%";
                _accuracyValueText.color = scoreColor;
            }

            // Metrics breakdown - Mistakes
            if (_mistakeLabelText != null)
            {
                _mistakeLabelText.text = "틀린 부분";
            }
            if (_mistakeValueText != null)
            {
                _mistakeValueText.text = $"{mistakes}%";
                _mistakeValueText.color = mistakes > 0 ? _scoreLowColor : _scoreHighColor;
            }

            // Wrong parts for highlighting
            DisplayWrongParts(state);
        }

        private void DisplayWrongParts(ShadowingState state)
        {
            if (_wrongPartsText == null) return;

            var wrongParts = state.WrongUnits;
            if (wrongParts == null || wrongParts.Length == 0)
            {
                _wrongPartsText.text = "";
                return;
            }

            // Show wrong characters/words with highlighting
            _wrongPartsText.supportRichText = true;
            var sb = new StringBuilder();
            sb.Append("<color=#888888>오류: </color>");
            sb.Append("<color=red>");
            sb.Append(string.Join(" ", wrongParts));
            sb.Append("</color>");
            _wrongPartsText.text = sb.ToString();
        }

        private void DisplayPronunciation(ShadowingState state)
        {
            // Check if pronunciation data is available from either source
            bool hasTutorPron = (state.TutorPronunciationWeak != null && state.TutorPronunciationWeak.Length > 0)
                             || (state.TutorPronunciationStrong != null && state.TutorPronunciationStrong.Length > 0);
            bool hasEvalPron = state.PronunciationAvailable;

            // Show/hide pronunciation section
            if (_pronunciationSection != null)
            {
                _pronunciationSection.SetActive(hasTutorPron || hasEvalPron);
            }

            if (!hasTutorPron && !hasEvalPron)
            {
                // Clear all pronunciation UI
                if (_pronunciationHeaderText != null) _pronunciationHeaderText.text = "";
                if (_weakPronunciationText != null) _weakPronunciationText.text = "";
                if (_strongPronunciationText != null) _strongPronunciationText.text = "";
                if (_pronunciationCommentText != null) _pronunciationCommentText.text = "";
                return;
            }

            // Header
            if (_pronunciationHeaderText != null)
            {
                _pronunciationHeaderText.text = "발음 피드백";
            }

            // Weak pronunciation (needs work)
            if (_weakPronunciationText != null)
            {
                _weakPronunciationText.supportRichText = true;
                var sb = new StringBuilder();

                // First try tutor pronunciation data (from /api/feedback)
                var tutorWeak = state.TutorPronunciationWeak;
                if (tutorWeak != null && tutorWeak.Length > 0)
                {
                    sb.AppendLine("<b><color=#FF6600>연습 필요:</color></b>");
                    foreach (var item in tutorWeak)
                    {
                        sb.AppendLine($"<color=red>• {item.token}</color>");
                        if (!string.IsNullOrEmpty(item.reason))
                            sb.AppendLine($"  <color=#888888>{item.reason}</color>");
                        if (!string.IsNullOrEmpty(item.tip))
                            sb.AppendLine($"  <color=#4CAF50>팁: {item.tip}</color>");
                    }
                }
                // Fallback to eval pronunciation data (from /api/eval)
                else
                {
                    var weak = state.WeakPronunciation;
                    if (weak != null && weak.Length > 0)
                    {
                        sb.AppendLine("<b><color=#FF6600>연습 필요:</color></b>");
                        foreach (var item in weak)
                        {
                            sb.AppendLine($"<color=red>• {item.token}</color>");
                            if (!string.IsNullOrEmpty(item.reason))
                                sb.AppendLine($"  <color=#888888>{item.reason}</color>");
                            if (!string.IsNullOrEmpty(item.tip))
                                sb.AppendLine($"  <color=#4CAF50>팁: {item.tip}</color>");
                        }
                    }
                }

                _weakPronunciationText.text = sb.ToString();
            }

            // Strong pronunciation (good)
            if (_strongPronunciationText != null)
            {
                _strongPronunciationText.supportRichText = true;
                var sb = new StringBuilder();

                // First try tutor pronunciation data (from /api/feedback)
                var tutorStrong = state.TutorPronunciationStrong;
                if (tutorStrong != null && tutorStrong.Length > 0)
                {
                    sb.AppendLine("<b><color=#4CAF50>잘했어요:</color></b>");
                    foreach (var item in tutorStrong)
                    {
                        sb.AppendLine($"<color=green>• {item.token}</color>");
                        if (!string.IsNullOrEmpty(item.reason))
                            sb.AppendLine($"  <color=#888888>{item.reason}</color>");
                    }
                }
                // Fallback to eval pronunciation data
                else
                {
                    var strong = state.StrongPronunciation;
                    if (strong != null && strong.Length > 0)
                    {
                        sb.AppendLine("<b><color=#4CAF50>잘했어요:</color></b>");
                        foreach (var item in strong)
                        {
                            sb.AppendLine($"<color=green>• {item.token}</color>");
                            if (!string.IsNullOrEmpty(item.reason))
                                sb.AppendLine($"  <color=#888888>{item.reason}</color>");
                        }
                    }
                }

                _strongPronunciationText.text = sb.ToString();
            }

            // Pronunciation comment
            if (_pronunciationCommentText != null)
            {
                string comment = state.PronunciationComment;
                _pronunciationCommentText.text = !string.IsNullOrEmpty(comment)
                    ? $"<i>{comment}</i>"
                    : "";
                _pronunciationCommentText.supportRichText = true;
            }
        }

        private void DisplayGrammar(ShadowingState state)
        {
            if (_grammarText == null) return;

            _grammarText.supportRichText = true;

            // NEW: Try TutorGrammarMistakes from /api/feedback (Grok tutor)
            var tutorMistakes = state.TutorGrammarMistakes;
            if (tutorMistakes != null && tutorMistakes.Length > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<b>문법 피드백:</b>\n");

                foreach (var m in tutorMistakes)
                {
                    // Format: "학생: X / 정답: Y / 이유: Z"
                    sb.AppendLine($"<color=red>학생: {m.youSaid}</color>");
                    sb.AppendLine($"<color=green>정답: {m.correct}</color>");
                    if (!string.IsNullOrEmpty(m.reasonKo))
                    {
                        sb.AppendLine($"<color=#888888>이유: {m.reasonKo}</color>");
                    }
                    sb.AppendLine();
                }

                _grammarText.text = sb.ToString();
                return;
            }

            // Fallback: Try GrammarMistakes format (from /api/eval)
            var mistakes = state.GrammarMistakes;
            if (mistakes != null && mistakes.Length > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<b>문법 피드백:</b>\n");

                foreach (var m in mistakes)
                {
                    sb.AppendLine($"<color=red>학생: {m.youSaid}</color>");
                    sb.AppendLine($"<color=green>정답: {m.correct}</color>");
                    if (!string.IsNullOrEmpty(m.why))
                    {
                        sb.AppendLine($"<color=#888888>이유: {m.why}</color>");
                    }
                    sb.AppendLine();
                }

                _grammarText.text = sb.ToString();
                return;
            }

            // Fallback: Legacy GrammarCorrections format
            var corrections = state.GrammarCorrections;
            if (corrections != null && corrections.Length > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<b>문법 수정:</b>\n");

                foreach (var c in corrections)
                {
                    sb.AppendLine($"<color=red>학생: {c.wrong}</color>");
                    sb.AppendLine($"<color=green>정답: {c.correct}</color>");
                    if (!string.IsNullOrEmpty(c.reason_ko))
                    {
                        sb.AppendLine($"<color=#888888>이유: {c.reason_ko}</color>");
                    }
                    sb.AppendLine();
                }

                _grammarText.text = sb.ToString();
                return;
            }

            // No grammar issues - friendly Korean message
            _grammarText.text = "<color=#4CAF50>문법 완벽해!</color>";
        }

        private void DisplayComment(ShadowingState state)
        {
            if (_commentText == null) return;

            _commentText.supportRichText = true;

            int score = state.AccuracyPercent;

            // DEBUG: Log all tutor-related state
            Debug.Log($"[ResultController] DisplayComment: score={score}%, " +
                      $"HasTutor={state.HasTutor}, HasTutorError={state.HasTutorError}, " +
                      $"TutorCommentKo=\"{state.TutorCommentKo}\", " +
                      $"TutorComment=\"{state.TutorComment}\", GrammarComment=\"{state.GrammarComment}\"");

            // Check if tutor feedback is available or if there's an error
            if (state.HasTutorError)
            {
                // Show tutor error - AI feedback unavailable
                var err = state.TutorError;
                string errorMsg = $"<color=#FF6600>AI feedback unavailable</color>\n";
                errorMsg += $"<color=#888888>Error: {err.code}</color>";
                if (!string.IsNullOrEmpty(err.message))
                {
                    errorMsg += $"\n<color=#888888>{err.message}</color>";
                }
                Debug.Log($"[ResultController] Showing tutor error: {err.code}");
                _commentText.text = errorMsg;
                return;
            }

            // If we have tutor with comment, use it
            string tutorComment = state.TutorCommentKo;
            if (!string.IsNullOrEmpty(tutorComment))
            {
                // Only show praise if accuracy is high enough
                if (score < 50 && ContainsPraise(tutorComment))
                {
                    // Low score but tutor praised - show friendly retry message instead (반말)
                    Debug.Log($"[ResultController] Overriding tutor praise for low score ({score}%)");
                    _commentText.text = "<b>그록:</b> 음, 다시 한 번 해볼까? 잘 들어봐~";
                }
                else
                {
                    Debug.Log($"[ResultController] Using tutor comment: {tutorComment}");
                    _commentText.text = $"<b>그록:</b> {tutorComment}";
                }
                return;
            }

            // Fallback: TutorComment from /api/eval or GrammarComment
            string comment = !string.IsNullOrEmpty(state.TutorComment)
                ? state.TutorComment
                : state.GrammarComment;

            if (!string.IsNullOrEmpty(comment))
            {
                Debug.Log($"[ResultController] Using fallback comment: {comment}");
                _commentText.text = $"<b>그록:</b> {comment}";
            }
            else
            {
                // Default comment based on score - no tutor available (3-tier system)
                string defaultComment = GetDefaultComment(score);
                Debug.Log($"[ResultController] Using default comment for score {score}%: {defaultComment}");
                _commentText.text = $"<b>그록:</b> {defaultComment}";
            }
        }

        /// <summary>
        /// Check if comment contains praise words that shouldn't be shown for low scores
        /// </summary>
        private bool ContainsPraise(string comment)
        {
            if (string.IsNullOrEmpty(comment)) return false;
            return comment.Contains("잘했") || comment.Contains("좋아") ||
                   comment.Contains("완벽") || comment.Contains("훌륭") ||
                   comment.Contains("Great") || comment.Contains("Excellent") ||
                   comment.Contains("Good") || comment.Contains("Perfect");
        }

        private void UpdateNextButton(ShadowingState state)
        {
            if (_nextButtonText != null)
            {
                if (state.IsLastAttempt)
                {
                    _nextButtonText.text = LABEL_NEXT_SENTENCE;
                }
                else
                {
                    _nextButtonText.text = LABEL_NEXT_ATTEMPT;
                }
            }
        }

        private void DisplayPlaceholder()
        {
            if (_attemptText != null) _attemptText.text = "";
            if (_sentenceText != null) _sentenceText.text = "(No sentence)";
            if (_transcriptText != null) _transcriptText.text = "";
            if (_scoreText != null) _scoreText.text = "0%";
            if (_scoreDescriptionText != null) _scoreDescriptionText.text = "";
            if (_diffText != null) _diffText.text = "";
            if (_grammarText != null) _grammarText.text = "";
            if (_commentText != null) _commentText.text = "";
            if (_scoreSlider != null) _scoreSlider.value = 0;
            if (_nextButtonText != null) _nextButtonText.text = LABEL_NEXT_ATTEMPT;

            // Clear metrics breakdown
            if (_accuracyLabelText != null) _accuracyLabelText.text = "";
            if (_accuracyValueText != null) _accuracyValueText.text = "";
            if (_mistakeLabelText != null) _mistakeLabelText.text = "";
            if (_mistakeValueText != null) _mistakeValueText.text = "";
            if (_wrongPartsText != null) _wrongPartsText.text = "";

            // Clear pronunciation section
            if (_pronunciationSection != null) _pronunciationSection.SetActive(false);
            if (_pronunciationHeaderText != null) _pronunciationHeaderText.text = "";
            if (_weakPronunciationText != null) _weakPronunciationText.text = "";
            if (_strongPronunciationText != null) _strongPronunciationText.text = "";
            if (_pronunciationCommentText != null) _pronunciationCommentText.text = "";
        }

        private Color GetScoreColor(int score)
        {
            if (score >= 80) return _scoreHighColor;
            if (score >= 50) return _scoreMediumColor;
            return _scoreLowColor;
        }

        private string GetScoreDescription(int score)
        {
            // Korean descriptions matching friendly tutor tone (반말)
            if (score >= 90) return "완벽해!";
            if (score >= 80) return "잘했어!";
            if (score >= 70) return "좋아!";
            if (score >= 50) return "조금 더 연습해 봐!";
            return "다시 해볼까?";
        }

        /// <summary>
        /// Get default comment based on 3-tier feedback system
        /// ✅ Correct (>=90%): "정확해! 잘했어~"
        /// ☑️ Partial (50-89%): "좋았어! 다시 한 번 해볼까?"
        /// ❌ Severe (<50%): "음, 다시 한 번 해볼까?"
        /// </summary>
        private string GetDefaultComment(int score)
        {
            if (score >= 90)
            {
                // Correct - praise and encourage
                return "정확해! 잘했어~";
            }
            else if (score >= 50)
            {
                // Partial - acknowledge effort, encourage retry
                return "좋았어! 조금 더 연습해 볼까?";
            }
            else
            {
                // Severe - gentle encouragement to retry (no praise)
                return "음, 다시 한 번 해볼까? 잘 들어봐~";
            }
        }

        #region Button Handlers

        private void OnRetryButtonClick()
        {
            Debug.Log("[ResultController] RETRY clicked");

            if (_isTransitioning)
            {
                // Check for timeout - if stuck too long, force reset
                if (Time.time - _transitionStartTime > TRANSITION_TIMEOUT)
                {
                    Debug.LogWarning("[ResultController] RETRY: Transition timeout, forcing reset");
                    ResetTransitionState();
                }
                else
                {
                    Debug.Log("[ResultController] RETRY: Already transitioning, ignoring");
                    return;
                }
            }

            StartTransition();

            ShadowingState.Instance?.RetryAttempt();

            LoadSceneSafely(_tutorRoomSceneName);
        }

        private void OnNextButtonClick()
        {
            Debug.Log("[ResultController] NEXT clicked");

            if (_isTransitioning)
            {
                // Check for timeout - if stuck too long, force reset
                if (Time.time - _transitionStartTime > TRANSITION_TIMEOUT)
                {
                    Debug.LogWarning("[ResultController] NEXT: Transition timeout, forcing reset");
                    ResetTransitionState();
                }
                else
                {
                    Debug.Log("[ResultController] NEXT: Already transitioning, ignoring");
                    return;
                }
            }

            StartTransition();

            if (ShadowingState.Instance != null)
            {
                bool hasNextAttempt = ShadowingState.Instance.AdvanceToNextAttempt();

                if (!hasNextAttempt && SentenceRepo.Instance != null)
                {
                    var nextSentence = SentenceRepo.Instance.GetNext();
                    if (nextSentence != null)
                    {
                        ShadowingState.Instance.StartNewSentence(nextSentence);
                    }
                }
            }

            LoadSceneSafely(_tutorRoomSceneName);
        }

        private void StartTransition()
        {
            _isTransitioning = true;
            _transitionStartTime = Time.time;

            // Disable buttons
            if (_retryButton != null) _retryButton.interactable = false;
            if (_nextButton != null) _nextButton.interactable = false;

            Debug.Log("[ResultController] Transition started");
        }

        private void ResetTransitionState()
        {
            _isTransitioning = false;
            _transitionStartTime = 0f;

            // Re-enable buttons
            if (_retryButton != null) _retryButton.interactable = true;
            if (_nextButton != null) _nextButton.interactable = true;

            Debug.Log("[ResultController] Transition state reset");
        }

        private void LoadSceneSafely(string sceneName)
        {
            try
            {
                Debug.Log($"[ResultController] Loading scene: {sceneName}");
                SceneManager.LoadScene(sceneName);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ResultController] Scene load failed: {e.Message}");
                ResetTransitionState();
            }
        }

        #endregion

        #region Keyboard Support and Update

        private void Update()
        {
            // Check for transition timeout in Update (backup safety)
            if (_isTransitioning && _transitionStartTime > 0f)
            {
                if (Time.time - _transitionStartTime > TRANSITION_TIMEOUT)
                {
                    Debug.LogWarning("[ResultController] Update: Transition timeout detected, forcing reset");
                    ResetTransitionState();
                }
                return; // Don't process keyboard while transitioning
            }

            // Keyboard shortcuts (Editor/Standalone only - not available on mobile with new Input System)
#if UNITY_EDITOR || UNITY_STANDALONE
            if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Escape))
            {
                OnRetryButtonClick();
            }
            else if (Input.GetKeyDown(KeyCode.N) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
            {
                OnNextButtonClick();
            }
#endif
        }

        #endregion

        #region UI Clickability Verification

        /// <summary>
        /// Verify that UI is properly configured for button clicks.
        /// Call this from Unity Editor or during debugging.
        /// </summary>
        [ContextMenu("Verify UI Clickability")]
        public void VerifyUIClickability()
        {
            Debug.Log("=== UI Clickability Verification ===");

            // Check EventSystem
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null)
            {
                Debug.LogError("FAIL: No EventSystem found in scene! Add EventSystem to the scene.");
            }
            else
            {
                Debug.Log($"OK: EventSystem found: {eventSystem.name}");
            }

            // Check Canvas and GraphicRaycaster
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("FAIL: No Canvas found in parent hierarchy!");
            }
            else
            {
                Debug.Log($"OK: Canvas found: {canvas.name}");

                var raycaster = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                if (raycaster == null)
                {
                    Debug.LogError("FAIL: Canvas missing GraphicRaycaster component!");
                }
                else
                {
                    Debug.Log($"OK: GraphicRaycaster found on canvas");
                }
            }

            // Check RETRY button
            if (_retryButton != null)
            {
                Debug.Log($"RETRY Button:");
                Debug.Log($"  - GameObject active: {_retryButton.gameObject.activeInHierarchy}");
                Debug.Log($"  - Interactable: {_retryButton.interactable}");
                Debug.Log($"  - Listener count: {_retryButton.onClick.GetPersistentEventCount()}");

                // Check if button has Image with Raycast Target
                var buttonImage = _retryButton.GetComponent<UnityEngine.UI.Image>();
                if (buttonImage != null)
                {
                    Debug.Log($"  - Image raycastTarget: {buttonImage.raycastTarget}");
                    if (!buttonImage.raycastTarget)
                    {
                        Debug.LogWarning("  WARNING: Image raycastTarget is false - button won't receive clicks!");
                    }
                }

                // Check CanvasGroup blocking
                var canvasGroup = _retryButton.GetComponentInParent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    Debug.Log($"  - CanvasGroup blocksRaycasts: {canvasGroup.blocksRaycasts}");
                    Debug.Log($"  - CanvasGroup interactable: {canvasGroup.interactable}");
                    if (!canvasGroup.blocksRaycasts || !canvasGroup.interactable)
                    {
                        Debug.LogWarning("  WARNING: CanvasGroup may be blocking button interaction!");
                    }
                }
            }
            else
            {
                Debug.LogError("FAIL: _retryButton reference is NULL!");
            }

            // Check NEXT button
            if (_nextButton != null)
            {
                Debug.Log($"NEXT Button:");
                Debug.Log($"  - GameObject active: {_nextButton.gameObject.activeInHierarchy}");
                Debug.Log($"  - Interactable: {_nextButton.interactable}");
                Debug.Log($"  - Listener count: {_nextButton.onClick.GetPersistentEventCount()}");

                var buttonImage = _nextButton.GetComponent<UnityEngine.UI.Image>();
                if (buttonImage != null)
                {
                    Debug.Log($"  - Image raycastTarget: {buttonImage.raycastTarget}");
                }

                var canvasGroup = _nextButton.GetComponentInParent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    Debug.Log($"  - CanvasGroup blocksRaycasts: {canvasGroup.blocksRaycasts}");
                    Debug.Log($"  - CanvasGroup interactable: {canvasGroup.interactable}");
                }
            }
            else
            {
                Debug.LogError("FAIL: _nextButton reference is NULL!");
            }

            Debug.Log("=== End UI Verification ===");
        }

        #endregion
    }
}
