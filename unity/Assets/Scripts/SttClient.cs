using System;
using System.Collections;
using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Speech-to-Text client that handles WAV upload to backend.
    /// </summary>
    public class SttClient : MonoBehaviour
    {
        private static SttClient _instance;
        public static SttClient Instance => _instance;

        [Header("Settings")]
        [SerializeField] private string _defaultLanguage = "ko";

        private bool _isProcessing = false;
        private Coroutine _currentRequest;

        // Events
        public event Action OnProcessingStart;
        public event Action<ApiClient.SttResponse> OnTranscriptReceived;
        public event Action<string> OnError;

        // Properties
        public bool IsProcessing => _isProcessing;
        public string DefaultLanguage => _defaultLanguage;

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
        /// Transcribe WAV audio data
        /// </summary>
        /// <param name="wavData">WAV-encoded audio bytes</param>
        /// <param name="language">Language code (default: "ko")</param>
        public void Transcribe(byte[] wavData, string language = null)
        {
            if (wavData == null || wavData.Length == 0)
            {
                OnError?.Invoke("No audio data to transcribe");
                return;
            }

            if (_isProcessing)
            {
                Debug.LogWarning("[SttClient] Already processing, cancelling previous request");
                Cancel();
            }

            _currentRequest = StartCoroutine(TranscribeCoroutine(wavData, language ?? _defaultLanguage));
        }

        /// <summary>
        /// Cancel current transcription request
        /// </summary>
        public void Cancel()
        {
            if (_currentRequest != null)
            {
                StopCoroutine(_currentRequest);
                _currentRequest = null;
                _isProcessing = false;
                Debug.Log("[SttClient] Request cancelled");
            }
        }

        private IEnumerator TranscribeCoroutine(byte[] wavData, string language)
        {
            _isProcessing = true;
            OnProcessingStart?.Invoke();

            Debug.Log($"[SttClient] Transcribing {wavData.Length} bytes, language: {language}");

            ApiClient.SttResponse result = null;
            string error = null;

            yield return ApiClient.PostStt(
                wavData,
                language,
                response =>
                {
                    result = response;
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
                Debug.LogError($"[SttClient] Error: {error}");
                OnError?.Invoke(error);
            }
            else if (result != null)
            {
                Debug.Log($"[SttClient] Transcript: {result.text}");
                OnTranscriptReceived?.Invoke(result);
            }
            else
            {
                OnError?.Invoke("Unknown error - no response");
            }
        }

        /// <summary>
        /// Convenience method to transcribe from MicRecorder
        /// </summary>
        public void TranscribeFromMic()
        {
            if (MicRecorder.Instance == null)
            {
                OnError?.Invoke("MicRecorder not available");
                return;
            }

            // Subscribe to recording complete event
            void OnComplete(byte[] wavData)
            {
                MicRecorder.Instance.OnRecordingComplete -= OnComplete;
                Transcribe(wavData, _defaultLanguage);
            }

            MicRecorder.Instance.OnRecordingComplete += OnComplete;
            MicRecorder.Instance.StopRecording();
        }
    }
}
