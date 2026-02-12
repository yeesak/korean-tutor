using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Editor tool to find and test which node actually controls visible teeth movement.
/// Use this to identify the correct jaw/teeth driver before wiring runtime.
/// </summary>
public class TeethFinderAndTester : EditorWindow
{
    GameObject _characterRoot;
    Transform[] _transformCandidates;
    SkinnedMeshRenderer[] _skinnedMeshes;
    float _testOpen = 0f;
    Vector2 _scrollPos;

    // Store base transforms for reset
    Dictionary<Transform, Vector3> _basePositions = new Dictionary<Transform, Vector3>();
    Dictionary<Transform, Quaternion> _baseRotations = new Dictionary<Transform, Quaternion>();

    [MenuItem("Tools/Teeth Debug/Teeth Finder & Tester")]
    static void Open() => GetWindow<TeethFinderAndTester>("Teeth Tester");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Find the EXACT node that moves visible teeth", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // Step 1: Find character
        EditorGUILayout.LabelField("Step 1: Find Character", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Find CharacterModel"))
        {
            _characterRoot = FindCharacterModel();
            if (_characterRoot != null)
            {
                BuildCandidates();
            }
        }
        _characterRoot = (GameObject)EditorGUILayout.ObjectField(_characterRoot, typeof(GameObject), true);
        EditorGUILayout.EndHorizontal();

        if (_characterRoot == null)
        {
            EditorGUILayout.HelpBox("Click 'Find CharacterModel' or drag the character root here.", MessageType.Warning);
            return;
        }

        EditorGUILayout.Space(8);

        // Step 2: List candidates
        EditorGUILayout.LabelField("Step 2: Candidates", EditorStyles.miniBoldLabel);
        if (GUILayout.Button("Rebuild Candidate List"))
        {
            BuildCandidates();
        }

        EditorGUILayout.LabelField($"Transform Candidates: {(_transformCandidates?.Length ?? 0)}");
        EditorGUILayout.LabelField($"SkinnedMeshRenderers: {(_skinnedMeshes?.Length ?? 0)}");

        EditorGUILayout.Space(8);

        // Step 3: Test
        EditorGUILayout.LabelField("Step 3: Test (Play Mode Only)", EditorStyles.miniBoldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter PLAY MODE to test teeth movement!", MessageType.Warning);
        }

        _testOpen = EditorGUILayout.Slider("Test Open (0..1)", _testOpen, 0f, 1f);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply Test to ALL Transforms"))
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[TeethTester] Enter Play Mode first!");
            }
            else
            {
                CacheBaseTransforms();
                ApplyTestToAllTransforms(_testOpen);
            }
        }
        if (GUILayout.Button("Reset All"))
        {
            if (Application.isPlaying)
            {
                ResetAllTransforms();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        // Scrollable list of candidates with individual test buttons
        EditorGUILayout.LabelField("Step 4: Test Individual Candidates", EditorStyles.miniBoldLabel);
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(300));

        if (_transformCandidates != null)
        {
            EditorGUILayout.LabelField("=== TRANSFORM CANDIDATES ===", EditorStyles.boldLabel);
            foreach (var t in _transformCandidates)
            {
                if (t == null) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(t.name, GUILayout.Width(180));
                if (GUILayout.Button("Test", GUILayout.Width(50)))
                {
                    if (Application.isPlaying)
                    {
                        CacheBaseTransform(t);
                        ApplyTestToSingleTransform(t, _testOpen);
                        Debug.Log($"[TeethTester] Testing: {GetPath(t)}");
                    }
                }
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                {
                    if (Application.isPlaying) ResetSingleTransform(t);
                }
                if (GUILayout.Button("Copy Path", GUILayout.Width(70)))
                {
                    EditorGUIUtility.systemCopyBuffer = GetPath(t);
                    Debug.Log($"[TeethTester] Copied: {GetPath(t)}");
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        if (_skinnedMeshes != null)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("=== SKINNED MESHES (check for blendshapes) ===", EditorStyles.boldLabel);
            foreach (var smr in _skinnedMeshes)
            {
                if (smr == null || smr.sharedMesh == null) continue;

                int blendCount = smr.sharedMesh.blendShapeCount;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{smr.name} ({blendCount} blendshapes)", GUILayout.Width(250));
                if (GUILayout.Button("List Blendshapes", GUILayout.Width(100)))
                {
                    ListBlendshapes(smr);
                }
                EditorGUILayout.EndHorizontal();

                // Show jaw/mouth related blendshapes
                if (blendCount > 0)
                {
                    for (int i = 0; i < blendCount; i++)
                    {
                        string bsName = smr.sharedMesh.GetBlendShapeName(i);
                        string lower = bsName.ToLowerInvariant();
                        if (lower.Contains("jaw") || lower.Contains("mouth") || lower.Contains("open") ||
                            lower.Contains("teeth") || lower.Contains("viseme"))
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField($"  [{i}] {bsName}", GUILayout.Width(220));
                            if (GUILayout.Button("Test 100", GUILayout.Width(60)))
                            {
                                if (Application.isPlaying)
                                {
                                    smr.SetBlendShapeWeight(i, 100f);
                                    Debug.Log($"[TeethTester] Set blendshape {bsName} = 100");
                                }
                            }
                            if (GUILayout.Button("Reset", GUILayout.Width(50)))
                            {
                                if (Application.isPlaying)
                                {
                                    smr.SetBlendShapeWeight(i, 0f);
                                }
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                }
            }
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "HOW TO USE:\n" +
            "1) Enter Play Mode\n" +
            "2) Click 'Find CharacterModel'\n" +
            "3) Click individual 'Test' buttons and WATCH which one moves the visible teeth\n" +
            "4) Copy the path of the working node\n" +
            "5) Use that node in TtsTeethDriver (assign in Inspector)\n\n" +
            "If NO transform works, check the SKINNED MESHES section for blendshapes.",
            MessageType.Info);
    }

    GameObject FindCharacterModel()
    {
        // Try exact paths
        string[] paths = { "Avatar/CharacterModel", "CharacterModel", "Avatar" };
        foreach (var path in paths)
        {
            var go = GameObject.Find(path);
            if (go != null)
            {
                Debug.Log($"[TeethTester] Found: {path}");
                return go;
            }
        }

        // Fallback: search for anything with CharacterModel in name
        var all = Object.FindObjectsOfType<GameObject>()
            .Where(g => g.activeInHierarchy)
            .Where(g => (g.name ?? "").ToLowerInvariant().Contains("character"))
            .FirstOrDefault();

        if (all != null)
        {
            Debug.Log($"[TeethTester] Found (fallback): {all.name}");
            return all;
        }

        // Last resort: find CC_Base_Body and go to root
        var body = Object.FindObjectsOfType<SkinnedMeshRenderer>()
            .FirstOrDefault(r => r.name == "CC_Base_Body");
        if (body != null)
        {
            Debug.Log($"[TeethTester] Found via CC_Base_Body");
            return body.transform.root.gameObject;
        }

        Debug.LogError("[TeethTester] Could not find CharacterModel!");
        return null;
    }

    void BuildCandidates()
    {
        if (_characterRoot == null) return;

        var allTransforms = _characterRoot.GetComponentsInChildren<Transform>(true);

        // Find transform candidates
        _transformCandidates = allTransforms.Where(t =>
        {
            var n = (t.name ?? "").ToLowerInvariant();
            return n.Contains("jaw") || n.Contains("teeth") || n.Contains("tooth") ||
                   n.Contains("mouth") || n.Contains("facial") || n.Contains("lip");
        }).ToArray();

        // Find skinned mesh renderers
        _skinnedMeshes = _characterRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        Debug.Log($"[TeethTester] Found {_transformCandidates.Length} transform candidates, {_skinnedMeshes.Length} skinned meshes");

        // Log all candidates
        foreach (var t in _transformCandidates)
        {
            Debug.Log($"[TeethTester] Transform: {GetPath(t)}");
        }
    }

    void CacheBaseTransforms()
    {
        if (_transformCandidates == null) return;
        foreach (var t in _transformCandidates)
        {
            CacheBaseTransform(t);
        }
    }

    void CacheBaseTransform(Transform t)
    {
        if (t == null) return;
        if (!_basePositions.ContainsKey(t))
        {
            _basePositions[t] = t.localPosition;
            _baseRotations[t] = t.localRotation;
        }
    }

    void ApplyTestToAllTransforms(float open01)
    {
        if (_transformCandidates == null) return;
        foreach (var t in _transformCandidates)
        {
            ApplyTestToSingleTransform(t, open01);
        }
        Debug.Log($"[TeethTester] Applied test (open={open01:F2}) to {_transformCandidates.Length} transforms. WATCH which one moves teeth!");
    }

    void ApplyTestToSingleTransform(Transform t, float open01)
    {
        if (t == null) return;

        // Apply vertical-only motion: pitch rotation + Y translation
        float pitch = 12f * open01;
        float yOffset = -0.004f * open01;

        Vector3 basePos = _basePositions.ContainsKey(t) ? _basePositions[t] : t.localPosition;
        Quaternion baseRot = _baseRotations.ContainsKey(t) ? _baseRotations[t] : t.localRotation;

        t.localRotation = baseRot * Quaternion.Euler(pitch, 0f, 0f);

        Vector3 newPos = basePos;
        newPos.y += yOffset;
        t.localPosition = newPos;
    }

    void ResetAllTransforms()
    {
        if (_transformCandidates == null) return;
        foreach (var t in _transformCandidates)
        {
            ResetSingleTransform(t);
        }
        Debug.Log("[TeethTester] Reset all transforms");
    }

    void ResetSingleTransform(Transform t)
    {
        if (t == null) return;
        if (_basePositions.ContainsKey(t))
        {
            t.localPosition = _basePositions[t];
        }
        if (_baseRotations.ContainsKey(t))
        {
            t.localRotation = _baseRotations[t];
        }
    }

    void ListBlendshapes(SkinnedMeshRenderer smr)
    {
        if (smr == null || smr.sharedMesh == null) return;
        var mesh = smr.sharedMesh;
        int count = mesh.blendShapeCount;
        Debug.Log($"[TeethTester] === {smr.name} has {count} blendshapes ===");
        for (int i = 0; i < count; i++)
        {
            string name = mesh.GetBlendShapeName(i);
            Debug.Log($"[TeethTester]   [{i}] {name}");
        }
    }

    static string GetPath(Transform t)
    {
        if (t == null) return "";
        string path = t.name;
        int depth = 0;
        while (t.parent != null && depth < 10)
        {
            t = t.parent;
            path = t.name + "/" + path;
            depth++;
        }
        return path;
    }
}
