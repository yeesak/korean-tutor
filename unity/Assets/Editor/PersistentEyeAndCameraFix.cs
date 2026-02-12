using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Persistent fix for eye occlusion and camera FOV.
    /// All changes are made in Edit Mode and saved to scene/prefab.
    /// </summary>
    public class PersistentEyeAndCameraFix
    {
        private const float TARGET_FOV = 14.69837f;

        [MenuItem("Tools/Eye Doctor/APPLY PERSISTENT FIX (Occlusion + FOV 14.69837)")]
        public static void ApplyPersistentFix()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("EXIT PLAY MODE FIRST! Changes must be made in Edit Mode to persist.");
                return;
            }

            Debug.Log("=== APPLYING PERSISTENT FIX ===\n");
            Debug.Log("Step 1: Disabling CC_Base_EyeOcclusion objects...");

            int occlusionCount = DisableAllEyeOcclusion();

            Debug.Log("\nStep 2: Setting Main Camera FOV to 14.69837...");

            bool cameraFixed = SetMainCameraFOV();

            Debug.Log("\nStep 3: Adding FOV Lock component...");

            bool lockAdded = AddFOVLockComponent();

            Debug.Log("\nStep 4: Saving scene and assets...");

            // Mark scene dirty and save
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            Debug.Log("\n=== PERSISTENT FIX COMPLETE ===");
            Debug.Log($"- Eye Occlusion objects disabled: {occlusionCount}");
            Debug.Log($"- Camera FOV set to {TARGET_FOV}: {cameraFixed}");
            Debug.Log($"- FOV Lock component added: {lockAdded}");
            Debug.Log("\nVERIFICATION:");
            Debug.Log("1. Enter Play Mode - confirm eyes visible, FOV = 14.69837");
            Debug.Log("2. Exit Play Mode - confirm settings persist");
        }

        [MenuItem("Tools/Eye Doctor/Step 1: Disable Eye Occlusion (Scene)")]
        public static int DisableAllEyeOcclusion()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("In Play Mode - changes won't persist!");
            }

            int count = 0;

            // Find ALL objects named CC_Base_EyeOcclusion (including inactive)
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

            foreach (GameObject obj in allObjects)
            {
                // Skip assets (only process scene objects)
                if (!obj.scene.IsValid()) continue;

                if (obj.name == "CC_Base_EyeOcclusion")
                {
                    // Disable the GameObject
                    if (obj.activeSelf)
                    {
                        Undo.RecordObject(obj, "Disable EyeOcclusion");
                        obj.SetActive(false);
                        EditorUtility.SetDirty(obj);
                        Debug.Log($"Disabled GameObject: {GetFullPath(obj.transform)}");
                        count++;
                    }

                    // Also disable any renderers
                    var smr = obj.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null && smr.enabled)
                    {
                        Undo.RecordObject(smr, "Disable EyeOcclusion Renderer");
                        smr.enabled = false;
                        EditorUtility.SetDirty(smr);
                    }

                    var mr = obj.GetComponent<MeshRenderer>();
                    if (mr != null && mr.enabled)
                    {
                        Undo.RecordObject(mr, "Disable EyeOcclusion Renderer");
                        mr.enabled = false;
                        EditorUtility.SetDirty(mr);
                    }

                    // Check if this is a prefab instance and apply override
                    ApplyPrefabOverrideIfNeeded(obj);
                }
            }

            Debug.Log($"Total EyeOcclusion objects disabled: {count}");
            return count;
        }

        [MenuItem("Tools/Eye Doctor/Step 2: Set Camera FOV 14.69837")]
        public static bool SetMainCameraFOV()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("In Play Mode - changes won't persist!");
            }

            // Find Main Camera
            Camera mainCam = Camera.main;

            if (mainCam == null)
            {
                // Try to find by name
                GameObject camObj = GameObject.Find("Main Camera");
                if (camObj != null)
                {
                    mainCam = camObj.GetComponent<Camera>();
                }
            }

            if (mainCam == null)
            {
                Debug.LogError("Main Camera not found!");
                return false;
            }

            // Set FOV
            Undo.RecordObject(mainCam, "Set Camera FOV");
            mainCam.fieldOfView = TARGET_FOV;
            EditorUtility.SetDirty(mainCam);

            Debug.Log($"Main Camera FOV set to: {mainCam.fieldOfView}");

            // Check for PortraitCameraFramer or similar components
            var allComponents = mainCam.GetComponents<MonoBehaviour>();
            foreach (var comp in allComponents)
            {
                if (comp == null) continue;

                string typeName = comp.GetType().Name.ToLower();
                if (typeName.Contains("portrait") || typeName.Contains("framer") || typeName.Contains("fov"))
                {
                    Debug.LogWarning($"Found component that may override FOV: {comp.GetType().Name}");
                    Debug.LogWarning("Consider disabling it or using the FOV Lock component.");
                }
            }

            // Apply prefab override if camera is part of prefab
            ApplyPrefabOverrideIfNeeded(mainCam.gameObject);

            return true;
        }

        [MenuItem("Tools/Eye Doctor/Step 3: Add FOV Lock Component")]
        public static bool AddFOVLockComponent()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("In Play Mode - changes won't persist!");
            }

            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                GameObject camObj = GameObject.Find("Main Camera");
                if (camObj != null)
                {
                    mainCam = camObj.GetComponent<Camera>();
                }
            }

            if (mainCam == null)
            {
                Debug.LogError("Main Camera not found!");
                return false;
            }

            // Check if FOVLock already exists
            var existingLock = mainCam.GetComponent<CameraFOVLock>();
            if (existingLock != null)
            {
                Undo.RecordObject(existingLock, "Update FOV Lock");
                existingLock.targetFOV = TARGET_FOV;
                existingLock.enabled = true;
                EditorUtility.SetDirty(existingLock);
                Debug.Log("FOV Lock component already exists - updated target FOV");
                return true;
            }

            // Add new FOV Lock component
            Undo.AddComponent<CameraFOVLock>(mainCam.gameObject);
            var newLock = mainCam.GetComponent<CameraFOVLock>();
            if (newLock != null)
            {
                newLock.targetFOV = TARGET_FOV;
                EditorUtility.SetDirty(newLock);
                Debug.Log("Added CameraFOVLock component to Main Camera");

                ApplyPrefabOverrideIfNeeded(mainCam.gameObject);
                return true;
            }

            return false;
        }

        [MenuItem("Tools/Eye Doctor/VERIFY: Check Current State")]
        public static void VerifyCurrentState()
        {
            Debug.Log("=== VERIFICATION CHECK ===\n");

            // Check EyeOcclusion
            Debug.Log("1. EYE OCCLUSION STATUS:");
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            int activeOcclusion = 0;
            foreach (GameObject obj in allObjects)
            {
                if (!obj.scene.IsValid()) continue;
                if (obj.name == "CC_Base_EyeOcclusion")
                {
                    string status = obj.activeSelf ? "ACTIVE (PROBLEM!)" : "Disabled (OK)";
                    Debug.Log($"   {GetFullPath(obj.transform)}: {status}");
                    if (obj.activeSelf) activeOcclusion++;
                }
            }
            Debug.Log($"   Active EyeOcclusion count: {activeOcclusion}");

            // Check Camera FOV
            Debug.Log("\n2. CAMERA FOV STATUS:");
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                GameObject camObj = GameObject.Find("Main Camera");
                if (camObj != null) mainCam = camObj.GetComponent<Camera>();
            }

            if (mainCam != null)
            {
                bool fovCorrect = Mathf.Approximately(mainCam.fieldOfView, TARGET_FOV);
                string status = fovCorrect ? "CORRECT" : "WRONG!";
                Debug.Log($"   Current FOV: {mainCam.fieldOfView} ({status})");
                Debug.Log($"   Target FOV: {TARGET_FOV}");

                var fovLock = mainCam.GetComponent<CameraFOVLock>();
                if (fovLock != null)
                {
                    Debug.Log($"   FOV Lock: Present, enabled={fovLock.enabled}, target={fovLock.targetFOV}");
                }
                else
                {
                    Debug.Log("   FOV Lock: NOT PRESENT");
                }
            }
            else
            {
                Debug.LogError("   Main Camera not found!");
            }

            Debug.Log("\n=== END VERIFICATION ===");
        }

        private static void ApplyPrefabOverrideIfNeeded(GameObject obj)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(obj))
            {
                // Get the root of the prefab instance
                GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(obj);
                if (prefabRoot != null)
                {
                    Debug.Log($"Applying prefab override for: {obj.name}");
                    PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
                }
            }
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
    }
}
