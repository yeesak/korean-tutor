using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using System.IO;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Editor tool to apply persistent front-end visual fixes:
    /// 1. Set camera FOV to 14.69837
    /// 2. Disable CC_Base_EyeOcclusion in scene and prefabs
    /// 3. Hide red UI overlay
    /// 4. Update PortraitCameraFramer defaults
    ///
    /// FRONTEND-ONLY: Does not touch backend/server/API code.
    /// </summary>
    public class ApplyFrontEndVisualFixes
    {
        private const float TARGET_FOV = 14.69837f;
        private static readonly Vector3 TARGET_CAMERA_POS = new Vector3(4.76837f, 1.59692f, -2.25214f);

        [MenuItem("Tools/Visual Fixes/Apply Front-End Visual Fixes (Save Assets)")]
        public static void ApplyAllFixes()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Error", "Exit Play Mode first!\n\nChanges must be made in Edit Mode to persist.", "OK");
                return;
            }

            Debug.Log("==========================================================");
            Debug.Log("       APPLYING PERSISTENT FRONT-END VISUAL FIXES");
            Debug.Log("==========================================================\n");

            int totalChanges = 0;

            // 1. Apply camera fixes to scene
            Debug.Log("--- STEP 1: Camera Fixes ---");
            totalChanges += ApplyCameraFixes();

            // 2. Update PortraitCameraFramer
            Debug.Log("\n--- STEP 2: PortraitCameraFramer Fixes ---");
            totalChanges += UpdatePortraitCameraFramer();

            // 3. Disable EyeOcclusion in scene
            Debug.Log("\n--- STEP 3: Disable EyeOcclusion (Scene) ---");
            totalChanges += DisableEyeOcclusionInScene();

            // 4. Disable EyeOcclusion in all prefabs
            Debug.Log("\n--- STEP 4: Disable EyeOcclusion (Prefabs) ---");
            totalChanges += DisableEyeOcclusionInPrefabs();

            // 5. Hide red UI overlay
            Debug.Log("\n--- STEP 5: Hide Red UI Overlay ---");
            totalChanges += HideRedUIOverlayInScene();

            // 6. Save everything
            Debug.Log("\n--- STEP 6: Saving Assets ---");
            SaveAll();

            Debug.Log("\n==========================================================");
            Debug.Log($"       FIXES COMPLETE - {totalChanges} changes made");
            Debug.Log("==========================================================");
            Debug.Log("\nVERIFICATION:");
            Debug.Log("1. Enter Play Mode - confirm FOV = 14.69837, eyes visible, no red overlay");
            Debug.Log("2. Exit Play Mode - confirm settings persist");
            Debug.Log("3. If FOV reverts, the CameraFOVLock component will force it back");
        }

        [MenuItem("Tools/Visual Fixes/Step 1 - Fix Camera (FOV 14.69837)")]
        public static int ApplyCameraFixes()
        {
            if (Application.isPlaying) return 0;

            int changes = 0;

            // Find Main Camera
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                GameObject camObj = GameObject.Find("Main Camera");
                if (camObj != null)
                    mainCam = camObj.GetComponent<Camera>();
            }

            if (mainCam == null)
            {
                Debug.LogError("Main Camera not found!");
                return 0;
            }

            // Set FOV
            if (mainCam.fieldOfView != TARGET_FOV)
            {
                Undo.RecordObject(mainCam, "Set Camera FOV");
                float oldFOV = mainCam.fieldOfView;
                mainCam.fieldOfView = TARGET_FOV;
                EditorUtility.SetDirty(mainCam);
                Debug.Log($"Camera FOV: {oldFOV} -> {TARGET_FOV}");
                changes++;
            }
            else
            {
                Debug.Log($"Camera FOV already at {TARGET_FOV}");
            }

            // Add or update CameraFOVLock component
            var fovLock = mainCam.GetComponent<CameraFOVLock>();
            if (fovLock == null)
            {
                Undo.AddComponent<CameraFOVLock>(mainCam.gameObject);
                fovLock = mainCam.GetComponent<CameraFOVLock>();
                Debug.Log("Added CameraFOVLock component");
                changes++;
            }

            if (fovLock != null)
            {
                Undo.RecordObject(fovLock, "Update FOV Lock");
                fovLock.targetFOV = TARGET_FOV;
                fovLock.lockEnabled = true;
                EditorUtility.SetDirty(fovLock);
            }

            // Mark scene dirty
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            return changes;
        }

        [MenuItem("Tools/Visual Fixes/Step 2 - Fix PortraitCameraFramer")]
        public static int UpdatePortraitCameraFramer()
        {
            if (Application.isPlaying) return 0;

            int changes = 0;

            // Find PortraitCameraFramer in scene
            PortraitCameraFramer framer = Object.FindObjectOfType<PortraitCameraFramer>();
            if (framer != null)
            {
                // Use reflection to update the serialized field
                SerializedObject so = new SerializedObject(framer);
                SerializedProperty fovProp = so.FindProperty("_portraitFOV");

                if (fovProp != null && fovProp.floatValue != TARGET_FOV)
                {
                    float oldValue = fovProp.floatValue;
                    fovProp.floatValue = TARGET_FOV;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(framer);
                    Debug.Log($"PortraitCameraFramer._portraitFOV: {oldValue} -> {TARGET_FOV}");
                    changes++;
                }
                else if (fovProp != null)
                {
                    Debug.Log($"PortraitCameraFramer._portraitFOV already at {TARGET_FOV}");
                }

                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
            else
            {
                Debug.Log("No PortraitCameraFramer found in scene");
            }

            // Also update the script default value
            UpdatePortraitCameraFramerScript();

            return changes;
        }

        private static void UpdatePortraitCameraFramerScript()
        {
            string scriptPath = "Assets/Scripts/PortraitCameraFramer.cs";
            if (!File.Exists(scriptPath))
            {
                Debug.Log($"Script not found at {scriptPath}");
                return;
            }

            string content = File.ReadAllText(scriptPath);

            // Replace the default FOV value
            string oldPattern = "_portraitFOV = 30f;";
            string newPattern = $"_portraitFOV = {TARGET_FOV}f;";

            if (content.Contains(oldPattern))
            {
                content = content.Replace(oldPattern, newPattern);
                File.WriteAllText(scriptPath, content);
                AssetDatabase.Refresh();
                Debug.Log($"Updated PortraitCameraFramer.cs default FOV from 30 to {TARGET_FOV}");
            }
            else if (content.Contains(newPattern))
            {
                Debug.Log($"PortraitCameraFramer.cs already has correct default FOV ({TARGET_FOV})");
            }
            else
            {
                Debug.LogWarning("Could not find _portraitFOV field in PortraitCameraFramer.cs");
            }
        }

        [MenuItem("Tools/Visual Fixes/Step 3 - Disable EyeOcclusion (Scene)")]
        public static int DisableEyeOcclusionInScene()
        {
            if (Application.isPlaying) return 0;

            int changes = 0;

            // Find all GameObjects including inactive
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

            foreach (GameObject obj in allObjects)
            {
                if (!obj.scene.IsValid()) continue;

                if (obj.name == "CC_Base_EyeOcclusion")
                {
                    if (obj.activeSelf)
                    {
                        Undo.RecordObject(obj, "Disable EyeOcclusion");
                        obj.SetActive(false);
                        EditorUtility.SetDirty(obj);
                        Debug.Log($"Disabled: {GetFullPath(obj.transform)}");
                        changes++;
                    }

                    // Disable renderers too
                    var smr = obj.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null && smr.enabled)
                    {
                        Undo.RecordObject(smr, "Disable EyeOcclusion Renderer");
                        smr.enabled = false;
                        EditorUtility.SetDirty(smr);
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"Total disabled in scene: {changes}");
            return changes;
        }

        [MenuItem("Tools/Visual Fixes/Step 4 - Disable EyeOcclusion (All Prefabs)")]
        public static int DisableEyeOcclusionInPrefabs()
        {
            if (Application.isPlaying) return 0;

            int changes = 0;

            // Find all prefab assets
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Art/Characters" });

            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // Load prefab contents
                GameObject prefabContents = PrefabUtility.LoadPrefabContents(path);
                if (prefabContents == null) continue;

                bool modified = false;

                // Find and disable EyeOcclusion
                Transform[] allTransforms = prefabContents.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in allTransforms)
                {
                    if (t.name == "CC_Base_EyeOcclusion")
                    {
                        if (t.gameObject.activeSelf)
                        {
                            t.gameObject.SetActive(false);
                            modified = true;
                            changes++;
                            Debug.Log($"Disabled in prefab {Path.GetFileName(path)}: {t.name}");
                        }

                        var smr = t.GetComponent<SkinnedMeshRenderer>();
                        if (smr != null && smr.enabled)
                        {
                            smr.enabled = false;
                            modified = true;
                        }
                    }
                }

                if (modified)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, path);
                }

                PrefabUtility.UnloadPrefabContents(prefabContents);
            }

            Debug.Log($"Total disabled in prefabs: {changes}");
            return changes;
        }

        [MenuItem("Tools/Visual Fixes/Step 5 - Hide Red UI Overlay")]
        public static int HideRedUIOverlayInScene()
        {
            if (Application.isPlaying) return 0;

            int changes = 0;

            string[] indicatorNames = new string[]
            {
                "RecordingIndicator",
                "RecordIndicator",
                "RedDot",
                "MicIndicator",
                "MouthOpenIndicator",
                "DebugOverlay",
                "SpeakingIndicator"
            };

            // Find all canvases
            Canvas[] canvases = Object.FindObjectsOfType<Canvas>(true);

            foreach (Canvas canvas in canvases)
            {
                // Check all Images
                Image[] images = canvas.GetComponentsInChildren<Image>(true);
                foreach (Image img in images)
                {
                    string nameLower = img.name.ToLower();
                    bool shouldDisable = false;

                    // Check by name
                    foreach (string indicator in indicatorNames)
                    {
                        if (nameLower.Contains(indicator.ToLower()))
                        {
                            shouldDisable = true;
                            break;
                        }
                    }

                    // Check if it's a small red element
                    if (!shouldDisable && IsRedColor(img.color))
                    {
                        if (img.rectTransform.sizeDelta.x < 100 && img.rectTransform.sizeDelta.y < 100)
                        {
                            if (nameLower.Contains("dot") || nameLower.Contains("indicator") ||
                                nameLower.Contains("record") || nameLower.Contains("mic"))
                            {
                                shouldDisable = true;
                            }
                        }
                    }

                    if (shouldDisable && img.gameObject.activeSelf)
                    {
                        Undo.RecordObject(img.gameObject, "Disable Red UI");
                        img.gameObject.SetActive(false);
                        EditorUtility.SetDirty(img.gameObject);
                        Debug.Log($"Disabled red UI: {GetFullPath(img.transform)}");
                        changes++;
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"Total UI elements disabled: {changes}");
            return changes;
        }

        private static void SaveAll()
        {
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("All changes saved to scene and assets");
        }

        private static bool IsRedColor(Color color)
        {
            return color.r > 0.7f && color.g < 0.4f && color.b < 0.4f && color.a > 0.1f;
        }

        private static string GetFullPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        [MenuItem("Tools/Visual Fixes/Verify Current State")]
        public static void VerifyState()
        {
            Debug.Log("=== VERIFICATION CHECK ===\n");

            // Camera
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                bool fovOK = Mathf.Approximately(mainCam.fieldOfView, TARGET_FOV);
                Debug.Log($"Camera FOV: {mainCam.fieldOfView} {(fovOK ? "(OK)" : "(WRONG!)")}");

                var fovLock = mainCam.GetComponent<CameraFOVLock>();
                Debug.Log($"FOV Lock: {(fovLock != null ? "Present" : "MISSING")}");
            }
            else
            {
                Debug.LogError("Main Camera not found!");
            }

            // EyeOcclusion
            int activeOcclusion = 0;
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (!obj.scene.IsValid()) continue;
                if (obj.name == "CC_Base_EyeOcclusion" && obj.activeSelf)
                {
                    activeOcclusion++;
                    Debug.LogWarning($"Active EyeOcclusion: {GetFullPath(obj.transform)}");
                }
            }
            Debug.Log($"Active EyeOcclusion count: {activeOcclusion} {(activeOcclusion == 0 ? "(OK)" : "(PROBLEM!)")}");

            // PortraitCameraFramer
            PortraitCameraFramer framer = Object.FindObjectOfType<PortraitCameraFramer>();
            if (framer != null)
            {
                SerializedObject so = new SerializedObject(framer);
                SerializedProperty fovProp = so.FindProperty("_portraitFOV");
                if (fovProp != null)
                {
                    bool fovOK = Mathf.Approximately(fovProp.floatValue, TARGET_FOV);
                    Debug.Log($"PortraitCameraFramer FOV: {fovProp.floatValue} {(fovOK ? "(OK)" : "(WRONG!)")}");
                }
            }

            Debug.Log("\n=== END VERIFICATION ===");
        }
    }
}
