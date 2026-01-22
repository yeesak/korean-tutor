using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowingTutor
{
    /// <summary>
    /// Settings panel for configuring app environment and options.
    /// Toggle with gear button or Escape key.
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        private static SettingsPanel _instance;
        public static SettingsPanel Instance => _instance;

        [Header("Panel")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _toggleButton; // Gear icon button to open settings

        [Header("Environment Settings")]
        [SerializeField] private Dropdown _environmentDropdown;
        [SerializeField] private InputField _lanIpInput;
        [SerializeField] private GameObject _lanIpContainer; // Show only when LAN selected
        [SerializeField] private Text _currentUrlText;

        [Header("Connection Status")]
        [SerializeField] private Text _statusText;
        [SerializeField] private Button _testConnectionButton;
        [SerializeField] private Image _statusIndicator;
        [SerializeField] private Color _statusConnected = Color.green;
        [SerializeField] private Color _statusDisconnected = Color.red;
        [SerializeField] private Color _statusTesting = Color.yellow;

        [Header("App Info")]
        [SerializeField] private Text _versionText;
        [SerializeField] private Text _buildInfoText;

        private bool _isOpen = false;
        private Coroutine _testCoroutine;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            InitializeUI();
            HidePanel();
        }

        private void Update()
        {
            // Toggle with Escape key (Editor/Standalone only - not available on mobile with new Input System)
#if UNITY_EDITOR || UNITY_STANDALONE
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_isOpen)
                    HidePanel();
                else
                    ShowPanel();
            }
#endif
        }

        private void InitializeUI()
        {
            // Setup close button
            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(HidePanel);
            }

            // Setup toggle button (gear icon)
            if (_toggleButton != null)
            {
                _toggleButton.onClick.AddListener(TogglePanel);
            }

            // Setup environment dropdown
            if (_environmentDropdown != null)
            {
                _environmentDropdown.ClearOptions();
                _environmentDropdown.AddOptions(new System.Collections.Generic.List<string>(
                    AppConfig.GetEnvironmentOptions()
                ));
                _environmentDropdown.value = AppConfig.Instance.GetEnvironmentIndex();
                _environmentDropdown.onValueChanged.AddListener(OnEnvironmentChanged);
            }

            // Setup LAN IP input
            if (_lanIpInput != null)
            {
                _lanIpInput.text = AppConfig.Instance.LanIP;
                _lanIpInput.onEndEdit.AddListener(OnLanIpChanged);
            }

            // Setup test connection button
            if (_testConnectionButton != null)
            {
                _testConnectionButton.onClick.AddListener(OnTestConnectionClick);
            }

            // Show version info
            UpdateVersionInfo();

            // Update URL display
            UpdateCurrentUrlDisplay();

            // Show/hide LAN IP field
            UpdateLanIpVisibility();
        }

        private void OnEnvironmentChanged(int index)
        {
            AppConfig.Instance.SetEnvironmentByIndex(index);
            UpdateCurrentUrlDisplay();
            UpdateLanIpVisibility();

            // Auto-test connection when environment changes
            TestConnection();
        }

        private void OnLanIpChanged(string newIp)
        {
            if (!string.IsNullOrEmpty(newIp))
            {
                AppConfig.Instance.SetLanIP(newIp);
                UpdateCurrentUrlDisplay();

                // Auto-test connection
                TestConnection();
            }
        }

        private void UpdateCurrentUrlDisplay()
        {
            if (_currentUrlText != null)
            {
                _currentUrlText.text = AppConfig.Instance.BackendBaseUrl;
            }
        }

        private void UpdateLanIpVisibility()
        {
            if (_lanIpContainer != null)
            {
                bool showLanIp = AppConfig.Instance.CurrentEnvironment == AppConfig.Environment.LAN;
                _lanIpContainer.SetActive(showLanIp);
            }
        }

        private void UpdateVersionInfo()
        {
            if (_versionText != null)
            {
                _versionText.text = $"v{Application.version}";
            }

            if (_buildInfoText != null)
            {
                string buildType = Debug.isDebugBuild ? "Debug" : "Release";
                string platform = Application.platform.ToString();
                _buildInfoText.text = $"{buildType} | {platform}";
            }
        }

        private void OnTestConnectionClick()
        {
            TestConnection();
        }

        public void TestConnection()
        {
            if (_testCoroutine != null)
            {
                StopCoroutine(_testCoroutine);
            }
            _testCoroutine = StartCoroutine(TestConnectionCoroutine());
        }

        private IEnumerator TestConnectionCoroutine()
        {
            // Show testing state
            SetStatus("Testing...", _statusTesting);

            if (_testConnectionButton != null)
            {
                _testConnectionButton.interactable = false;
            }

            // Make health check request
            string url = AppConfig.Instance.HealthUrl;
            using (UnityEngine.Networking.UnityWebRequest request =
                   UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                request.timeout = 5; // 5 second timeout

                yield return request.SendWebRequest();

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    // Parse response
                    try
                    {
                        string json = request.downloadHandler.text;
                        if (json.Contains("\"ok\":true"))
                        {
                            bool mockMode = json.Contains("\"mockMode\":true");
                            string modeText = mockMode ? " (Mock)" : " (Live)";
                            SetStatus("Connected" + modeText, _statusConnected);
                        }
                        else
                        {
                            SetStatus("Invalid response", _statusDisconnected);
                        }
                    }
                    catch
                    {
                        SetStatus("Parse error", _statusDisconnected);
                    }
                }
                else
                {
                    string errorMsg = "Connection failed";

                    // Provide helpful error messages
                    if (request.error.Contains("Cannot resolve"))
                    {
                        errorMsg = "Cannot reach server";
                    }
                    else if (request.error.Contains("Connection refused"))
                    {
                        errorMsg = "Server not running";
                    }
                    else if (request.error.Contains("timeout"))
                    {
                        errorMsg = "Connection timeout";
                    }

                    SetStatus(errorMsg, _statusDisconnected);
                    Debug.LogWarning($"[Settings] Connection test failed: {request.error}");
                }
            }

            if (_testConnectionButton != null)
            {
                _testConnectionButton.interactable = true;
            }
        }

        private void SetStatus(string message, Color color)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
                _statusText.color = color;
            }

            if (_statusIndicator != null)
            {
                _statusIndicator.color = color;
            }
        }

        public void ShowPanel()
        {
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(true);
                _isOpen = true;

                // Refresh current values
                if (_environmentDropdown != null)
                {
                    _environmentDropdown.value = AppConfig.Instance.GetEnvironmentIndex();
                }
                if (_lanIpInput != null)
                {
                    _lanIpInput.text = AppConfig.Instance.LanIP;
                }

                UpdateCurrentUrlDisplay();
                UpdateLanIpVisibility();

                // Test connection on open
                TestConnection();
            }
        }

        public void HidePanel()
        {
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
                _isOpen = false;
            }
        }

        public void TogglePanel()
        {
            if (_isOpen)
                HidePanel();
            else
                ShowPanel();
        }

        public bool IsOpen => _isOpen;
    }
}
