using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Analyzes eye diffuse textures to determine if they contain iris/pupil content.
    /// If missing, generates fallback textures with procedural iris+pupil.
    /// </summary>
    public class EyeDiffuseAnalyzer
    {
        private const string CHARACTER_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA";
        private const string GENERATED_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Generated/Eyes";
        private const string JSON_OUTPUT = "Assets/Temp/eye_diffuse_content.json";

        [System.Serializable]
        public class TextureStats
        {
            public string path;
            public int width;
            public int height;
            public float centerBrightness;
            public float outerBrightness;
            public float centerVariance;
            public float outerVariance;
            public float darkPixelPercent;
            public bool hasIrisPupil;
            public string conclusion;
        }

        [System.Serializable]
        public class AnalysisReport
        {
            public string timestamp;
            public TextureStats rightEye;
            public TextureStats leftEye;
            public string overallConclusion;
            public string action;
        }

        [MenuItem("Tools/Eye Doctor/Analyze Eye Diffuse Content")]
        public static void AnalyzeEyeDiffuseContent()
        {
            Debug.Log("=== EYE DIFFUSE CONTENT ANALYSIS ===\n");

            var report = new AnalysisReport
            {
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            // Find eye textures
            string[] possiblePaths = {
                $"{CHARACTER_PATH}/Edumeta_CharacterGirl_AAA 1.fbm/Std_Eye_R_Diffuse.png",
                $"{CHARACTER_PATH}/Edumeta_CharacterGirl_AAA.fbm/Std_Eye_R_Diffuse.png",
                $"{CHARACTER_PATH}/Textures/Std_Eye_R_Diffuse.png"
            };

            string rightEyePath = null;
            string leftEyePath = null;

            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    rightEyePath = path;
                    leftEyePath = path.Replace("_R_", "_L_");
                    break;
                }
            }

            if (rightEyePath == null)
            {
                Debug.LogWarning("Could not find eye diffuse textures!");
                report.overallConclusion = "Eye textures not found";
                report.action = "Generate fallback textures";
            }
            else
            {
                // Analyze right eye
                report.rightEye = AnalyzeTexture(rightEyePath);
                Debug.Log($"Right Eye: {report.rightEye.conclusion}");

                // Analyze left eye
                if (File.Exists(leftEyePath))
                {
                    report.leftEye = AnalyzeTexture(leftEyePath);
                    Debug.Log($"Left Eye: {report.leftEye.conclusion}");
                }

                // Determine overall conclusion
                bool rightHasIris = report.rightEye?.hasIrisPupil ?? false;
                bool leftHasIris = report.leftEye?.hasIrisPupil ?? false;

                if (rightHasIris && leftHasIris)
                {
                    report.overallConclusion = "Both eye textures contain iris/pupil data";
                    report.action = "No fallback needed - check cornea transparency instead";
                }
                else if (!rightHasIris && !leftHasIris)
                {
                    report.overallConclusion = "BOTH eye textures are missing iris/pupil";
                    report.action = "Generate fallback eye textures";
                }
                else
                {
                    report.overallConclusion = "Mixed - one eye has iris/pupil, other does not";
                    report.action = "Generate fallback for missing eye";
                }
            }

            // Save JSON report
            SaveJsonReport(report);

            Debug.Log($"\nOverall: {report.overallConclusion}");
            Debug.Log($"Action: {report.action}");
            Debug.Log($"\nReport saved to: {JSON_OUTPUT}");

            // If iris/pupil missing, offer to generate
            if (report.action.Contains("Generate"))
            {
                Debug.Log("\nRun 'Generate Fallback Eye Textures' to create iris+pupil textures.");
            }
        }

        [MenuItem("Tools/Eye Doctor/Generate Fallback Eye Textures")]
        public static void GenerateFallbackEyeTextures()
        {
            Debug.Log("=== GENERATING FALLBACK EYE TEXTURES ===\n");

            // Ensure directory exists
            if (!Directory.Exists(GENERATED_PATH))
            {
                Directory.CreateDirectory(GENERATED_PATH);
                Debug.Log($"Created directory: {GENERATED_PATH}");
            }

            // Generate right eye
            string rightPath = $"{GENERATED_PATH}/Generated_Eye_R_Diffuse.png";
            GenerateEyeTexture(rightPath, new Color(0.4f, 0.25f, 0.1f)); // Brown iris
            Debug.Log($"Generated: {rightPath}");

            // Generate left eye (same as right for symmetry)
            string leftPath = $"{GENERATED_PATH}/Generated_Eye_L_Diffuse.png";
            GenerateEyeTexture(leftPath, new Color(0.4f, 0.25f, 0.1f)); // Brown iris
            Debug.Log($"Generated: {leftPath}");

            AssetDatabase.Refresh();

            // Configure import settings
            ConfigureTextureImport(rightPath);
            ConfigureTextureImport(leftPath);

            Debug.Log("\nFallback eye textures generated!");
            Debug.Log("Run 'Assign Generated Eye Textures' to apply them to materials.");
        }

        [MenuItem("Tools/Eye Doctor/Assign Generated Eye Textures")]
        public static void AssignGeneratedEyeTextures()
        {
            Debug.Log("=== ASSIGNING GENERATED EYE TEXTURES ===\n");

            string rightTexPath = $"{GENERATED_PATH}/Generated_Eye_R_Diffuse.png";
            string leftTexPath = $"{GENERATED_PATH}/Generated_Eye_L_Diffuse.png";

            Texture2D rightTex = AssetDatabase.LoadAssetAtPath<Texture2D>(rightTexPath);
            Texture2D leftTex = AssetDatabase.LoadAssetAtPath<Texture2D>(leftTexPath);

            if (rightTex == null || leftTex == null)
            {
                Debug.LogError("Generated textures not found! Run 'Generate Fallback Eye Textures' first.");
                return;
            }

            // Find eye materials
            string[] matSearchPaths = {
                $"{CHARACTER_PATH}/Materials_Clean",
                $"{CHARACTER_PATH}/Materials"
            };

            int assignedCount = 0;

            foreach (string searchPath in matSearchPaths)
            {
                if (!Directory.Exists(searchPath)) continue;

                string[] matFiles = Directory.GetFiles(searchPath, "*.mat", SearchOption.AllDirectories);
                foreach (string matFile in matFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(matFile).ToLower();

                    // Only process eye diffuse materials (not cornea, occlusion, etc.)
                    if (!fileName.Contains("eye") || fileName.Contains("cornea") ||
                        fileName.Contains("occlusion") || fileName.Contains("tearline"))
                        continue;

                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(matFile);
                    if (mat == null) continue;

                    Texture2D texToAssign = null;
                    if (fileName.Contains("_r_") || fileName.EndsWith("_r"))
                    {
                        texToAssign = rightTex;
                    }
                    else if (fileName.Contains("_l_") || fileName.EndsWith("_l"))
                    {
                        texToAssign = leftTex;
                    }

                    if (texToAssign != null)
                    {
                        if (mat.HasProperty("_MainTex"))
                        {
                            mat.SetTexture("_MainTex", texToAssign);
                        }
                        if (mat.HasProperty("_BaseMap"))
                        {
                            mat.SetTexture("_BaseMap", texToAssign);
                        }
                        EditorUtility.SetDirty(mat);
                        assignedCount++;
                        Debug.Log($"Assigned to: {matFile}");
                    }
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"\nAssigned textures to {assignedCount} material(s)");
        }

        private static TextureStats AnalyzeTexture(string path)
        {
            var stats = new TextureStats { path = path };

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
            {
                stats.conclusion = "Texture not found";
                return stats;
            }

            stats.width = tex.width;
            stats.height = tex.height;

            // Make texture readable if needed
            string texPath = AssetDatabase.GetAssetPath(tex);
            var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            bool wasReadable = importer != null && importer.isReadable;

            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            }

            if (!tex.isReadable)
            {
                stats.conclusion = "Cannot read texture pixels";
                return stats;
            }

            // Analyze pixels
            Color[] pixels = tex.GetPixels();
            int w = tex.width;
            int h = tex.height;

            // Define center region (iris/pupil area) - roughly center 40% of texture
            int centerX = w / 2;
            int centerY = h / 2;
            int centerRadius = Mathf.Min(w, h) / 5; // 20% of smaller dimension

            float centerSum = 0f;
            float outerSum = 0f;
            int centerCount = 0;
            int outerCount = 0;
            int darkCount = 0;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color pixel = pixels[y * w + x];
                    float brightness = (pixel.r + pixel.g + pixel.b) / 3f;

                    float distFromCenter = Mathf.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));

                    if (distFromCenter < centerRadius)
                    {
                        centerSum += brightness;
                        centerCount++;
                    }
                    else
                    {
                        outerSum += brightness;
                        outerCount++;
                    }

                    if (brightness < 0.2f) darkCount++;
                }
            }

            stats.centerBrightness = centerCount > 0 ? centerSum / centerCount : 0f;
            stats.outerBrightness = outerCount > 0 ? outerSum / outerCount : 0f;
            stats.darkPixelPercent = (darkCount * 100f) / pixels.Length;

            // Calculate variance in center region
            float centerMean = stats.centerBrightness;
            float centerVarianceSum = 0f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float distFromCenter = Mathf.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
                    if (distFromCenter < centerRadius)
                    {
                        Color pixel = pixels[y * w + x];
                        float brightness = (pixel.r + pixel.g + pixel.b) / 3f;
                        float diff = brightness - centerMean;
                        centerVarianceSum += diff * diff;
                    }
                }
            }
            stats.centerVariance = centerCount > 0 ? centerVarianceSum / centerCount : 0f;

            // Restore original readable state
            if (importer != null && !wasReadable)
            {
                importer.isReadable = false;
                importer.SaveAndReimport();
            }

            // Determine if iris/pupil exists
            // Criteria:
            // - Center should be darker than outer (pupil/iris is darker than sclera)
            // - Should have some dark pixels (pupil)
            // - Center should have some variance (iris pattern)
            bool centerDarker = stats.centerBrightness < stats.outerBrightness - 0.1f;
            bool hasDarkPixels = stats.darkPixelPercent > 1f;
            bool hasVariance = stats.centerVariance > 0.005f;

            stats.hasIrisPupil = (centerDarker || hasDarkPixels) && hasVariance;

            if (stats.hasIrisPupil)
            {
                stats.conclusion = $"Contains iris/pupil (dark%={stats.darkPixelPercent:F1}, variance={stats.centerVariance:F4})";
            }
            else
            {
                stats.conclusion = $"Missing iris/pupil (dark%={stats.darkPixelPercent:F1}, variance={stats.centerVariance:F4})";
            }

            return stats;
        }

        private static void GenerateEyeTexture(string path, Color irisColor)
        {
            int size = 1024;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);

            int centerX = size / 2;
            int centerY = size / 2;
            float irisRadius = size * 0.35f;
            float pupilRadius = size * 0.12f;
            float limbusRadius = size * 0.38f; // Dark ring around iris

            Color scleraColor = new Color(0.97f, 0.95f, 0.93f); // Off-white sclera
            Color limbusColor = new Color(0.15f, 0.1f, 0.08f);  // Dark limbus ring
            Color pupilColor = new Color(0.02f, 0.02f, 0.02f);  // Near-black pupil

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Mathf.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
                    Color pixelColor;

                    if (dist < pupilRadius)
                    {
                        // Pupil
                        pixelColor = pupilColor;
                    }
                    else if (dist < irisRadius)
                    {
                        // Iris with radial gradient
                        float t = (dist - pupilRadius) / (irisRadius - pupilRadius);

                        // Add some radial variation for iris texture
                        float angle = Mathf.Atan2(y - centerY, x - centerX);
                        float noiseVal = Mathf.PerlinNoise(angle * 5f + 100f, dist * 0.1f);

                        Color innerIris = irisColor * 0.6f;
                        Color outerIris = irisColor * 1.2f;
                        pixelColor = Color.Lerp(innerIris, outerIris, t);
                        pixelColor = Color.Lerp(pixelColor, pixelColor * (0.7f + noiseVal * 0.6f), 0.5f);
                    }
                    else if (dist < limbusRadius)
                    {
                        // Limbus (dark ring around iris)
                        float t = (dist - irisRadius) / (limbusRadius - irisRadius);
                        pixelColor = Color.Lerp(limbusColor, scleraColor, t);
                    }
                    else
                    {
                        // Sclera
                        pixelColor = scleraColor;

                        // Add subtle blood vessel effect at edges
                        if (dist > size * 0.45f)
                        {
                            float edgeFactor = (dist - size * 0.45f) / (size * 0.05f);
                            edgeFactor = Mathf.Clamp01(edgeFactor);
                            pixelColor = Color.Lerp(pixelColor, new Color(0.95f, 0.85f, 0.85f), edgeFactor * 0.3f);
                        }
                    }

                    pixelColor.a = 1f;
                    tex.SetPixel(x, y, pixelColor);
                }
            }

            tex.Apply();

            byte[] pngData = tex.EncodeToPNG();
            File.WriteAllBytes(path, pngData);
            Object.DestroyImmediate(tex);
        }

        private static void ConfigureTextureImport(string path)
        {
            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
        }

        private static void SaveJsonReport(AnalysisReport report)
        {
            string dir = Path.GetDirectoryName(JSON_OUTPUT);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(JSON_OUTPUT, json);
            AssetDatabase.Refresh();
        }
    }
}
