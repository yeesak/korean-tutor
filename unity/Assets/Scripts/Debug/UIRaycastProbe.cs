using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ShadowingTutor.Diagnostics
{
    /// <summary>
    /// Debug probe that logs all UI raycast hits on touch/click.
    /// Attach to any GameObject in the scene to enable.
    /// </summary>
    public class UIRaycastProbe : MonoBehaviour
    {
        [SerializeField] private bool _logEveryFrame = false;
        [SerializeField] private bool _logOnTouch = true;

        private PointerEventData _pointerEventData;
        private List<RaycastResult> _raycastResults = new List<RaycastResult>();
        private bool _wasTouching = false;

        private void Start()
        {
            LogInputSystemStatus();
        }

        private void Update()
        {
            if (EventSystem.current == null)
            {
                if (Time.frameCount % 300 == 0)
                    UnityEngine.Debug.LogWarning("[UIRaycastProbe] No EventSystem.current!");
                return;
            }

            // Detect touch/click start
            bool isTouching = false;
            Vector2 pointerPos = Vector2.zero;

            // Check touch input (New Input System)
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                isTouching = true;
                pointerPos = Touchscreen.current.primaryTouch.position.ReadValue();
            }
            // Check mouse input (New Input System)
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                isTouching = true;
                pointerPos = Mouse.current.position.ReadValue();
            }

            // Log on touch begin or every frame if configured
            bool shouldLog = _logEveryFrame || (_logOnTouch && isTouching && !_wasTouching);

            if (shouldLog && isTouching)
            {
                LogRaycastResults(pointerPos);
            }

            _wasTouching = isTouching;
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
                UnityEngine.Debug.LogWarning("[UIRaycastProbe] NO HITS! Check: Canvas has GraphicRaycaster? EventSystem has InputModule?");
                LogEventSystemDiagnostics();
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

            // Highlight if top hit is NOT the StartButton/MainButton
            var topHit = _raycastResults[0].gameObject;
            if (topHit.name != "StartButton" && topHit.name != "MainButton" &&
                topHit.GetComponent<Button>() == null)
            {
                UnityEngine.Debug.LogWarning($"[UIRaycastProbe] TOP HIT is '{topHit.name}' which is NOT a button - this may be blocking clicks!");
            }
        }

        private void LogEventSystemDiagnostics()
        {
            var es = EventSystem.current;
            UnityEngine.Debug.Log($"[UIRaycastProbe] EventSystem: {es.name}, enabled={es.enabled}");

            // Check input modules
            var modules = es.GetComponents<BaseInputModule>();
            foreach (var module in modules)
            {
                UnityEngine.Debug.Log($"[UIRaycastProbe] InputModule: {module.GetType().Name}, enabled={module.enabled}");
            }

            // Check for GraphicRaycasters
            var raycasters = FindObjectsOfType<GraphicRaycaster>();
            foreach (var rc in raycasters)
            {
                UnityEngine.Debug.Log($"[UIRaycastProbe] GraphicRaycaster on '{rc.gameObject.name}', enabled={rc.enabled}");
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

            // Check for InputSystemUIInputModule (NEW - should be present and enabled)
            var inputSystemModule = es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
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
