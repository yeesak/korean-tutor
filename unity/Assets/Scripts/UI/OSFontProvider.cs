using UnityEngine;

namespace ShadowingTutor.UI
{
    /// <summary>
    /// Provides Korean-capable fonts from the OS.
    /// Uses Font.CreateDynamicFontFromOSFont to access system fonts.
    /// Falls back to Arial if no Korean font is available.
    /// </summary>
    public static class OSFontProvider
    {
        // Korean-capable font names to try (in priority order)
        private static readonly string[] KOREAN_FONT_NAMES = new string[]
        {
            // macOS / iOS
            "Apple SD Gothic Neo",
            "AppleSDGothicNeo-Regular",
            "AppleSDGothicNeo",
            ".AppleSystemUIFont",
            // Android / Linux
            "Noto Sans CJK KR",
            "Noto Sans KR",
            "NotoSansCJK-Regular",
            "Droid Sans Fallback",
            // Windows
            "Malgun Gothic",
            "맑은 고딕",
            "Gulim",
            "굴림"
        };

        private static Font _cachedFont;
        private static bool _fallbackWarningLogged = false;
        private static bool _fontFoundLogged = false;

        /// <summary>
        /// Get a font capable of rendering Korean text.
        /// Tries OS fonts first, falls back to Arial.
        /// </summary>
        /// <param name="size">Font size (used for dynamic font creation)</param>
        /// <returns>A Font instance (never null - returns Arial as last resort)</returns>
        public static Font GetKoreanCapableFont(int size = 48)
        {
            // Return cached font if already found
            if (_cachedFont != null && IsLikelyValid(_cachedFont))
            {
                return _cachedFont;
            }

            // Try each Korean font name
            foreach (string fontName in KOREAN_FONT_NAMES)
            {
                Font font = Font.CreateDynamicFontFromOSFont(fontName, size);
                if (IsLikelyValid(font))
                {
                    _cachedFont = font;
                    if (!_fontFoundLogged)
                    {
                        Debug.Log($"[OSFontProvider] Using OS font: {fontName}");
                        _fontFoundLogged = true;
                    }
                    return _cachedFont;
                }
            }

            // Fallback to Arial (built-in)
            _cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_cachedFont == null)
            {
                // Last resort: create from OS
                _cachedFont = Font.CreateDynamicFontFromOSFont("Arial", size);
            }

            if (!_fallbackWarningLogged)
            {
                Debug.LogWarning(
                    "[OSFontProvider] No Korean-capable OS font found!\n" +
                    "Korean text may render as squares.\n" +
                    "Falling back to Arial."
                );
                _fallbackWarningLogged = true;
            }

            return _cachedFont;
        }

        /// <summary>
        /// Check if a font is likely valid and usable.
        /// </summary>
        public static bool IsLikelyValid(Font font)
        {
            if (font == null) return false;

            // Check if font has any character info (basic validity check)
            // Dynamic fonts should always have characterInfo available
            try
            {
                // Request a common character to trigger font loading
                font.RequestCharactersInTexture("A가", 48);
                return font.dynamic || font.characterInfo.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get list of available OS font names (for debugging).
        /// </summary>
        public static string[] GetAvailableFonts()
        {
            return Font.GetOSInstalledFontNames();
        }

        /// <summary>
        /// Log all available OS fonts (for debugging).
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void LogAvailableFonts()
        {
            string[] fonts = GetAvailableFonts();
            Debug.Log($"[OSFontProvider] Available OS fonts ({fonts.Length}):\n" + string.Join("\n", fonts));
        }

        /// <summary>
        /// Clear cached font (useful if font needs to be reloaded).
        /// </summary>
        public static void ClearCache()
        {
            _cachedFont = null;
            _fontFoundLogged = false;
        }
    }
}
