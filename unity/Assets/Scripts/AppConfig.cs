using UnityEngine;
using TMPro;

namespace ShadowingTutor
{
    /// <summary>
    /// Backend configuration for the Shadowing Tutor app.
    /// Supports multiple environments: Local, LAN, Staging, Production.
    /// Environment selection persists via PlayerPrefs.
    /// </summary>
    [CreateAssetMenu(fileName = "AppConfig", menuName = "ShadowingTutor/AppConfig")]
    public class AppConfig : ScriptableObject
    {
        private static AppConfig _instance;

        /// <summary>
        /// Available environment types
        /// </summary>
        public enum Environment
        {
            Local,      // http://localhost:3000 (Editor only)
            LAN,        // http://192.168.x.x:3000 (Device testing)
            Staging,    // https://staging-api.example.com
            Production  // https://api.example.com
        }

        private const string PREFS_KEY_ENVIRONMENT = "ShadowingTutor_Environment";
        private const string PREFS_KEY_LAN_IP = "ShadowingTutor_LanIP";

        [Header("Environment URLs")]
        [Tooltip("URL for local development (Editor)")]
        [SerializeField] private string _localUrl = "http://localhost:3000";

        [Tooltip("URL template for LAN testing (replace IP at runtime)")]
        [SerializeField] private string _lanUrlTemplate = "http://{IP}:3000";

        [Tooltip("URL for staging environment")]
        [SerializeField] private string _stagingUrl = "https://staging-api.example.com";

        [Tooltip("URL for production environment (MUST be set before Android build)")]
        [SerializeField] private string _productionUrl = "https://YOUR-BACKEND.onrender.com";

        [Header("Default Settings")]
        [Tooltip("Default environment for Editor")]
        [SerializeField] private Environment _defaultEditorEnvironment = Environment.Local;

        [Tooltip("Default environment for builds")]
        [SerializeField] private Environment _defaultBuildEnvironment = Environment.Production;

        [Header("LAN Configuration")]
        [Tooltip("Default LAN IP address")]
        [SerializeField] private string _defaultLanIP = "192.168.1.100";

        [Header("TTS Settings")]
        [Tooltip("Speed profile for TTS (1=slow, 2=normal, 3=fast)")]
        [Range(1, 3)]
        [SerializeField] private int _speedProfile = 2;

        [Header("Audio Settings")]
        [Tooltip("Sample rate for microphone recording")]
        [SerializeField] private int _micSampleRate = 16000;

        [Tooltip("Maximum recording duration in seconds")]
        [SerializeField] private float _maxRecordingDuration = 10f;

        [Header("Debug Settings")]
        [Tooltip("Fixed seed for sentence shuffle (0 = random each session, >0 = deterministic)")]
        [SerializeField] private int _debugShuffleSeed = 0;

        [Header("Korean Font (TMP)")]
        [Tooltip("Korean TMP font asset. If null, will try Resources.Load('TMP_Korean')")]
        [SerializeField] private TMP_FontAsset _koreanFont;

        // Runtime state
        private Environment? _cachedEnvironment;
        private string _cachedLanIP;

