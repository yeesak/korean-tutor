#if UNITY_EDITOR
using UnityEngine;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Configuration for auto-starting the local backend server in Unity Editor.
    /// Create via Assets > Create > ShadowingTutor > Local Server Config
    /// </summary>
    [CreateAssetMenu(fileName = "LocalServerConfig", menuName = "ShadowingTutor/Local Server Config")]
    public class LocalServerConfig : ScriptableObject
    {
        private static LocalServerConfig _instance;

        [Header("Server Location")]
        [Tooltip("Absolute path to the backend server folder (contains package.json)")]
        [SerializeField] private string _serverWorkingDirectory = "";

        [Header("Start Command")]
        [Tooltip("Command to run (e.g., 'npm', 'node', or full path to npm)")]
        [SerializeField] private string _command = "npm";

        [Tooltip("Arguments for the command (e.g., 'start' or 'run dev')")]
        [SerializeField] private string _arguments = "start";

        [Header("Connection")]
        [Tooltip("Base URL to check for server health")]
        [SerializeField] private string _baseUrl = "http://localhost:3000";

        [Tooltip("Health check endpoint path")]
        [SerializeField] private string _healthEndpoint = "/api/health";

        [Header("Timeouts")]
        [Tooltip("How long to wait for server to start (seconds)")]
        [SerializeField] private float _startupTimeout = 15f;

        [Tooltip("Health check request timeout (seconds)")]
        [SerializeField] private int _healthCheckTimeout = 3;

        [Header("Behavior")]
        [Tooltip("Enable auto-start when entering Play Mode")]
        [SerializeField] private bool _autoStartEnabled = true;

        [Tooltip("Kill server process when exiting Play Mode")]
        [SerializeField] private bool _killOnExit = true;

        [Tooltip("Show server output in Unity Console")]
        [SerializeField] private bool _showServerLogs = true;

        // Properties
        public string ServerWorkingDirectory => _serverWorkingDirectory;
        public string Command => _command;
        public string Arguments => _arguments;
        public string BaseUrl => _baseUrl;
        public string HealthEndpoint => _healthEndpoint;
        public string HealthUrl => $"{_baseUrl.TrimEnd('/')}{_healthEndpoint}";
        public float StartupTimeout => _startupTimeout;
        public int HealthCheckTimeout => _healthCheckTimeout;
        public bool AutoStartEnabled => _autoStartEnabled;
        public bool KillOnExit => _killOnExit;
        public bool ShowServerLogs => _showServerLogs;

        /// <summary>
        /// Get the singleton instance from Resources folder
        /// </summary>
        public static LocalServerConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<LocalServerConfig>("LocalServerConfig");
                    if (_instance == null)
                    {
                        Debug.LogWarning("[LocalServerConfig] No config found in Resources. Create one via Assets > Create > ShadowingTutor > Local Server Config");
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Validate configuration
        /// </summary>
        public bool Validate(out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(_serverWorkingDirectory))
            {
                error = "Server working directory is not set";
                return false;
            }

            if (!System.IO.Directory.Exists(_serverWorkingDirectory))
            {
                error = $"Server directory does not exist: {_serverWorkingDirectory}";
                return false;
            }

            if (string.IsNullOrEmpty(_command))
            {
                error = "Command is not set";
                return false;
            }

            if (string.IsNullOrEmpty(_baseUrl))
            {
                error = "Base URL is not set";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Auto-detect backend folder relative to Unity project
        /// </summary>
        [ContextMenu("Auto-Detect Backend Folder")]
        public void AutoDetectBackendFolder()
        {
            // Try common relative paths
            string unityProjectPath = Application.dataPath;  // .../unity/Assets
            string projectRoot = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(unityProjectPath));  // .../AI-demo

            string[] possiblePaths = new string[]
            {
                System.IO.Path.Combine(projectRoot, "backend"),
                System.IO.Path.Combine(projectRoot, "server"),
                System.IO.Path.Combine(projectRoot, "api"),
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(unityProjectPath), "backend"),
            };

            foreach (string path in possiblePaths)
            {
                if (System.IO.Directory.Exists(path))
                {
                    string packageJson = System.IO.Path.Combine(path, "package.json");
                    if (System.IO.File.Exists(packageJson))
                    {
                        _serverWorkingDirectory = path;
                        Debug.Log($"[LocalServerConfig] Found backend at: {path}");
                        #if UNITY_EDITOR
                        UnityEditor.EditorUtility.SetDirty(this);
                        #endif
                        return;
                    }
                }
            }

            Debug.LogWarning("[LocalServerConfig] Could not auto-detect backend folder. Please set manually.");
        }

        #if UNITY_EDITOR
        [ContextMenu("Log Configuration")]
        private void LogConfiguration()
        {
            Debug.Log($"[LocalServerConfig] Configuration:\n" +
                     $"  Working Dir: {_serverWorkingDirectory}\n" +
                     $"  Command: {_command} {_arguments}\n" +
                     $"  Base URL: {_baseUrl}\n" +
                     $"  Health URL: {HealthUrl}\n" +
                     $"  Auto-Start: {_autoStartEnabled}\n" +
                     $"  Kill On Exit: {_killOnExit}");
        }
        #endif
    }
}
#endif
