using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace ShadowingTutor.Diagnostics
{
    /// <summary>
    /// Persistent file logger that works WITHOUT PC connection.
    /// Logs to Application.persistentDataPath/app_log.txt
    ///
    /// Access via: FileLogger.LogPath
    /// Share via: Android file manager or adb pull when available
    /// </summary>
    public class FileLogger : MonoBehaviour
    {
        private static FileLogger _instance;
        private static string _logPath;
        private static StreamWriter _writer;
        private static StringBuilder _buffer = new StringBuilder();
        private static int _bufferedLines = 0;
        private const int FLUSH_THRESHOLD = 10;

        public static string LogPath => _logPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (_instance != null) return;

            // Create persistent GameObject
            var go = new GameObject("FileLogger");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FileLogger>();

            // Setup log file path
            _logPath = Path.Combine(Application.persistentDataPath, "app_log.txt");

            try
            {
                // Rotate old log if too large (>1MB)
                if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 1024 * 1024)
                {
                    string oldPath = _logPath + ".old";
                    if (File.Exists(oldPath)) File.Delete(oldPath);
                    File.Move(_logPath, oldPath);
                }

                // Open file for append
                _writer = new StreamWriter(_logPath, append: true, Encoding.UTF8);
                _writer.AutoFlush = false;

                // Log startup
                string header = $"\n{'='} {'='} {'='} APP START {'='} {'='} {'='}\n" +
                               $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                               $"Platform: {Application.platform}\n" +
                               $"Version: {Application.version}\n" +
                               $"Unity: {Application.unityVersion}\n" +
                               $"LogPath: {_logPath}\n" +
                               $"{'='} {'='} {'='} {'='} {'='} {'='} {'='} {'='} {'='}";

                WriteLineInternal(header);

                // Subscribe to Unity log messages
                Application.logMessageReceived += OnLogMessageReceived;

                Debug.Log($"[FileLogger] Logging to: {_logPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileLogger] Failed to initialize: {e.Message}");
            }
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (_writer == null) return;

            string prefix = type switch
            {
                LogType.Error => "[ERROR]",
                LogType.Exception => "[EXCEPTION]",
                LogType.Warning => "[WARN]",
                LogType.Assert => "[ASSERT]",
                _ => "[INFO]"
            };

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"{timestamp} {prefix} {condition}";

            // Include stack trace for errors/exceptions
            if ((type == LogType.Error || type == LogType.Exception) && !string.IsNullOrEmpty(stackTrace))
            {
                line += $"\n  {stackTrace.Replace("\n", "\n  ")}";
            }

            WriteLineInternal(line);
        }

        private static void WriteLineInternal(string line)
        {
            if (_writer == null) return;

            lock (_buffer)
            {
                _buffer.AppendLine(line);
                _bufferedLines++;

                if (_bufferedLines >= FLUSH_THRESHOLD)
                {
                    FlushBuffer();
                }
            }
        }

        private static void FlushBuffer()
        {
            if (_writer == null || _buffer.Length == 0) return;

            try
            {
                lock (_buffer)
                {
                    _writer.Write(_buffer.ToString());
                    _writer.Flush();
                    _buffer.Clear();
                    _bufferedLines = 0;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileLogger] Flush failed: {e.Message}");
            }
        }

        /// <summary>
        /// Write a custom log entry directly
        /// </summary>
        public static void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            WriteLineInternal($"{timestamp} [CUSTOM] {message}");
        }

        /// <summary>
        /// Log network request start
        /// </summary>
        public static void LogNetworkStart(string method, string url)
        {
            Log($"NET START: {method} {url}");
        }

        /// <summary>
        /// Log network request end
        /// </summary>
        public static void LogNetworkEnd(string url, int statusCode, string result)
        {
            Log($"NET END: {url} -> {statusCode} {result}");
        }

        /// <summary>
        /// Get last N lines from log for overlay display
        /// </summary>
        public static string GetRecentLogs(int lines = 20)
        {
            if (string.IsNullOrEmpty(_logPath) || !File.Exists(_logPath))
                return "No log file";

            try
            {
                FlushBuffer(); // Ensure all buffered content is written

                var allLines = File.ReadAllLines(_logPath);
                int start = Math.Max(0, allLines.Length - lines);
                var recent = new StringBuilder();
                for (int i = start; i < allLines.Length; i++)
                {
                    recent.AppendLine(allLines[i]);
                }
                return recent.ToString();
            }
            catch (Exception e)
            {
                return $"Error reading log: {e.Message}";
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                WriteLineInternal($"{DateTime.Now:HH:mm:ss.fff} [INFO] App paused");
                FlushBuffer();
            }
            else
            {
                WriteLineInternal($"{DateTime.Now:HH:mm:ss.fff} [INFO] App resumed");
            }
        }

        private void OnApplicationQuit()
        {
            WriteLineInternal($"{DateTime.Now:HH:mm:ss.fff} [INFO] App quit");
            FlushBuffer();

            Application.logMessageReceived -= OnLogMessageReceived;

            if (_writer != null)
            {
                _writer.Close();
                _writer = null;
            }
        }
    }
}
