using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShadowingTutor.Diagnostics
{
    /// <summary>
    /// On-screen debug overlay that works WITHOUT PC connection.
    /// Toggle with 3-finger tap or shake gesture.
    ///
    /// Displays:
    /// - Current scene
    /// - Backend URL and status
    /// - Last error message
    /// - Recent log entries
    /// - Log file path (for manual retrieval)
    /// </summary>
    public class DebugOverlay : MonoBehaviour
    {
        private static DebugOverlay _instance;
        private bool _visible = false;
        private Vector2 _scrollPosition;
        private string _lastError = "None";
        private string _lastNetworkUrl = "None";
        private string _lastNetworkStatus = "None";
        private float _lastTapTime = 0;
        private int _tapCount = 0;
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _buttonStyle;
        private Rect _windowRect;

        // Singleton access
        public static DebugOverlay Instance => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            // Only in development builds or editor
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_instance != null) return;

            var go = new GameObject("DebugOverlay");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DebugOverlay>();

            Debug.Log("[DebugOverlay] Initialized. 3-finger tap to toggle.");
            #endif
        }

        private void Awake()
        {
            _windowRect = new Rect(10, 10, Screen.width - 20, Screen.height * 0.6f);
        }

        private void Update()
        {
            if (ShouldToggleOverlayThisFrame())
            {
                _visible = !_visible;
                Debug.Log($"[DebugOverlay] Toggled: {_visible}");
            }
        }

        /// <summary>
        /// Check if overlay should toggle this frame.
        /// Supports both Input System and legacy Input Manager.
        /// </summary>
        private bool ShouldToggleOverlayThisFrame()
        {
            // Editor keyboard shortcut (F12)
            #if UNITY_EDITOR
                #if ENABLE_INPUT_SYSTEM
                if (Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame)
                    return true;
                #elif ENABLE_LEGACY_INPUT_MANAGER
                if (Input.GetKeyDown(KeyCode.F12))
                    return true;
                #endif
            #endif

            // Mobile 3-finger tap
            #if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null)
            {
                var touches = Touchscreen.current.touches;
                int pressedCount = 0;
                bool allJustPressed = true;

                foreach (var touch in touches)
                {
                    if (touch.press.isPressed)
                    {
                        pressedCount++;
                        if (!touch.press.wasPressedThisFrame)
                            allJustPressed = false;
                    }
                }

                if (pressedCount == 3 && allJustPressed)
                    return true;
            }
            #elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount == 3)
            {
                bool allBegan = true;
                foreach (Touch t in Input.touches)
                {
                    if (t.phase != TouchPhase.Began) allBegan = false;
                }
                if (allBegan)
                    return true;
            }
            #endif

            return false;
        }

        /// <summary>
        /// Record network request for display
        /// </summary>
        public static void RecordNetworkRequest(string url, string status)
        {
            if (_instance != null)
            {
                _instance._lastNetworkUrl = url;
                _instance._lastNetworkStatus = status;
            }
        }

        /// <summary>
        /// Record error for display
        /// </summary>
        public static void RecordError(string error)
        {
            if (_instance != null)
            {
                _instance._lastError = $"{DateTime.Now:HH:mm:ss} {error}";
            }
        }

        /// <summary>
        /// Force show the overlay
        /// </summary>
        public static void Show()
        {
            if (_instance != null) _instance._visible = true;
        }

        /// <summary>
        /// Force hide the overlay
        /// </summary>
        public static void Hide()
        {
            if (_instance != null) _instance._visible = false;
        }

        private void OnGUI()
        {
            if (!_visible) return;

            // Initialize styles
            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.box);
                _boxStyle.normal.background = MakeTexture(2, 2, new Color(0, 0, 0, 0.85f));

                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.fontSize = Mathf.Max(14, Screen.height / 40);
                _labelStyle.normal.textColor = Color.white;
                _labelStyle.wordWrap = true;

                _buttonStyle = new GUIStyle(GUI.skin.button);
                _buttonStyle.fontSize = Mathf.Max(16, Screen.height / 35);
            }

            _windowRect = GUI.Window(12345, _windowRect, DrawWindow, "Debug Overlay (3-finger tap to close)");
        }

        private void DrawWindow(int windowID)
        {
            float lineHeight = _labelStyle.fontSize + 4;
            float y = 25;
            float width = _windowRect.width - 20;

            // Scene info
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            GUI.Label(new Rect(10, y, width, lineHeight), $"Scene: {sceneName}", _labelStyle);
            y += lineHeight;

            // Backend info
            string backendUrl = "Not configured";
            string environment = "Unknown";
            try
            {
                var config = AppConfig.Instance;
                if (config != null)
                {
                    backendUrl = config.BackendBaseUrl;
                    environment = config.CurrentEnvironment.ToString();
                }
            }
            catch { }

            GUI.Label(new Rect(10, y, width, lineHeight), $"Environment: {environment}", _labelStyle);
            y += lineHeight;

            GUI.Label(new Rect(10, y, width, lineHeight * 2), $"Backend: {backendUrl}", _labelStyle);
            y += lineHeight * 2;

            // Network status
            GUI.Label(new Rect(10, y, width, lineHeight), $"Last URL: {_lastNetworkUrl}", _labelStyle);
            y += lineHeight;

            GUI.Label(new Rect(10, y, width, lineHeight), $"Status: {_lastNetworkStatus}", _labelStyle);
            y += lineHeight;

            // Last error
            GUI.contentColor = Color.red;
            GUI.Label(new Rect(10, y, width, lineHeight * 2), $"Error: {_lastError}", _labelStyle);
            GUI.contentColor = Color.white;
            y += lineHeight * 2;

            // Log file path
            string logPath = FileLogger.LogPath ?? "Not initialized";
            GUI.Label(new Rect(10, y, width, lineHeight * 2), $"Log: {logPath}", _labelStyle);
            y += lineHeight * 2;

            // Buttons
            float buttonWidth = (width - 10) / 2;
            float buttonHeight = 50;

            if (GUI.Button(new Rect(10, y, buttonWidth, buttonHeight), "Refresh", _buttonStyle))
            {
                // Force refresh
            }

            if (GUI.Button(new Rect(15 + buttonWidth, y, buttonWidth, buttonHeight), "Copy Log Path", _buttonStyle))
            {
                GUIUtility.systemCopyBuffer = logPath;
                Debug.Log($"[DebugOverlay] Log path copied: {logPath}");
            }
            y += buttonHeight + 10;

            // Recent logs
            GUI.Label(new Rect(10, y, width, lineHeight), "Recent Logs:", _labelStyle);
            y += lineHeight;

            float logHeight = _windowRect.height - y - 30;
            string recentLogs = FileLogger.GetRecentLogs(15);
            _scrollPosition = GUI.BeginScrollView(new Rect(10, y, width, logHeight), _scrollPosition,
                new Rect(0, 0, width - 20, recentLogs.Split('\n').Length * lineHeight));
            GUI.Label(new Rect(0, 0, width - 20, recentLogs.Split('\n').Length * lineHeight), recentLogs, _labelStyle);
            GUI.EndScrollView();

            // Make window draggable
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 25));
        }

        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;

            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
