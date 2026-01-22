# Free Avatar Import Guide

How to import a free humanoid 3D avatar and connect it to LipSyncController.

## Asset Selection Criteria

Choose an avatar that meets these requirements:

| Requirement | Why |
|-------------|-----|
| **Free license** | Must allow use in demos/prototypes |
| **Humanoid rig** | Unity's Humanoid system for animations |
| **Jaw bone OR blendshapes** | Required for lip sync (mouth open/close) |
| **FBX format** | Best Unity compatibility |

## Search Keywords (Unity Asset Store)

Open **Window > Asset Store** or visit the Asset Store in browser. Use these searches:

1. **"free humanoid character"** - General humanoid models
2. **"free anime character unity"** - Stylized characters (often have blendshapes)
3. **"free low poly character"** - Lightweight models good for mobile
4. **"free robot character"** - Mechanical characters with jaw rigs
5. **"free medieval character"** - Fantasy style characters

Filter by: **Price: Free**, **Category: 3D > Characters > Humanoids**

## Import Steps

### 1. Download from Asset Store

```
Window > Asset Store > Search > [your keywords]
> Filter: Free
> Click asset > "Add to My Assets"
> "Open in Unity" > Import
```

### 2. Import via Package Manager

```
Window > Package Manager
> Packages: My Assets (dropdown, top-left)
> Find your downloaded asset
> Click "Import" (bottom-right)
> Import All (or select specific folders)
```

### 3. Locate the Prefab

After import, find the model:
```
Project window > Search: "t:Model" or "t:Prefab"
```

Common locations:
- `Assets/[AssetName]/Prefabs/`
- `Assets/[AssetName]/Models/`
- `Assets/[AssetName]/Characters/`

## Configure Humanoid Rig

1. Select the FBX model in Project window
2. In Inspector, click **Rig** tab
3. Set **Animation Type**: `Humanoid`
4. Click **Apply**
5. Click **Configure...** to verify bone mapping
6. Ensure all required bones show green checkmarks
7. Click **Done**

## Place in TutorRoom Scene

1. Open `Assets/Scenes/TutorRoom.unity`
2. Delete or disable the placeholder **Avatar** (Capsule)
3. Drag your character prefab into the Hierarchy
4. Position: `X=0, Y=0, Z=0` (adjust as needed)
5. Rotation: Face the camera (`Y=180` if facing wrong way)
6. Scale: Adjust so character fills ~60% of screen height

## Wire Up LipSyncController

### Option A: Jaw Bone (Preferred)

1. Select your avatar in Hierarchy
2. Expand the skeleton to find the jaw bone:
   - Common names: `Jaw`, `jaw`, `Head_Jaw`, `Bip01_Jaw`, `Chin`
3. Select **LipSyncController** in Hierarchy
4. In Inspector, drag the jaw bone to **Jaw Bone** field
5. Adjust settings:
   - **Rotation Axis**: Usually `(1, 0, 0)` for X-axis
   - **Max Jaw Angle**: `10-20` degrees
   - **Responsiveness**: `15`

### Option B: Blendshapes

1. Find the **SkinnedMeshRenderer** on your avatar (usually on face/body mesh)
2. In Inspector, expand **BlendShapes** to see available shapes
3. Note the index of mouth-open shape (names like `Mouth_Open`, `A`, `aa`, `vrc.v_aa`)
4. Select **LipSyncController** in Hierarchy
5. Assign:
   - **Skinned Mesh**: Drag the SkinnedMeshRenderer component
   - **Mouth Open Blendshape Index**: The index number (0-based)
   - **Max Blendshape Weight**: `100`

### Assign AudioSource

The TtsPlayer creates its own AudioSource, but LipSyncController needs to read from it:

1. Ensure **TtsPlayer** exists in scene (created automatically by TutorRoomController)
2. LipSyncController subscribes to TtsPlayer events automatically
3. No manual AudioSource assignment needed

## Add Idle Animation (Optional)

### If Asset Includes Animations

1. Find animation clips in the asset folder
2. Create **Animator Controller**: `Assets > Create > Animator Controller`
3. Open it (double-click), drag idle clip to canvas
4. Set as default state (right-click > Set as Layer Default State)
5. Add Animator component to avatar, assign controller

### Fallback: Unity's Default

```
Window > Package Manager > Unity Registry
> Search "Starter Assets" or "Animation Rigging"
> Import basic idle animations
```

### Minimal Breathing Script (No Animation)

Add this script for subtle movement without animations:

```csharp
// IdleBreathing.cs - attach to avatar root
using UnityEngine;
public class IdleBreathing : MonoBehaviour {
    [SerializeField] float amplitude = 0.02f;
    [SerializeField] float speed = 1f;
    Vector3 startPos;
    void Start() => startPos = transform.localPosition;
    void Update() => transform.localPosition = startPos +
        Vector3.up * Mathf.Sin(Time.time * speed) * amplitude;
}
```

## Troubleshooting

### No Jaw Bone Found

1. Check skeleton hierarchy for alternative names
2. Use **Auto-Configure** in LipSyncController (context menu)
3. Fall back to blendshapes if no jaw exists

### No Blendshapes Available

1. Model may not have facial morphs - use jaw bone instead
2. Create simple jaw bone: Add empty child to head, animate via script
3. Consider different avatar asset

### Rig Not Humanoid

1. Select FBX in Project
2. Inspector > Rig > Animation Type: **Humanoid**
3. Click **Apply**
4. If auto-mapping fails, click **Configure** and manually assign bones

### Avatar Too Big/Small

1. Select avatar root in Hierarchy
2. Adjust Scale uniformly (e.g., `0.5, 0.5, 0.5` or `2, 2, 2`)
3. Or: Select FBX > Inspector > Model > Scale Factor > Apply

### Materials Pink (Shader Error)

**For URP projects:**
```
Edit > Rendering > Materials > Convert Built-in to URP
```

**Manual fix:**
1. Select each material in Project
2. Change Shader to `Universal Render Pipeline/Lit`

**For Built-in Render Pipeline:**
1. Materials should work by default
2. If pink, reassign Standard shader

### Mouth Moves Wrong Direction

1. Try different **Rotation Axis** in LipSyncController:
   - `(1,0,0)` = X-axis (most common)
   - `(0,1,0)` = Y-axis
   - `(0,0,1)` = Z-axis
2. Try negative values: `(-1,0,0)`
3. Adjust **Max Jaw Angle** (negative opens jaw upward)

### Lip Sync Not Working

1. Verify TtsPlayer is playing audio (check Console logs)
2. Verify LipSyncController has valid target (jaw or blendshape)
3. Increase **Sensitivity** value (try 10-20)
4. Decrease **Threshold** value (try 0.005)
5. Test manually: Right-click LipSyncController > "Test Open Mouth"
