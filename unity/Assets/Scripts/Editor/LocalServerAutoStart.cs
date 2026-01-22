#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Automatically starts the local backend server when entering Play Mode in Unity Editor.
    /// Prevents duplicate launches and kills process when exiting Play Mode.
    ///
    /// Configuration: Create a LocalServerConfig asset in Resources folder.
    /// </summary>
    [InitializeOnLoad]
    public static class LocalServerAutoStart
    {
        private static Process _serverProcess;
        private static bool _isStarting;
        private static bool _startupMessageLogged;
        private static bool _triedLocalhostFallback;
        private static System.Collections.Generic.List<string> _outputBuffer = new System.Collections.Generic.List<string>();
        private static readonly object _outputLock = new object();
        private const int MAX_OUTPUT_LINES = 50;

        static LocalServerAutoStart()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Auto-create LocalServerConfig asset if missing
        /// </summary>
        private static LocalServerConfig CreateConfigAsset()
        {
            // Ensure Resources folder exists
            string resourcesPath = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesPath))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            string configPath = "Assets/Resources/LocalServerConfig.asset";

            // Create new config
            LocalServerConfig config = ScriptableObject.CreateInstance<LocalServerConfig>();

            // Auto-detect backend folder
            string unityProjectPath = Application.dataPath;  // .../unity/Assets
            string projectRoot = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(unityProjectPath));

            string[] possiblePaths = new string[]
            {
                System.IO.Path.Combine(projectRoot, "backend"),
                System.IO.Path.Combine(projectRoot, "server"),
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(unityProjectPath), "backend"),
            };

            string detectedPath = null;
            foreach (string path in possiblePaths)
            {
                if (System.IO.Directory.Exists(path))
                {
                    string packageJson = System.IO.Path.Combine(path, "package.json");
                    if (System.IO.File.Exists(packageJson))
                    {
                        detectedPath = path;
                        break;
                    }
                }
            }

            if (detectedPath != null)
            {
                // Use reflection to set the private field
                var field = typeof(LocalServerConfig).GetField("_serverWorkingDirectory",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(config, detectedPath);
                }
                UnityEngine.Debug.Log($"[LocalServerAutoStart] Auto-detected backend at: {detectedPath}");
            }
            else
            {
                UnityEngine.Debug.LogWarning("[LocalServerAutoStart] Could not auto-detect backend folder. Please configure manually.");
            }

            // Save asset
            AssetDatabase.CreateAsset(config, configPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Reload to get the saved instance
            config = AssetDatabase.LoadAssetAtPath<LocalServerConfig>(configPath);

            UnityEngine.Debug.Log($"[LocalServerAutoStart] Created LocalServerConfig at: {configPath}");

            // Select in Inspector
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);

            return config;
        }

        private static async void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    await EnsureServerRunning();
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    TryKillServer();
                    break;
            }
        }

        /// <summary>
        /// Ensure the server is running. Starts it if not reachable.
        /// </summary>
        private static async Task EnsureServerRunning()
        {
            LocalServerConfig config = LocalServerConfig.Instance;
            if (config == null)
            {
                // Show dialog and offer to auto-create config
                bool createConfig = EditorUtility.DisplayDialog(
                    "Local Server Config Missing",
                    "No LocalServerConfig found in Resources folder.\n\n" +
                    "This config is needed to auto-start the backend server when entering Play Mode.\n\n" +
                    "Would you like to create one now?",
                    "Create Config",
                    "Skip (Manual Start)"
                );

                if (createConfig)
                {
                    config = CreateConfigAsset();
                    if (config == null)
                    {
                        UnityEngine.Debug.LogError("[LocalServerAutoStart] Failed to create config asset");
                        return;
                    }
                }
                else
                {
                    UnityEngine.Debug.LogWarning(
                        "[LocalServerAutoStart] Skipped config creation. Start backend manually:\n" +
                        "  cd /path/to/backend && npm start"
                    );
                    return;
                }
            }

            if (!config.AutoStartEnabled)
            {
                UnityEngine.Debug.Log("[LocalServerAutoStart] Auto-start disabled in config");
                return;
            }

            // Validate configuration
            if (!config.Validate(out string configError))
            {
                UnityEngine.Debug.LogError($"[LocalServerAutoStart] Invalid config: {configError}");
                return;
            }

            // Prevent concurrent startup attempts
            if (_isStarting)
            {
                return;
            }
            _isStarting = true;
            _startupMessageLogged = false;
            _triedLocalhostFallback = false;

            try
            {
                // Check if server is already running
                if (await IsServerReachable(config))
                {
                    UnityEngine.Debug.Log($"[LocalServerAutoStart] Server already running at {config.BaseUrl}");
                    return;
                }

                // Check if we already have a process running
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    UnityEngine.Debug.Log("[LocalServerAutoStart] Server process exists, waiting for startup...");
                }
                else
                {
                    // Start new server process
                    StartServerProcess(config);
                }

                // Wait for server to become reachable
                await WaitForServerReady(config);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[LocalServerAutoStart] Failed: {ex.Message}");
                LogManualStartInstructions(config);
            }
            finally
            {
                _isStarting = false;
            }
        }

        /// <summary>
        /// Start the server process
        /// </summary>
        private static void StartServerProcess(LocalServerConfig config)
        {
            // Clear output buffer
            lock (_outputLock)
            {
                _outputBuffer.Clear();
            }

            string workingDir = config.ServerWorkingDirectory;
            string configCommand = config.Command;
            string configArgs = config.Arguments;

            // Explicit logging for debugging
            UnityEngine.Debug.Log("═══════════════════════════════════════════════════════════════");
            UnityEngine.Debug.Log("[LocalServerAutoStart] PROCESS LAUNCH ATTEMPT");
            UnityEngine.Debug.Log($"  Config Working Directory: {workingDir}");
            UnityEngine.Debug.Log($"  Config Command: {configCommand}");
            UnityEngine.Debug.Log($"  Config Arguments: {configArgs}");
            UnityEngine.Debug.Log($"  Directory Exists: {System.IO.Directory.Exists(workingDir)}");

            // Verify working directory exists
            if (!System.IO.Directory.Exists(workingDir))
            {
                UnityEngine.Debug.LogError($"[LocalServerAutoStart] ✗ Working directory does not exist: {workingDir}");
                return;
            }

            // Check for package.json
            string packageJsonPath = System.IO.Path.Combine(workingDir, "package.json");
            UnityEngine.Debug.Log($"  package.json Exists: {System.IO.File.Exists(packageJsonPath)}");
            UnityEngine.Debug.Log("═══════════════════════════════════════════════════════════════");

            string processFileName = "(not set)";
            string processArguments = "(not set)";

            try
            {
                #if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                // Use login shell to ensure PATH includes nvm, homebrew, etc.
                // IMPORTANT: Use proper escaping for zsh -lc
                string shell = System.IO.File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/bash";

                // Check if command is relative (npm, node) vs absolute path
                bool isRelativeCommand = !configCommand.StartsWith("/") && !configCommand.Contains("/");

                if (isRelativeCommand)
                {
                    // Run via login shell: /bin/zsh -lc "cd '/path/to/backend' && npm start"
                    // Note: Use double quotes for -lc argument, single quotes for paths inside
                    string shellCommand = $"cd '{workingDir}' && {configCommand} {configArgs}";
                    processFileName = shell;
                    processArguments = $"-lc \"{shellCommand}\"";

                    UnityEngine.Debug.Log($"[LocalServerAutoStart] Using login shell for relative command:");
                    UnityEngine.Debug.Log($"  Shell: {shell}");
                    UnityEngine.Debug.Log($"  Full command: {shell} -lc \"{shellCommand}\"");
                }
                else
                {
                    // Absolute path - run directly
                    processFileName = configCommand;
                    processArguments = configArgs;
                    UnityEngine.Debug.Log($"[LocalServerAutoStart] Using absolute path: {configCommand} {configArgs}");
                }
                #elif UNITY_EDITOR_WIN
                // On Windows, use cmd.exe for npm
                if (configCommand == "npm" || configCommand == "node")
                {
                    processFileName = "cmd.exe";
                    processArguments = $"/c cd /d \"{workingDir}\" && {configCommand} {configArgs}";
                    UnityEngine.Debug.Log($"[LocalServerAutoStart] Using cmd.exe: {processArguments}");
                }
                else
                {
                    processFileName = configCommand;
                    processArguments = configArgs;
                }
                #else
                processFileName = configCommand;
                processArguments = configArgs;
                #endif

                UnityEngine.Debug.Log("───────────────────────────────────────────────────────────────");
                UnityEngine.Debug.Log($"[LocalServerAutoStart] ProcessStartInfo:");
                UnityEngine.Debug.Log($"  FileName: {processFileName}");
                UnityEngine.Debug.Log($"  Arguments: {processArguments}");
                UnityEngine.Debug.Log($"  WorkingDirectory: {workingDir}");
                UnityEngine.Debug.Log("───────────────────────────────────────────────────────────────");

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = processFileName,
                    Arguments = processArguments,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _serverProcess = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                // Always capture output for diagnostics (first 50 lines)
                _serverProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        string data = e.Data;
                        lock (_outputLock)
                        {
                            if (_outputBuffer.Count < MAX_OUTPUT_LINES)
                            {
                                _outputBuffer.Add($"[stdout] {data}");
                            }
                        }

                        if (config.ShowServerLogs)
                        {
                            EditorApplication.delayCall += () =>
                            {
                                UnityEngine.Debug.Log($"[Backend] {data}");
                            };
                        }
                    }
                };

                _serverProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        string data = e.Data;
                        lock (_outputLock)
                        {
                            if (_outputBuffer.Count < MAX_OUTPUT_LINES)
                            {
                                _outputBuffer.Add($"[stderr] {data}");
                            }
                        }

                        if (config.ShowServerLogs)
                        {
                            EditorApplication.delayCall += () =>
                            {
                                // npm often outputs normal info to stderr
                                if (data.Contains("error") || data.Contains("Error") || data.Contains("ERROR"))
                                {
                                    UnityEngine.Debug.LogError($"[Backend] {data}");
                                }
                                else
                                {
                                    UnityEngine.Debug.Log($"[Backend] {data}");
                                }
                            };
                        }
                    }
                };

                _serverProcess.Exited += (sender, e) =>
                {
                    int exitCode = -1;
                    try { exitCode = _serverProcess.ExitCode; } catch { }

                    EditorApplication.delayCall += () =>
                    {
                        UnityEngine.Debug.LogWarning($"[LocalServerAutoStart] Server process EXITED with code: {exitCode}");
                        LogCapturedOutput();
                    };
                };

                // Attempt to start
                UnityEngine.Debug.Log("[LocalServerAutoStart] Calling Process.Start()...");
                bool started = _serverProcess.Start();

                if (started)
                {
                    int pid = _serverProcess.Id;
                    UnityEngine.Debug.Log($"[LocalServerAutoStart] ✓ Process.Start() SUCCEEDED");
                    UnityEngine.Debug.Log($"  PID: {pid}");
                    UnityEngine.Debug.Log($"  HasExited: {_serverProcess.HasExited}");

                    _serverProcess.BeginOutputReadLine();
                    _serverProcess.BeginErrorReadLine();
                }
                else
                {
                    UnityEngine.Debug.LogError("[LocalServerAutoStart] ✗ Process.Start() returned FALSE (rare)");
                    LogManualStartInstructions(config);
                }
            }
            catch (System.ComponentModel.Win32Exception w32ex)
            {
                UnityEngine.Debug.LogError(
                    $"[LocalServerAutoStart] ✗ Win32Exception (file not found or access denied):\n" +
                    $"  NativeErrorCode: {w32ex.NativeErrorCode}\n" +
                    $"  Message: {w32ex.Message}\n" +
                    $"  FileName attempted: {processFileName}\n" +
                    $"  Arguments: {processArguments}"
                );
                LogManualStartInstructions(config);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(
                    $"[LocalServerAutoStart] ✗ Exception starting process:\n" +
                    $"  Type: {ex.GetType().Name}\n" +
                    $"  Message: {ex.Message}\n" +
                    $"  StackTrace:\n{ex.StackTrace}"
                );
                LogManualStartInstructions(config);
            }
        }

        /// <summary>
        /// Log captured stdout/stderr from the process
        /// </summary>
        private static void LogCapturedOutput()
        {
            lock (_outputLock)
            {
                if (_outputBuffer.Count > 0)
                {
                    UnityEngine.Debug.Log($"[LocalServerAutoStart] Captured output ({_outputBuffer.Count} lines):");
                    foreach (string line in _outputBuffer)
                    {
                        UnityEngine.Debug.Log($"  {line}");
                    }
                }
                else
                {
                    UnityEngine.Debug.LogWarning("[LocalServerAutoStart] No stdout/stderr captured from process");
                }
            }
        }

        /// <summary>
        /// Wait for the server to become reachable (max 10 seconds, non-blocking)
        /// </summary>
        private static async Task WaitForServerReady(LocalServerConfig config)
        {
            // Hard cap at 10 seconds to not block gameplay
            const float MAX_TIMEOUT = 10f;
            float timeout = Mathf.Min(config.StartupTimeout, MAX_TIMEOUT);
            float elapsed = 0f;
            float checkInterval = 1.0f; // Check every second

            string baseUrl = config.BaseUrl.TrimEnd('/');
            UnityEngine.Debug.Log($"[LocalServerAutoStart] Waiting for server at {baseUrl} (max {timeout}s)...");

            int checkCount = 0;
            while (elapsed < timeout)
            {
                checkCount++;
                UnityEngine.Debug.Log($"[LocalServerAutoStart] Health check attempt #{checkCount} (elapsed: {elapsed:F1}s)");

                if (await IsServerReachable(config))
                {
                    UnityEngine.Debug.Log($"[LocalServerAutoStart] ✓ Server is ready! (took {elapsed:F1}s)");
                    return;
                }

                // Check if process exited early
                if (_serverProcess != null && _serverProcess.HasExited)
                {
                    int exitCode = -1;
                    try { exitCode = _serverProcess.ExitCode; } catch { }
                    UnityEngine.Debug.LogError($"[LocalServerAutoStart] ✗ Server process exited early with code {exitCode}");
                    LogCapturedOutput();
                    LogManualStartInstructions(config);
                    return;
                }

                await Task.Delay((int)(checkInterval * 1000));
                elapsed += checkInterval;
            }

            // Timeout reached - log error but don't block
            UnityEngine.Debug.LogError(
                "═══════════════════════════════════════════════════════════════\n" +
                $"[LocalServerAutoStart] Server not reachable after {timeout}s\n" +
                $"  Base URL: {baseUrl}\n" +
                $"  Tried: /api/health, /health, /\n" +
                $"  Also tried: 127.0.0.1 fallback\n" +
                "  Gameplay will continue, but API calls may fail.\n" +
                "═══════════════════════════════════════════════════════════════"
            );
            LogCapturedOutput();
            LogManualStartInstructions(config);
        }

        /// <summary>
        /// Check if server is reachable via health endpoint with fallback chain
        /// Tries: /api/health -> /health -> / (stops at first success)
        /// Also tries 127.0.0.1 if localhost fails
        /// </summary>
        private static async Task<bool> IsServerReachable(LocalServerConfig config)
        {
            string baseUrl = config.BaseUrl.TrimEnd('/');

            // Build list of base URLs to try (localhost -> 127.0.0.1 fallback)
            var baseUrls = new System.Collections.Generic.List<string> { baseUrl };

            // If using localhost, also try 127.0.0.1 as fallback
            if (baseUrl.Contains("localhost") && !_triedLocalhostFallback)
            {
                string fallbackUrl = baseUrl.Replace("localhost", "127.0.0.1");
                baseUrls.Add(fallbackUrl);
            }

            // Health endpoint paths to try
            string[] healthPaths = new string[] { "/api/health", "/health", "" };

            foreach (string currentBaseUrl in baseUrls)
            {
                foreach (string path in healthPaths)
                {
                    string fullUrl = currentBaseUrl + path;
                    var (success, statusCode, error) = await TryHealthCheck(fullUrl, config.HealthCheckTimeout);

                    if (success)
                    {
                        UnityEngine.Debug.Log($"[LocalServerAutoStart] ✓ Health check succeeded: {fullUrl}");

                        // If we succeeded with 127.0.0.1 fallback, remember it
                        if (currentBaseUrl.Contains("127.0.0.1") && baseUrl.Contains("localhost"))
                        {
                            _triedLocalhostFallback = true;
                            UnityEngine.Debug.Log("[LocalServerAutoStart] Note: 127.0.0.1 worked instead of localhost");
                        }
                        return true;
                    }

                    UnityEngine.Debug.Log($"[LocalServerAutoStart] Health check: {fullUrl} -> {(statusCode > 0 ? $"HTTP {statusCode}" : error)}");
                }
            }

            return false;
        }

        /// <summary>
        /// Try a single health check URL
        /// Returns: (success, httpStatusCode, errorMessage)
        /// </summary>
        private static async Task<(bool success, int statusCode, string error)> TryHealthCheck(string url, int timeoutSeconds)
        {
            try
            {
                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    request.timeout = timeoutSeconds;
                    var operation = request.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Delay(50);
                    }

                    long responseCode = request.responseCode;

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        return (true, (int)responseCode, null);
                    }

                    // Return details about why it failed
                    string errorDetail = request.result switch
                    {
                        UnityWebRequest.Result.ConnectionError => $"ConnectionError: {request.error}",
                        UnityWebRequest.Result.ProtocolError => $"HTTP {responseCode}",
                        UnityWebRequest.Result.DataProcessingError => $"DataError: {request.error}",
                        _ => request.error ?? "Unknown error"
                    };

                    return (false, (int)responseCode, errorDetail);
                }
            }
            catch (Exception ex)
            {
                return (false, 0, $"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Kill the server process when exiting Play Mode
        /// </summary>
        private static void TryKillServer()
        {
            LocalServerConfig config = LocalServerConfig.Instance;
            if (config != null && !config.KillOnExit)
            {
                UnityEngine.Debug.Log("[LocalServerAutoStart] Keeping server running (KillOnExit disabled)");
                return;
            }

            if (_serverProcess == null)
            {
                return;
            }

            try
            {
                if (!_serverProcess.HasExited)
                {
                    UnityEngine.Debug.Log($"[LocalServerAutoStart] Stopping server (PID: {_serverProcess.Id})");

                    // Try graceful kill first
                    #if UNITY_EDITOR_WIN
                    // On Windows, use taskkill to kill process tree
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = $"/T /F /PID {_serverProcess.Id}",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                    }
                    catch
                    {
                        _serverProcess.Kill();
                    }
                    #else
                    // On Unix, kill process group
                    try
                    {
                        // Kill the entire process tree
                        _serverProcess.Kill();
                    }
                    catch { }
                    #endif

                    _serverProcess.Dispose();
                    UnityEngine.Debug.Log("[LocalServerAutoStart] Server stopped");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LocalServerAutoStart] Error stopping server: {ex.Message}");
            }
            finally
            {
                _serverProcess = null;
            }
        }

        /// <summary>
        /// Log clear instructions for manual start
        /// </summary>
        private static void LogManualStartInstructions(LocalServerConfig config)
        {
            string workingDir = config?.ServerWorkingDirectory ?? "[not configured]";
            string command = config != null ? $"{config.Command} {config.Arguments}" : "npm start";

            UnityEngine.Debug.LogError(
                "═══════════════════════════════════════════════════════════════\n" +
                "  LOCAL SERVER AUTO-START FAILED\n" +
                "───────────────────────────────────────────────────────────────\n" +
                "  To start manually, open Terminal and run:\n" +
                $"\n    cd \"{workingDir}\"\n" +
                $"    {command}\n" +
                "\n───────────────────────────────────────────────────────────────\n" +
                "  Configuration: Assets/Resources/LocalServerConfig.asset\n" +
                "═══════════════════════════════════════════════════════════════"
            );
        }

        /// <summary>
        /// Menu item to manually start server
        /// </summary>
        [MenuItem("Tools/ShadowingTutor/Start Local Server")]
        public static async void MenuStartServer()
        {
            LocalServerConfig config = LocalServerConfig.Instance;
            if (config == null)
            {
                UnityEngine.Debug.LogError("[LocalServerAutoStart] No LocalServerConfig found. Create one first.");
                return;
            }

            if (await IsServerReachable(config))
            {
                UnityEngine.Debug.Log($"[LocalServerAutoStart] Server already running at {config.BaseUrl}");
                return;
            }

            StartServerProcess(config);
        }

        /// <summary>
        /// Menu item to stop server
        /// </summary>
        [MenuItem("Tools/ShadowingTutor/Stop Local Server")]
        public static void MenuStopServer()
        {
            TryKillServer();
        }

        /// <summary>
        /// Menu item to check server status
        /// </summary>
        [MenuItem("Tools/ShadowingTutor/Check Server Status")]
        public static async void MenuCheckStatus()
        {
            LocalServerConfig config = LocalServerConfig.Instance;
            if (config == null)
            {
                UnityEngine.Debug.LogError("[LocalServerAutoStart] No LocalServerConfig found");
                return;
            }

            bool reachable = await IsServerReachable(config);
            if (reachable)
            {
                UnityEngine.Debug.Log($"[LocalServerAutoStart] Server is RUNNING at {config.BaseUrl}");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[LocalServerAutoStart] Server is NOT REACHABLE at {config.BaseUrl}");
            }
        }

        /// <summary>
        /// Menu item to create and setup LocalServerConfig
        /// </summary>
        [MenuItem("Tools/ShadowingTutor/Setup Local Server Config")]
        public static void MenuSetupConfig()
        {
            // Ensure Resources folder exists
            string resourcesPath = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesPath))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            string configPath = "Assets/Resources/LocalServerConfig.asset";

            // Check if config already exists
            LocalServerConfig existingConfig = AssetDatabase.LoadAssetAtPath<LocalServerConfig>(configPath);
            if (existingConfig != null)
            {
                UnityEngine.Debug.Log("[LocalServerAutoStart] Config already exists. Opening in Inspector.");
                Selection.activeObject = existingConfig;
                EditorGUIUtility.PingObject(existingConfig);
                return;
            }

            // Create new config
            LocalServerConfig config = ScriptableObject.CreateInstance<LocalServerConfig>();

            // Auto-detect backend folder
            string unityProjectPath = Application.dataPath;  // .../unity/Assets
            string projectRoot = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(unityProjectPath));

            string[] possiblePaths = new string[]
            {
                System.IO.Path.Combine(projectRoot, "backend"),
                System.IO.Path.Combine(projectRoot, "server"),
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(unityProjectPath), "backend"),
            };

            foreach (string path in possiblePaths)
            {
                if (System.IO.Directory.Exists(path))
                {
                    string packageJson = System.IO.Path.Combine(path, "package.json");
                    if (System.IO.File.Exists(packageJson))
                    {
                        // Use reflection to set the private field
                        var field = typeof(LocalServerConfig).GetField("_serverWorkingDirectory",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            field.SetValue(config, path);
                        }
                        UnityEngine.Debug.Log($"[LocalServerAutoStart] Auto-detected backend at: {path}");
                        break;
                    }
                }
            }

            // Save asset
            AssetDatabase.CreateAsset(config, configPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Select in Inspector
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);

            UnityEngine.Debug.Log(
                "[LocalServerAutoStart] Created LocalServerConfig at: " + configPath + "\n" +
                "Please verify the settings in the Inspector."
            );
        }
    }
}
#endif
