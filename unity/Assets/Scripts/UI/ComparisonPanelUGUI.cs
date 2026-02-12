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

        // Dynamic sizing references (set by CreateDynamic)
        private RectTransform _panelRect;
        private RectTransform _contentRect;
        private ScrollRect _scrollRect;

        // Height constraints
        private const float MIN_PANEL_HEIGHT = 280f;
        private const float MAX_PANEL_HEIGHT_RATIO = 0.70f;  // 70% of screen height max

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

            // Adjust panel height based on content
            AdjustPanelHeight();

            Debug.Log($"[ComparisonPanelUGUI] Shown: target='{targetSentence}', user='{userTranscript}', accuracy={accuracyPercent}%");
        }

        /// <summary>
        /// Adjust panel height to fit content, with max height clamping.
        /// If content exceeds max, enable scrolling.
        /// </summary>
        private void AdjustPanelHeight()
        {
            if (_panelRect == null || _contentRect == null) return;

            // Force layout rebuild to get accurate sizes
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRect);

            // Get content's preferred height
            float contentHeight = _contentRect.rect.height;
            if (contentHeight <= 0)
            {
                // Fallback: sum up text preferred heights
                contentHeight = CalculateContentHeight();
            }

            // Calculate max allowed height (70% of screen)
            float maxHeight = Screen.height * MAX_PANEL_HEIGHT_RATIO;

            // Clamp height
            float panelHeight = Mathf.Clamp(contentHeight, MIN_PANEL_HEIGHT, maxHeight);

            // Apply height to panel
            _panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight);

            // Enable/disable scrolling based on content overflow
            if (_scrollRect != null)
            {
                bool needsScroll = contentHeight > maxHeight;
                _scrollRect.enabled = needsScroll;
                _scrollRect.verticalNormalizedPosition = 1f;  // Scroll to top

                // Log only once per adjustment
                if (needsScroll)
                {
                    Debug.Log($"[ComparisonPanelUGUI] Panel height clamped: content={contentHeight:F0}, panel={panelHeight:F0}, scrolling enabled");
                }
            }
        }

        /// <summary>
        /// Calculate content height by summing text preferred heights.
        /// Used as fallback when ContentSizeFitter hasn't computed yet.
        /// </summary>
        private float CalculateContentHeight()
        {
            float height = 80f;  // Base padding (top + bottom)
            float spacing = 20f;
            int textCount = 0;

            if (_userLabel != null)
            {
                height += _userLabel.preferredHeight;
                textCount++;
            }
            if (_userText != null)
            {
                height += _userText.preferredHeight;
                textCount++;
            }
            if (_targetLabel != null)
            {
                height += _targetLabel.preferredHeight;
                textCount++;
            }
            if (_targetText != null)
            {
                height += _targetText.preferredHeight;
                textCount++;
            }
            if (_accuracyText != null)
            {
                height += _accuracyText.preferredHeight;
                textCount++;
            }

            // Add spacing between elements
            if (textCount > 1)
            {
                height += spacing * (textCount - 1);
            }

            return height;
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
        /// Uses ScrollRect to handle long text that overflows the panel.
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

            // Create panel root (fixed size, acts as viewport)
            GameObject panelGO = new GameObject("ComparisonPanelUGUI");
            panelGO.transform.SetParent(parentCanvas.transform, false);

            // Add RectTransform - centered, 85% width, auto-height based on content
            // Uses anchors for horizontal stretch, but vertical is controlled by ContentSizeFitter
            RectTransform panelRect = panelGO.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.075f, 0.5f);   // 7.5% from sides, vertically centered
            panelRect.anchorMax = new Vector2(0.925f, 0.5f);   // Same - vertical pivot in center
            panelRect.pivot = new Vector2(0.5f, 0.5f);         // Center pivot for nice positioning
            panelRect.anchoredPosition = new Vector2(0, 100);  // Slightly above center
            panelRect.sizeDelta = new Vector2(0, 300);         // Initial height, will be adjusted

            // Add background image
            Image bgImage = panelGO.AddComponent<Image>();
            bgImage.color = new Color(1f, 1f, 1f, 0.97f);

            // Add CanvasGroup
            CanvasGroup canvasGroup = panelGO.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = false;

            // Add Mask to clip content inside panel bounds
            Mask mask = panelGO.AddComponent<Mask>();
            mask.showMaskGraphic = true;  // Show the background

            // Create scrollable content container
            GameObject contentGO = new GameObject("Content");
            contentGO.transform.SetParent(panelGO.transform, false);

            RectTransform contentRect = contentGO.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);  // Top-left anchor for scroll
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            // Add VerticalLayoutGroup to content
            VerticalLayoutGroup layout = contentGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(50, 50, 40, 40);
            layout.spacing = 20;
            layout.childAlignment = TextAnchor.UpperCenter;  // Top align for scroll
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // Add ContentSizeFitter to content (grows with text)
            ContentSizeFitter contentFitter = contentGO.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Add ScrollRect to panel for overflow handling
            ScrollRect scrollRect = panelGO.AddComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;
            scrollRect.inertia = true;
            scrollRect.decelerationRate = 0.135f;

            // Add ComparisonPanelUGUI component
            ComparisonPanelUGUI panel = panelGO.AddComponent<ComparisonPanelUGUI>();
            panel._backgroundImage = bgImage;
            panel._canvasGroup = canvasGroup;
            panel._koreanFont = koreanFont;

            // Create text elements inside content container
            // ORDER: User utterance FIRST (top, biggest), then answer, then accuracy
            Color labelColor = new Color(0.3f, 0.3f, 0.3f);

            // User utterance at TOP - biggest font for emphasis
            panel._userLabel = CreateTextElement(contentGO.transform, "UserLabel", "내 발음:", 40, FontStyle.Bold, labelColor, koreanFont);
            panel._userText = CreateTextElement(contentGO.transform, "UserText", "", 72, FontStyle.Normal, Color.black, koreanFont);
            panel._userText.supportRichText = true;

            // Correct answer below
            panel._targetLabel = CreateTextElement(contentGO.transform, "TargetLabel", "정답:", 40, FontStyle.Bold, labelColor, koreanFont);
            panel._targetText = CreateTextElement(contentGO.transform, "TargetText", "", 56, FontStyle.Normal, Color.black, koreanFont);

            // Accuracy at bottom
            panel._accuracyText = CreateTextElement(contentGO.transform, "AccuracyText", "", 44, FontStyle.Bold, Color.black, koreanFont);
            panel._accuracyText.supportRichText = true;

            // Store references for dynamic height adjustment
            panel._panelRect = panelRect;
            panel._contentRect = contentRect;
            panel._scrollRect = scrollRect;

            Debug.Log("[ComparisonPanelUGUI] Dynamic panel created with ScrollRect support");
            return panel;
        }

        /// <summary>
        /// Create a Text element with specified properties.
        /// Enables Best Fit (auto-sizing) with min/max bounds to prevent overflow.
        /// </summary>
        private static Text CreateTextElement(Transform parent, string name, string text, int fontSize, FontStyle style, Color color, Font font)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            // Add RectTransform
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, fontSize * 1.5f);

            // Add LayoutElement - flexible height to grow with wrapped text
            LayoutElement layoutElem = go.AddComponent<LayoutElement>();
            layoutElem.minHeight = fontSize * 0.8f;   // Reduced min for Best Fit
            layoutElem.flexibleWidth = 1f;
            layoutElem.flexibleHeight = 1f;  // Allow height to grow

            // Add ContentSizeFitter to size based on text content
            ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Add Text component with Best Fit (auto-sizing)
            Text textComp = go.AddComponent<Text>();
            textComp.text = text;
            textComp.font = font;
            textComp.fontSize = fontSize;
            textComp.fontStyle = style;
            textComp.color = color;
            textComp.alignment = TextAnchor.MiddleCenter;
            textComp.horizontalOverflow = HorizontalWrapMode.Wrap;
            textComp.verticalOverflow = VerticalWrapMode.Truncate;  // Truncate as final fallback
            textComp.supportRichText = true;

            // Enable Best Fit (auto-sizing like TMP autosize)
            textComp.resizeTextForBestFit = true;
            textComp.resizeTextMinSize = Math.Max(18, fontSize / 3);  // Readable minimum
            textComp.resizeTextMaxSize = fontSize;

            return textComp;
        }

        #endregion
    }
}
