using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Debug helper to dump all blendshape names from SkinnedMeshRenderers.
    /// Attach to character root and use context menu to dump.
    /// </summary>
    public class BlendshapeDumper : MonoBehaviour
    {
        [ContextMenu("Dump Blendshapes")]
        public void Dump()
        {
            var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Debug.Log($"[BlendshapeDumper] Found {renderers.Length} SkinnedMeshRenderers under {name}");

            int totalBlendshapes = 0;
            int mouthRelated = 0;

            foreach (var r in renderers)
            {
                if (r == null || r.sharedMesh == null)
                {
                    Debug.Log($"[BlendshapeDumper] Renderer {r?.name} has no mesh");
                    continue;
                }

                var mesh = r.sharedMesh;
                int count = mesh.blendShapeCount;
                totalBlendshapes += count;

                Debug.Log($"[BlendshapeDumper] {r.name} mesh={mesh.name} blendShapeCount={count}");

                for (int i = 0; i < count; i++)
                {
                    string bsName = mesh.GetBlendShapeName(i);
                    string lower = bsName.ToLower();

                    // Check if mouth-related
                    bool isMouth = lower.Contains("mouth") || lower.Contains("jaw") ||
                                   lower.Contains("lip") || lower.Contains("viseme") ||
                                   lower.Contains("aa") || lower.Contains("oh") ||
                                   lower.Contains("ee") || lower.Contains("open");

                    string marker = isMouth ? " [MOUTH]" : "";
                    if (isMouth) mouthRelated++;

                    Debug.Log($"[BlendshapeDumper]   [{i}] {bsName}{marker}");
                }
            }

            Debug.Log($"[BlendshapeDumper] Summary: {totalBlendshapes} total blendshapes, {mouthRelated} mouth-related");
        }

        [ContextMenu("Dump Mouth Blendshapes Only")]
        public void DumpMouthOnly()
        {
            var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Debug.Log($"[BlendshapeDumper] Scanning {renderers.Length} renderers for mouth blendshapes...");

            foreach (var r in renderers)
            {
                if (r == null || r.sharedMesh == null) continue;

                var mesh = r.sharedMesh;
                int count = mesh.blendShapeCount;

                for (int i = 0; i < count; i++)
                {
                    string bsName = mesh.GetBlendShapeName(i);
                    string lower = bsName.ToLower();

                    bool isMouth = lower.Contains("mouth") || lower.Contains("jaw") ||
                                   lower.Contains("lip") || lower.Contains("viseme") ||
                                   lower.Contains("aa") || lower.Contains("oh") ||
                                   lower.Contains("ee") || lower.Contains("open") ||
                                   lower.Contains("fv") || lower.Contains("mbp");

                    if (isMouth)
                    {
                        Debug.Log($"[BlendshapeDumper] {r.name}[{i}] = {bsName}");
                    }
                }
            }
        }

        [ContextMenu("List All Transforms (Bones)")]
        public void DumpBones()
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            Debug.Log($"[BlendshapeDumper] Found {transforms.Length} transforms under {name}");

            foreach (var t in transforms)
            {
                string lower = t.name.ToLower();
                bool isJaw = lower.Contains("jaw");
                bool isLip = lower.Contains("lip");
                bool isMouth = lower.Contains("mouth");

                if (isJaw || isLip || isMouth)
                {
                    string marker = isJaw ? "[JAW]" : (isLip ? "[LIP]" : "[MOUTH]");
                    Debug.Log($"[BlendshapeDumper] {marker} {GetPath(t)}");
                }
            }
        }

        private string GetPath(Transform t)
        {
            string path = t.name;
            Transform current = t.parent;
            int depth = 0;
            while (current != null && depth < 5)
            {
                path = current.name + "/" + path;
                current = current.parent;
                depth++;
            }
            return path;
        }
    }
}
