using System;
using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Central state management for the shadowing session.
    ///
    /// NEW INTERLEAVED 3-STEP FLOW:
    /// For each sentence, user makes 3 attempts:
    ///   Attempt 1: TTS (normal) → Record → Evaluate → Feedback
    ///   Attempt 2: TTS (slower) → Record → Evaluate → Feedback
    ///   Attempt 3: TTS (faster) → Record → Evaluate → Feedback
    /// Then move to next sentence.
    ///
    /// State machine: Idle → Loading → Listening → Recording → Processing → ShowingResult
    /// </summary>
    public class ShadowingState : MonoBehaviour
    {
        private static ShadowingState _instance;
        public static ShadowingState Instance => _instance;

        public enum Phase
        {
            Idle,           // Ready to start (button shows START)
            Loading,        // Loading TTS audio
            Listening,      // Playing TTS once (button shows LISTENING...)
            Recording,      // User is speaking (button shows RECORDING - Tap to stop)
            Processing,     // STT + Feedback in progress (button shows EVALUATING...)
            ShowingResult,  // Displaying result screen
            Error           // Error occurred
        }

        [Header("Current State")]
        [SerializeField] private Phase _currentPhase = Phase.Idle;

        // Attempt tracking (1, 2, or 3)
        private int _attemptIndex = 1;
        public const int MAX_ATTEMPTS = 3;

        // Current sentence data
        private SentenceRepo.Sentence _currentSentence;

        // Results - structured format
        private string _transcript = "";
        private string _diffRichText = "";

        // Metrics (CER-based)
        private int _accuracyPercent = 0;
        private int _wrongPercent = 0;
        private float _cer = 0f;
        private float _wer = 0f;

        // Diff data
        private ApiClient.DiffUnit[] _diffUnits = new ApiClient.DiffUnit[0];
        private string[] _wrongUnits = new string[0];

        // Pronunciation (from /api/eval - xAI Realtime)
        private bool _pronunciationAvailable = false;
        private string _pronunciationNote = "";
        private ApiClient.WeakPronunciationItem[] _weakPronunciation = new ApiClient.WeakPronunciationItem[0];
        private ApiClient.StrongPronunciationItem[] _strongPronunciation = new ApiClient.StrongPronunciationItem[0];
        private string _pronunciationComment = "";

        // Grammar corrections (from /api/eval)
        private ApiClient.GrammarCorrection[] _grammarCorrections = new ApiClient.GrammarCorrection[0];
        private string _grammarComment = "";
        private ApiClient.GrammarMistake[] _grammarMistakes = new ApiClient.GrammarMistake[0];
        private string _tutorComment = "";

        // NEW: Tutor feedback from Grok (from /api/feedback)
        private ApiClient.TutorFeedback _tutor = null;
        private ApiClient.TutorError _tutorError = null;
        private ApiClient.TutorGrammarMistake[] _tutorGrammarMistakes = new ApiClient.TutorGrammarMistake[0];
        private ApiClient.TutorPronunciationItem[] _tutorPronunciationWeak = new ApiClient.TutorPronunciationItem[0];
        private ApiClient.TutorPronunciationItem[] _tutorPronunciationStrong = new ApiClient.TutorPronunciationItem[0];

        // Other
        private string _nextSentence = "";
        private string _errorMessage = "";

        // Attempt history (for showing progress)
        private int[] _attemptScores = new int[MAX_ATTEMPTS];

        // Events
        public event Action<Phase> OnPhaseChanged;
        public event Action<int> OnAttemptChanged;
        public event Action<string> OnTranscriptReceived;
        public event Action<string> OnFeedbackReceived;
        public event Action<string> OnError;

        // Properties - Basic
        public Phase CurrentPhase => _currentPhase;
        public SentenceRepo.Sentence CurrentSentence => _currentSentence;
        public int AttemptIndex => _attemptIndex;
        public int MaxAttempts => MAX_ATTEMPTS;
        public bool IsLastAttempt => _attemptIndex >= MAX_ATTEMPTS;
        public string Transcript => _transcript;
        public string DiffRichText => _diffRichText;

        // Properties - Metrics
        public int AccuracyPercent => _accuracyPercent;
        public int WrongPercent => _wrongPercent;
        public float CER => _cer;
        public float WER => _wer;
        public int Score => _accuracyPercent;

        // Properties - Diff
        public ApiClient.DiffUnit[] DiffUnits => _diffUnits;
        public string[] WrongUnits => _wrongUnits;

        // Properties - Pronunciation (from /api/eval)
        public bool PronunciationAvailable => _pronunciationAvailable;
        public string PronunciationNote => _pronunciationNote;
        public ApiClient.WeakPronunciationItem[] WeakPronunciation => _weakPronunciation;
        public ApiClient.StrongPronunciationItem[] StrongPronunciation => _strongPronunciation;
        public string PronunciationComment => _pronunciationComment;
        public bool HasWeakPronunciation => _weakPronunciation != null && _weakPronunciation.Length > 0;
        public bool HasStrongPronunciation => _strongPronunciation != null && _strongPronunciation.Length > 0;

        // Properties - Grammar (from /api/eval)
        public ApiClient.GrammarCorrection[] GrammarCorrections => _grammarCorrections;
        public string GrammarComment => _grammarComment;
        public ApiClient.GrammarMistake[] GrammarMistakes => _grammarMistakes;
        public string TutorComment => _tutorComment;
        public bool HasGrammarMistakes => _grammarMistakes != null && _grammarMistakes.Length > 0;

        // Properties - Tutor (from /api/feedback with Grok)
        public ApiClient.TutorFeedback Tutor => _tutor;
        public ApiClient.TutorError TutorError => _tutorError;
        public bool HasTutor => _tutor != null;
        public bool HasTutorError => _tutorError != null && !string.IsNullOrEmpty(_tutorError.code);
        public ApiClient.TutorGrammarMistake[] TutorGrammarMistakes => _tutorGrammarMistakes;
        public ApiClient.TutorPronunciationItem[] TutorPronunciationWeak => _tutorPronunciationWeak;
        public ApiClient.TutorPronunciationItem[] TutorPronunciationStrong => _tutorPronunciationStrong;
        public string TutorCommentKo => _tutor?.commentKo ?? "";
        public bool HasTutorGrammarMistakes => _tutorGrammarMistakes != null && _tutorGrammarMistakes.Length > 0;

        // 3-Tier Feedback Level: "correct", "partial", "severe"
        public string FeedbackLevel => _tutor?.feedbackLevel ?? (AccuracyPercent >= 90 ? "correct" : AccuracyPercent >= 50 ? "partial" : "severe");
        public bool IsFeedbackCorrect => FeedbackLevel == "correct";
        public bool IsFeedbackPartial => FeedbackLevel == "partial";
        public bool IsFeedbackSevere => FeedbackLevel == "severe";

        // Properties - Other
        public string NextSentence => _nextSentence;
        public string ErrorMessage => _errorMessage;
        public int[] AttemptScores => _attemptScores;

        // Backward compatibility
        public int ListenCount => 1;  // Always 1 listen per attempt now
        public int MaxListens => 1;
        public bool CanRecord => _currentPhase == Phase.Idle;
        public string Feedback => _grammarComment;
        public string[] Mistakes => _wrongUnits;

        /// <summary>
        /// Get TTS speed profile for current attempt
        /// Attempt 1: Normal (1), Attempt 2: Slower (0), Attempt 3: Faster (2)
        /// </summary>
        public int GetSpeedProfileForAttempt()
        {
            switch (_attemptIndex)
            {
                case 1: return 1;  // Normal
                case 2: return 0;  // Slower
                case 3: return 2;  // Faster
                default: return 1;
            }
        }

        /// <summary>
        /// Get attempt label for UI display
        /// </summary>
        public string GetAttemptLabel()
        {
            switch (_attemptIndex)
            {
                case 1: return "Attempt 1/3 (Normal)";
                case 2: return "Attempt 2/3 (Slower)";
                case 3: return "Attempt 3/3 (Faster)";
                default: return $"Attempt {_attemptIndex}/{MAX_ATTEMPTS}";
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Set the current phase
        /// </summary>
        public void SetPhase(Phase phase)
        {
            if (_currentPhase != phase)
            {
                Debug.Log($"[ShadowingState] Phase: {_currentPhase} -> {phase}");
                _currentPhase = phase;
                OnPhaseChanged?.Invoke(phase);
            }
        }

        /// <summary>
        /// Start a new sentence (resets attempt to 1)
        /// </summary>
        public void StartNewSentence(SentenceRepo.Sentence sentence)
        {
            _currentSentence = sentence;
            _attemptIndex = 1;
            ResetAttemptData();
            _attemptScores = new int[MAX_ATTEMPTS];
            SetPhase(Phase.Idle);
            OnAttemptChanged?.Invoke(_attemptIndex);
            Debug.Log($"[ShadowingState] New sentence: {sentence.korean}, starting attempt 1");
        }

        /// <summary>
        /// Advance to next attempt (called from Result screen NEXT button)
        /// Returns true if moved to next attempt, false if should move to next sentence
        /// </summary>
        public bool AdvanceToNextAttempt()
        {
            // Store current attempt score
            if (_attemptIndex >= 1 && _attemptIndex <= MAX_ATTEMPTS)
            {
                _attemptScores[_attemptIndex - 1] = _accuracyPercent;
            }

            if (_attemptIndex < MAX_ATTEMPTS)
            {
                _attemptIndex++;
                ResetAttemptData();
                SetPhase(Phase.Idle);
                OnAttemptChanged?.Invoke(_attemptIndex);
                Debug.Log($"[ShadowingState] Advanced to attempt {_attemptIndex}");
                return true;
            }
            else
            {
                Debug.Log("[ShadowingState] All attempts complete, ready for next sentence");
                return false;
            }
        }

        /// <summary>
        /// Reset data for a new attempt (keeps sentence, clears results)
        /// </summary>
        private void ResetAttemptData()
        {
            _transcript = "";
            _diffRichText = "";
            _accuracyPercent = 0;
            _wrongPercent = 0;
            _cer = 0f;
            _wer = 0f;
            _diffUnits = new ApiClient.DiffUnit[0];
            _wrongUnits = new string[0];

            // Pronunciation
            _pronunciationAvailable = false;
            _pronunciationNote = "";
            _weakPronunciation = new ApiClient.WeakPronunciationItem[0];
            _strongPronunciation = new ApiClient.StrongPronunciationItem[0];
            _pronunciationComment = "";

            // Grammar
            _grammarCorrections = new ApiClient.GrammarCorrection[0];
            _grammarComment = "";
            _grammarMistakes = new ApiClient.GrammarMistake[0];
            _tutorComment = "";

            // Tutor (from /api/feedback with Grok)
            _tutor = null;
            _tutorError = null;
            _tutorGrammarMistakes = new ApiClient.TutorGrammarMistake[0];
            _tutorPronunciationWeak = new ApiClient.TutorPronunciationItem[0];
            _tutorPronunciationStrong = new ApiClient.TutorPronunciationItem[0];

            _nextSentence = "";
            _errorMessage = "";
        }

        /// <summary>
        /// Retry current attempt (same sentence, same attempt index)
        /// </summary>
        public void RetryAttempt()
        {
            ResetAttemptData();
            SetPhase(Phase.Idle);
            Debug.Log($"[ShadowingState] Retrying attempt {_attemptIndex}");
        }

        /// <summary>
        /// Set the transcript from STT
        /// </summary>
        public void SetTranscript(string transcript)
        {
            _transcript = transcript ?? "";
            OnTranscriptReceived?.Invoke(_transcript);
            Debug.Log($"[ShadowingState] Transcript: {_transcript}");

            // Generate diff rich text
            if (_currentSentence != null && !string.IsNullOrEmpty(_transcript))
            {
                _diffRichText = DiffHighlighter.GenerateDiff(_currentSentence.korean, _transcript);
            }
        }

        /// <summary>
        /// Set feedback from the structured API response
        /// </summary>
        public void SetFeedbackFromResponse(ApiClient.FeedbackResponse response)
        {
            if (response == null) return;

            // Use top-level fields first (preferred), fall back to metrics object
            _accuracyPercent = response.textAccuracyPercent > 0
                ? response.textAccuracyPercent
                : (response.metrics?.accuracyPercent ?? 0);
            _wrongPercent = response.mistakePercent > 0
                ? response.mistakePercent
                : (response.metrics?.wrongPercent ?? 0);
            _cer = response.metrics?.cer ?? 0f;
            _wer = response.metrics?.wer ?? 0f;

            // Diff - use wrongParts if available
            _diffUnits = response.diff?.units ?? new ApiClient.DiffUnit[0];
            _wrongUnits = response.diff?.wrongParts ?? response.diff?.wrongUnits ?? new string[0];

            // Generate diff rich text from diff units
            if (_diffUnits.Length > 0)
            {
                _diffRichText = GenerateDiffRichTextFromUnits(_diffUnits);
            }

            // Pronunciation
            _pronunciationAvailable = response.pronunciation?.available ?? false;
            _pronunciationNote = response.pronunciation?.note ?? "";

            // Grammar (legacy)
            _grammarCorrections = response.grammar?.corrections ?? new ApiClient.GrammarCorrection[0];
            _grammarComment = response.grammar?.comment_ko ?? "";

            // NEW: Tutor feedback from Grok
            _tutor = response.tutor;
            _tutorError = response.tutorError;

            if (_tutor != null)
            {
                // Extract tutor data for easier UI access
                _tutorGrammarMistakes = _tutor.grammarMistakes ?? new ApiClient.TutorGrammarMistake[0];
                _tutorPronunciationWeak = _tutor.pronunciationWeak ?? new ApiClient.TutorPronunciationItem[0];
                _tutorPronunciationStrong = _tutor.pronunciationStrong ?? new ApiClient.TutorPronunciationItem[0];
                _tutorComment = _tutor.commentKo ?? "";
                _grammarComment = _tutor.commentKo ?? "";  // Also update legacy field
            }
            else
            {
                _tutorGrammarMistakes = new ApiClient.TutorGrammarMistake[0];
                _tutorPronunciationWeak = new ApiClient.TutorPronunciationItem[0];
                _tutorPronunciationStrong = new ApiClient.TutorPronunciationItem[0];
            }

            // Next sentence
            _nextSentence = response.nextSentence ?? "";

            OnFeedbackReceived?.Invoke(_tutorComment);

            string tutorStatus = _tutor != null ? "OK" : (_tutorError != null ? $"ERROR:{_tutorError.code}" : "null");
            Debug.Log($"[ShadowingState] Feedback: attempt={_attemptIndex}, accuracy={_accuracyPercent}%, tutor={tutorStatus}");
        }

        /// <summary>
        /// Set feedback from the new /api/eval response (combined STT + scoring + pronunciation + grammar)
        /// </summary>
        public void SetEvalResponse(ApiClient.EvalResponse response)
        {
            if (response == null) return;

            // Metrics
            _accuracyPercent = response.textAccuracyPercent > 0
                ? response.textAccuracyPercent
                : (response.metrics?.accuracyPercent ?? 0);
            _wrongPercent = response.mistakePercent > 0
                ? response.mistakePercent
                : (response.metrics?.wrongPercent ?? 0);
            _cer = response.metrics?.cer ?? 0f;

            // Diff
            _diffUnits = response.diff?.units ?? new ApiClient.DiffUnit[0];
            _wrongUnits = response.diff?.wrongParts ?? response.diff?.wrongUnits ?? new string[0];

            // Generate diff rich text from diff units
            if (_diffUnits.Length > 0)
            {
                _diffRichText = GenerateDiffRichTextFromUnits(_diffUnits);
            }

            // Pronunciation (from xAI Realtime)
            _pronunciationAvailable = response.pronunciation?.available ?? false;
            _weakPronunciation = response.pronunciation?.weakPronunciation ?? new ApiClient.WeakPronunciationItem[0];
            _strongPronunciation = response.pronunciation?.strongPronunciation ?? new ApiClient.StrongPronunciationItem[0];
            _pronunciationComment = response.pronunciation?.shortComment ?? "";

            // Grammar (from xAI Text)
            _grammarMistakes = response.grammar?.mistakes ?? new ApiClient.GrammarMistake[0];
            _tutorComment = response.grammar?.tutorComment ?? "";

            // Also set legacy fields for backward compat
            _grammarComment = _tutorComment;

            OnFeedbackReceived?.Invoke(_tutorComment);
            Debug.Log($"[ShadowingState] Eval: attempt={_attemptIndex}, accuracy={_accuracyPercent}%, " +
                      $"pronAvailable={_pronunciationAvailable}, grammarMistakes={_grammarMistakes.Length}");
        }

        /// <summary>
        /// Generate rich text diff from diff units for display.
        /// Uses colors only - NO brackets or parentheses wrapping.
        /// </summary>
        private string GenerateDiffRichTextFromUnits(ApiClient.DiffUnit[] units)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var unit in units)
            {
                switch (unit.status)
                {
                    case "correct":
                        sb.Append(unit.unit);
                        break;
                    case "wrong":
                        sb.Append($"<color=red>{unit.unit}</color>");
                        break;
                    case "missing":
                        sb.Append($"<color=#FF6600>{unit.unit}</color>");
                        break;
                    case "extra":
                        sb.Append($"<color=#FFCC00>{unit.unit}</color>");
                        break;
                    default:
                        sb.Append(unit.unit);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Set error state
        /// </summary>
        public void SetError(string message)
        {
            _errorMessage = message;
            SetPhase(Phase.Error);
            OnError?.Invoke(message);
            Debug.LogError($"[ShadowingState] Error: {message}");
        }

        /// <summary>
        /// Clear error and return to idle
        /// </summary>
        public void ClearError()
        {
            _errorMessage = "";
            SetPhase(Phase.Idle);
        }

        /// <summary>
        /// Get average score across all attempts
        /// </summary>
        public int GetAverageScore()
        {
            int total = 0;
            int count = 0;
            for (int i = 0; i < _attemptIndex && i < MAX_ATTEMPTS; i++)
            {
                if (_attemptScores[i] > 0)
                {
                    total += _attemptScores[i];
                    count++;
                }
            }
            return count > 0 ? total / count : 0;
        }

        // Backward compatibility methods
        public void Retry() => RetryAttempt();
        public void IncrementListenCount() { } // No-op for new flow
        public float GetListenProgress() => 1f;
        public bool AllListensComplete() => true;

        public void SetFeedback(int score, float wer, string feedback, string corrected, string[] alternatives, string[] mistakes, string nextSentence)
        {
            _accuracyPercent = score;
            _wrongPercent = 100 - score;
            _cer = wer;
            _wer = wer;
            _grammarComment = feedback ?? "";
            _wrongUnits = mistakes ?? new string[0];
            _nextSentence = nextSentence ?? "";
            OnFeedbackReceived?.Invoke(_grammarComment);
        }
    }
}
