using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Word-level diff highlighter for comparing original and spoken text.
    /// Outputs rich text with red highlighting for incorrect/missing tokens.
    /// Uses Levenshtein distance for robust comparison.
    /// </summary>
    public static class DiffHighlighter
    {
        // Rich text colors
        private const string COLOR_CORRECT = "#00FF00";  // Green
        private const string COLOR_WRONG = "#FF0000";    // Red
        private const string COLOR_MISSING = "#FF6600";  // Orange
        private const string COLOR_EXTRA = "#FFFF00";    // Yellow

        /// <summary>
        /// Generate diff rich text comparing original and spoken text
        /// </summary>
        /// <param name="original">The correct/expected text</param>
        /// <param name="spoken">The user's spoken text (from STT)</param>
        /// <returns>Rich text with color highlighting</returns>
        public static string GenerateDiff(string original, string spoken)
        {
            if (string.IsNullOrEmpty(original))
                return spoken ?? "";

            if (string.IsNullOrEmpty(spoken))
                return $"<color={COLOR_MISSING}>{original}</color>";

            // Tokenize both strings
            string[] origTokens = Tokenize(original);
            string[] spokenTokens = Tokenize(spoken);

            // Compute edit operations using dynamic programming
            var operations = ComputeEditOperations(origTokens, spokenTokens);

            // Build rich text result
            StringBuilder result = new StringBuilder();

            foreach (var op in operations)
            {
                switch (op.Type)
                {
                    case EditType.Match:
                        // Correct: show in green
                        result.Append($"<color={COLOR_CORRECT}>{op.Token}</color> ");
                        break;

                    case EditType.Substitute:
                        // Wrong: show spoken word in red (no brackets)
                        result.Append($"<color={COLOR_WRONG}>{op.Token}</color> ");
                        break;

                    case EditType.Insert:
                        // Extra word spoken: show in yellow (no brackets)
                        result.Append($"<color={COLOR_EXTRA}>{op.Token}</color> ");
                        break;

                    case EditType.Delete:
                        // Missing word: show in orange (no parentheses)
                        result.Append($"<color={COLOR_MISSING}>{op.Token}</color> ");
                        break;
                }
            }

            return result.ToString().TrimEnd();
        }

        /// <summary>
        /// Generate simple comparison without rich text
        /// </summary>
        public static DiffResult ComputeDiff(string original, string spoken)
        {
            if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(spoken))
            {
                return new DiffResult
                {
                    Similarity = 0f,
                    CorrectWords = 0,
                    TotalWords = string.IsNullOrEmpty(original) ? 0 : Tokenize(original).Length,
                    MissingWords = new List<string>(),
                    WrongWords = new List<string>()
                };
            }

            string[] origTokens = Tokenize(original);
            string[] spokenTokens = Tokenize(spoken);
            var operations = ComputeEditOperations(origTokens, spokenTokens);

            int correct = 0;
            List<string> missing = new List<string>();
            List<string> wrong = new List<string>();

            foreach (var op in operations)
            {
                switch (op.Type)
                {
                    case EditType.Match:
                        correct++;
                        break;
                    case EditType.Delete:
                        missing.Add(op.Token);
                        break;
                    case EditType.Substitute:
                        wrong.Add(op.Token);
                        break;
                }
            }

            return new DiffResult
            {
                Similarity = origTokens.Length > 0 ? (float)correct / origTokens.Length : 0f,
                CorrectWords = correct,
                TotalWords = origTokens.Length,
                MissingWords = missing,
                WrongWords = wrong
            };
        }

        /// <summary>
        /// Tokenize Korean/English text into words
        /// </summary>
        private static string[] Tokenize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new string[0];

            // Split on whitespace and filter empty
            List<string> tokens = new List<string>();
            string[] parts = text.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                string cleaned = CleanToken(part);
                if (!string.IsNullOrEmpty(cleaned))
                    tokens.Add(cleaned);
            }

            return tokens.ToArray();
        }

        /// <summary>
        /// Clean a token (remove punctuation, normalize)
        /// </summary>
        private static string CleanToken(string token)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in token)
            {
                // Keep Korean characters, letters, digits
                if (IsKorean(c) || char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Check if character is Korean
        /// </summary>
        private static bool IsKorean(char c)
        {
            // Korean Unicode ranges
            return (c >= 0xAC00 && c <= 0xD7AF) ||  // Hangul Syllables
                   (c >= 0x1100 && c <= 0x11FF) ||  // Hangul Jamo
                   (c >= 0x3130 && c <= 0x318F);    // Hangul Compatibility Jamo
        }

        /// <summary>
        /// Compute edit operations using Wagner-Fischer algorithm
        /// </summary>
        private static List<EditOperation> ComputeEditOperations(string[] original, string[] spoken)
        {
            int m = original.Length;
            int n = spoken.Length;

            // DP table for edit distance
            int[,] dp = new int[m + 1, n + 1];

            // Initialize base cases
            for (int i = 0; i <= m; i++) dp[i, 0] = i;
            for (int j = 0; j <= n; j++) dp[0, j] = j;

            // Fill DP table
            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    int cost = TokensMatch(original[i - 1], spoken[j - 1]) ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1,      // Delete
                                 dp[i, j - 1] + 1),     // Insert
                        dp[i - 1, j - 1] + cost         // Match/Substitute
                    );
                }
            }

            // Backtrack to find operations
            List<EditOperation> operations = new List<EditOperation>();
            int x = m, y = n;

            while (x > 0 || y > 0)
            {
                if (x > 0 && y > 0 && TokensMatch(original[x - 1], spoken[y - 1]))
                {
                    operations.Insert(0, new EditOperation(EditType.Match, original[x - 1]));
                    x--; y--;
                }
                else if (x > 0 && y > 0 && dp[x, y] == dp[x - 1, y - 1] + 1)
                {
                    // Substitution - show the spoken word as wrong
                    operations.Insert(0, new EditOperation(EditType.Substitute, spoken[y - 1]));
                    x--; y--;
                }
                else if (y > 0 && dp[x, y] == dp[x, y - 1] + 1)
                {
                    // Insertion (extra word in spoken)
                    operations.Insert(0, new EditOperation(EditType.Insert, spoken[y - 1]));
                    y--;
                }
                else if (x > 0)
                {
                    // Deletion (missing word from original)
                    operations.Insert(0, new EditOperation(EditType.Delete, original[x - 1]));
                    x--;
                }
            }

            return operations;
        }

        /// <summary>
        /// Check if two tokens match (case-insensitive, normalized)
        /// </summary>
        private static bool TokensMatch(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;

            // Exact match
            if (a == b) return true;

            // Case-insensitive match for non-Korean
            if (a.ToLower() == b.ToLower()) return true;

            // For Korean, allow minor variations (same syllable blocks)
            if (IsKorean(a[0]) && IsKorean(b[0]))
            {
                // Simple fuzzy match: if 80% characters match
                int matches = 0;
                int minLen = Math.Min(a.Length, b.Length);
                for (int i = 0; i < minLen; i++)
                {
                    if (a[i] == b[i]) matches++;
                }
                float similarity = (float)matches / Math.Max(a.Length, b.Length);
                return similarity >= 0.8f;
            }

            return false;
        }

        // Helper types
        private enum EditType { Match, Insert, Delete, Substitute }

        private struct EditOperation
        {
            public EditType Type;
            public string Token;

            public EditOperation(EditType type, string token)
            {
                Type = type;
                Token = token;
            }
        }

        public struct DiffResult
        {
            public float Similarity;
            public int CorrectWords;
            public int TotalWords;
            public List<string> MissingWords;
            public List<string> WrongWords;
        }
    }
}
