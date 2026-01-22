#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Editor script that ensures UIActions.asset exists in Resources/Input.
/// Runs on editor load and creates the asset if missing.
/// </summary>
[InitializeOnLoad]
public static class EnsureUIActionsAsset
{
    private const string AssetPath = "Assets/Resources/Input/UIActions.asset";
    private const string ResourcePath = "Input/UIActions";

    static EnsureUIActionsAsset()
    {
        // Delay to ensure AssetDatabase is ready
        EditorApplication.delayCall += EnsureAssetExists;
    }

    [MenuItem("Tools/Input/Regenerate UI Actions Asset")]
    public static void RegenerateAsset()
    {
        if (System.IO.File.Exists(AssetPath))
        {
            AssetDatabase.DeleteAsset(AssetPath);
        }
        EnsureAssetExists();
        Debug.Log("[EnsureUIActionsAsset] Asset regenerated at: " + AssetPath);
    }

    private static void EnsureAssetExists()
    {
        // Check if asset already exists
        var existing = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
        if (existing != null)
        {
            return; // Asset exists, nothing to do
        }

        // Ensure directory exists
        string dir = System.IO.Path.GetDirectoryName(AssetPath);
        if (!System.IO.Directory.Exists(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }

        // Create the InputActionAsset
        var asset = ScriptableObject.CreateInstance<InputActionAsset>();

        // Add UI action map
        var uiMap = asset.AddActionMap("UI");

        // Point (Value/Vector2) - pointer position
        var pointAction = uiMap.AddAction("Point", InputActionType.PassThrough, expectedControlLayout: "Vector2");
        pointAction.AddBinding("<Pointer>/position");
        pointAction.AddBinding("<Touchscreen>/touch*/position");

        // Click (Button) - primary click/tap
        var clickAction = uiMap.AddAction("Click", InputActionType.PassThrough, expectedControlLayout: "Button");
        clickAction.AddBinding("<Pointer>/press");
        clickAction.AddBinding("<Touchscreen>/touch*/press");
        clickAction.AddBinding("<Mouse>/leftButton");

        // RightClick (Button)
        var rightClickAction = uiMap.AddAction("RightClick", InputActionType.PassThrough, expectedControlLayout: "Button");
        rightClickAction.AddBinding("<Mouse>/rightButton");

        // MiddleClick (Button)
        var middleClickAction = uiMap.AddAction("MiddleClick", InputActionType.PassThrough, expectedControlLayout: "Button");
        middleClickAction.AddBinding("<Mouse>/middleButton");

        // ScrollWheel (Value/Vector2)
        var scrollAction = uiMap.AddAction("ScrollWheel", InputActionType.PassThrough, expectedControlLayout: "Vector2");
        scrollAction.AddBinding("<Mouse>/scroll");

        // Navigate (Value/Vector2) - keyboard/gamepad navigation
        var navigateAction = uiMap.AddAction("Navigate", InputActionType.PassThrough, expectedControlLayout: "Vector2");

        // Keyboard WASD composite
        navigateAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/s")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/a")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/d")
            .With("Right", "<Keyboard>/rightArrow");

        // Gamepad left stick
        navigateAction.AddBinding("<Gamepad>/leftStick");

        // Submit (Button)
        var submitAction = uiMap.AddAction("Submit", InputActionType.Button, expectedControlLayout: "Button");
        submitAction.AddBinding("<Keyboard>/enter");
        submitAction.AddBinding("<Keyboard>/space");
        submitAction.AddBinding("<Gamepad>/buttonSouth");

        // Cancel (Button)
        var cancelAction = uiMap.AddAction("Cancel", InputActionType.Button, expectedControlLayout: "Button");
        cancelAction.AddBinding("<Keyboard>/escape");
        cancelAction.AddBinding("<Gamepad>/buttonEast");

        // Save the asset
        AssetDatabase.CreateAsset(asset, AssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[EnsureUIActionsAsset] Created UI Actions asset at: " + AssetPath);
    }
}
#endif
