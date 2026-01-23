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
            if (standaloneModule != null)
            {
                Debug.LogWarning("[UIINPUT] Found legacy StandaloneInputModule, disabling it...");
                standaloneModule.enabled = false;
            }

            // Ensure InputSystemUIInputModule exists
            var inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                Debug.Log("[UIINPUT] No InputSystemUIInputModule found, adding one...");
                inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            // Load InputActionAsset from Resources
            InputActionAsset actionsAsset = Resources.Load<InputActionAsset>(ResourcePath);

            if (actionsAsset == null)
            {
                Debug.LogWarning($"[UIINPUT] Could not load InputActionAsset from Resources/{ResourcePath}. Creating fallback actions...");
                actionsAsset = CreateFallbackUIActions();
            }

            if (actionsAsset != null)
            {
                // Assign actions asset if missing or different
                if (inputModule.actionsAsset == null || inputModule.actionsAsset.name != actionsAsset.name)
                {
                    Debug.Log($"[UIINPUT] Assigning actionsAsset: {actionsAsset.name}");
                    inputModule.actionsAsset = actionsAsset;
                }

                // Verify actions are assigned to the module
                AssignActionsToModule(inputModule, actionsAsset);

                // CRITICAL: Enable the action asset so it processes input
                actionsAsset.Enable();
                Debug.Log("[UIINPUT] Actions ENABLED");
            }
            else
            {
                Debug.LogError("[UIINPUT] CRITICAL: Failed to create or load InputActionAsset. UI input will NOT work!");
            }

            // Log final status
            string assetName = inputModule.actionsAsset != null ? inputModule.actionsAsset.name : "NONE";
            Debug.Log($"[UIINPUT] EventSystem ok, module=InputSystemUIInputModule, actionsAsset={assetName}");

            // Log action binding status
            LogModuleActionStatus(inputModule);

            // Auto-attach UIRaycastProbe in debug builds
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            SpawnUIRaycastProbe();
#endif
        }

        private static void LogModuleActionStatus(InputSystemUIInputModule module)
        {
            // Single clear startup log proving UI input is configured
            bool hasAsset = module.actionsAsset != null;
            bool hasPoint = module.point?.action != null;
            bool hasClick = module.leftClick?.action != null;
            bool pointEnabled = hasPoint && module.point.action.enabled;
            bool clickEnabled = hasClick && module.leftClick.action.enabled;

            Debug.Log($"[UIINPUT] === UI INPUT STATUS ===");
            Debug.Log($"[UIINPUT] actionsAsset: {(hasAsset ? module.actionsAsset.name : "NULL")}");
            Debug.Log($"[UIINPUT] point: {(hasPoint ? $"{module.point.action.name} (enabled={pointEnabled})" : "NULL")}");
            Debug.Log($"[UIINPUT] leftClick: {(hasClick ? $"{module.leftClick.action.name} (enabled={clickEnabled})" : "NULL")}");
            Debug.Log($"[UIINPUT] move: {(module.move?.action != null ? "OK" : "NULL")}");
            Debug.Log($"[UIINPUT] submit: {(module.submit?.action != null ? "OK" : "NULL")}");

            if (hasAsset && hasPoint && hasClick && pointEnabled && clickEnabled)
            {
                Debug.Log("[UIINPUT] === UI INPUT READY - Touch/Click should work ===");
            }
            else
            {
                Debug.LogWarning("[UIINPUT] === UI INPUT INCOMPLETE - Some actions missing or disabled ===");
            }
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

                Debug.Log("[UIINPUT] Fallback InputActionAsset created successfully with 8 actions");
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
                Debug.LogWarning("[UIINPUT] No 'UI' action map found in asset!");
                return;
            }

            // Assign actions to module using InputActionReference
            // These are the standard UI actions that InputSystemUIInputModule expects
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

                // Create references and assign
                if (pointAction != null)
                    module.point = InputActionReference.Create(pointAction);
                if (clickAction != null)
                    module.leftClick = InputActionReference.Create(clickAction);
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
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[UIINPUT] Error assigning actions to module: {e.Message}");
            }
        }
    }
}
