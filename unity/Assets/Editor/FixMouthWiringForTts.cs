using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Editor tool to fix TTS mouth animation wiring.
    /// Ensures correct face mesh is selected (NOT teeth) and audio source is wired.
    ///
    /// FRONTEND-ONLY: Does not touch backend/server/API code.
    /// </summary>
    public class FixMouthWiringForTts
    {
        [MenuItem("Tools/Character Setup/Fix Mouth Wiring For TTS")]
        public static void FixMouthWiring()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Error", "Exit Play Mode first!\n\nThis tool configures scene objects.", "OK");
                return;
            }

            Debug.Log("==========================================================");
            Debug.Log("       FIX MOUTH WIRING FOR TTS");
            Debug.Log("==========================================================\n");

            bool success = true;
            string summary = "";

            // Step 1: Find or create TtsMouthController
            Debug.Log("--- Step 1: Ensure TtsMouthController ---");
            TtsMouthController mouthController = Object.FindObjectOfType<TtsMouthController>();

            if (mouthController == null)
            {
                // Find character root to attach to
                Transform characterRoot = FindCharacterRoot();

                if (characterRoot != null)
                {
                    Undo.AddComponent<TtsMouthController>(characterRoot.gameObject);
                    mouthController = characterRoot.GetComponent<TtsMouthController>();
                    Debug.Log($"Created TtsMouthController on: {characterRoot.name}");
                    summary += "- Created TtsMouthController\n";
                }
                else
                {
                    // Create standalone
                    GameObject go = new GameObject("_TtsMouthController");
                    Undo.RegisterCreatedObjectUndo(go, "Create TtsMouthController");
                    mouthController = go.AddComponent<TtsMouthController>();
                    Debug.Log("Created standalone TtsMouthController GameObject");
                    summary += "- Created TtsMouthController (standalone)\n";
                }
            }
            else
            {
                Debug.Log($"TtsMouthController already exists on: {mouthController.gameObject.name}");
                summary += "- TtsMouthController: EXISTS\n";
            }

            // Step 2: Find TTS AudioSource
            Debug.Log("\n--- Step 2: Find TTS AudioSource ---");
            AudioSource ttsAudio = FindTtsAudioSource();

            if (ttsAudio != null)
            {
                // Wire to controller via SerializedObject
                SerializedObject so = new SerializedObject(mouthController);
                SerializedProperty audioProp = so.FindProperty("_ttsAudioSource");
                if (audioProp != null)
                {
                    audioProp.objectReferenceValue = ttsAudio;
                    so.ApplyModifiedProperties();
                }
                EditorUtility.SetDirty(mouthController);

                string clipName = ttsAudio.clip != null ? ttsAudio.clip.name : "null";
                Debug.Log($"TTS AudioSource wired: {GetFullPath(ttsAudio.transform)} clip={clipName}");
                summary += $"- TTS AudioSource: {ttsAudio.gameObject.name}\n";
            }
            else
            {
                Debug.LogWarning("TTS AudioSource NOT FOUND - will be resolved at runtime");
                summary += "- TTS AudioSource: NOT FOUND (runtime)\n";
                success = false;
            }

            // Step 3: Find character and face renderer
            Debug.Log("\n--- Step 3: Find Face Renderer ---");
            Transform charRoot = FindCharacterRoot();
            SkinnedMeshRenderer faceRenderer = null;
            int blendshapeIndex = -1;
            string blendshapeName = "";

            if (charRoot != null)
            {
                Debug.Log($"Character root: {GetFullPath(charRoot)}");

                // Find best face renderer
                faceRenderer = FindBestFaceRenderer(charRoot);

                if (faceRenderer != null)
                {
                    // Find best blendshape
                    blendshapeIndex = FindBestMouthBlendshape(faceRenderer, out blendshapeName);

                    // Wire to controller
                    SerializedObject so = new SerializedObject(mouthController);

                    SerializedProperty faceProp = so.FindProperty("_faceRenderer");
                    if (faceProp != null)
                    {
                        faceProp.objectReferenceValue = faceRenderer;
                    }

                    SerializedProperty indexProp = so.FindProperty("_mouthBlendshapeIndex");
                    if (indexProp != null)
                    {
                        indexProp.intValue = blendshapeIndex;
                    }

                    SerializedProperty nameProp = so.FindProperty("_selectedBlendshapeName");
                    if (nameProp != null)
                    {
                        nameProp.stringValue = blendshapeName;
                    }

                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(mouthController);

                    int blendCount = faceRenderer.sharedMesh != null ? faceRenderer.sharedMesh.blendShapeCount : 0;
                    Debug.Log($"Face renderer wired: {GetFullPath(faceRenderer.transform)} blendShapeCount={blendCount}");
                    summary += $"- Face Renderer: {faceRenderer.name}\n";

                    if (blendshapeIndex >= 0)
                    {
                        Debug.Log($"Blendshape wired: {blendshapeName} index={blendshapeIndex}");
                        summary += $"- Blendshape: {blendshapeName} (index {blendshapeIndex})\n";
                    }
                    else
                    {
                        Debug.LogWarning("No suitable mouth blendshape found");
                        summary += "- Blendshape: NOT FOUND\n";
                    }
                }
                else
                {
                    Debug.LogWarning("Face renderer NOT FOUND");
                    summary += "- Face Renderer: NOT FOUND\n";
                    success = false;
                }

                // Step 4: Find jaw bone
                Debug.Log("\n--- Step 4: Find Jaw Bone ---");
                Transform jawBone = FindJawBone(charRoot);

                if (jawBone != null)
                {
                    SerializedObject so = new SerializedObject(mouthController);
                    SerializedProperty jawProp = so.FindProperty("_jawBone");
                    if (jawProp != null)
                    {
                        jawProp.objectReferenceValue = jawBone;
                        so.ApplyModifiedProperties();
                    }
                    EditorUtility.SetDirty(mouthController);

                    Debug.Log($"Jaw bone wired: {GetFullPath(jawBone)}");
                    summary += $"- Jaw Bone: {jawBone.name}\n";
                }
                else
                {
                    Debug.Log("Jaw bone not found (blendshapes will be used)");
                    summary += "- Jaw Bone: NOT FOUND\n";
                }
            }
            else
            {
                Debug.LogWarning("Character root NOT FOUND");
                summary += "- Character: NOT FOUND\n";
                success = false;
            }

            // Step 5: Disable old LipSyncController to avoid conflicts
            Debug.Log("\n--- Step 5: Check for Conflicting Controllers ---");
            LipSyncController oldController = Object.FindObjectOfType<LipSyncController>();
            if (oldController != null && oldController.enabled)
            {
                Debug.Log($"Found old LipSyncController on {oldController.gameObject.name} - will be disabled during TTS playback by TtsMouthController");
                summary += "- Old LipSyncController: FOUND (will be managed)\n";
            }

            // Save scene
            Debug.Log("\n--- Step 6: Save ---");
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log("\n==========================================================");
            Debug.Log("       FIX COMPLETE");
            Debug.Log("==========================================================");
            Debug.Log("\nSUMMARY:");
            Debug.Log(summary);

            if (success)
            {
                Debug.Log("\nVERIFICATION:");
                Debug.Log("1. Enter Play Mode");
                Debug.Log("2. Trigger TTS speech");
                Debug.Log("3. Console should show:");
                Debug.Log("   - [Mouth] TTS AudioSource: <path>");
                Debug.Log("   - [Mouth] FaceRenderer selected: <path> (NOT Teeth)");
                Debug.Log("   - [Mouth] Blendshape/Jaw bone selected");
                Debug.Log("4. Mouth should visibly animate during speech");
                Debug.Log("5. Mouth should close when speech ends");
            }
            else
            {
                Debug.LogWarning("\nSome components were not found. They will be auto-wired at runtime.");
            }
        }

        [MenuItem("Tools/Character Setup/Verify Mouth Wiring")]
        public static void VerifyMouthWiring()
        {
            Debug.Log("=== MOUTH WIRING VERIFICATION ===\n");

            // Check TtsMouthController
            TtsMouthController mouthController = Object.FindObjectOfType<TtsMouthController>();
            Debug.Log($"1. TtsMouthController: {(mouthController != null ? "PRESENT on " + mouthController.gameObject.name : "MISSING")}");

            if (mouthController != null)
            {
                SerializedObject so = new SerializedObject(mouthController);

                var audioProp = so.FindProperty("_ttsAudioSource");
                var audioSource = audioProp?.objectReferenceValue as AudioSource;
                Debug.Log($"   - TTS AudioSource: {(audioSource != null ? audioSource.gameObject.name : "NOT SET")}");

                var faceProp = so.FindProperty("_faceRenderer");
                var faceRenderer = faceProp?.objectReferenceValue as SkinnedMeshRenderer;
                Debug.Log($"   - Face Renderer: {(faceRenderer != null ? faceRenderer.name : "NOT SET")}");

                var indexProp = so.FindProperty("_mouthBlendshapeIndex");
                Debug.Log($"   - Blendshape Index: {indexProp?.intValue ?? -1}");

                var nameProp = so.FindProperty("_selectedBlendshapeName");
                Debug.Log($"   - Blendshape Name: {nameProp?.stringValue ?? "NOT SET"}");

                var jawProp = so.FindProperty("_jawBone");
                var jawBone = jawProp?.objectReferenceValue as Transform;
                Debug.Log($"   - Jaw Bone: {(jawBone != null ? jawBone.name : "NOT SET")}");
            }

            // Check for conflicts
            Debug.Log("\n2. Potential Conflicts:");
            LipSyncController oldController = Object.FindObjectOfType<LipSyncController>();
            if (oldController != null)
            {
                Debug.Log($"   - LipSyncController found on: {oldController.gameObject.name}");
                Debug.Log($"     (Will be disabled during TTS by TtsMouthController)");
            }
            else
            {
                Debug.Log("   - No conflicting LipSyncController found");
            }

            // Check TtsPlayer
            Debug.Log("\n3. TtsPlayer:");
            TtsPlayer ttsPlayer = Object.FindObjectOfType<TtsPlayer>();
            if (ttsPlayer != null)
            {
                Debug.Log($"   - Found on: {ttsPlayer.gameObject.name}");
                var audio = ttsPlayer.GetComponent<AudioSource>();
                Debug.Log($"   - AudioSource: {(audio != null ? "PRESENT" : "MISSING")}");
            }
            else
            {
                Debug.Log("   - NOT FOUND");
            }

            Debug.Log("\n=== END VERIFICATION ===");
        }

        private static Transform FindCharacterRoot()
        {
            string[] paths = { "Avatar/CharacterModel", "CharacterModel", "Avatar" };

            foreach (string path in paths)
            {
                GameObject found = GameObject.Find(path);
                if (found != null) return found.transform;
            }

            // Try to find by CC_Base_Body
            SkinnedMeshRenderer[] renderers = Object.FindObjectsOfType<SkinnedMeshRenderer>();
            foreach (var smr in renderers)
            {
                if (smr.name == "CC_Base_Body")
                {
                    Transform t = smr.transform;
                    while (t.parent != null)
                    {
                        if (t.name == "CharacterModel" || t.name == "Avatar")
                            return t;
                        t = t.parent;
                    }
                    return smr.transform.root;
                }
            }

            return null;
        }

        private static AudioSource FindTtsAudioSource()
        {
            TtsPlayer ttsPlayer = Object.FindObjectOfType<TtsPlayer>();
            if (ttsPlayer != null)
            {
                AudioSource audio = ttsPlayer.GetComponent<AudioSource>();
                if (audio != null) return audio;

                audio = ttsPlayer.GetComponentInChildren<AudioSource>();
                if (audio != null) return audio;
            }

            // Search by name
            AudioSource[] allAudio = Object.FindObjectsOfType<AudioSource>();
            foreach (var audio in allAudio)
            {
                if (audio.gameObject.name.Contains("TTS") || audio.gameObject.name.Contains("Tts"))
                {
                    return audio;
                }
            }

            return null;
        }

        private static readonly string[] ExcludedNames = {
            "teeth", "tooth", "tongue", "eye", "eyelash", "brow", "hair", "scalp", "cap"
        };

        private static readonly string[] PreferredNames = { "face", "head", "body" };

        private static SkinnedMeshRenderer FindBestFaceRenderer(Transform root)
        {
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            SkinnedMeshRenderer best = null;
            int bestScore = int.MinValue;

            foreach (var smr in renderers)
            {
                string nameLower = smr.name.ToLower();

                // Check exclusions
                bool excluded = false;
                foreach (string excl in ExcludedNames)
                {
                    if (nameLower.Contains(excl))
                    {
                        excluded = true;
                        break;
                    }
                }
                if (excluded) continue;

                int score = 0;

                // +100 if name contains preferred
                foreach (string pref in PreferredNames)
                {
                    if (nameLower.Contains(pref))
                    {
                        score += 100;
                        break;
                    }
                }

                // +50 if has blendshapes
                if (smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
                {
                    score += 50;

                    // +5 per mouth blendshape
                    for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                    {
                        string bsName = smr.sharedMesh.GetBlendShapeName(i).ToLower();
                        if (bsName.Contains("viseme") || bsName.Contains("jaw") ||
                            bsName.Contains("mouth") || bsName.Contains("lip"))
                        {
                            score += 5;
                        }
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = smr;
                }
            }

            return best;
        }

        private static readonly string[][] BlendshapePriority = {
            new[] { "viseme_aa", "viseme_a" },
            new[] { "jawopen", "jaw_open" },
            new[] { "mouthopen", "mouth_open" },
            new[] { "lipopen", "open" }
        };

        private static int FindBestMouthBlendshape(SkinnedMeshRenderer renderer, out string name)
        {
            name = "";
            if (renderer == null || renderer.sharedMesh == null)
                return -1;

            int count = renderer.sharedMesh.blendShapeCount;

            foreach (var patterns in BlendshapePriority)
            {
                for (int i = 0; i < count; i++)
                {
                    string bsName = renderer.sharedMesh.GetBlendShapeName(i).ToLower();

                    foreach (string pattern in patterns)
                    {
                        if (bsName.Contains(pattern))
                        {
                            name = renderer.sharedMesh.GetBlendShapeName(i);
                            return i;
                        }
                    }
                }
            }

            return -1;
        }

        private static Transform FindJawBone(Transform root)
        {
            string[] jawNames = { "CC_Base_JawRoot", "JawRoot", "CC_Base_UpperJaw", "Jaw" };

            foreach (string name in jawNames)
            {
                Transform found = FindBoneRecursive(root, name);
                if (found != null) return found;
            }

            return null;
        }

        private static Transform FindBoneRecursive(Transform parent, string name)
        {
            if (parent.name == name)
                return parent;

            foreach (Transform child in parent)
            {
                Transform found = FindBoneRecursive(child, name);
                if (found != null) return found;
            }
            return null;
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
