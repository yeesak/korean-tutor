using System.Text.RegularExpressions;
using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Utility class for normalizing STT transcripts.
    /// Removes noise annotations like (noise), [music], {cough}, etc.
    /// </summary>
    public static class TranscriptUtils
    {
        // Regex patterns for bracketed content (compiled for performance)
        private static readonly Regex ParenthesesPattern = new Regex(@"\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex SquareBracketsPattern = new Regex(@"\[[^\]]*\]", RegexOptions.Compiled);
        private static readonly Regex CurlyBracesPattern = new Regex(@"\{[^}]*\}", RegexOptions.Compiled);
        private static readonly Regex AngleBracketsPattern = new Regex(@"<[^>]*>", RegexOptions.Compiled);
        private static readonly Regex WhitespacePattern = new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>
        /// Removes bracketed annotations from STT transcript.
        /// Handles: (noise), [music], {cough}, &lt;...&gt;
        /// Safe for null/empty input. Collapses whitespace and trims.
        /// </summary>
        /// <param name="input">Raw STT transcript</param>
        /// <returns>Cleaned transcript with brackets removed</returns>
        public static string NormalizeTranscript(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            string result = input;

            // Remove bracketed segments (non-greedy, handles multiple occurrences)
            // Order doesn't matter since patterns don't overlap
            result = ParenthesesPattern.Replace(result, " ");
            result = SquareBracketsPattern.Replace(result, " ");
            result = CurlyBracesPattern.Replace(result, " ");
            result = AngleBracketsPattern.Replace(result, " ");

            // Collapse multiple whitespace (spaces, tabs, newlines) to single space
            result = WhitespacePattern.Replace(result, " ");

            // Trim leading/trailing whitespace
            result = result.Trim();

            return result;
        }

        /// <summary>
        /// Debug helper: logs both raw and normalized transcript.
        /// </summary>
        public static string NormalizeWithLog(string input, string context = "STT")
        {
            string normalized = NormalizeTranscript(input);

            if (input != normalized)
            {
                Debug.Log($"[TranscriptUtils] {context} normalized: '{input}' -> '{normalized}'");
            }

            return normalized;
        }

        /// <summary>
        /// Validation result for Korean transcript check.
        /// </summary>
        public struct TranscriptValidation
        {
            public bool isValid;
            public float koreanRatio;
            public int totalLetters;
            public int koreanLetters;
            public string invalidReason;
        }

        /// <summary>
        /// Validate that transcript is primarily Korean text.
        /// Returns invalid if:
        /// - Transcript is null/empty
        /// - Less than 30% of letters are Korean (Hangul)
        /// - Contains primarily Chinese, Japanese, or other scripts
        /// </summary>
        /// <param name="transcript">Normalized transcript text</param>
        /// <param name="minKoreanRatio">Minimum ratio of Korean letters (default 0.3)</param>
        /// <returns>Validation result with details</returns>
        public static TranscriptValidation ValidateKoreanTranscript(string transcript, float minKoreanRatio = 0.3f)
        {
            var result = new TranscriptValidation
            {
                isValid = false,
                koreanRatio = 0f,
                totalLetters = 0,
                koreanLetters = 0,
                invalidReason = null
            };

            // Null/empty check
            if (string.IsNullOrWhiteSpace(transcript))
            {
                result.invalidReason = "음성이 인식되지 않았어요";
                return result;
            }

            // Count letters (exclude spaces, punctuation, numbers)
            int koreanCount = 0;
            int letterCount = 0;
            int chineseCount = 0;
            int japaneseCount = 0;

            foreach (char c in transcript)
            {
                // Skip whitespace and common punctuation
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsDigit(c))
                    continue;

                letterCount++;

                // Korean Hangul range: 0xAC00-0xD7AF (syllables) + 0x1100-0x11FF (jamo)
                if ((c >= 0xAC00 && c <= 0xD7AF) || (c >= 0x1100 && c <= 0x11FF) ||
                    (c >= 0x3130 && c <= 0x318F))  // Compatibility Jamo
                {
                    koreanCount++;
                }
                // Chinese character range (CJK Unified Ideographs)
                else if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF))
                {
                    chineseCount++;
                }
                // Japanese Hiragana (0x3040-0x309F) and Katakana (0x30A0-0x30FF)
                else if ((c >= 0x3040 && c <= 0x309F) || (c >= 0x30A0 && c <= 0x30FF))
                {
                    japaneseCount++;
                }
            }

            result.totalLetters = letterCount;
            result.koreanLetters = koreanCount;

            // No letters found
            if (letterCount == 0)
            {
                result.invalidReason = "인식된 텍스트가 없어요";
                return result;
            }

            result.koreanRatio = (float)koreanCount / letterCount;

            // Check if primarily Chinese (wrong language detected)
            if (chineseCount > koreanCount && chineseCount > letterCount * 0.3f)
            {
                result.invalidReason = "발음이 잘 안 들렸어요. 다시 말해 주세요";
                Debug.LogWarning($"[STT] invalid transcript reason=chinese '{transcript}' (chinese={chineseCount}, korean={koreanCount})");
                return result;
            }

            // Check if primarily Japanese (wrong language detected)
            if (japaneseCount > koreanCount && japaneseCount > letterCount * 0.3f)
            {
                result.invalidReason = "발음이 잘 안 들렸어요. 다시 말해 주세요";
                Debug.LogWarning($"[STT] invalid transcript reason=japanese '{transcript}' (japanese={japaneseCount}, korean={koreanCount})");
                return result;
            }

            // Check Korean ratio
            if (result.koreanRatio < minKoreanRatio)
            {
                result.invalidReason = "발음이 잘 안 들렸어요. 다시 말해 주세요";
                Debug.LogWarning($"[STT] invalid transcript reason=lowKorean ratio={result.koreanRatio:P0} text='{transcript}'");
                return result;
            }

            // Valid Korean transcript
            result.isValid = true;
            return result;
        }

        /// <summary>
        /// Quick check if transcript is valid Korean (convenience method).
        /// </summary>
        public static bool IsValidKoreanTranscript(string transcript)
        {
            return ValidateKoreanTranscript(transcript).isValid;
        }

        #region QnA Intent Classification

        /// <summary>
        /// Intent types for Q&A decision flow.
        /// </summary>
        public enum QnAIntent
        {
            ADVANCE,         // User wants to move to next topic
            ASK_QUESTION,    // User is asking a question
            UNKNOWN          // Cannot determine - treat as question
        }

        /// <summary>
        /// Keywords indicating user wants to advance to next topic (NO more questions).
        /// EXACT LIST from requirements - deterministic, no fuzzy matching.
        /// </summary>
        private static readonly string[] AdvanceKeywords = new string[]
        {
            // Negation / no questions (all forms)
            "없어", "없어요", "없습니다", "없는데", "없는데요",
            "아니", "아니요", "아뇨", "아니에요", "아닙니다",
            "괜찮아", "괜찮아요", "괜찮습니다",
            // Advance / next topic
            "넘어가", "넘어가요", "넘어가자", "넘어갈게", "넘어가도 돼",
            "다음", "다음으로", "다음 주제", "다음으로 넘어가", "다음으로 넘어가자",
            // No more questions (explicit)
            "질문 없어", "질문 없어요", "질문 없습니다",
            "더 없어", "더 없어요", "더 없습니다",
            "궁금한 거 없어", "궁금한거 없어", "궁금한 게 없어",
            // Stop / end / acknowledgment
            "그만", "끝", "종료", "됐어", "됐어요",
            "알겠어", "알겠어요", "알겠습니다"
        };

        /// <summary>
        /// Keywords indicating user is asking a question.
        /// </summary>
        private static readonly string[] QuestionKeywords = new string[]
        {
            // Korean interrogatives
            "뭐야", "뭔데", "뭐예요", "뭐에요", "무엇", "뭘",
            "어떻게", "어떡해", "어째서",
            "왜", "왜요",
            "언제", "언제요",
            "어디", "어디서", "어디요",
            "누구", "누가",
            "얼마", "얼마나", "몇",
            "무슨", "어떤", "어느",
            "알려", "설명", "가르쳐",
            // Question mark
            "?"
        };

        /// <summary>
        /// Keywords indicating user wants to continue asking questions (affirmative to "또 다른 질문 있으신가요?").
        /// These should be treated as ASK_QUESTION to trigger reprompt or allow next question.
        /// </summary>
        private static readonly string[] ContinueKeywords = new string[]
        {
            "네", "예", "응", "어", "그래", "있어", "있어요", "있습니다",
            "질문", "하나", "하나 더", "한 개", "한개"
        };

        /// <summary>
        /// Determine user intent from transcript in Q&A decision context.
        /// CRITICAL: Check QUESTION keywords FIRST to handle cases like "아니 그게 뭐야?"
        /// Only use this in the Q&A decision phase (after "또 다른 질문 있으신가요?").
        /// </summary>
        /// <param name="transcript">User's normalized transcript</param>
        /// <returns>Determined intent (ADVANCE, ASK_QUESTION, or UNKNOWN)</returns>
        public static QnAIntent DetermineQnAIntent(string transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return QnAIntent.UNKNOWN;
            }

            string lower = transcript.ToLower().Trim();

            // STEP 1: Check for QUESTION indicators FIRST
            // This prevents "아니 그게 뭐야?" from matching "아니" as ADVANCE
            bool hasQuestionMarker = false;
            foreach (string keyword in QuestionKeywords)
            {
                if (lower.Contains(keyword.ToLower()))
                {
                    hasQuestionMarker = true;
                    Debug.Log($"[QNA] ASK_QUESTION detected: '{transcript}' contains question marker '{keyword}'");
                    return QnAIntent.ASK_QUESTION;
                }
            }

            // STEP 2: Check for ADVANCE keywords (only if no question markers found)
            // For short responses like "없어", "아니", "다음" - these are ADVANCE
            foreach (string keyword in AdvanceKeywords)
            {
                if (lower.Contains(keyword.ToLower()))
                {
                    // Extra guard: If transcript is long (>10 chars) and contains ADVANCE keyword,
                    // it might be a question that happens to contain the word (e.g., "다음 문장은 뭐야?")
                    // But we already checked for question markers above, so this is likely ADVANCE
                    Debug.Log($"[QNA] intent=ADVANCE: '{transcript}' matched '{keyword}'");
                    return QnAIntent.ADVANCE;
                }
            }

            // STEP 2.5: Check for CONTINUE keywords (affirmative responses like "네", "응", "있어")
            // These indicate user wants to ask another question - treat as ASK_QUESTION
            foreach (string keyword in ContinueKeywords)
            {
                if (lower.Contains(keyword.ToLower()))
                {
                    Debug.Log($"[QNA] ASK_QUESTION (continue): '{transcript}' matched '{keyword}'");
                    return QnAIntent.ASK_QUESTION;
                }
            }

            // STEP 3: If transcript is long enough, assume it's a question
            if (transcript.Length >= 3)
            {
                Debug.Log($"[QNA] ASK_QUESTION assumed (transcript length={transcript.Length}): '{transcript}'");
                return QnAIntent.ASK_QUESTION;
            }

            Debug.Log($"[QNA] UNKNOWN: '{transcript}'");
            return QnAIntent.UNKNOWN;
        }

        /// <summary>
        /// Check if intent indicates user wants to advance to next topic.
        /// Convenience method for common check.
        /// </summary>
        public static bool IsAdvanceIntent(string transcript)
        {
            return DetermineQnAIntent(transcript) == QnAIntent.ADVANCE;
        }

        #endregion

        #region Unit Test Helpers (Editor only)

