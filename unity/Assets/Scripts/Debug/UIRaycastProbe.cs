using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace ShadowingTutor.Diagnostics
{
    /// <summary>
    /// Debug probe that logs UI raycast hits ONLY on click/tap begin.
    /// No per-frame spam. Attach to any GameObject in the scene to enable.
    /// </summary>
    public class UIRaycastProbe : MonoBehaviour
    {
        [SerializeField] private bool _logEveryFrame = false;
        [SerializeField] private bool _logOnTouch = true;

        private PointerEventData _pointerEventData;
        private List<RaycastResult> _raycastResults = new List<RaycastResult>();

        private void Start()
        {
            LogInputSystemStatus();
        }

        private void Update()
        {
            // Only probe on actual click/tap begin (no spam)
            if (!WasPointerPressedThisFrame(out Vector2 pointerPos))
            {
                return;
            }

            if (EventSystem.current == null)
            {
                UnityEngine.Debug.LogWarning("[UIRaycastProbe] Click detected but NO EventSystem.current!");
                return;
            }

            if (_logOnTouch || _logEveryFrame)
            {
                LogRaycastResults(pointerPos);
            }
        }

        /// <summary>
        /// Returns true ONLY on the frame when pointer/touch was pressed.
        /// Supports both Input System and Legacy Input.
        /// </summary>
        private bool WasPointerPressedThisFrame(out Vector2 pos)
        {
            pos = Vector2.zero;

            #if ENABLE_INPUT_SYSTEM
            // Check touch input (New Input System) - any touch that just started
            if (Touchscreen.current != null)
            {
                foreach (var touch in Touchscreen.current.touches)
                {
                    if (touch.press.wasPressedThisFrame)
                    {
                        pos = touch.position.ReadValue();
                        return true;
                    }
                }
            }

            // Check mouse input (New Input System)
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                pos = Mouse.current.position.ReadValue();
                return true;
            }
            #elif ENABLE_LEGACY_INPUT_MANAGER
            // Check mouse input (Legacy)
            if (Input.GetMouseButtonDown(0))
            {
                pos = Input.mousePosition;
                return true;
            }

            // Check touch input (Legacy)
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                pos = Input.GetTouch(0).position;
                return true;
            }
            #endif

            return false;
        }

        private void LogRaycastResults(Vector2 screenPos)
        {
            _pointerEventData = new PointerEventData(EventSystem.current)
            {
                position = screenPos
            };

            _raycastResults.Clear();
            EventSystem.current.RaycastAll(_pointerEventData, _raycastResults);

            UnityEngine.Debug.Log($"[UIRaycastProbe] === RAYCAST at ({screenPos.x:F0}, {screenPos.y:F0}) ===");
            UnityEngine.Debug.Log($"[UIRaycastProbe] Hit count: {_raycastResults.Count}");

            if (_raycastResults.Count == 0)
            {
                LogNoHitsDiagnostics(screenPos);
                return;
            }

            for (int i = 0; i < _raycastResults.Count; i++)
            {
                var hit = _raycastResults[i];
                var go = hit.gameObject;
                string interactable = "";

                // Check if it's a Button
                var button = go.GetComponent<Button>();
                if (button != null)
                {
                    interactable = button.interactable ? " [Button:INTERACTABLE]" : " [Button:DISABLED]";
                }

                // Check if it's a Graphic with raycastTarget
                var graphic = go.GetComponent<Graphic>();
                string raycastInfo = graphic != null ? $" raycastTarget={graphic.raycastTarget}" : "";

                // Get hierarchy path
                string path = GetGameObjectPath(go);

                UnityEngine.Debug.Log($"[UIRaycastProbe] Hit[{i}]: {go.name}{interactable}{raycastInfo} | depth={hit.depth} | path={path}");
            }

            // Highlight if top hit is NOT a button
            var topHit = _raycastResults[0].gameObject;
            if (topHit.name != "StartButton" && topHit.name != "MainButton" &&
                topHit.GetComponent<Button>() == null)
            {
                UnityEngine.Debug.LogWarning($"[UIRaycastProbe] TOP HIT is '{topHit.name}' which is NOT a button - this may be blocking clicks!");
            }
        }

        /// <summary>
        /// Log actionable diagnostics when raycast finds no hits.
        /// </summary>
        private void LogNoHitsDiagnostics(Vector2 screenPos)
        {
            var es = EventSystem.current;
            string moduleName = es.currentInputModule != null ? es.currentInputModule.GetType().Name : "NONE";

            UnityEngine.Debug.LogWarning(
                $"[UIRaycastProbe] NO HITS on click at ({screenPos.x:F0}, {screenPos.y:F0}).\n" +
                $"  EventSystem: {es.name} (enabled={es.enabled})\n" +
                $"  CurrentInputModule: {moduleName}"
            );

            // List all canvases and their GraphicRaycaster status
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            UnityEngine.Debug.Log($"[UIRaycastProbe] Found {canvases.Length} Canvas(es):");
            foreach (var canvas in canvases)
            {
                var gr = canvas.GetComponent<GraphicRaycaster>();
                string grStatus = gr != null ? $"GraphicRaycaster={gr.enabled}" : "NO GraphicRaycaster!";
                UnityEngine.Debug.Log($"  - '{canvas.name}': renderMode={canvas.renderMode}, enabled={canvas.enabled}, {grStatus}");
            }

            // List input modules on EventSystem
            var modules = es.GetComponents<BaseInputModule>();
            UnityEngine.Debug.Log($"[UIRaycastProbe] InputModules on EventSystem ({modules.Length}):");
            foreach (var module in modules)
            {
                UnityEngine.Debug.Log($"  - {module.GetType().Name}: enabled={module.enabled}");
            }
        }

        private void LogInputSystemStatus()
        {
            UnityEngine.Debug.Log("[UIRaycastProbe] === INPUT SYSTEM STATUS ===");

            // Check EventSystem
            var es = EventSystem.current;
            if (es == null)
            {
                UnityEngine.Debug.LogError("[UIRaycastProbe] NO EventSystem.current!");
                return;
            }

            UnityEngine.Debug.Log($"[UIRaycastProbe] EventSystem: {es.name}");

            // Check for StandaloneInputModule (OLD - should NOT be present)
            var standalone = es.GetComponent<StandaloneInputModule>();
            if (standalone != null && standalone.enabled)
            {
                UnityEngine.Debug.LogError("[UIRaycastProbe] CONFLICT: StandaloneInputModule is ENABLED! Should use InputSystemUIInputModule only.");
            }

            #if ENABLE_INPUT_SYSTEM
            // Check for InputSystemUIInputModule (NEW - should be present and enabled)
            var inputSystemModule = es.GetComponent<InputSystemUIInputModule>();
            if (inputSystemModule == null)
            {
                UnityEngine.Debug.LogError("[UIRaycastProbe] MISSING: InputSystemUIInputModule not found on EventSystem!");
            }
            else if (!inputSystemModule.enabled)
            {
                UnityEngine.Debug.LogError("[UIRaycastProbe] DISABLED: InputSystemUIInputModule is disabled!");
            }
            else
            {
                UnityEngine.Debug.Log($"[UIRaycastProbe] InputSystemUIInputModule: ENABLED, actionsAsset={(inputSystemModule.actionsAsset != null ? inputSystemModule.actionsAsset.name : "NULL")}");

                // Check action references
                if (inputSystemModule.point?.action == null)
                    UnityEngine.Debug.LogWarning("[UIRaycastProbe] InputSystemUIInputModule.point action is NULL");
                if (inputSystemModule.leftClick?.action == null)
                    UnityEngine.Debug.LogWarning("[UIRaycastProbe] InputSystemUIInputModule.leftClick action is NULL");
            }

            // Check devices
            UnityEngine.Debug.Log($"[UIRaycastProbe] Touchscreen: {(Touchscreen.current != null ? "AVAILABLE" : "NOT AVAILABLE")}");
            UnityEngine.Debug.Log($"[UIRaycastProbe] Mouse: {(Mouse.current != null ? "AVAILABLE" : "NOT AVAILABLE")}");
            #endif
        }

        private string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            Transform parent = go.transform.parent;
            int depth = 0;
            while (parent != null && depth < 5)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
                depth++;
            }
            return path;
        }
    }
}
