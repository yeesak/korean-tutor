using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ShadowingTutor
{
    /// <summary>
    /// Grok API client for generating dynamic tutor lines at runtime.
    /// Grok generates context-aware Korean tutor speech, NOT reading fixed scripts.
    /// </summary>
    public static class GrokClient
    {
        // Grok API configuration
        private const float GROK_TIMEOUT_SECONDS = 30f;  // Increased for longer LLM responses
        private const int MAX_LAST_MESSAGE_LENGTH = 220;

        #region Prompt Templates (EXACT as specified)

        /// <summary>
        /// System prompt for Grok - defines tutor persona and rules.
        /// EXACT content as specified in requirements.
        /// </summary>
        private const string GROK_SYSTEM_PROMPT = @"You are a Korean tutor line generator for a speech practice app. Output only the final Korean line(s) to be spoken.

Rules: Korean only, 1-3 short sentences, no markdown/JSON, no meta parentheses, no typos, no contradictions, avoid repeating lastMessageText, synthesize context-aware lines (not fixed script).

States:
- PROMPT: ask user to repeat targetPhrase naturally.
- FEEDBACK_EXCELLENT: strong praise only (e.g., ""정말 잘했어요!""). Do NOT ask about moving on or suggest retry - Q&A flow handles that separately.
- FEEDBACK_PASS: praise + short coaching using mismatchSummary + ask optional one more polish attempt (""한 번만 더 매끈하게 해볼까요?"" yes/no).
- FEEDBACK_FAIL/RETRY: encouragement + mismatchSummary + clear retry intent including 'I will read again' meaning (""제가 다시 읽어드릴게요"").
- SEASON_END: yes/no prompt meaning 'move to next season?' (end with a short yes/no question).";

        /// <summary>
        /// User prompt template - filled with context each turn.
        /// EXACT format as specified in requirements.
        /// </summary>
        private const string GROK_USER_PROMPT_TEMPLATE = @"Create a tutor line for the current state using this context.

[CONTEXT]
state={1}
seasonTitle={2}
targetPhrase={3}
attemptNumber={4}
accuracyPercent={5}
mismatchSummary={6}
lastMessageText={7}

Return ONLY the final Korean line(s).";

        #endregion

        #region Context and Branches

        /// <summary>
        /// State types for Grok prompt context.
        /// Maps to FSM states that need tutor speech.
        /// </summary>
        public enum TutorState
        {
            INTRO,              // SeasonIntro - greeting + learning intent (generated locally, not via Grok)
            PROMPT,             // TopicPrompt - ask user to repeat target phrase
            FEEDBACK_EXCELLENT, // Feedback when accuracy >= 90% - strong praise, ask to move on
            FEEDBACK_PASS,      // Feedback when 65% <= accuracy < 90% - praise + coaching + optional polish
            FEEDBACK_FAIL,      // Feedback when accuracy < 65% - encouragement + retry
            RETRY,              // RetryPrompt - guided retry after fail
            SEASON_END,         // SeasonEndPrompt - ask yes/no for next season
            QUESTION_ANSWER     // Answer user's on-topic question about the lesson
        }

        /// <summary>
        /// Time of day categories for context-aware greetings.
        /// </summary>
        public enum TimeOfDay
        {
            MORNING,    // 5:00 - 11:59
            MIDDAY,     // 12:00 - 16:59
            EVENING,    // 17:00 - 20:59
            NIGHT       // 21:00 - 4:59
        }

        /// <summary>
        /// Context for generating tutor lines.
        /// All fields captured at message generation time.
        /// </summary>
        public class TutorMessageContext
        {
            public TimeOfDay timeOfDay;
            public TutorState state;
            public string seasonTitle;
            public string targetPhrase;
            public int attemptNumber;
            public int accuracyPercent;
            public string mismatchSummary;
            public string lastMessageText;

            public TutorMessageContext()
            {
                timeOfDay = GetTimeOfDay();
                state = TutorState.INTRO;
                attemptNumber = 1;
                accuracyPercent = 0;
                mismatchSummary = "";
                lastMessageText = "";
            }

            /// <summary>
            /// Create context for a specific state.
            /// </summary>
            public static TutorMessageContext Create(
                TutorState state,
                string seasonTitle,
                string targetPhrase = "",
                int attemptNumber = 1,
                int accuracyPercent = 0,
                string mismatchSummary = "",
                string lastMessageText = "")
            {
                return new TutorMessageContext
                {
                    timeOfDay = GetTimeOfDay(),
                    state = state,
                    seasonTitle = seasonTitle ?? "",
                    targetPhrase = targetPhrase ?? "",
                    attemptNumber = attemptNumber,
                    accuracyPercent = accuracyPercent,
                    mismatchSummary = mismatchSummary ?? "",
                    lastMessageText = TruncateLastMessage(lastMessageText)
                };
            }
        }

        /// <summary>
        /// Get time of day category from local time.
        /// Requirements: MORNING (05-10), MIDDAY (11-15), EVENING (16-20), NIGHT (else)
        /// </summary>
        public static TimeOfDay GetTimeOfDay()
        {
            int hour = DateTime.Now.Hour;
            if (hour >= 5 && hour <= 10) return TimeOfDay.MORNING;
            if (hour >= 11 && hour <= 15) return TimeOfDay.MIDDAY;
            if (hour >= 16 && hour <= 20) return TimeOfDay.EVENING;
            return TimeOfDay.NIGHT;
        }

        /// <summary>
        /// Build the season intro line LOCALLY (not via Grok) for reliable TTS.
        /// Format: "안녕하세요! {greeting} 오늘 배울 주제는 "{seasonTitle}"이고 "{topicCount}"개의 표현을 배워볼거예요! 시작할게요!"
        /// </summary>
        public static string BuildIntroLine(string seasonTitle, int topicCount)
        {
            string greeting = GetTimeOfDay() switch
            {
                TimeOfDay.MORNING => "좋은 아침이에요!",
                TimeOfDay.MIDDAY => "좋은 점심이에요!",
                TimeOfDay.EVENING => "좋은 저녁이에요!",
                TimeOfDay.NIGHT => "좋은 밤이에요!",
                _ => "좋은 하루예요!"
            };

            return $"안녕하세요! {greeting} 오늘 배울 주제는 \"{seasonTitle}\"이고 {topicCount}개의 표현을 배워볼거예요! 시작할게요!";
        }

        /// <summary>
        /// Truncate lastMessageText to max length for prompt.
        /// </summary>
        private static string TruncateLastMessage(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= MAX_LAST_MESSAGE_LENGTH) return text;
            return text.Substring(0, MAX_LAST_MESSAGE_LENGTH);
        }

        #endregion

        #region Fallback Lines

        // Hardcoded fallback lines when Grok times out or fails
        private static readonly string[] FALLBACK_INTRO = {
            "안녕하세요! 오늘도 열심히 연습해봐요!",
            "반가워요! 같이 공부해볼까요?"
        };

        private static readonly string[] FALLBACK_PROMPT = {
            "자, 따라 해봐요!",
            "제가 먼저 말할게요, 따라 해봐요!"
        };

        private static readonly string[] FALLBACK_FEEDBACK_EXCELLENT = {
            "정말 잘했어요! 완전 현지인 같아요!",
            "완벽해요! 한국인인 줄 알았어요!"
        };

        private static readonly string[] FALLBACK_FEEDBACK_PASS = {
            "잘했어요! 거의 완벽했어요! 한 번만 더 매끈하게 해볼까요?",
            "좋아요! 조금만 더 다듬으면 완벽해요! 한 번 더 해볼까요?"
        };

        private static readonly string[] FALLBACK_FEEDBACK_FAIL = {
            "괜찮아요! 다시 한 번 해봐요. 제가 다시 읽어드릴게요.",
            "조금만 더 연습하면 완벽할 거예요! 제가 다시 읽어드릴게요."
        };

        private static readonly string[] FALLBACK_RETRY = {
            "다시 한 번 해봐요! 제가 다시 읽어드릴게요.",
            "한 번 더 연습해볼까요? 잘 들어봐요!"
        };

        private static readonly string[] FALLBACK_SEASON_END = {
            "이번 시즌 끝났어요! 다음으로 넘길까요?"
        };

        private static readonly string[] FALLBACK_QUESTION_ANSWER = {
            "오늘 주제에 대해서만 질문해 주세요!"
        };

        private static string GetFallbackLine(TutorState state)
        {
            string[] options = state switch
            {
                TutorState.INTRO => FALLBACK_INTRO,
                TutorState.PROMPT => FALLBACK_PROMPT,
                TutorState.FEEDBACK_EXCELLENT => FALLBACK_FEEDBACK_EXCELLENT,
                TutorState.FEEDBACK_PASS => FALLBACK_FEEDBACK_PASS,
                TutorState.FEEDBACK_FAIL => FALLBACK_FEEDBACK_FAIL,
                TutorState.RETRY => FALLBACK_RETRY,
                TutorState.SEASON_END => FALLBACK_SEASON_END,
                TutorState.QUESTION_ANSWER => FALLBACK_QUESTION_ANSWER,
                _ => FALLBACK_PROMPT
            };
            return options[UnityEngine.Random.Range(0, options.Length)];
        }

        #endregion

        #region Mismatch Summary Helpers

        /// <summary>
        /// Generate mismatch summary for timeout/empty transcript cases.
        /// </summary>
        public const string MISMATCH_TIMEOUT = "음성이 잘 들리지 않았어요";

        /// <summary>
        /// Build a mismatch summary from wrong parts.
        /// </summary>
        public static string BuildMismatchSummary(string targetPhrase, string wrongParts)
        {
            if (string.IsNullOrEmpty(wrongParts))
            {
                return "";
            }

            // Simple format: mention the problematic part
            return $"'{wrongParts}' 발음을 조금 더 연습해봐요";
        }

        #endregion

        #region API Call

        /// <summary>
        /// Build the user prompt with context variables filled in.
        /// </summary>
        private static string BuildUserPrompt(TutorMessageContext ctx)
        {
            return string.Format(GROK_USER_PROMPT_TEMPLATE,
                ctx.timeOfDay.ToString(),
                ctx.state.ToString(),
                ctx.seasonTitle ?? "",
                ctx.targetPhrase ?? "",
                ctx.attemptNumber,
                ctx.accuracyPercent,
                ctx.mismatchSummary ?? "",
                ctx.lastMessageText ?? ""
            );
        }

        /// <summary>
        /// Grok API request body.
        /// </summary>
        [Serializable]
        private class GrokRequest
        {
            public string model = "grok-3-mini";
            public Message[] messages;
            public float temperature = 0.7f;
            public int max_tokens = 150;

            [Serializable]
            public class Message
            {
                public string role;
                public string content;
            }
        }

        [Serializable]
        private class GrokResponse
        {
            public Choice[] choices;

            [Serializable]
            public class Choice
            {
                public Message message;
            }

            [Serializable]
            public class Message
            {
                public string content;
            }
        }

        /// <summary>
        /// Generate a tutor line using Grok API.
        /// Falls back to hardcoded line on timeout or error.
        /// </summary>
        /// <param name="ctx">Context for the tutor line</param>
        /// <param name="onSuccess">Callback with generated line</param>
        /// <param name="onError">Callback with error message (still provides fallback)</param>
        public static IEnumerator GenerateTutorLineAsync(
            TutorMessageContext ctx,
            Action<string> onSuccess,
            Action<string> onError = null)
        {
            string userPrompt = BuildUserPrompt(ctx);
            Debug.Log($"[GROK] Generating tutor line: state={ctx.state}, accuracy={ctx.accuracyPercent}%");

            // Build request
            var request = new GrokRequest
            {
                messages = new GrokRequest.Message[]
                {
                    new GrokRequest.Message { role = "system", content = GROK_SYSTEM_PROMPT },
                    new GrokRequest.Message { role = "user", content = userPrompt }
                }
            };

            string jsonBody = JsonUtility.ToJson(request);
            string baseUrl = AppConfig.Instance?.BackendBaseUrl;
            if (string.IsNullOrEmpty(baseUrl))
            {
                Debug.LogError("[GROK] Backend URL not configured in AppConfig");
                string fallback = GetFallbackLine(ctx.state);
                onError?.Invoke("Backend URL not configured");
                onSuccess?.Invoke(fallback);
                yield break;
            }
            string grokUrl = baseUrl + "/api/grok";
            Debug.Log($"[GROK] POST url={grokUrl}");

            using (UnityWebRequest www = new UnityWebRequest(grokUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.timeout = (int)GROK_TIMEOUT_SECONDS;

                float startTime = Time.realtimeSinceStartup;
                yield return www.SendWebRequest();
                float elapsed = Time.realtimeSinceStartup - startTime;

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[GROK] Request failed after {elapsed:F1}s: {www.error}");
                    string fallback = GetFallbackLine(ctx.state);
                    onError?.Invoke(www.error);
                    onSuccess?.Invoke(fallback);
                    yield break;
                }

                try
                {
                    var response = JsonUtility.FromJson<GrokResponse>(www.downloadHandler.text);
                    string tutorLine = response?.choices?[0]?.message?.content?.Trim() ?? "";

                    if (string.IsNullOrEmpty(tutorLine))
                    {
                        Debug.LogWarning("[GROK] Empty response, using fallback");
                        tutorLine = GetFallbackLine(ctx.state);
                    }

                    // Clean up any markdown/JSON artifacts that might slip through
                    tutorLine = CleanTutorLine(tutorLine);

                    Debug.Log($"[GROK] Generated ({elapsed:F1}s): {tutorLine}");
                    onSuccess?.Invoke(tutorLine);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GROK] Parse error: {ex.Message}");
                    string fallback = GetFallbackLine(ctx.state);
                    onError?.Invoke(ex.Message);
                    onSuccess?.Invoke(fallback);
                }
            }
        }

        /// <summary>
        /// Clean up any artifacts from Grok output.
        /// </summary>
        private static string CleanTutorLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;

            // Remove markdown code blocks
            line = line.Replace("```", "");

            // Remove JSON artifacts
            if (line.StartsWith("{") || line.StartsWith("["))
            {
                // Try to extract content from JSON
                var match = System.Text.RegularExpressions.Regex.Match(line, @"""content""\s*:\s*""([^""]+)""");
                if (match.Success)
                {
                    line = match.Groups[1].Value;
                }
            }

            // Remove bullet points
            line = System.Text.RegularExpressions.Regex.Replace(line, @"^[\-\*\•]\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);

            // Remove parenthetical meta
            line = System.Text.RegularExpressions.Regex.Replace(line, @"\([^)]*\)", "");

            // Collapse multiple spaces
            line = System.Text.RegularExpressions.Regex.Replace(line, @"\s+", " ");

            return line.Trim();
        }

        #endregion

        #region Legacy Compatibility

        /// <summary>
        /// Legacy branch enum for backward compatibility.
        /// Use TutorState instead.
        /// </summary>
        [Obsolete("Use TutorState instead")]
        public enum TutorBranch
        {
            INTRO = 0,
            PROMPT = 1,
            FEEDBACK_PASS = 2,
            FEEDBACK_FAIL = 3,
            SEASON_END = 4
        }

        #endregion
    }
}
