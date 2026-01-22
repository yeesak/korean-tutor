using System;
using System.Collections;
using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Client for fetching pronunciation feedback from xAI Grok via backend.
    /// Deterministic WER scoring + LLM-generated feedback.
    /// </summary>
    public class FeedbackClient : MonoBehaviour
    {
        private static FeedbackClient _instance;
        public static FeedbackClient Instance => _instance;

        private bool _isProcessing = false;
        private Coroutine _currentRequest;

        // Events
        public event Action OnProcessingStart;
        public event Action<ApiClient.FeedbackResponse> OnFeedbackResponseReceived;
        public event Action<string> OnError;

        // Properties
        public bool IsProcessing => _isProcessing;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        /// <summary>
        /// Get feedback for pronunciation comparison
        /// </summary>
        /// <param name="targetText">The target/expected sentence</param>
        /// <param name="transcriptText">What the user actually said (STT result)</param>
        public void GetFeedback(string targetText, string transcriptText)
        {
            if (string.IsNullOrEmpty(targetText))
            {
                OnError?.Invoke("Target sentence is required");
                return;
            }

            if (string.IsNullOrEmpty(transcriptText))
            {
                OnError?.Invoke("Transcript text is required");
                return;
            }

            if (_isProcessing)
            {
                Debug.LogWarning("[FeedbackClient] Already processing, cancelling previous request");
                Cancel();
            }

            _currentRequest = StartCoroutine(GetFeedbackCoroutine(targetText, transcriptText));
        }

        /// <summary>
        /// Cancel current feedback request
        /// </summary>
        public void Cancel()
        {
            if (_currentRequest != null)
            {
                StopCoroutine(_currentRequest);
                _currentRequest = null;
                _isProcessing = false;
                Debug.Log("[FeedbackClient] Request cancelled");
            }
        }

        private IEnumerator GetFeedbackCoroutine(string targetText, string transcriptText)
        {
            _isProcessing = true;
            OnProcessingStart?.Invoke();

            Debug.Log($"[FeedbackClient] Getting feedback for: '{targetText}' vs '{transcriptText}'");

            ApiClient.FeedbackResponse feedbackResponse = null;
            string error = null;

            yield return ApiClient.PostFeedback(
                targetText,
                transcriptText,
                response =>
                {
                    feedbackResponse = response;
                },
                err =>
                {
                    error = err;
                }
            );

            _isProcessing = false;
            _currentRequest = null;

            if (error != null)
            {
                Debug.LogError($"[FeedbackClient] Error: {error}");
                OnError?.Invoke(error);
            }
            else if (feedbackResponse != null)
            {
                Debug.Log($"[FeedbackClient] Feedback: accuracy={feedbackResponse.AccuracyPercent}%, cer={feedbackResponse.CER:F3}");
                OnFeedbackResponseReceived?.Invoke(feedbackResponse);
            }
            else
            {
                OnError?.Invoke("Empty feedback received");
            }
        }

        /// <summary>
        /// Get feedback using current state
        /// </summary>
        public void GetFeedbackFromState()
        {
            if (ShadowingState.Instance == null)
            {
                OnError?.Invoke("ShadowingState not available");
                return;
            }

            var state = ShadowingState.Instance;
            if (state.CurrentSentence == null)
            {
                OnError?.Invoke("No current sentence");
                return;
            }

            GetFeedback(
                state.CurrentSentence.korean,
                state.Transcript
            );
        }
    }
}
