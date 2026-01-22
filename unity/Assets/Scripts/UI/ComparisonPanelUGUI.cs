using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowingTutor.UI
{
    /// <summary>
    /// UGUI-based comparison panel for displaying target vs user speech.
    /// Uses OS fonts for Korean support (no TMP dependency).
    /// Supports rich text coloring: wrong parts RED, correct parts BLACK.
    /// </summary>
    public class ComparisonPanelUGUI : MonoBehaviour
    {
        [Header("Text References")]
        [SerializeField] private Text _targetLabel;
        [SerializeField] private Text _targetText;
        [SerializeField] private Text _userLabel;
        [SerializeField] private Text _userText;
        [SerializeField] private Text _accuracyText;

        [Header("Panel")]
        [SerializeField] private Image _backgroundImage;
        [SerializeField] private CanvasGroup _canvasGroup;

        // Colors
        private const string COLOR_CORRECT = "#000000";  // Black
        private const string COLOR_WRONG = "#FF0000";    // Red
        private const string COLOR_ACCURACY_GOOD = "#00AA00";   // Green
        private const string COLOR_ACCURACY_PARTIAL = "#FF8800"; // Orange
        private const string COLOR_ACCURACY_BAD = "#FF0000";     // Red

        // Accuracy thresholds
        private const int THRESHOLD_GOOD = 90;
        private const int THRESHOLD_PARTIAL = 65;

        private Font _koreanFont;

        private void Awake()
        {
            // Get Korean-capable font from OS
            _koreanFont = OSFontProvider.GetKoreanCapableFont(48);
            AssignFontToAllTexts();
        }

        /// <summary>
        /// Assign the Korean font to all Text components.
        /// </summary>
        private void AssignFontToAllTexts()
        {
            if (_koreanFont == null) return;

            Text[] allTexts = GetComponentsInChildren<Text>(true);
            foreach (var text in allTexts)
            {
                text.font = _koreanFont;
            }
            Debug.Log($"[ComparisonPanelUGUI] Assigned font to {allTexts.Length} Text components");
        }

        /// <summary>
        /// Show the panel and update content.
        /// </summary>
        public void Show(string targetSentence, string userTranscript, int accuracyPercent)
        {
            gameObject.SetActive(true);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
            }

            // Update target text (plain black)
            if (_targetLabel != null)
            {
                _targetLabel.text = "정답:";
            }
            if (_targetText != null)
            {
                _targetText.text = EscapeRichText(targetSentence);
            }

            // Update user text with diff highlighting
            if (_userLabel != null)
            {
                _userLabel.text = "내 발음:";
            }
            if (_userText != null)
            {
                string cleanTranscript = NormalizeTranscript(userTranscript);
                _userText.text = BuildDiffRichText(cleanTranscript, targetSentence);
                _userText.supportRichText = true;
            }

            // Update accuracy with color
            if (_accuracyText != null)
            {
                string color = accuracyPercent >= THRESHOLD_GOOD ? COLOR_ACCURACY_GOOD :
                               accuracyPercent >= THRESHOLD_PARTIAL ? COLOR_ACCURACY_PARTIAL : COLOR_ACCURACY_BAD;
                _accuracyText.text = $"<color={color}>정확도: {accuracyPercent}%</color>";
                _accuracyText.supportRichText = true;
            }

            Debug.Log($"[ComparisonPanelUGUI] Shown: target='{targetSentence}', user='{userTranscript}', accuracy={accuracyPercent}%");
        }

        /// <summary>
        /// Hide the panel.
        /// </summary>
        public void Hide()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
            }
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Build rich text with wrong characters highlighted in red.
        /// Uses LCS (Longest Common Subsequence) for character-level diff.
        /// </summary>
        private string BuildDiffRichText(string userText, string targetText)
        {
            if (string.IsNullOrEmpty(userText))
            {
                return $"<color={COLOR_WRONG}>(음성 없음)</color>";
            }

            if (string.IsNullOrEmpty(targetText))
            {
                return EscapeRichText(userText);
            }

            // Normalize for comparison
            string normUser = NormalizeForComparison(userText);
            string normTarget = NormalizeForComparison(targetText);

            // Exact match = all black
            if (normUser == normTarget)
            {
                return $"<color={COLOR_CORRECT}>{EscapeRichText(userText)}</color>";
            }

            // Compute LCS matches
            bool[] userMatches = ComputeLCSMatches(normUser, normTarget);

            // Build rich text from original (preserving spaces/punctuation)
            return BuildRichTextFromMatches(userText, normUser, userMatches);
        }

        /// <summary>
        /// Normalize text for comparison: remove spaces, punctuation, lowercase.
        /// </summary>
        private string NormalizeForComparison(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new StringBuilder();
            foreach (char c in text)
            {
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
        private bool IsKoreanChar(char c)
        {
            return (c >= 0xAC00 && c <= 0xD7A3) ||  // Hangul Syllables
                   (c >= 0x1100 && c <= 0x11FF) ||  // Hangul Jamo
                   (c >= 0x3130 && c <= 0x318F);    // Hangul Compatibility Jamo
        }

        /// <summary>
        /// Compute which characters in userNorm are part of LCS with targetNorm.
        /// </summary>
        private bool[] ComputeLCSMatches(string userNorm, string targetNorm)
        {
            int m = userNorm.Length;
            int n = targetNorm.Length;

            // DP table
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

            // Backtrack to find matches
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
        /// Build rich text from original text using match information.
        /// </summary>
        private string BuildRichTextFromMatches(string original, string normalized, bool[] normMatches)
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
                        if (inWrongSpan)
                        {
                            result.Append("</color>");
                            inWrongSpan = false;
                        }
                        if (!inCorrectSpan)
                        {
                            result.Append($"<color={COLOR_CORRECT}>");
                            inCorrectSpan = true;
                        }
                    }
                    else
                    {
                        if (inCorrectSpan)
                        {
                            result.Append("</color>");
                            inCorrectSpan = false;
                        }
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
                    // Whitespace/punctuation: keep current span
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
        /// Normalize transcript: remove bracketed noise, collapse whitespace.
        /// </summary>
        private string NormalizeTranscript(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // Remove bracketed noise: (), [], {}, <>
            string cleaned = Regex.Replace(text, @"\([^)]*\)|\[[^\]]*\]|\{[^}]*\}|<[^>]*>", "");

            // Collapse whitespace
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return cleaned;
        }

        /// <summary>
        /// Escape rich text special characters.
        /// </summary>
        private string EscapeRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        /// <summary>
        /// Escape a single character.
        /// </summary>
        private string EscapeChar(char c)
        {
            return c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                _ => c.ToString()
            };
        }

        #region Static Factory

        /// <summary>
        /// Create a ComparisonPanelUGUI dynamically at runtime.
        /// </summary>
        public static ComparisonPanelUGUI CreateDynamic(Canvas parentCanvas)
        {
            if (parentCanvas == null)
            {
                Debug.LogError("[ComparisonPanelUGUI] Cannot create - no Canvas provided");
                return null;
            }

            // Get Korean font
            Font koreanFont = OSFontProvider.GetKoreanCapableFont(48);

            // Create panel root
            GameObject panelGO = new GameObject("ComparisonPanelUGUI");
            panelGO.transform.SetParent(parentCanvas.transform, false);

            // Add RectTransform - centered, 85% width, upper area
            RectTransform panelRect = panelGO.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.075f, 0.30f);  // 7.5% from sides, 30% from bottom
            panelRect.anchorMax = new Vector2(0.925f, 0.95f);  // 7.5% from sides, 5% from top
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            // Add background image
            Image bgImage = panelGO.AddComponent<Image>();
            bgImage.color = new Color(1f, 1f, 1f, 0.97f);

            // Add CanvasGroup
            CanvasGroup canvasGroup = panelGO.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = false;

            // Add VerticalLayoutGroup
            VerticalLayoutGroup layout = panelGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(50, 50, 40, 40);
            layout.spacing = 20;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // Add ContentSizeFitter
            ContentSizeFitter fitter = panelGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Add ComparisonPanelUGUI component
            ComparisonPanelUGUI panel = panelGO.AddComponent<ComparisonPanelUGUI>();
            panel._backgroundImage = bgImage;
            panel._canvasGroup = canvasGroup;
            panel._koreanFont = koreanFont;

            // Create text elements
            Color labelColor = new Color(0.3f, 0.3f, 0.3f);

            panel._targetLabel = CreateTextElement(panelGO.transform, "TargetLabel", "정답:", 40, FontStyle.Bold, labelColor, koreanFont);
            panel._targetText = CreateTextElement(panelGO.transform, "TargetText", "", 60, FontStyle.Normal, Color.black, koreanFont);
            panel._userLabel = CreateTextElement(panelGO.transform, "UserLabel", "내 발음:", 40, FontStyle.Bold, labelColor, koreanFont);
            panel._userText = CreateTextElement(panelGO.transform, "UserText", "", 72, FontStyle.Normal, Color.black, koreanFont);
            panel._userText.supportRichText = true;
            panel._accuracyText = CreateTextElement(panelGO.transform, "AccuracyText", "", 48, FontStyle.Bold, Color.black, koreanFont);
            panel._accuracyText.supportRichText = true;

            Debug.Log("[ComparisonPanelUGUI] Dynamic panel created");
            return panel;
        }

        /// <summary>
        /// Create a Text element with specified properties.
        /// </summary>
        private static Text CreateTextElement(Transform parent, string name, string text, int fontSize, FontStyle style, Color color, Font font)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            // Add RectTransform
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, fontSize * 1.5f);

            // Add LayoutElement
            LayoutElement layoutElem = go.AddComponent<LayoutElement>();
            layoutElem.minHeight = fontSize * 1.2f;
            layoutElem.preferredHeight = fontSize * 1.5f;
            layoutElem.flexibleWidth = 1f;

            // Add Text component
            Text textComp = go.AddComponent<Text>();
            textComp.text = text;
            textComp.font = font;
            textComp.fontSize = fontSize;
            textComp.fontStyle = style;
            textComp.color = color;
            textComp.alignment = TextAnchor.MiddleCenter;
            textComp.horizontalOverflow = HorizontalWrapMode.Wrap;
            textComp.verticalOverflow = VerticalWrapMode.Overflow;
            textComp.supportRichText = true;

            return textComp;
        }

        #endregion
    }
}
