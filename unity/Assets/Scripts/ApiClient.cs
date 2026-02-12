using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ShadowingTutor
{
    /// <summary>
    /// UnityWebRequest wrappers for backend API communication.
    /// Handles JSON requests, multipart uploads, and audio downloads.
    /// </summary>
    public static class ApiClient
    {
        private const float DEFAULT_TIMEOUT = 30f;
        private const float CONNECTIVITY_CHECK_INTERVAL = 30f;  // Re-check after 30 seconds
        private const float ERROR_LOG_THROTTLE = 10f;           // Log same error every 10s max

        // Connectivity state
        private static bool _isBackendReachable = true;  // Assume reachable initially
        private static float _lastConnectivityCheck = -999f;
        private static float _lastErrorLogTime = -999f;
        private static string _lastErrorMessage = "";

        /// <summary>
        /// Whether the backend is currently reachable (updated by CheckHealth)
        /// </summary>
        public static bool IsBackendReachable => _isBackendReachable;

        /// <summary>
        /// Log error with throttling to prevent spam
        /// </summary>
        private static void LogErrorThrottled(string message)
        {
            float now = Time.realtimeSinceStartup;
            if (message != _lastErrorMessage || (now - _lastErrorLogTime) > ERROR_LOG_THROTTLE)
            {
                Debug.LogError($"[ApiClient] {message}");
                _lastErrorMessage = message;
                _lastErrorLogTime = now;
            }
        }

        /// <summary>
        /// Mark backend as unreachable and log clear message
        /// </summary>
        private static void MarkUnreachable(string reason)
        {
            if (_isBackendReachable)
            {
                _isBackendReachable = false;
                string baseUrl = AppConfig.Instance?.BackendBaseUrl ?? "unknown";
                Debug.LogError($"[ApiClient] Backend unreachable at {baseUrl}. Reason: {reason}\n" +
                              $"  → Check: Is server running? Correct IP/port? Firewall?");
            }
        }

        /// <summary>
        /// Mark backend as reachable
        /// </summary>
        private static void MarkReachable()
        {
            if (!_isBackendReachable)
            {
                _isBackendReachable = true;
                Debug.Log("[ApiClient] Backend connection restored!");
            }
        }

        #region Data Classes

        [Serializable]
        public class TtsRequest
        {
            public string text;
            public int speedProfile;
        }

        [Serializable]
        public class FeedbackRequest
        {
            public string targetText;
            public string transcriptText;
        }

        // New structured feedback response matching backend
        [Serializable]
        public class FeedbackMetrics
        {
            public int accuracyPercent;
            public int wrongPercent;
            public int textAccuracyPercent;  // Explicit field (same as accuracyPercent)
            public int mistakePercent;       // Explicit field (same as wrongPercent)
            public float cer;
            public float wer;
        }

        [Serializable]
        public class DiffUnit
        {
            public string unit;
            public string status; // "correct", "wrong", "missing", "extra"
            public string got;    // Only present for "wrong" status
        }

        [Serializable]
        public class FeedbackDiff
        {
            public DiffUnit[] units;
            public string[] wrongUnits;
            public string[] wrongParts;  // Alias for UI highlighting
        }

        [Serializable]
        public class PronunciationSegment
        {
            public string segment;
            public float score;
            public string tip_ko;
        }

        [Serializable]
        public class FeedbackPronunciation
        {
            public bool available;
            public PronunciationSegment[] good;
            public PronunciationSegment[] weak;
            public string note;
        }

        [Serializable]
        public class GrammarCorrection
        {
            public string wrong;
            public string correct;
            public string reason_ko;
            public string example_ko;
        }

        [Serializable]
        public class FeedbackGrammar
        {
            public GrammarCorrection[] corrections;
            public string comment_ko;
        }

        // =====================================================
        // Tutor Response Classes (from /api/feedback with Grok)
        // =====================================================

        [Serializable]
        public class TutorGrammarMistake
        {
            public string youSaid;
            public string correct;
            public string reasonKo;
        }

        [Serializable]
        public class TutorPronunciationItem
        {
            public string token;
            public string reason;
            public string tip;  // Only for weak pronunciation
        }

        /// <summary>
        /// Tutor feedback from Grok LLM (when available)
        /// 3-Tier Feedback: "correct" (>=90%), "partial" (50-89%), "severe" (<50%)
        /// </summary>
        [Serializable]
        public class TutorFeedback
        {
            public string feedbackLevel;  // "correct", "partial", "severe"
            public TutorGrammarMistake[] grammarMistakes;
            public string commentKo;
            // Legacy fields for backward compatibility
            public TutorPronunciationItem[] pronunciationWeak;
            public TutorPronunciationItem[] pronunciationStrong;

            // Helper properties
            public bool IsCorrect => feedbackLevel == "correct";
            public bool IsPartial => feedbackLevel == "partial";
            public bool IsSevere => feedbackLevel == "severe";
        }

        /// <summary>
        /// Error information when tutor feedback is unavailable
        /// </summary>
        [Serializable]
        public class TutorError
        {
            public string code;     // TIMEOUT, UNAUTHORIZED, RATE_LIMITED, HTTP_xxx, PARSE_ERROR, NOT_CONFIGURED
            public string message;
            public string details;
        }

        [Serializable]
        public class FeedbackResponse
        {
            public bool ok;
            public string targetText;
            public string transcriptText;

            // Top-level score fields for easy UI binding
            public int textAccuracyPercent;  // 0-100, main score
            public int mistakePercent;       // 0-100, complement of accuracy
            public int score;                // Alias for backward compatibility

            // Detailed metrics and diff
            public FeedbackMetrics metrics;
            public FeedbackDiff diff;
            public FeedbackPronunciation pronunciation;
            public FeedbackGrammar grammar;
            public string nextSentence;
            public string error;
            public string details;

            // NEW: Tutor feedback from Grok LLM
            public TutorFeedback tutor;      // null if Grok failed
            public TutorError tutorError;    // Error info when tutor is null

            // Computed properties for backward compatibility
            public int AccuracyPercent => textAccuracyPercent > 0 ? textAccuracyPercent : (metrics?.accuracyPercent ?? 0);
            public int WrongPercent => mistakePercent > 0 ? mistakePercent : (metrics?.wrongPercent ?? 0);
            public float CER => metrics?.cer ?? 0f;
            public float WER => metrics?.wer ?? 0f;
            public string[] WrongParts => diff?.wrongParts ?? diff?.wrongUnits ?? new string[0];

            // Tutor availability check
            public bool HasTutor => tutor != null;
            public bool HasTutorError => tutorError != null && !string.IsNullOrEmpty(tutorError.code);
        }

        [Serializable]
        public class SttResponse
        {
            public bool ok;
            public string text;                    // Clean text for UI display
            public string transcriptText;          // Alias for UI (preferred)
            public string rawTranscriptText;       // Original text for debugging
            public SttWord[] words;
            public string language;
            public float duration;
            public string error;
            public string details;

            /// <summary>
            /// Get the clean transcript text (prefers transcriptText, falls back to text)
            /// </summary>
            public string CleanText => !string.IsNullOrEmpty(transcriptText) ? transcriptText : text;
        }

        [Serializable]
        public class SttWord
        {
            public string word;
            public float start;
            public float end;
        }

        [Serializable]
        public class HealthResponse
        {
            public bool ok;
            public string timestamp;
            public string mode;
            public bool ttsConfigured;
            public bool sttConfigured;
            public bool llmConfigured;
            public string ttsProvider;
            public string sttProvider;
            public string llmProvider;
        }

        // =====================================================
        // /api/eval Response Classes (Combined Evaluation)
        // =====================================================

        [Serializable]
        public class WeakPronunciationItem
        {
            public string token;
            public string reason;
            public string tip;
        }

        [Serializable]
        public class StrongPronunciationItem
        {
            public string token;
            public string reason;
        }

        [Serializable]
        public class EvalPronunciation
        {
            public bool available;
            public WeakPronunciationItem[] weakPronunciation;
            public StrongPronunciationItem[] strongPronunciation;
            public string shortComment;
        }

        [Serializable]
        public class GrammarMistake
        {
            public string youSaid;
            public string correct;
            public string why;
        }

        [Serializable]
        public class EvalGrammar
        {
            public GrammarMistake[] mistakes;
            public string tutorComment;
        }

        [Serializable]
        public class EvalMetrics
        {
            public int accuracyPercent;
            public int wrongPercent;
            public int textAccuracyPercent;
            public int mistakePercent;
            public float cer;
        }

        /// <summary>
        /// Response from /api/eval - combined STT + scoring + pronunciation + grammar
        /// </summary>
        [Serializable]
        public class EvalResponse
        {
            public bool ok;
            public string targetText;
            public string transcriptText;
            public string rawTranscriptText;

            // Top-level scores
            public int textAccuracyPercent;
            public int mistakePercent;
            public int score;

            // Detailed metrics
            public EvalMetrics metrics;

            // Diff for highlighting
            public FeedbackDiff diff;

            // Pronunciation (from xAI Realtime)
            public EvalPronunciation pronunciation;

            // Grammar (from xAI Text)
            public EvalGrammar grammar;

            // Error handling
            public string error;
            public string details;

            // Computed properties
            public int AccuracyPercent => textAccuracyPercent > 0 ? textAccuracyPercent : (metrics?.accuracyPercent ?? 0);
            public int WrongPercent => mistakePercent > 0 ? mistakePercent : (metrics?.wrongPercent ?? 0);
            public float CER => metrics?.cer ?? 0f;
            public string[] WrongParts => diff?.wrongParts ?? diff?.wrongUnits ?? new string[0];

            public bool HasPronunciation => pronunciation != null && pronunciation.available;
            public bool HasGrammar => grammar != null && grammar.mistakes != null && grammar.mistakes.Length > 0;
        }

        #endregion

        /// <summary>
        /// Check backend health (updates connectivity state)
        /// </summary>
        public static IEnumerator CheckHealth(Action<HealthResponse> onSuccess, Action<string> onError)
        {
            string url = AppConfig.Instance.HealthUrl;
            Debug.Log($"[ApiClient] Health check: GET {url}");

            // Log to diagnostics
            Diagnostics.FileLogger.LogNetworkStart("GET", url);
            Diagnostics.DebugOverlay.RecordNetworkRequest(url, "Checking...");

            _lastConnectivityCheck = Time.realtimeSinceStartup;

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 60;  // Allow time for Render free-tier cold start (can take 60s+)
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = $"Health check failed: {request.error}";
                    MarkUnreachable(request.error);

                    // Log to diagnostics
                    Diagnostics.FileLogger.LogNetworkEnd(url, (int)request.responseCode, request.error);
                    Diagnostics.DebugOverlay.RecordNetworkRequest(url, $"FAILED: {request.error}");
                    Diagnostics.DebugOverlay.RecordError(error);

                    onError?.Invoke(error);
                    yield break;
                }

                try
                {
                    HealthResponse response = JsonUtility.FromJson<HealthResponse>(request.downloadHandler.text);
                    MarkReachable();

                    // Log to diagnostics
                    Diagnostics.FileLogger.LogNetworkEnd(url, 200, "OK");
                    Diagnostics.DebugOverlay.RecordNetworkRequest(url, "OK");

                    onSuccess?.Invoke(response);
                }
                catch (Exception e)
                {
                    string error = $"Failed to parse health response: {e.Message}";
                    Diagnostics.DebugOverlay.RecordError(error);
                    onError?.Invoke(error);
                }
            }
        }

        /// <summary>
        /// Quick connectivity check (non-blocking callback style).
        /// Returns immediately if recently checked.
        /// </summary>
        public static IEnumerator QuickConnectivityCheck(Action<bool> onResult)
        {
            float now = Time.realtimeSinceStartup;

            // Skip if recently checked
            if ((now - _lastConnectivityCheck) < CONNECTIVITY_CHECK_INTERVAL)
            {
                onResult?.Invoke(_isBackendReachable);
                yield break;
            }

            yield return CheckHealth(
                response => {
                    onResult?.Invoke(true);
                },
                error => {
                    onResult?.Invoke(false);
                }
            );
        }

        /// <summary>
        /// Request TTS audio from backend.
        /// Handles connectivity failures gracefully with throttled logging.
        /// </summary>
        public static IEnumerator GetTts(string text, int speedProfile, Action<AudioClip> onSuccess, Action<string> onError)
        {
            // Early exit if backend is known to be unreachable (skip request)
            if (!_isBackendReachable)
            {
                float now = Time.realtimeSinceStartup;
                // Only retry connectivity check periodically
                if ((now - _lastConnectivityCheck) < CONNECTIVITY_CHECK_INTERVAL)
                {
                    LogErrorThrottled("TTS skipped - backend unreachable. Will retry connectivity later.");
                    onError?.Invoke("Backend unreachable");
                    yield break;
                }
            }

            string url = AppConfig.Instance.TtsUrl;
            Debug.Log($"[ApiClient] POST {url} text={text.Substring(0, Math.Min(20, text.Length))}...");

            TtsRequest body = new TtsRequest { text = text, speedProfile = speedProfile };
            string json = JsonUtility.ToJson(body);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 30;  // Increased for longer Grok answers (TTS generation)

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // Detect connectivity issues
                    bool isConnectivityError = request.error.Contains("Cannot connect") ||
                                              request.error.Contains("Unable to connect") ||
                                              request.error.Contains("Network") ||
                                              request.result == UnityWebRequest.Result.ConnectionError;

                    if (isConnectivityError)
                    {
                        MarkUnreachable(request.error);
                        _lastConnectivityCheck = Time.realtimeSinceStartup;
                    }

                    // Try to parse error response
                    string errorMsg = request.error;
                    if (request.downloadHandler != null && request.downloadHandler.data != null)
                    {
                        try
                        {
                            string errorJson = Encoding.UTF8.GetString(request.downloadHandler.data);
                            var errorResponse = JsonUtility.FromJson<FeedbackResponse>(errorJson);
                            if (!string.IsNullOrEmpty(errorResponse?.error))
                            {
                                errorMsg = errorResponse.error;
                            }
                        }
                        catch { }
                    }

                    LogErrorThrottled($"TTS request failed: {errorMsg}");
                    onError?.Invoke($"TTS request failed: {errorMsg}");
                    yield break;
                }

                // Success - mark reachable
                MarkReachable();

                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null || clip.length == 0)
                {
                    onError?.Invoke("TTS returned empty audio");
                    yield break;
                }

                clip.name = $"TTS_{text.GetHashCode()}";
                Debug.Log($"[ApiClient] TTS received: {clip.length}s, {clip.frequency}Hz");
                onSuccess?.Invoke(clip);
            }
        }

        /// <summary>
        /// Upload audio for STT transcription
        /// </summary>
        public static IEnumerator PostStt(byte[] wavData, string language, Action<SttResponse> onSuccess, Action<string> onError)
        {
            string url = AppConfig.Instance.SttUrl;
            Debug.Log($"[ApiClient] POST {url} ({wavData.Length} bytes)");

            // Create multipart form
            WWWForm form = new WWWForm();
            form.AddBinaryData("audio", wavData, "recording.wav", "audio/wav");
            if (!string.IsNullOrEmpty(language))
            {
                form.AddField("language", language);
            }

            using (UnityWebRequest request = UnityWebRequest.Post(url, form))
            {
                request.timeout = (int)DEFAULT_TIMEOUT;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string errorMsg = request.error;
                    try
                    {
                        var errorResponse = JsonUtility.FromJson<SttResponse>(request.downloadHandler.text);
                        if (!string.IsNullOrEmpty(errorResponse?.error))
                        {
                            errorMsg = errorResponse.error;
                            if (!string.IsNullOrEmpty(errorResponse.details))
                                errorMsg += $": {errorResponse.details}";
                        }
                    }
                    catch { }
                    onError?.Invoke($"STT request failed: {errorMsg}");
                    yield break;
                }

                try
                {
                    SttResponse response = JsonUtility.FromJson<SttResponse>(request.downloadHandler.text);
                    if (!response.ok)
                    {
                        onError?.Invoke(response.error ?? "STT failed");
                        yield break;
                    }
                    Debug.Log($"[ApiClient] STT result: {response.text}");
                    onSuccess?.Invoke(response);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse STT response: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Request pronunciation feedback (legacy)
        /// </summary>
        public static IEnumerator PostFeedback(string targetText, string transcriptText, Action<FeedbackResponse> onSuccess, Action<string> onError)
        {
            string url = AppConfig.Instance.FeedbackUrl;
            Debug.Log($"[ApiClient] POST {url}");

            FeedbackRequest body = new FeedbackRequest
            {
                targetText = targetText,
                transcriptText = transcriptText
            };
            string json = JsonUtility.ToJson(body);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = (int)DEFAULT_TIMEOUT;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string errorMsg = request.error;
                    try
                    {
                        var errorResponse = JsonUtility.FromJson<FeedbackResponse>(request.downloadHandler.text);
                        if (!string.IsNullOrEmpty(errorResponse?.error))
                        {
                            errorMsg = errorResponse.error;
                        }
                    }
                    catch { }
                    onError?.Invoke($"Feedback request failed: {errorMsg}");
                    yield break;
                }

                try
                {
                    FeedbackResponse response = JsonUtility.FromJson<FeedbackResponse>(request.downloadHandler.text);
                    if (!response.ok)
                    {
                        onError?.Invoke(response.error ?? "Feedback failed");
                        yield break;
                    }
                    Debug.Log($"[ApiClient] Feedback: accuracy={response.AccuracyPercent}%, cer={response.CER:F3}");
                    onSuccess?.Invoke(response);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse feedback response: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Combined evaluation: STT + scoring + pronunciation + grammar
        /// This is the new single-call endpoint that replaces STT + Feedback.
        /// </summary>
        /// <param name="wavData">WAV audio data</param>
        /// <param name="targetText">Target sentence to compare against</param>
        /// <param name="locale">Locale (default: ko-KR)</param>
        /// <param name="onSuccess">Success callback with EvalResponse</param>
        /// <param name="onError">Error callback</param>
        public static IEnumerator PostEval(byte[] wavData, string targetText, string locale, Action<EvalResponse> onSuccess, Action<string> onError)
        {
            string url = AppConfig.Instance.EvalUrl;
            Debug.Log($"[ApiClient] POST {url} ({wavData.Length} bytes, target={targetText.Substring(0, Math.Min(20, targetText.Length))}...)");

            // Create multipart form
            WWWForm form = new WWWForm();
            form.AddBinaryData("audio", wavData, "recording.wav", "audio/wav");
            form.AddField("targetText", targetText);
            if (!string.IsNullOrEmpty(locale))
            {
                form.AddField("locale", locale);
            }

            using (UnityWebRequest request = UnityWebRequest.Post(url, form))
            {
                request.timeout = 60; // Longer timeout for combined operation
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string errorMsg = request.error;
                    try
                    {
                        var errorResponse = JsonUtility.FromJson<EvalResponse>(request.downloadHandler.text);
                        if (!string.IsNullOrEmpty(errorResponse?.error))
                        {
                            errorMsg = errorResponse.error;
                            if (!string.IsNullOrEmpty(errorResponse.details))
                                errorMsg += $": {errorResponse.details}";
                        }
                    }
                    catch { }
                    onError?.Invoke($"Eval request failed: {errorMsg}");
                    yield break;
                }

                try
                {
                    string responseText = request.downloadHandler.text;
                    Debug.Log($"[ApiClient] Eval raw response: {responseText.Substring(0, Math.Min(200, responseText.Length))}...");

                    EvalResponse response = JsonUtility.FromJson<EvalResponse>(responseText);
                    if (!response.ok)
                    {
                        onError?.Invoke(response.error ?? "Evaluation failed");
                        yield break;
                    }

                    Debug.Log($"[ApiClient] Eval result: accuracy={response.AccuracyPercent}%, pron={response.HasPronunciation}, grammar={response.HasGrammar}");
                    onSuccess?.Invoke(response);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ApiClient] Failed to parse eval response: {e.Message}");
                    onError?.Invoke($"Failed to parse eval response: {e.Message}");
                }
            }
        }

        /// <summary>
        /// PostEval with default locale
        /// </summary>
        public static IEnumerator PostEval(byte[] wavData, string targetText, Action<EvalResponse> onSuccess, Action<string> onError)
        {
            return PostEval(wavData, targetText, "ko-KR", onSuccess, onError);
        }
    }
}
