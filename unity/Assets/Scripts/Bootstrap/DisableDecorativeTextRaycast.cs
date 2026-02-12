using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ShadowingTutor.Bootstrap
{
    /// <summary>
    /// Bootstrap script that disables raycastTarget on decorative (non-interactive) Text elements.
    /// This prevents non-button Text/TMP_Text from blocking UI clicks.
    ///
    /// Safe behavior:
    /// - Only disables raycastTarget on Text/TMP_Text NOT under a Selectable (Button/Toggle/InputField/etc.)
    /// - Runs once after scene load
    /// - Can be manually triggered via DisableDecorativeRaycasts()
    /// </summary>
    public static class DisableDecorativeTextRaycast
    {
        private static bool _initialized = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            DisableDecorativeRaycasts();
        }

        /// <summary>
        /// Disable raycastTarget on all decorative (non-interactive) text elements.
        /// Safe to call multiple times (idempotent).
        /// </summary>
        public static void DisableDecorativeRaycasts()
        {
            int disabledCount = 0;

            // Process Unity UI Text components
            var texts = Object.FindObjectsOfType<Text>(true);
            foreach (var text in texts)
            {
                if (text.raycastTarget && !IsUnderSelectable(text.transform))
                {
                    text.raycastTarget = false;
                    disabledCount++;
                    Debug.Log($"[UIRaycast] Disabled raycastTarget on Text: {GetPath(text.transform)}");
                }
            }

            // Process TextMeshPro components
            var tmpTexts = Object.FindObjectsOfType<TextMeshProUGUI>(true);
            foreach (var tmp in tmpTexts)
            {
                if (tmp.raycastTarget && !IsUnderSelectable(tmp.transform))
                {
                    tmp.raycastTarget = false;
                    disabledCount++;
                    Debug.Log($"[UIRaycast] Disabled raycastTarget on TMP: {GetPath(tmp.transform)}");
                }
            }

            Debug.Log($"[UIRaycast] DisableDecorativeRaycasts complete: disabled {disabledCount} non-interactive text elements");
        }

        /// <summary>
        /// Check if this transform is under a Selectable (Button, Toggle, InputField, etc.)
        /// If so, it's part of an interactive element and should keep raycastTarget.
        /// </summary>
        private static bool IsUnderSelectable(Transform t)
        {
            Transform current = t;
            while (current != null)
            {
                // Check if this object has a Selectable component
                if (current.GetComponent<Selectable>() != null)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        /// <summary>
        /// Get hierarchy path for logging
        /// </summary>
        private static string GetPath(Transform t)
        {
            string path = t.name;
            Transform parent = t.parent;
            int depth = 0;
            while (parent != null && depth < 4)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
                depth++;
            }
            return path;
        }
    }
}