        /// <summary>
        /// Singleton instance. Creates default if not found in Resources.
        /// </summary>
        public static AppConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<AppConfig>("AppConfig");
                    if (_instance == null)
                    {
                        Debug.LogWarning("[AppConfig] No AppConfig found in Resources. Using defaults.");
                        _instance = CreateInstance<AppConfig>();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Current environment (persisted in PlayerPrefs)
        /// </summary>
        public Environment CurrentEnvironment
        {
            get
            {
                if (!_cachedEnvironment.HasValue)
                {
                    int savedEnv = PlayerPrefs.GetInt(PREFS_KEY_ENVIRONMENT, -1);
                    if (savedEnv >= 0 && savedEnv <= 3)
                    {
                        _cachedEnvironment = (Environment)savedEnv;
                    }
                    else
                    {
                        // Use default based on platform
                        #if UNITY_EDITOR
                        _cachedEnvironment = _defaultEditorEnvironment;
                        #else
                        _cachedEnvironment = _defaultBuildEnvironment;
                        #endif
                    }
                }
                return _cachedEnvironment.Value;
            }
        }

        /// <summary>
        /// Current LAN IP address (persisted in PlayerPrefs)
        /// </summary>
        public string LanIP
        {
            get
            {
                if (string.IsNullOrEmpty(_cachedLanIP))
                {
                    _cachedLanIP = PlayerPrefs.GetString(PREFS_KEY_LAN_IP, _defaultLanIP);
                }
                return _cachedLanIP;
            }
        }

        /// <summary>
        /// Gets the effective backend base URL for current environment.
        /// On device builds (iOS/Android), automatically switches from localhost to LAN.
        /// </summary>
        public string BackendBaseUrl
        {
            get
            {
                string url = CurrentEnvironment switch
                {
                    Environment.Local => _localUrl,
                    Environment.LAN => _lanUrlTemplate.Replace("{IP}", LanIP),
                    Environment.Staging => _stagingUrl,
                    Environment.Production => _productionUrl,
                    _ => _localUrl
                };

                // PLATFORM CHECK: localhost won't work on device builds
                #if !UNITY_EDITOR
                if (IsLocalhostUrl(url))
                {
                    // Auto-switch to LAN if localhost detected on device
                    if (!string.IsNullOrEmpty(LanIP) && LanIP != "192.168.1.100")
                    {
                        string lanUrl = _lanUrlTemplate.Replace("{IP}", LanIP);
                        if (!_localhostWarningLogged)
                        {
                            Debug.LogWarning($"[AppConfig] localhost not available on device. Auto-switching to LAN: {lanUrl}");
                            _localhostWarningLogged = true;
                        }
                        return lanUrl.TrimEnd('/');
                    }
                    else
                    {
                        if (!_localhostWarningLogged)
                        {
                            Debug.LogError("[AppConfig] localhost not available on device. Please configure LAN IP in Settings or use Production environment.");
                            _localhostWarningLogged = true;
                        }
                    }
                }
                #endif

                return url.TrimEnd('/');
            }
        }

        private bool _localhostWarningLogged = false;

        /// <summary>
        /// Check if URL is localhost (not reachable from device)
        /// </summary>
        private bool IsLocalhostUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string lower = url.ToLower();
            return lower.Contains("localhost") || lower.Contains("127.0.0.1");
        }

        /// <summary>
        /// Whether current environment uses HTTPS
        /// </summary>
        public bool IsSecure => BackendBaseUrl.StartsWith("https://");

        /// <summary>
        /// Whether running in a release build (not Editor, not Development)
        /// </summary>
        public bool IsReleaseBuild
        {
            get
            {
                #if UNITY_EDITOR
                return false;
                #else
                return !Debug.isDebugBuild;
                #endif
            }
        }

        /// <summary>
        /// TTS speed profile (1=slow, 2=normal, 3=fast)
        /// </summary>
        public int SpeedProfile => _speedProfile;

        /// <summary>
        /// Microphone sample rate in Hz
        /// </summary>
        public int MicSampleRate => _micSampleRate;

        /// <summary>
        /// Maximum recording duration in seconds
        /// </summary>
        public float MaxRecordingDuration => _maxRecordingDuration;

        /// <summary>
        /// Debug shuffle seed (0 = random, >0 = deterministic for testing)
        /// </summary>
        public int DebugShuffleSeed => _debugShuffleSeed;

        /// <summary>
        /// Korean TMP font asset (single source of truth).
        /// Returns assigned font, or attempts Resources.Load("TMP_Korean").
        /// </summary>
        public TMP_FontAsset KoreanFont
        {
            get
            {
                if (_koreanFont != null)
                    return _koreanFont;

                // Try loading from Resources
                _koreanFont = Resources.Load<TMP_FontAsset>("TMP_Korean");
                return _koreanFont;
            }
        }

        /// <summary>
        /// Set the Korean font at runtime (also caches it).
        /// </summary>
        public void SetKoreanFont(TMP_FontAsset font)
        {
            _koreanFont = font;
        }

        /// <summary>
        /// Full URL for TTS endpoint
        /// </summary>
        public string TtsUrl => $"{BackendBaseUrl}/api/tts";

        /// <summary>
        /// Full URL for STT endpoint
        /// </summary>
        public string SttUrl => $"{BackendBaseUrl}/api/stt";

        /// <summary>
        /// Full URL for Feedback endpoint
        /// </summary>
        public string FeedbackUrl => $"{BackendBaseUrl}/api/feedback";

        /// <summary>
        /// Full URL for Sentences endpoint
        /// </summary>
        public string SentencesUrl => $"{BackendBaseUrl}/api/sentences";

        /// <summary>
        /// Full URL for Health endpoint
        /// </summary>
        public string HealthUrl => $"{BackendBaseUrl}/api/health";

        /// <summary>
        /// Full URL for Eval endpoint (combined STT + scoring + pronunciation + grammar)
        /// </summary>
        public string EvalUrl => $"{BackendBaseUrl}/api/eval";

