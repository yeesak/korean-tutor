using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace ShadowingTutor
{
    /// <summary>
    /// Runtime front-end visual fixes:
    /// 1. Locks camera FOV to 14.69837
    /// 2. Disables CC_Base_EyeOcclusion objects
    /// 3. Hides red UI overlay
    ///
    /// FRONTEND-ONLY: Does not touch backend/server/API code.
    /// </summary>
    public class FrontEndVisualFixes : MonoBehaviour
    {
        [Header("Camera Settings")]
        [Tooltip("Lock camera FOV to this exact value")]
        public float targetFOV = 14.69837f;

        [Tooltip("Force camera position (enable only if needed)")]
        public bool forceCameraPosition = false;

        [Tooltip("Target camera position")]
        public Vector3 targetCameraPosition = new Vector3(4.76837f, 1.59692f, -2.25214f);

        [Header("UI Settings")]
        [Tooltip("Names/paths of UI objects to disable (partial match)")]
        public string[] forceDisableUINames = new string[]
        {
            "RecordingIndicator",
            "RecordIndicator",
            "RedDot",
            "MicIndicator",
            "DebugOverlay",
            "MouthOpenIndicator"
        };

        [Header("Debug")]
        [Tooltip("Enable detailed logging")]
        public bool verboseLogging = true;

        private Camera _mainCamera;
        private PortraitCameraFramer _portraitFramer;
        private bool _initialized = false;

        // Static instance for auto-creation
        private static FrontEndVisualFixes _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            // Create persistent instance if none exists
            if (_instance == null)
            {
                GameObject go = new GameObject("_FrontEndVisualFixes");
                _instance = go.AddComponent<FrontEndVisualFixes>();
                DontDestroyOnLoad(go);
                Debug.Log("[FrontEndFixes] Auto-created runtime fix manager");
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            ApplyAllFixes();
        }

        private void Start()
        {
            // Apply again in Start to beat other scripts that run in Awake
            ApplyAllFixes();
        }

        private void LateUpdate()
        {
            // Continuously enforce FOV lock
            EnforceCameraFOV();
        }

        /// <summary>
        /// Apply all front-end visual fixes.
        /// </summary>
        public void ApplyAllFixes()
        {
            if (verboseLogging)
                Debug.Log("[FrontEndFixes] ========== APPLYING ALL FIXES ==========");

            // 1. Camera FOV and Position
            ApplyCameraFixes();

            // 2. Update PortraitCameraFramer
            UpdatePortraitFramer();

            // 3. Disable EyeOcclusion
            DisableAllEyeOcclusion();

            // 4. Hide Red UI Overlay
            HideRedUIOverlay();

            _initialized = true;

            if (verboseLogging)
                Debug.Log("[FrontEndFixes] ========== ALL FIXES APPLIED ==========\n");
        }

        private void ApplyCameraFixes()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                GameObject camObj = GameObject.Find("Main Camera");
                if (camObj != null)
                    _mainCamera = camObj.GetComponent<Camera>();
            }

            if (_mainCamera == null)
            {
                Debug.LogWarning("[FrontEndFixes] Main Camera not found!");
                return;
            }

            // Set FOV
            float oldFOV = _mainCamera.fieldOfView;
            _mainCamera.fieldOfView = targetFOV;

            if (verboseLogging)
                Debug.Log($"[FrontEndFixes] Camera FOV: {oldFOV} -> {targetFOV}");

            // Set position if enabled
            if (forceCameraPosition)
            {
                Vector3 oldPos = _mainCamera.transform.position;
                _mainCamera.transform.position = targetCameraPosition;

                if (verboseLogging)
                    Debug.Log($"[FrontEndFixes] Camera Position: {oldPos} -> {targetCameraPosition}");
            }
        }

        private void UpdatePortraitFramer()
        {
            if (_mainCamera == null) return;

            _portraitFramer = _mainCamera.GetComponent<PortraitCameraFramer>();
            if (_portraitFramer == null)
            {
                if (verboseLogging)
                    Debug.Log("[FrontEndFixes] No PortraitCameraFramer found on Main Camera");
                return;
            }

            // Use reflection to set the private _portraitFOV field
            var field = typeof(PortraitCameraFramer).GetField("_portraitFOV",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                float oldValue = (float)field.GetValue(_portraitFramer);
                field.SetValue(_portraitFramer, targetFOV);

                if (verboseLogging)
                    Debug.Log($"[FrontEndFixes] PortraitCameraFramer._portraitFOV: {oldValue} -> {targetFOV}");
            }
            else
            {
                Debug.LogWarning("[FrontEndFixes] Could not find _portraitFOV field in PortraitCameraFramer");
            }
        }

        private void EnforceCameraFOV()
        {
            if (_mainCamera != null && _mainCamera.fieldOfView != targetFOV)
            {
                _mainCamera.fieldOfView = targetFOV;
            }
        }

        private void DisableAllEyeOcclusion()
        {
            int count = 0;

            // Find ALL GameObjects including inactive ones
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

            foreach (GameObject obj in allObjects)
            {
                // Skip assets (only process scene objects)
                if (obj.hideFlags == HideFlags.NotEditable || obj.hideFlags == HideFlags.HideAndDontSave)
                    continue;

                // Check if it's in a valid scene
                if (!obj.scene.IsValid())
                    continue;

                if (obj.name == "CC_Base_EyeOcclusion")
                {
                    if (obj.activeSelf)
                    {
                        obj.SetActive(false);
                        count++;

                        if (verboseLogging)
                            Debug.Log($"[FrontEndFixes] Disabled EyeOcclusion: {GetFullPath(obj.transform)}");
                    }

                    // Also disable renderer if present
                    var smr = obj.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null && smr.enabled)
                    {
                        smr.enabled = false;
                    }

                    var mr = obj.GetComponent<MeshRenderer>();
                    if (mr != null && mr.enabled)
                    {
                        mr.enabled = false;
                    }
                }
            }

            if (verboseLogging)
                Debug.Log($"[FrontEndFixes] Total EyeOcclusion objects disabled: {count}");
        }

        private void HideRedUIOverlay()
        {
            if (verboseLogging)
                Debug.Log("[FrontEndFixes] Searching for red UI overlay...");

            // Find all canvases
            Canvas[] canvases = FindObjectsOfType<Canvas>(true);
            List<string> disabledObjects = new List<string>();
            List<string> candidatesFound = new List<string>();

            foreach (Canvas canvas in canvases)
            {
                // Get all Image and RawImage components
                Image[] images = canvas.GetComponentsInChildren<Image>(true);
                RawImage[] rawImages = canvas.GetComponentsInChildren<RawImage>(true);

                foreach (Image img in images)
                {
                    bool shouldDisable = ShouldDisableUIElement(img.gameObject, img.color);

                    if (IsRedColor(img.color) || shouldDisable)
                    {
                        string path = GetFullPath(img.transform);
                        candidatesFound.Add($"Image: {path} (Color: {img.color}, Size: {img.rectTransform.sizeDelta})");

                        if (shouldDisable)
                        {
                            img.gameObject.SetActive(false);
                            disabledObjects.Add(path);
                        }
                    }
                }

                foreach (RawImage img in rawImages)
                {
                    bool shouldDisable = ShouldDisableUIElement(img.gameObject, img.color);

                    if (IsRedColor(img.color) || shouldDisable)
                    {
                        string path = GetFullPath(img.transform);
                        candidatesFound.Add($"RawImage: {path} (Color: {img.color}, Size: {img.rectTransform.sizeDelta})");

                        if (shouldDisable)
                        {
                            img.gameObject.SetActive(false);
                            disabledObjects.Add(path);
                        }
                    }
                }
            }

            if (verboseLogging)
            {
                Debug.Log($"[FrontEndFixes] Red UI candidates found: {candidatesFound.Count}");
                foreach (string candidate in candidatesFound)
                {
                    Debug.Log($"  - {candidate}");
                }

                Debug.Log($"[FrontEndFixes] UI objects disabled: {disabledObjects.Count}");
                foreach (string disabled in disabledObjects)
                {
                    Debug.Log($"  - DISABLED: {disabled}");
                }
            }
        }

        private bool ShouldDisableUIElement(GameObject obj, Color color)
        {
            string nameLower = obj.name.ToLower();

            // Check against force disable list
            foreach (string pattern in forceDisableUINames)
            {
                if (nameLower.Contains(pattern.ToLower()))
                {
                    return true;
                }
            }

            // Check if it's a small red dot/indicator
            if (IsRedColor(color))
            {
                RectTransform rt = obj.GetComponent<RectTransform>();
                if (rt != null)
                {
                    // Small size suggests indicator/dot
                    if (rt.sizeDelta.x < 100 && rt.sizeDelta.y < 100)
                    {
                        // Check for indicator-like names
                        if (nameLower.Contains("dot") || nameLower.Contains("indicator") ||
                            nameLower.Contains("record") || nameLower.Contains("mic") ||
                            nameLower.Contains("debug") || nameLower.Contains("mouth"))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool IsRedColor(Color color)
        {
            // Check if color is predominantly red
            return color.r > 0.7f && color.g < 0.4f && color.b < 0.4f && color.a > 0.1f;
        }

        private string GetFullPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        /// <summary>
        /// Public method to manually re-apply fixes (can be called from other scripts).
        /// </summary>
        public static void ReapplyFixes()
        {
            if (_instance != null)
            {
                _instance.ApplyAllFixes();
            }
        }
    }
}
