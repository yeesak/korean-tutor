using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Comprehensive character render audit with Edit/Play mode comparison.
    /// Identifies exactly what happens to eye materials at runtime.
    /// </summary>
    public class CharacterRenderAuditV2 : EditorWindow
    {
        private const string PREFAB_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
        private const string JSON_OUTPUT = "Assets/Temp/character_render_audit.json";
        private const string SNAPSHOT_OUTPUT = "Assets/Temp/runtime_material_snapshot.txt";
        private const string DIFF_OUTPUT = "Assets/Temp/edit_play_diff.txt";

        // Cache for edit mode snapshot
        private static Dictionary<string, MaterialSnapshot> _editModeCache = new Dictionary<string, MaterialSnapshot>();

        #region Data Classes

        [System.Serializable]
        public class TextureData
        {
            public string propertyName;
            public string textureName;
            public string assetPath;
            public string guid;
            public int width;
            public int height;
            public string format;
            public string importType;
            public bool sRGB;
            public string alphaSource;
            public bool alphaIsTransparency;
            public bool isNull;
        }

        [System.Serializable]
        public class MaterialSnapshot
        {
            public string rendererPath;
            public int slotIndex;
            public string materialName;
            public string materialPath;
            public string materialGuid;
            public bool isInstance;
            public string shaderName;
            public bool shaderSupported;
            public int renderQueue;
            public float modeValue;
            public string renderMode;
            public bool zWrite;
            public int srcBlend;
            public int dstBlend;
            public List<TextureData> textures = new List<TextureData>();
            public List<string> keywords = new List<string>();
            public string colorHex;
            public float cutoff;
        }

        [System.Serializable]
        public class RendererData
        {
            public string path;
            public string meshName;
            public string category;
            public List<MaterialSnapshot> materials = new List<MaterialSnapshot>();
        }

        [System.Serializable]
        public class DiffEntry
        {
            public string key;
            public string editModeValue;
            public string playModeValue;
            public bool changed;
            public string issue;
        }

        [System.Serializable]
        public class AuditReportV2
        {
            public string timestamp;
            public string mode;
            public string characterSource;
            public List<RendererData> renderers = new List<RendererData>();
            public List<DiffEntry> diffEntries = new List<DiffEntry>();
            public string rootCauseEvidence;
            public string determinedCause;
        }

        #endregion

        #region Menu Items

        [MenuItem("Tools/Character Audit/1. SNAPSHOT Edit Mode (Before Play)")]
        public static void SnapshotEditMode()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("Exit Play Mode first to capture Edit Mode snapshot!");
                return;
            }

            _editModeCache.Clear();
            var report = GenerateReport(false);

            // Cache for later comparison
            foreach (var renderer in report.renderers)
            {
                foreach (var mat in renderer.materials)
                {
                    string key = $"{renderer.path}[{mat.slotIndex}]";
                    _editModeCache[key] = mat;
                }
            }

            SaveSnapshot(report, "EDIT MODE");
            Debug.Log($"Edit Mode snapshot captured: {report.renderers.Count} renderers, {_editModeCache.Count} material slots cached");
            Debug.Log("Now enter Play Mode and run '2. SNAPSHOT Play Mode'");
        }

        [MenuItem("Tools/Character Audit/2. SNAPSHOT Play Mode + Generate DIFF")]
        public static void SnapshotPlayModeAndDiff()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Enter Play Mode first!");
                return;
            }

            var report = GenerateReport(true);
            SaveSnapshot(report, "PLAY MODE");

            // Generate diff
            var diff = GenerateDiff(report);
            report.diffEntries = diff;

            // Analyze root cause
            AnalyzeRootCause(report);

            // Save full JSON
            SaveJsonReport(report);
            SaveDiffReport(diff, report.determinedCause, report.rootCauseEvidence);

            Debug.Log($"\n=== AUDIT COMPLETE ===");
            Debug.Log($"JSON Report: {JSON_OUTPUT}");
            Debug.Log($"Snapshot: {SNAPSHOT_OUTPUT}");
            Debug.Log($"Diff Report: {DIFF_OUTPUT}");
            Debug.Log($"\nDETERMINED ROOT CAUSE: {report.determinedCause}");
            Debug.Log($"Evidence: {report.rootCauseEvidence}");
        }

        [MenuItem("Tools/Character Audit/3. QUICK CHECK (Current State Only)")]
        public static void QuickCheck()
        {
            var report = GenerateReport(Application.isPlaying);
            SaveSnapshot(report, Application.isPlaying ? "PLAY MODE" : "EDIT MODE");
            SaveJsonReport(report);

            PrintQuickSummary(report);
        }

        #endregion

        #region Report Generation

        private static AuditReportV2 GenerateReport(bool isPlayMode)
        {
            var report = new AuditReportV2
            {
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                mode = isPlayMode ? "PlayMode" : "EditMode"
            };

            GameObject character = FindCharacter(isPlayMode);
            if (character == null)
            {
                Debug.LogError("Cannot find character!");
                return report;
            }

            report.characterSource = isPlayMode ? "Runtime: Avatar/CharacterModel" : $"Prefab: {PREFAB_PATH}";

            var renderers = character.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var data = AnalyzeRenderer(renderer, character.transform, isPlayMode);
                report.renderers.Add(data);
            }

            return report;
        }

        private static GameObject FindCharacter(bool isPlayMode)
        {
            if (isPlayMode)
            {
                var avatar = GameObject.Find("Avatar");
                if (avatar != null)
                {
                    var charModel = avatar.transform.Find("CharacterModel");
                    if (charModel != null) return charModel.gameObject;
                }
            }

            // Fallback to prefab
            return AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        }

        private static RendererData AnalyzeRenderer(Renderer renderer, Transform root, bool isPlayMode)
        {
            var data = new RendererData
            {
                path = GetPath(renderer.gameObject, root)
            };

            // Categorize
            string nameLower = renderer.name.ToLowerInvariant();
            if (nameLower.Contains("cc_base_eye") && !nameLower.Contains("occlusion"))
                data.category = "EYE_BASE";
            else if (nameLower.Contains("occlusion"))
                data.category = "EYE_OCCLUSION";
            else if (nameLower.Contains("tearline"))
                data.category = "TEARLINE";
            else if (nameLower.Contains("eyelash"))
                data.category = "EYELASH";
            else if (nameLower.Contains("hair") || nameLower.Contains("scalp"))
                data.category = "HAIR";
            else
                data.category = "OTHER";

            if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                data.meshName = smr.sharedMesh.name;

            // Get materials - use .materials in play mode (runtime instances), .sharedMaterials in edit mode
            Material[] mats = isPlayMode ? renderer.materials : renderer.sharedMaterials;

            for (int i = 0; i < mats.Length; i++)
            {
                var snapshot = AnalyzeMaterial(mats[i], i, data.path);
                data.materials.Add(snapshot);
            }

            return data;
        }

        private static MaterialSnapshot AnalyzeMaterial(Material mat, int index, string rendererPath)
        {
            var snapshot = new MaterialSnapshot
            {
                rendererPath = rendererPath,
                slotIndex = index
            };

            if (mat == null)
            {
                snapshot.materialName = "NULL";
                snapshot.shaderName = "NULL";
                return snapshot;
            }

            snapshot.materialName = mat.name;
            snapshot.isInstance = mat.name.Contains("(Instance)");

            string assetPath = AssetDatabase.GetAssetPath(mat);
            snapshot.materialPath = string.IsNullOrEmpty(assetPath) ? "(runtime)" : assetPath;
            if (!string.IsNullOrEmpty(assetPath))
                snapshot.materialGuid = AssetDatabase.AssetPathToGUID(assetPath);

            // Shader
            if (mat.shader != null)
            {
                snapshot.shaderName = mat.shader.name;
                snapshot.shaderSupported = mat.shader.isSupported;
            }
            else
            {
                snapshot.shaderName = "NULL";
            }

            snapshot.renderQueue = mat.renderQueue;

            // Render mode
            if (mat.HasProperty("_Mode"))
            {
                snapshot.modeValue = mat.GetFloat("_Mode");
                snapshot.renderMode = snapshot.modeValue switch
                {
                    0 => "Opaque",
                    1 => "Cutout",
                    2 => "Fade",
                    3 => "Transparent",
                    _ => $"Unknown({snapshot.modeValue})"
                };
            }

            // ZWrite, blend
            if (mat.HasProperty("_ZWrite"))
                snapshot.zWrite = mat.GetInt("_ZWrite") == 1;
            if (mat.HasProperty("_SrcBlend"))
                snapshot.srcBlend = mat.GetInt("_SrcBlend");
            if (mat.HasProperty("_DstBlend"))
                snapshot.dstBlend = mat.GetInt("_DstBlend");

            // Color
            if (mat.HasProperty("_Color"))
                snapshot.colorHex = ColorUtility.ToHtmlStringRGBA(mat.GetColor("_Color"));

            // Cutoff
            if (mat.HasProperty("_Cutoff"))
                snapshot.cutoff = mat.GetFloat("_Cutoff");

            // Keywords
            snapshot.keywords = mat.shaderKeywords.ToList();

            // Textures - check BOTH _MainTex and _BaseMap
            snapshot.textures.Add(AnalyzeTexture(mat, "_MainTex"));
            snapshot.textures.Add(AnalyzeTexture(mat, "_BaseMap"));
            snapshot.textures.Add(AnalyzeTexture(mat, "_BumpMap"));

            return snapshot;
        }

        private static TextureData AnalyzeTexture(Material mat, string propertyName)
        {
            var data = new TextureData { propertyName = propertyName };

            if (!mat.HasProperty(propertyName))
            {
                data.textureName = "(no property)";
                data.isNull = true;
                return data;
            }

            var tex = mat.GetTexture(propertyName) as Texture2D;
            if (tex == null)
            {
                data.textureName = "NULL";
                data.isNull = true;
                return data;
            }

            data.textureName = tex.name;
            data.width = tex.width;
            data.height = tex.height;
            data.format = tex.format.ToString();
            data.isNull = false;

            string texPath = AssetDatabase.GetAssetPath(tex);
            data.assetPath = texPath;
            if (!string.IsNullOrEmpty(texPath))
            {
                data.guid = AssetDatabase.AssetPathToGUID(texPath);

                var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                if (importer != null)
                {
                    data.importType = importer.textureType.ToString();
                    data.sRGB = importer.sRGBTexture;
                    data.alphaSource = importer.alphaSource.ToString();
                    data.alphaIsTransparency = importer.alphaIsTransparency;
                }
            }

            return data;
        }

        #endregion

        #region Diff Generation

        private static List<DiffEntry> GenerateDiff(AuditReportV2 playReport)
        {
            var diffs = new List<DiffEntry>();

            if (_editModeCache.Count == 0)
            {
                diffs.Add(new DiffEntry
                {
                    key = "WARNING",
                    editModeValue = "No Edit Mode snapshot",
                    playModeValue = "Run '1. SNAPSHOT Edit Mode' first",
                    changed = true,
                    issue = "Cannot compare without Edit Mode baseline"
                });
                return diffs;
            }

            foreach (var renderer in playReport.renderers)
            {
                // Focus on eye-related renderers
                if (renderer.category != "EYE_BASE" && renderer.category != "EYE_OCCLUSION" &&
                    renderer.category != "TEARLINE" && renderer.category != "EYELASH" &&
                    renderer.category != "HAIR")
                    continue;

                foreach (var playMat in renderer.materials)
                {
                    string key = $"{renderer.path}[{playMat.slotIndex}]";

                    if (!_editModeCache.TryGetValue(key, out var editMat))
                    {
                        diffs.Add(new DiffEntry
                        {
                            key = key,
                            editModeValue = "NOT FOUND",
                            playModeValue = playMat.materialName,
                            changed = true,
                            issue = "Material slot not in Edit Mode cache"
                        });
                        continue;
                    }

                    // Compare material name
                    if (editMat.materialName != playMat.materialName)
                    {
                        diffs.Add(new DiffEntry
                        {
                            key = $"{key}/materialName",
                            editModeValue = editMat.materialName,
                            playModeValue = playMat.materialName,
                            changed = true,
                            issue = playMat.isInstance ? "Material became instance" : "Material changed"
                        });
                    }

                    // Compare shader
                    if (editMat.shaderName != playMat.shaderName)
                    {
                        diffs.Add(new DiffEntry
                        {
                            key = $"{key}/shader",
                            editModeValue = editMat.shaderName,
                            playModeValue = playMat.shaderName,
                            changed = true,
                            issue = "Shader changed at runtime"
                        });
                    }

                    // Compare _MainTex
                    var editMainTex = editMat.textures.FirstOrDefault(t => t.propertyName == "_MainTex");
                    var playMainTex = playMat.textures.FirstOrDefault(t => t.propertyName == "_MainTex");

                    if (editMainTex != null && playMainTex != null)
                    {
                        bool editHasTex = !editMainTex.isNull;
                        bool playHasTex = !playMainTex.isNull;

                        if (editHasTex && !playHasTex)
                        {
                            diffs.Add(new DiffEntry
                            {
                                key = $"{key}/_MainTex",
                                editModeValue = editMainTex.textureName,
                                playModeValue = "NULL",
                                changed = true,
                                issue = "CRITICAL: _MainTex LOST at runtime!"
                            });
                        }
                        else if (editHasTex && playHasTex && editMainTex.textureName != playMainTex.textureName)
                        {
                            diffs.Add(new DiffEntry
                            {
                                key = $"{key}/_MainTex",
                                editModeValue = editMainTex.textureName,
                                playModeValue = playMainTex.textureName,
                                changed = true,
                                issue = "Texture changed"
                            });
                        }
                    }

                    // Compare render mode
                    if (editMat.renderMode != playMat.renderMode)
                    {
                        diffs.Add(new DiffEntry
                        {
                            key = $"{key}/renderMode",
                            editModeValue = editMat.renderMode,
                            playModeValue = playMat.renderMode,
                            changed = true,
                            issue = "Render mode changed"
                        });
                    }

                    // Compare renderQueue
                    if (editMat.renderQueue != playMat.renderQueue)
                    {
                        diffs.Add(new DiffEntry
                        {
                            key = $"{key}/renderQueue",
                            editModeValue = editMat.renderQueue.ToString(),
                            playModeValue = playMat.renderQueue.ToString(),
                            changed = true,
                            issue = "RenderQueue changed"
                        });
                    }
                }
            }

            return diffs;
        }

        #endregion

        #region Root Cause Analysis

        private static void AnalyzeRootCause(AuditReportV2 report)
        {
            var evidence = new StringBuilder();
            string cause = "UNKNOWN";

            // Check 1: Did _MainTex become NULL?
            var lostTextureDiffs = report.diffEntries.Where(d => d.key.Contains("_MainTex") && d.issue.Contains("LOST")).ToList();
            if (lostTextureDiffs.Count > 0)
            {
                cause = "CAUSE_5_RUNTIME_OVERRIDE";
                evidence.AppendLine("EVIDENCE: _MainTex became NULL at runtime:");
                foreach (var d in lostTextureDiffs)
                {
                    evidence.AppendLine($"  - {d.key}: EditMode={d.editModeValue} -> PlayMode={d.playModeValue}");
                }
            }

            // Check 2: Eye materials still have NULL textures (even in Edit mode)?
            var eyeRenderers = report.renderers.Where(r => r.category == "EYE_BASE").ToList();
            foreach (var eye in eyeRenderers)
            {
                foreach (var mat in eye.materials)
                {
                    var mainTex = mat.textures.FirstOrDefault(t => t.propertyName == "_MainTex");
                    if (mainTex != null && mainTex.isNull)
                    {
                        if (cause == "UNKNOWN") cause = "CAUSE_1_MISSING_TEXTURE";
                        evidence.AppendLine($"EVIDENCE: Eye material missing _MainTex:");
                        evidence.AppendLine($"  - {mat.rendererPath}[{mat.slotIndex}] {mat.materialName}: _MainTex is NULL");
                    }
                }
            }

            // Check 3: Overlays are Opaque?
            var overlays = report.renderers.Where(r => r.category == "EYE_OCCLUSION" || r.category == "TEARLINE").ToList();
            foreach (var overlay in overlays)
            {
                foreach (var mat in overlay.materials)
                {
                    if (mat.renderMode == "Opaque")
                    {
                        if (cause == "UNKNOWN") cause = "CAUSE_3_OVERLAY_BLOCKING";
                        evidence.AppendLine($"EVIDENCE: Overlay is OPAQUE (blocking iris):");
                        evidence.AppendLine($"  - {mat.rendererPath}[{mat.slotIndex}] {mat.materialName}: Mode={mat.renderMode}");
                    }
                }
            }

            // Check 4: Material became instance with different shader?
            var shaderChangeDiffs = report.diffEntries.Where(d => d.key.Contains("/shader") && d.changed).ToList();
            if (shaderChangeDiffs.Count > 0 && cause == "UNKNOWN")
            {
                cause = "CAUSE_5_RUNTIME_OVERRIDE";
                evidence.AppendLine("EVIDENCE: Shader changed at runtime:");
                foreach (var d in shaderChangeDiffs)
                {
                    evidence.AppendLine($"  - {d.key}: {d.editModeValue} -> {d.playModeValue}");
                }
            }

            report.determinedCause = cause;
            report.rootCauseEvidence = evidence.ToString();
        }

        #endregion

        #region Output

        private static void SaveSnapshot(AuditReportV2 report, string label)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== CHARACTER MATERIAL SNAPSHOT ({label}) ===");
            sb.AppendLine($"Timestamp: {report.timestamp}");
            sb.AppendLine($"Source: {report.characterSource}");
            sb.AppendLine();

            // Eye renderers
            foreach (var r in report.renderers.Where(x => x.category == "EYE_BASE"))
            {
                sb.AppendLine($"--- {r.path} ({r.category}) ---");
                foreach (var m in r.materials)
                {
                    var mainTex = m.textures.FirstOrDefault(t => t.propertyName == "_MainTex");
                    string texInfo = mainTex != null ? (mainTex.isNull ? "NULL!" : $"{mainTex.textureName} ({mainTex.width}x{mainTex.height})") : "N/A";

                    sb.AppendLine($"  [{m.slotIndex}] {m.materialName}");
                    sb.AppendLine($"      Shader: {m.shaderName} | Mode: {m.renderMode} | Queue: {m.renderQueue}");
                    sb.AppendLine($"      _MainTex: {texInfo}");
                    sb.AppendLine($"      Instance: {m.isInstance}");
                }
            }

            // Overlays
            sb.AppendLine();
            foreach (var r in report.renderers.Where(x => x.category == "EYE_OCCLUSION" || x.category == "TEARLINE"))
            {
                sb.AppendLine($"--- {r.path} ({r.category}) ---");
                foreach (var m in r.materials)
                {
                    sb.AppendLine($"  [{m.slotIndex}] {m.materialName}");
                    sb.AppendLine($"      Mode: {m.renderMode} | Queue: {m.renderQueue} | ZWrite: {m.zWrite}");
                }
            }

            // Hair
            sb.AppendLine();
            foreach (var r in report.renderers.Where(x => x.category == "HAIR" || x.category == "EYELASH"))
            {
                sb.AppendLine($"--- {r.path} ({r.category}) ---");
                foreach (var m in r.materials)
                {
                    var mainTex = m.textures.FirstOrDefault(t => t.propertyName == "_MainTex");
                    string texInfo = mainTex != null ? (mainTex.isNull ? "NULL" : mainTex.textureName) : "N/A";

                    sb.AppendLine($"  [{m.slotIndex}] {m.materialName}");
                    sb.AppendLine($"      Mode: {m.renderMode} | Queue: {m.renderQueue} | Cutoff: {m.cutoff}");
                    sb.AppendLine($"      _MainTex: {texInfo}");
                }
            }

            EnsureDirectory();
            File.WriteAllText(SNAPSHOT_OUTPUT, sb.ToString());
            AssetDatabase.Refresh();
            Debug.Log(sb.ToString());
        }

        private static void SaveJsonReport(AuditReportV2 report)
        {
            EnsureDirectory();
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(JSON_OUTPUT, json);
            AssetDatabase.Refresh();
        }

        private static void SaveDiffReport(List<DiffEntry> diffs, string cause, string evidence)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== EDIT MODE vs PLAY MODE DIFF ===");
            sb.AppendLine();

            if (diffs.Count == 0)
            {
                sb.AppendLine("No differences detected between Edit and Play mode.");
            }
            else
            {
                sb.AppendLine($"Found {diffs.Count} differences:");
                sb.AppendLine();
                foreach (var d in diffs)
                {
                    sb.AppendLine($"[{(d.issue.Contains("CRITICAL") ? "!!!" : "---")}] {d.key}");
                    sb.AppendLine($"     Edit: {d.editModeValue}");
                    sb.AppendLine($"     Play: {d.playModeValue}");
                    sb.AppendLine($"     Issue: {d.issue}");
                    sb.AppendLine();
                }
            }

            sb.AppendLine();
            sb.AppendLine("=== ROOT CAUSE DETERMINATION ===");
            sb.AppendLine($"Cause: {cause}");
            sb.AppendLine();
            sb.AppendLine(evidence);

            EnsureDirectory();
            File.WriteAllText(DIFF_OUTPUT, sb.ToString());
            AssetDatabase.Refresh();
        }

        private static void PrintQuickSummary(AuditReportV2 report)
        {
            Debug.Log($"\n=== QUICK CHECK ({report.mode}) ===");
            Debug.Log($"Source: {report.characterSource}");

            var eyeBase = report.renderers.FirstOrDefault(r => r.category == "EYE_BASE");
            if (eyeBase != null)
            {
                Debug.Log($"\nEYE BASE ({eyeBase.path}):");
                foreach (var m in eyeBase.materials)
                {
                    var mainTex = m.textures.FirstOrDefault(t => t.propertyName == "_MainTex");
                    string status = mainTex != null && !mainTex.isNull ? "OK" : "MISSING!";
                    Debug.Log($"  [{m.slotIndex}] {m.materialName}: _MainTex={status}");
                }
            }
        }

        private static void EnsureDirectory()
        {
            string dir = Path.GetDirectoryName(JSON_OUTPUT);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static string GetPath(GameObject obj, Transform root)
        {
            var parts = new List<string>();
            var current = obj.transform;
            while (current != null && current != root)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }
            return string.Join("/", parts);
        }

        #endregion
    }
}
