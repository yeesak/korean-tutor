using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Analyzes eye textures to determine if they contain iris/pupil data.
    /// Also creates a comprehensive report of eye rendering setup.
    /// </summary>
    public class EyeTextureAnalyzer
    {
        private const string PREFAB_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
        private const string JSON_OUTPUT = "Assets/Temp/eye_texture_existence_report.json";
        private const string TXT_OUTPUT = "Assets/Temp/eye_texture_existence_report.txt";

        [System.Serializable]
        public class TextureAnalysis
        {
            public string path;
            public string guid;
            public long fileSizeBytes;
            public int width;
            public int height;
            public string format;
            public float minBrightness;
            public float maxBrightness;
            public float meanBrightness;
            public float variance;
            public float percentNearWhite;
            public float percentDark;
            public bool hasPupilIris;
            public string analysisDetails;
        }

        [System.Serializable]
        public class MaterialSlotData
        {
            public int slotIndex;
            public string materialName;
            public string materialPath;
            public string materialGuid;
            public string shaderName;
            public string renderMode;
            public int renderQueue;
            public string mainTexName;
            public string mainTexPath;
            public string mainTexGuid;
            public TextureAnalysis mainTexAnalysis;
        }

        [System.Serializable]
        public class RendererData
        {
            public string path;
            public string category;
            public List<MaterialSlotData> slots = new List<MaterialSlotData>();
        }

        [System.Serializable]
        public class EyeTextureReport
        {
            public string timestamp;
            public string characterPath;
            public string mode;
            public List<RendererData> eyeRenderers = new List<RendererData>();
            public string overallConclusion;
            public string recommendedAction;
        }

        [MenuItem("Tools/Eye Analysis/Analyze Eye Textures (Generate Report)")]
        public static void AnalyzeEyeTextures()
        {
            Debug.Log("=== EYE TEXTURE ANALYSIS ===\n");

            var report = new EyeTextureReport
            {
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                mode = Application.isPlaying ? "PlayMode" : "EditMode"
            };

            // Find character
            GameObject character = FindCharacter();
            if (character == null)
            {
                Debug.LogError("Cannot find character!");
                return;
            }

            report.characterPath = Application.isPlaying ? "Avatar/CharacterModel" : PREFAB_PATH;

            // Find eye-related renderers
            var renderers = character.GetComponentsInChildren<Renderer>(true);
            var sb = new StringBuilder();
            sb.AppendLine("=== EYE TEXTURE EXISTENCE REPORT ===");
            sb.AppendLine($"Timestamp: {report.timestamp}");
            sb.AppendLine($"Character: {report.characterPath}");
            sb.AppendLine($"Mode: {report.mode}");
            sb.AppendLine();

            bool anyMissingPupil = false;

            foreach (var renderer in renderers)
            {
                string nameLower = renderer.name.ToLowerInvariant();
                if (!nameLower.Contains("eye") && !nameLower.Contains("cornea") && !nameLower.Contains("tearline"))
                    continue;

                var rendererData = new RendererData
                {
                    path = renderer.name,
                    category = GetCategory(nameLower)
                };

                sb.AppendLine($"--- {renderer.name} ({rendererData.category}) ---");

                Material[] mats = Application.isPlaying ? renderer.materials : renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null) continue;

                    var slotData = new MaterialSlotData
                    {
                        slotIndex = i,
                        materialName = mat.name,
                        materialPath = AssetDatabase.GetAssetPath(mat),
                        shaderName = mat.shader != null ? mat.shader.name : "NULL",
                        renderQueue = mat.renderQueue
                    };

                    if (!string.IsNullOrEmpty(slotData.materialPath))
                        slotData.materialGuid = AssetDatabase.AssetPathToGUID(slotData.materialPath);

                    // Get render mode
                    if (mat.HasProperty("_Mode"))
                    {
                        float mode = mat.GetFloat("_Mode");
                        slotData.renderMode = mode switch { 0 => "Opaque", 1 => "Cutout", 2 => "Fade", 3 => "Transparent", _ => $"Unknown({mode})" };
                    }
                    else
                    {
                        slotData.renderMode = "N/A";
                    }

                    sb.AppendLine($"  [{i}] {mat.name}");
                    sb.AppendLine($"      Shader: {slotData.shaderName}");
                    sb.AppendLine($"      Mode: {slotData.renderMode}, Queue: {slotData.renderQueue}");

                    // Analyze _MainTex
                    if (mat.HasProperty("_MainTex"))
                    {
                        var tex = mat.GetTexture("_MainTex") as Texture2D;
                        if (tex != null)
                        {
                            slotData.mainTexName = tex.name;
                            slotData.mainTexPath = AssetDatabase.GetAssetPath(tex);
                            if (!string.IsNullOrEmpty(slotData.mainTexPath))
                                slotData.mainTexGuid = AssetDatabase.AssetPathToGUID(slotData.mainTexPath);

                            // Analyze texture content
                            var analysis = AnalyzeTextureContent(tex, slotData.mainTexPath);
                            slotData.mainTexAnalysis = analysis;

                            sb.AppendLine($"      _MainTex: {tex.name} ({tex.width}x{tex.height})");
                            sb.AppendLine($"      TexPath: {slotData.mainTexPath}");
                            sb.AppendLine($"      GUID: {slotData.mainTexGuid}");
                            sb.AppendLine($"      Analysis: min={analysis.minBrightness:F3}, max={analysis.maxBrightness:F3}, mean={analysis.meanBrightness:F3}");
                            sb.AppendLine($"      %NearWhite: {analysis.percentNearWhite:F1}%, %Dark: {analysis.percentDark:F1}%");
                            sb.AppendLine($"      CONTAINS PUPIL/IRIS: {(analysis.hasPupilIris ? "YES" : "NO")}");
                            sb.AppendLine($"      Details: {analysis.analysisDetails}");

                            if (!analysis.hasPupilIris && rendererData.category == "EYE_BASE")
                            {
                                anyMissingPupil = true;
                            }
                        }
                        else
                        {
                            slotData.mainTexName = "NULL";
                            sb.AppendLine($"      _MainTex: NULL - NO TEXTURE ASSIGNED!");

                            if (rendererData.category == "EYE_BASE")
                            {
                                anyMissingPupil = true;
                            }
                        }
                    }

                    rendererData.slots.Add(slotData);
                    sb.AppendLine();
                }

                report.eyeRenderers.Add(rendererData);
            }

            // Conclusion
            if (anyMissingPupil)
            {
                report.overallConclusion = "PROBLEM: Eye base texture is missing pupil/iris OR texture is NULL";
                report.recommendedAction = "Generate new eye textures with proper iris/pupil OR fix texture assignment";
            }
            else
            {
                report.overallConclusion = "Eye textures appear to contain pupil/iris data";
                report.recommendedAction = "Check runtime material/shader conversion or overlay rendering order";
            }

            sb.AppendLine("=== CONCLUSION ===");
            sb.AppendLine(report.overallConclusion);
            sb.AppendLine(report.recommendedAction);

            // Save reports
            EnsureDirectory();
            File.WriteAllText(TXT_OUTPUT, sb.ToString());
            File.WriteAllText(JSON_OUTPUT, JsonUtility.ToJson(report, true));
            AssetDatabase.Refresh();

            Debug.Log(sb.ToString());
            Debug.Log($"\nReports saved to:\n  {TXT_OUTPUT}\n  {JSON_OUTPUT}");
        }

        private static TextureAnalysis AnalyzeTextureContent(Texture2D tex, string assetPath)
        {
            var analysis = new TextureAnalysis
            {
                path = assetPath,
                width = tex.width,
                height = tex.height,
                format = tex.format.ToString()
            };

            if (!string.IsNullOrEmpty(assetPath))
            {
                analysis.guid = AssetDatabase.AssetPathToGUID(assetPath);
                var fileInfo = new FileInfo(assetPath);
                if (fileInfo.Exists)
                    analysis.fileSizeBytes = fileInfo.Length;
            }

            // Try to read pixels
            try
            {
                // Make texture readable temporarily
                string texPath = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(texPath))
                {
                    var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                    bool wasReadable = importer != null && importer.isReadable;

                    if (importer != null && !importer.isReadable)
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                    }

                    if (tex.isReadable)
                    {
                        AnalyzePixels(tex, analysis);
                    }
                    else
                    {
                        // Fallback: use file size heuristic
                        analysis.analysisDetails = "Cannot read pixels - using file size heuristic";
                        if (analysis.fileSizeBytes > 1000000)
                        {
                            analysis.hasPupilIris = true;
                            analysis.analysisDetails += $" - Large file ({analysis.fileSizeBytes} bytes) suggests real content";
                        }
                        else
                        {
                            analysis.hasPupilIris = false;
                            analysis.analysisDetails += $" - Small file ({analysis.fileSizeBytes} bytes) may be placeholder";
                        }
                    }

                    // Restore original readable state if needed
                    if (importer != null && !wasReadable)
                    {
                        importer.isReadable = false;
                        importer.SaveAndReimport();
                    }
                }
            }
            catch (System.Exception e)
            {
                analysis.analysisDetails = $"Error analyzing: {e.Message}";
                // Fallback
                if (analysis.fileSizeBytes > 1000000)
                {
                    analysis.hasPupilIris = true;
                }
            }

            return analysis;
        }

        private static void AnalyzePixels(Texture2D tex, TextureAnalysis analysis)
        {
            Color[] pixels = tex.GetPixels();
            int totalPixels = pixels.Length;

            float minBright = 1f;
            float maxBright = 0f;
            float sumBright = 0f;
            int nearWhiteCount = 0;
            int darkCount = 0;

            foreach (var pixel in pixels)
            {
                float brightness = (pixel.r + pixel.g + pixel.b) / 3f;
                sumBright += brightness;

                if (brightness < minBright) minBright = brightness;
                if (brightness > maxBright) maxBright = brightness;

                if (brightness > 0.9f) nearWhiteCount++;
                if (brightness < 0.2f) darkCount++;
            }

            analysis.minBrightness = minBright;
            analysis.maxBrightness = maxBright;
            analysis.meanBrightness = sumBright / totalPixels;
            analysis.percentNearWhite = (nearWhiteCount * 100f) / totalPixels;
            analysis.percentDark = (darkCount * 100f) / totalPixels;

            // Calculate variance
            float sumVariance = 0f;
            foreach (var pixel in pixels)
            {
                float brightness = (pixel.r + pixel.g + pixel.b) / 3f;
                float diff = brightness - analysis.meanBrightness;
                sumVariance += diff * diff;
            }
            analysis.variance = sumVariance / totalPixels;

            // Determine if pupil/iris exists
            // Criteria:
            // - Must have some dark pixels (pupil) - percentDark > 1%
            // - Must have range (not uniform) - variance > 0.01
            // - Must have contrast - (maxBright - minBright) > 0.3

            bool hasDark = analysis.percentDark > 1f;
            bool hasVariance = analysis.variance > 0.01f;
            bool hasContrast = (analysis.maxBrightness - analysis.minBrightness) > 0.3f;

            analysis.hasPupilIris = hasDark && hasVariance && hasContrast;

            analysis.analysisDetails = $"Dark:{hasDark}, Variance:{hasVariance}, Contrast:{hasContrast}";
            if (analysis.hasPupilIris)
            {
                analysis.analysisDetails += " -> Contains pupil/iris";
            }
            else
            {
                if (!hasDark) analysis.analysisDetails += " -> Missing dark (pupil) regions";
                if (!hasVariance) analysis.analysisDetails += " -> Too uniform";
                if (!hasContrast) analysis.analysisDetails += " -> Low contrast";
            }
        }

        private static string GetCategory(string name)
        {
            if (name.Contains("occlusion")) return "EYE_OCCLUSION";
            if (name.Contains("tearline")) return "TEARLINE";
            if (name.Contains("cornea")) return "CORNEA";
            if (name.Contains("cc_base_eye")) return "EYE_BASE";
            return "EYE_OTHER";
        }

        private static GameObject FindCharacter()
        {
            if (Application.isPlaying)
            {
                var avatar = GameObject.Find("Avatar");
                if (avatar != null)
                {
                    var charModel = avatar.transform.Find("CharacterModel");
                    if (charModel != null) return charModel.gameObject;
                }
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        }

        private static void EnsureDirectory()
        {
            string dir = "Assets/Temp";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
