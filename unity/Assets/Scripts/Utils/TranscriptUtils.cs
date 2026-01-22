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
