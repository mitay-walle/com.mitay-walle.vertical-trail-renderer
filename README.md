# Problem
Using [Line Renderer](https://docs.unity3d.com/Manual/class-LineRenderer.html) to draw vertical trails or walls can produce incorrect results when the object has a non-zero Z transform or moves in 3D space.  
The issue is discussed here:  
https://discussions.unity.com/t/vertical-line-using-line-renderer-with-transform-z/762604

# Solution
`VerticalTrailRenderer`-script that generates a mesh.  
It records object movement and builds a vertical quad strip using a [Mesh](https://docs.unity3d.com/Manual/class-Mesh.html) rendered by a [MeshRenderer](https://docs.unity3d.com/Manual/class-MeshRenderer.html).

- vertical trail generated from movement points  
- configurable lifetime and fade  
- no GC  

# Installation
- Place the script anywhere inside **`Project/Assets/`** in a Unity project
- Add the script to a GameObject.
- Assign a material to `_trailMaterial`.
- Move the object — the script generates a vertical trail mesh automatically.
