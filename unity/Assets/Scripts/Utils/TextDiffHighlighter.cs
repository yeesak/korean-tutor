using System;
using System.Collections.Generic;
using System.Text;

namespace ShadowingTutor
{
    /// <summary>
    /// Generates rich text with color highlighting for text comparison.
    /// Uses LCS (Longest Common Subsequence) for character-level diff.
    /// Output is compatible with Unity UI Text and TextMeshPro (rich text enabled).
    /// </summary>
    public static class TextDiffHighlighter
    {
        // Colors for rich text
        public const string COLOR_CORRECT = "#000000";   // Black for correct characters
        public const string COLOR_WRONG = "#FF0000";     // Red for wrong/missing characters
        public const string COLOR_TARGET = "#000000";    // Black for target sentence

        /// <summary>
        /// Build rich text for the USER's transcript with incorrect parts highlighted in red.
        /// Correct characters are black, incorrect characters are red.
        /// </summary>
        /// <param name="userText">User's STT transcript (normalized)</param>
        /// <param name="targetText">Target sentence to compare against</param>
        /// <returns>Rich text string with color tags</returns>
        public static string BuildUserRichText(string userText, string targetText)
        {
            if (string.IsNullOrEmpty(userText))
            {
                return $"<color={COLOR_WRONG}>(no speech detected)</color>";
            }

            if (string.IsNullOrEmpty(targetText))
            {
                // No target to compare - just return black text
                return EscapeRichText(userText);
            }

            // Normalize both texts for comparison (remove spaces, punctuation)
            string normUser = NormalizeForComparison(userText);
            string normTarget = NormalizeForComparison(targetText);

            // If exact match, return all black
            if (normUser == normTarget)
            {
                return $"<color={COLOR_CORRECT}>{EscapeRichText(userText)}</color>";
            }

            // Compute LCS to find which characters in user text match target
            bool[] userMatches = ComputeLCSMatches(normUser, normTarget);

            // Build rich text from original user text (preserving spaces/punctuation)
            // Map normalized indices back to original
            return BuildRichTextFromMatches(userText, normUser, userMatches);
        }

        /// <summary>
        /// Build rich text for target sentence (plain black, for consistency).
        /// </summary>
        public static string BuildTargetRichText(string targetText)
        {
            if (string.IsNullOrEmpty(targetText))
            {
                return "";
            }
            return $"<color={COLOR_TARGET}>{EscapeRichText(targetText)}</color>";
        }

        /// <summary>
        /// Normalize text for comparison: remove spaces, punctuation, lowercase.
        /// This creates a "core" string for LCS comparison.
        /// </summary>
        private static string NormalizeForComparison(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new StringBuilder();
            foreach (char c in text)
            {
                // Keep Korean characters and alphanumeric, skip whitespace/punctuation
                if (IsKoreanChar(c) || char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Check if character is Korean (Hangul).
        /// </summary>
        private static bool IsKoreanChar(char c)
        {
            // Hangul Syllables: U+AC00 to U+D7A3
            // Hangul Jamo: U+1100 to U+11FF
            // Hangul Compatibility Jamo: U+3130 to U+318F
            return (c >= 0xAC00 && c <= 0xD7A3) ||
                   (c >= 0x1100 && c <= 0x11FF) ||
                   (c >= 0x3130 && c <= 0x318F);
        }

        /// <summary>
        /// Compute which characters in userNorm are part of LCS with targetNorm.
        /// Returns boolean array where true = character is in LCS (correct).
        /// </summary>
        private static bool[] ComputeLCSMatches(string userNorm, string targetNorm)
        {
            int m = userNorm.Length;
            int n = targetNorm.Length;

            // DP table for LCS length
            int[,] dp = new int[m + 1, n + 1];

            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    if (userNorm[i - 1] == targetNorm[j - 1])
                    {
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    }
                    else
                    {
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                    }
                }
            }

            // Backtrack to find which characters in user are in LCS
            bool[] matches = new bool[m];
            int pi = m, pj = n;

            while (pi > 0 && pj > 0)
            {
                if (userNorm[pi - 1] == targetNorm[pj - 1])
                {
                    matches[pi - 1] = true;
                    pi--;
                    pj--;
                }
                else if (dp[pi - 1, pj] > dp[pi, pj - 1])
                {
                    pi--;
                }
                else
                {
                    pj--;
                }
            }

            return matches;
        }

        /// <summary>
        /// Build rich text string from original text using match information.
        /// Maps normalized indices back to original text positions.
        /// </summary>
        private static string BuildRichTextFromMatches(string original, string normalized, bool[] normMatches)
        {
            var result = new StringBuilder();
            int normIndex = 0;
            bool inCorrectSpan = false;
            bool inWrongSpan = false;

            for (int i = 0; i < original.Length; i++)
            {
                char c = original[i];
                bool isSignificant = IsKoreanChar(c) || char.IsLetterOrDigit(c);

                if (isSignificant && normIndex < normalized.Length)
                {
                    bool isCorrect = normMatches[normIndex];
                    normIndex++;

                    if (isCorrect)
                    {
                        // Close wrong span if open
                        if (inWrongSpan)
                        {
                            result.Append("</color>");
                            inWrongSpan = false;
                        }
                        // Open correct span if not already
                        if (!inCorrectSpan)
                        {
                            result.Append($"<color={COLOR_CORRECT}>");
                            inCorrectSpan = true;
                        }
                    }
                    else
                    {
                        // Close correct span if open
                        if (inCorrectSpan)
                        {
                            result.Append("</color>");
                            inCorrectSpan = false;
                        }
                        // Open wrong span if not already
                        if (!inWrongSpan)
                        {
                            result.Append($"<color={COLOR_WRONG}>");
                            inWrongSpan = true;
                        }
                    }

                    result.Append(EscapeChar(c));
                }
                else
                {
                    // Whitespace/punctuation: keep current span color
                    result.Append(EscapeChar(c));
                }
            }

            // Close any open span
            if (inCorrectSpan || inWrongSpan)
            {
                result.Append("</color>");
            }

            return result.ToString();
        }

        /// <summary>
        /// Escape characters that have special meaning in rich text.
        /// </summary>
        private static string EscapeRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            return text
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        /// <summary>
        /// Escape a single character for rich text.
        /// </summary>
        private static string EscapeChar(char c)
        {
            return c switch
            {
                '<' => "&lt;",
                '>' => "&gt;",
                _ => c.ToString()
            };
        }

        /// <summary>
        /// Get accuracy percentage based on LCS match ratio.
        /// </summary>
        public static int ComputeAccuracyFromLCS(string userText, string targetText)
        {
            if (string.IsNullOrEmpty(targetText)) return 0;
            if (string.IsNullOrEmpty(userText)) return 0;

            string normUser = NormalizeForComparison(userText);
            string normTarget = NormalizeForComparison(targetText);

            if (normTarget.Length == 0) return 0;
            if (normUser == normTarget) return 100;

            int lcsLength = ComputeLCSLength(normUser, normTarget);
            int accuracy = (int)((float)lcsLength / normTarget.Length * 100);
            return Math.Max(0, Math.Min(100, accuracy));
        }

        /// <summary>
        /// Compute LCS length only (for accuracy calculation).
        /// </summary>
        private static int ComputeLCSLength(string s1, string s2)
        {
            int m = s1.Length;
            int n = s2.Length;

            int[,] dp = new int[m + 1, n + 1];

            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    if (s1[i - 1] == s2[j - 1])
                    {
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    }
                    else
                    {
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                    }
                }
            }

            return dp[m, n];
        }
    }
}