#if UNITY_EDITOR
        /// <summary>
        /// Run basic validation tests. Call from Unity Editor menu or test script.
        /// </summary>
        [UnityEditor.MenuItem("Tools/TranscriptUtils/Run Tests")]
        public static void RunTests()
        {
            bool allPassed = true;

            // Test 1: Basic parentheses
            allPassed &= AssertEquals(
                NormalizeTranscript("안녕 (잡음) 오늘은 겨울이야"),
                "안녕 오늘은 겨울이야",
                "Test 1: Basic parentheses"
            );

            // Test 2: Mixed brackets
            allPassed &= AssertEquals(
                NormalizeTranscript("안녕 (noise) 오늘은 [music] 겨울이야"),
                "안녕 오늘은 겨울이야",
                "Test 2: Mixed brackets"
            );

            // Test 3: Multiple same type
            allPassed &= AssertEquals(
                NormalizeTranscript("(noise) 어 (숨소리)"),
                "어",
                "Test 3: Multiple parentheses"
            );

            // Test 4: All bracket types
            allPassed &= AssertEquals(
                NormalizeTranscript("Hello (noise) world [music] test {cough} end <beep>"),
                "Hello world test end",
                "Test 4: All bracket types"
            );

            // Test 5: Null/empty handling
            allPassed &= AssertEquals(NormalizeTranscript(null), "", "Test 5a: Null");
            allPassed &= AssertEquals(NormalizeTranscript(""), "", "Test 5b: Empty");
            allPassed &= AssertEquals(NormalizeTranscript("   "), "", "Test 5c: Whitespace only");

            // Test 6: No brackets (pass-through)
            allPassed &= AssertEquals(
                NormalizeTranscript("안녕하세요"),
                "안녕하세요",
                "Test 6: No brackets"
            );

            // Test 7: Only brackets
            allPassed &= AssertEquals(
                NormalizeTranscript("(noise) [music]"),
                "",
                "Test 7: Only brackets"
            );

            // Test 8: Whitespace collapse
            allPassed &= AssertEquals(
                NormalizeTranscript("안녕   하세요\n오늘\t좋아요"),
                "안녕 하세요 오늘 좋아요",
                "Test 8: Whitespace collapse"
            );

            Debug.Log($"[TranscriptUtils] Tests completed: {(allPassed ? "ALL PASSED" : "SOME FAILED")}");
        }

        private static bool AssertEquals(string actual, string expected, string testName)
        {
            bool passed = actual == expected;
            if (passed)
            {
                Debug.Log($"[TranscriptUtils] PASS: {testName}");
            }
            else
            {
                Debug.LogError($"[TranscriptUtils] FAIL: {testName}\n  Expected: '{expected}'\n  Actual:   '{actual}'");
            }
            return passed;
        }
#endif

        #endregion
    }
}
