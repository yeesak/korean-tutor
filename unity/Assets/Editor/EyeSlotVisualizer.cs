using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Visualizes which material slot renders the visible eye surface by assigning
    /// distinct colored Unlit materials to each slot.
    /// </summary>
    public class EyeSlotVisualizer
    {
        private const string REPORT_PATH = "Assets/Temp/eye_slot_visualizer.txt";

        // Store original materials for restoration
        private static Dictionary<Renderer, Material[]> _originalMaterials = new Dictionary<Renderer, Material[]>();
        private static bool _isVisualizerActive = false;

        private static readonly Color[] SlotColors = new Color[]
        {
            Color.red,      // Slot 0
            Color.green,    // Slot 1
            Color.blue,     // Slot 2
            Color.yellow,   // Slot 3
            Color.cyan,     // Slot 4
            Color.magenta,  // Slot 5
            new Color(1f, 0.5f, 0f), // Slot 6 - Orange
            new Color(0.5f, 0f, 1f)  // Slot 7 - Purple
        };

        [MenuItem("Tools/Eye Doctor/Slot Visualizer/1) ACTIVATE Slot Colors")]
        public static void ActivateSlotVisualizer()
        {
            if (_isVisualizerActive)
            {
                Debug.LogWarning("Slot Visualizer is already active. Deactivate first.");
                return;
            }

            Debug.Log("=== EYE SLOT VISUALIZER ===\n");

            var sb = new StringBuilder();
            sb.AppendLine("=== EYE SLOT VISUALIZER REPORT ===");
            sb.AppendLine($"Timestamp: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Play Mode: {Application.isPlaying}");
            sb.AppendLine();
            sb.AppendLine("COLOR KEY:");
            sb.AppendLine("  Slot 0 = RED");
            sb.AppendLine("  Slot 1 = GREEN");
            sb.AppendLine("  Slot 2 = BLUE");
            sb.AppendLine("  Slot 3 = YELLOW");
            sb.AppendLine("  Slot 4 = CYAN");
            sb.AppendLine("  Slot 5 = MAGENTA");
            sb.AppendLine();

            // Find character
            GameObject character = FindActiveCharacter();
            if (character == null)
            {
                Debug.LogError("Cannot find character! Looking for Avatar/CharacterModel");
                return;
            }

            sb.AppendLine($"Character: {GetHierarchyPath(character.transform)}");
            sb.AppendLine();

            // Find eye renderers
            var renderers = character.GetComponentsInChildren<Renderer>(true);
            int processedCount = 0;

            Shader unlitColorShader = Shader.Find("Unlit/Color");
            if (unlitColorShader == null)
            {
                Debug.LogError("Cannot find Unlit/Color shader!");
                return;
            }

            sb.AppendLine("--- EYE RENDERERS ---");

            foreach (var renderer in renderers)
            {
                string nameLower = renderer.name.ToLowerInvariant();
                if (!nameLower.Contains("eye") && !nameLower.Contains("cornea"))
                    continue;

                sb.AppendLine($"\nRenderer: {renderer.name}");
                sb.AppendLine($"  Path: {GetHierarchyPath(renderer.transform)}");

                // Store original materials
                Material[] originalMats = Application.isPlaying ? renderer.materials : renderer.sharedMaterials;
                _originalMaterials[renderer] = originalMats;

                // Create visualization materials
                Material[] vizMats = new Material[originalMats.Length];
                for (int i = 0; i < originalMats.Length; i++)
                {
                    Material original = originalMats[i];
                    string originalName = original != null ? original.name : "NULL";

                    // Create colored material
                    Material colorMat = new Material(unlitColorShader);
                    Color slotColor = SlotColors[i % SlotColors.Length];
                    colorMat.color = slotColor;
                    colorMat.name = $"SlotViz_{i}_{GetColorName(slotColor)}";

                    vizMats[i] = colorMat;

                    sb.AppendLine($"  Slot {i}: {originalName} -> {GetColorName(slotColor)}");
                }

                // Apply visualization materials
                if (Application.isPlaying)
                {
                    renderer.materials = vizMats;
                }
                else
                {
                    renderer.sharedMaterials = vizMats;
                }

                processedCount++;
            }

            _isVisualizerActive = true;

            sb.AppendLine();
            sb.AppendLine($"Processed {processedCount} eye renderers");
            sb.AppendLine();
            sb.AppendLine("LOOK AT THE CHARACTER'S EYES NOW:");
            sb.AppendLine("- Which color do you see covering the eye?");
            sb.AppendLine("- That slot's material is rendering on top.");
            sb.AppendLine();
            sb.AppendLine("Run 'DEACTIVATE Restore Original' when done.");

            // Save report
            SaveReport(sb.ToString());

            Debug.Log(sb.ToString());
            Debug.Log($"\nReport saved to: {REPORT_PATH}");
        }

        [MenuItem("Tools/Eye Doctor/Slot Visualizer/2) DEACTIVATE Restore Original")]
        public static void DeactivateSlotVisualizer()
        {
            if (!_isVisualizerActive)
            {
                Debug.LogWarning("Slot Visualizer is not active.");
                return;
            }

            Debug.Log("Restoring original materials...");

            foreach (var kvp in _originalMaterials)
            {
                Renderer renderer = kvp.Key;
                Material[] originalMats = kvp.Value;

                if (renderer != null)
                {
                    if (Application.isPlaying)
                    {
                        renderer.materials = originalMats;
                    }
                    else
                    {
                        renderer.sharedMaterials = originalMats;
                    }
                }
            }

            _originalMaterials.Clear();
            _isVisualizerActive = false;

            Debug.Log("Original materials restored.");
        }

        private static string GetColorName(Color c)
        {
            if (c == Color.red) return "RED";
            if (c == Color.green) return "GREEN";
            if (c == Color.blue) return "BLUE";
            if (c == Color.yellow) return "YELLOW";
            if (c == Color.cyan) return "CYAN";
            if (c == Color.magenta) return "MAGENTA";
            if (c.r > 0.9f && c.g > 0.4f && c.g < 0.6f) return "ORANGE";
            if (c.b > 0.9f && c.r > 0.4f && c.r < 0.6f) return "PURPLE";
            return "UNKNOWN";
        }

        private static GameObject FindActiveCharacter()
        {
            // Try to find in active scene
            GameObject avatar = GameObject.Find("Avatar");
            if (avatar != null)
            {
                Transform charModel = avatar.transform.Find("CharacterModel");
                if (charModel != null)
                {
                    return charModel.gameObject;
                }
            }

            // Fallback: search for any object with CC_Base_Eye child
            var allRenderers = Object.FindObjectsOfType<Renderer>(true);
            foreach (var renderer in allRenderers)
            {
                if (renderer.name.Contains("CC_Base_Eye"))
                {
                    Transform current = renderer.transform;
                    while (current.parent != null)
                    {
                        if (current.name == "CharacterModel" || current.name.Contains("Edumeta"))
                        {
                            return current.gameObject;
                        }
                        current = current.parent;
                    }
                    return renderer.transform.root.gameObject;
                }
            }

            return null;
        }

        private static string GetHierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private static void SaveReport(string content)
        {
            string dir = Path.GetDirectoryName(REPORT_PATH);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(REPORT_PATH, content);
            AssetDatabase.Refresh();
        }
    }
}