        /// <summary>
        /// Full URL for Pronounce (xAI Realtime) endpoint
        /// </summary>
        public string PronounceUrl => $"{BackendBaseUrl}/api/pronounce_grok";

        /// <summary>
        /// Set the current environment
        /// </summary>
        public void SetEnvironment(Environment env)
        {
            _cachedEnvironment = env;
            PlayerPrefs.SetInt(PREFS_KEY_ENVIRONMENT, (int)env);
            PlayerPrefs.Save();
            Debug.Log($"[AppConfig] Environment set to: {env} -> {BackendBaseUrl}");
        }

        /// <summary>
        /// Set the LAN IP address
        /// </summary>
        public void SetLanIP(string ip)
        {
            _cachedLanIP = ip;
            PlayerPrefs.SetString(PREFS_KEY_LAN_IP, ip);
            PlayerPrefs.Save();
            Debug.Log($"[AppConfig] LAN IP set to: {ip}");
        }

        /// <summary>
        /// Get all environment options as string array (for dropdown)
        /// </summary>
        public static string[] GetEnvironmentOptions()
        {
            return new string[] { "Local", "LAN", "Staging", "Production" };
        }

        /// <summary>
        /// Get current environment index (for dropdown)
        /// </summary>
        public int GetEnvironmentIndex()
        {
            return (int)CurrentEnvironment;
        }

        /// <summary>
        /// Set environment by index (from dropdown)
        /// </summary>
        public void SetEnvironmentByIndex(int index)
        {
            if (index >= 0 && index <= 3)
            {
                SetEnvironment((Environment)index);
            }
        }

        /// <summary>
        /// Reset to default environment
        /// </summary>
        public void ResetToDefault()
        {
            PlayerPrefs.DeleteKey(PREFS_KEY_ENVIRONMENT);
            PlayerPrefs.DeleteKey(PREFS_KEY_LAN_IP);
            _cachedEnvironment = null;
            _cachedLanIP = null;
            Debug.Log($"[AppConfig] Reset to defaults. Current: {CurrentEnvironment}");
        }

        /// <summary>
        /// Validate current configuration
        /// </summary>
        public bool ValidateConfiguration(out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(BackendBaseUrl))
            {
                error = "Backend URL is empty";
                return false;
            }

            if (CurrentEnvironment == Environment.LAN && string.IsNullOrEmpty(LanIP))
            {
                error = "LAN IP is not configured";
                return false;
            }

            // CRITICAL: Block localhost on Android builds
            #if UNITY_ANDROID && !UNITY_EDITOR
            if (IsLocalhostUrl(BackendBaseUrl))
            {
                error = "localhost는 Android에서 사용할 수 없습니다.\n" +
                        "Settings에서 Production 환경을 선택하거나\n" +
                        "LAN IP를 설정해주세요.";
                return false;
            }
            #endif

            // Check for placeholder URLs
            if (BackendBaseUrl.Contains("YOUR-BACKEND") || BackendBaseUrl.Contains("example.com"))
            {
                #if !UNITY_EDITOR
                error = "Backend URL이 설정되지 않았습니다.\n" +
                        "AppConfig에서 Production URL을 설정해주세요.";
                return false;
                #else
                Debug.LogWarning("[AppConfig] Using placeholder URL. Set production URL before building.");
                #endif
            }

            // Warn about HTTP in release builds (iOS ATS issue)
            if (IsReleaseBuild && !IsSecure)
            {
                Debug.LogWarning($"[AppConfig] Using HTTP ({BackendBaseUrl}) in release build. iOS may block this.");
            }

            return true;
        }

        /// <summary>
        /// Check if current config is valid for device deployment
        /// </summary>
        public bool IsReadyForDeviceBuild
        {
            get
            {
                if (string.IsNullOrEmpty(_productionUrl)) return false;
                if (_productionUrl.Contains("YOUR-BACKEND")) return false;
                if (_productionUrl.Contains("example.com")) return false;
                if (IsLocalhostUrl(_productionUrl)) return false;
                return true;
            }
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Editor helper to log current configuration
        /// </summary>
        [ContextMenu("Log Configuration")]
        private void LogConfiguration()
        {
            Debug.Log($"[AppConfig] Current Configuration:\n" +
                     $"  Environment: {CurrentEnvironment}\n" +
                     $"  Backend URL: {BackendBaseUrl}\n" +
                     $"  Is Secure: {IsSecure}\n" +
                     $"  LAN IP: {LanIP}\n" +
                     $"  Speed Profile: {SpeedProfile}");
        }
        #endif
    }
}
