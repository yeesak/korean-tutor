using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace ShadowingTutor.Bootstrap
{
    /// <summary>
    /// Runtime bootstrap that ensures UI input is properly configured.
    /// Runs after scene load and self-heals any misconfiguration.
    /// This allows Android UI buttons to be clickable even if scene setup is incomplete.
    ///
    /// Loading priority:
    /// 1. Try Resources/Input/UIActions (.inputactions JSON format - most reliable)
    /// 2. Fall back to runtime-created InputActionAsset if loading fails
    /// </summary>
    public static class EnsureUIInputRuntime
    {
        private const string ResourcePath = "Input/UIActions";
        private static bool _initialized = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            Debug.Log("[UIINPUT] === EnsureUIInputRuntime Initialize ===");
            EnsureEventSystemAndModule();
        }

        /// <summary>
        /// Call this manually if needed (e.g., after scene change).
        /// </summary>
        public static void EnsureEventSystemAndModule()
        {
            // Find or create EventSystem
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystem = Object.FindObjectOfType<EventSystem>();
            }

            if (eventSystem == null)
            {
                Debug.Log("[UIINPUT] No EventSystem found, creating one...");
                var go = new GameObject("EventSystem");
                eventSystem = go.AddComponent<EventSystem>();
            }

            // Check for legacy StandaloneInputModule and disable it
            var standaloneModule = eventSystem.GetComponent<StandaloneInputModule>();
            bool hadLegacyModule = standaloneModule != null && standaloneModule.enabled;
            if (standaloneModule != null)
            {
                standaloneModule.enabled = false;
                if (hadLegacyModule)
                {
                    Debug.LogWarning("[UIINPUT] Disabled legacy StandaloneInputModule to prevent conflict");
                }
            }

            // Ensure InputSystemUIInputModule exists
            var inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                Debug.Log("[UIINPUT] Adding InputSystemUIInputModule...");
                inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            // Ensure the module is enabled
            if (!inputModule.enabled)
            {
                inputModule.enabled = true;
                Debug.Log("[UIINPUT] Enabled InputSystemUIInputModule");
            }

            // Load or create InputActionAsset
            InputActionAsset actionsAsset = TryLoadUIActionsAsset();

            if (actionsAsset == null)
            {
                Debug.LogWarning("[UIINPUT] Resources load failed, creating fallback actions at runtime...");
                actionsAsset = CreateFallbackUIActions();
            }

            if (actionsAsset != null)
            {
                // Assign actions asset
                inputModule.actionsAsset = actionsAsset;
                Debug.Log($"[UIINPUT] Assigned actionsAsset: {actionsAsset.name}");

                // CRITICAL: Explicitly assign action references to the module
                AssignActionsToModule(inputModule, actionsAsset);

                // CRITICAL: Enable the action asset so it processes input
                actionsAsset.Enable();
                Debug.Log("[UIINPUT] Actions ENABLED");
            }
            else
            {
                Debug.LogError("[UIINPUT] CRITICAL: Failed to create or load InputActionAsset. UI input will NOT work!");
            }

            // Log comprehensive status for debugging (especially via adb logcat)
            LogFinalStatus(inputModule, hadLegacyModule);

            // Auto-attach UIRaycastProbe in debug builds
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            SpawnUIRaycastProbe();
#endif
        }

        private static InputActionAsset TryLoadUIActionsAsset()
        {
            // Try loading from Resources
            Debug.Log($"[UIINPUT] Attempting Resources.Load<InputActionAsset>(\"{ResourcePath}\")...");

            InputActionAsset asset = Resources.Load<InputActionAsset>(ResourcePath);

            if (asset != null)
            {
                Debug.Log($"[UIINPUT] Successfully loaded InputActionAsset from Resources: {asset.name}");

                // Verify the asset has the UI action map
                var uiMap = asset.FindActionMap("UI");
                if (uiMap == null)
                {
                    Debug.LogWarning("[UIINPUT] Loaded asset has no 'UI' action map!");
                    return null;
                }

                var pointAction = uiMap.FindAction("Point");
                var clickAction = uiMap.FindAction("Click");

                if (pointAction == null || clickAction == null)
                {
                    Debug.LogWarning("[UIINPUT] Loaded asset missing Point or Click actions!");
                    return null;
                }

                Debug.Log($"[UIINPUT] Asset verified: UI map has Point and Click actions");
                return asset;
            }

            Debug.LogWarning($"[UIINPUT] Resources.Load returned null for path: {ResourcePath}");
            return null;
        }

        private static void LogFinalStatus(InputSystemUIInputModule module, bool hadLegacyModule)
        {
            // Comprehensive status log for adb logcat debugging
            bool hasAsset = module.actionsAsset != null;
            bool hasPoint = module.point?.action != null;
            bool hasClick = module.leftClick?.action != null;
            bool pointEnabled = hasPoint && module.point.action.enabled;
            bool clickEnabled = hasClick && module.leftClick.action.enabled;
            bool moduleEnabled = module.enabled;

            Debug.Log("[UIINPUT] ========================================");
            Debug.Log("[UIINPUT] === FINAL UI INPUT STATUS ===");
            Debug.Log($"[UIINPUT] actionsAsset={( hasAsset ? module.actionsAsset.name : "NULL" )}");
            Debug.Log($"[UIINPUT] pointNull={!hasPoint}, clickNull={!hasClick}");
            Debug.Log($"[UIINPUT] pointEnabled={pointEnabled}, clickEnabled={clickEnabled}");
            Debug.Log($"[UIINPUT] moduleEnabled={moduleEnabled}, legacyDisabled={hadLegacyModule}");

            if (hasPoint)
                Debug.Log($"[UIINPUT] point action: {module.point.action.name}, bindings={module.point.action.bindings.Count}");
            if (hasClick)
                Debug.Log($"[UIINPUT] click action: {module.leftClick.action.name}, bindings={module.leftClick.action.bindings.Count}");

            // Additional action status
            Debug.Log($"[UIINPUT] move: {(module.move?.action != null ? "OK" : "NULL")}");
            Debug.Log($"[UIINPUT] submit: {(module.submit?.action != null ? "OK" : "NULL")}");
            Debug.Log($"[UIINPUT] cancel: {(module.cancel?.action != null ? "OK" : "NULL")}");

            if (hasAsset && hasPoint && hasClick && pointEnabled && clickEnabled && moduleEnabled)
            {
                Debug.Log("[UIINPUT] === UI INPUT READY - Touch/Click SHOULD work ===");
            }
            else
            {
                Debug.LogError("[UIINPUT] === UI INPUT INCOMPLETE - Touch may NOT work ===");
            }
            Debug.Log("[UIINPUT] ========================================");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static void SpawnUIRaycastProbe()
        {
            // Check if probe already exists
            if (Object.FindObjectOfType<ShadowingTutor.Diagnostics.UIRaycastProbe>() != null)
                return;

            var probeGO = new GameObject("UIRaycastProbe");
            probeGO.AddComponent<ShadowingTutor.Diagnostics.UIRaycastProbe>();
            Object.DontDestroyOnLoad(probeGO);
            Debug.Log("[UIINPUT] UIRaycastProbe auto-spawned for debugging");
        }
#endif

        private static InputActionAsset CreateFallbackUIActions()
        {
            try
            {
                var asset = ScriptableObject.CreateInstance<InputActionAsset>();
                asset.name = "UIActions_Fallback";

                // Create UI action map
                var uiMap = asset.AddActionMap("UI");

                // Point action (for pointer position) - PassThrough for continuous position updates
                var pointAction = uiMap.AddAction("Point", InputActionType.PassThrough);
                pointAction.AddBinding("<Pointer>/position");
                pointAction.AddBinding("<Touchscreen>/touch*/position");

                // Click action (for button presses) - PassThrough to catch press/release
                var clickAction = uiMap.AddAction("Click", InputActionType.PassThrough);
                clickAction.AddBinding("<Pointer>/press");
                clickAction.AddBinding("<Touchscreen>/touch*/press");
                clickAction.AddBinding("<Mouse>/leftButton");

                // RightClick
                var rightClickAction = uiMap.AddAction("RightClick", InputActionType.Button);
                rightClickAction.AddBinding("<Mouse>/rightButton");

                // MiddleClick
                var middleClickAction = uiMap.AddAction("MiddleClick", InputActionType.Button);
                middleClickAction.AddBinding("<Mouse>/middleButton");

                // ScrollWheel
                var scrollAction = uiMap.AddAction("ScrollWheel", InputActionType.PassThrough);
                scrollAction.AddBinding("<Mouse>/scroll");

                // Navigate - PassThrough for continuous input
                var navigateAction = uiMap.AddAction("Navigate", InputActionType.PassThrough);
                navigateAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w")
                    .With("Up", "<Keyboard>/upArrow")
                    .With("Down", "<Keyboard>/s")
                    .With("Down", "<Keyboard>/downArrow")
                    .With("Left", "<Keyboard>/a")
                    .With("Left", "<Keyboard>/leftArrow")
                    .With("Right", "<Keyboard>/d")
                    .With("Right", "<Keyboard>/rightArrow");
                navigateAction.AddBinding("<Gamepad>/leftStick");

                // Submit
                var submitAction = uiMap.AddAction("Submit", InputActionType.Button);
                submitAction.AddBinding("<Keyboard>/enter");
                submitAction.AddBinding("<Keyboard>/space");
                submitAction.AddBinding("<Gamepad>/buttonSouth");

                // Cancel
                var cancelAction = uiMap.AddAction("Cancel", InputActionType.Button);
                cancelAction.AddBinding("<Keyboard>/escape");
                cancelAction.AddBinding("<Gamepad>/buttonEast");

                Debug.Log("[UIINPUT] Fallback InputActionAsset created with 8 actions");
                return asset;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UIINPUT] Failed to create fallback actions: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        private static void AssignActionsToModule(InputSystemUIInputModule module, InputActionAsset asset)
        {
            // Find the UI action map
            var uiMap = asset.FindActionMap("UI");
            if (uiMap == null)
            {
                Debug.LogError("[UIINPUT] No 'UI' action map found in asset!");
                return;
            }

            Debug.Log("[UIINPUT] Assigning action references to module...");

            // Assign actions to module using InputActionReference
            try
            {
                var pointAction = uiMap.FindAction("Point");
                var clickAction = uiMap.FindAction("Click");
                var scrollAction = uiMap.FindAction("ScrollWheel");
                var navigateAction = uiMap.FindAction("Navigate");
                var submitAction = uiMap.FindAction("Submit");
                var cancelAction = uiMap.FindAction("Cancel");
                var middleClickAction = uiMap.FindAction("MiddleClick");
                var rightClickAction = uiMap.FindAction("RightClick");

                // Create references and assign - CRITICAL for mobile
                if (pointAction != null)
                {
                    module.point = InputActionReference.Create(pointAction);
                    Debug.Log($"[UIINPUT] Assigned point: {pointAction.name} with {pointAction.bindings.Count} bindings");
                }
                else
                {
                    Debug.LogError("[UIINPUT] Point action not found in asset!");
                }

                if (clickAction != null)
                {
                    module.leftClick = InputActionReference.Create(clickAction);
                    Debug.Log($"[UIINPUT] Assigned leftClick: {clickAction.name} with {clickAction.bindings.Count} bindings");
                }
                else
                {
                    Debug.LogError("[UIINPUT] Click action not found in asset!");
                }

                if (scrollAction != null)
                    module.scrollWheel = InputActionReference.Create(scrollAction);
                if (navigateAction != null)
                    module.move = InputActionReference.Create(navigateAction);
                if (submitAction != null)
                    module.submit = InputActionReference.Create(submitAction);
                if (cancelAction != null)
                    module.cancel = InputActionReference.Create(cancelAction);
                if (middleClickAction != null)
                    module.middleClick = InputActionReference.Create(middleClickAction);
                if (rightClickAction != null)
                    module.rightClick = InputActionReference.Create(rightClickAction);

                Debug.Log("[UIINPUT] Action references assigned successfully");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UIINPUT] Error assigning actions to module: {e.Message}\n{e.StackTrace}");
            }
        }
    }
}
