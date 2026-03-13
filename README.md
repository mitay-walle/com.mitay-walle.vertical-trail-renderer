<img width="876" height="583" alt="image_2026-03-13_16-15-57" src="https://github.com/user-attachments/assets/fdbdd0d4-e037-4ba2-a7a9-bd2370a5d6f4" />
<img width="551" height="310" alt="image_2026-03-13_16-13-55" src="https://github.com/user-attachments/assets/3411e8dc-0cc4-45ae-8a12-0c72441f9e19" />

# Problem
Using [Trail Renderer](https://docs.unity3d.com/Manual/class-TrailRenderer.html) to draw vertical trails or walls can produce incorrect results when the object has a non-zero Z transform or moves in 3D space.  
The issue is discussed here:  
https://discussions.unity.com/t/vertical-line-using-line-renderer-with-transform-z/762604

# Solution
`VerticalTrailRenderer`-script that generates a mesh, to draw vertical trail.  
It records object movement and builds a vertical quad strip using a [Mesh](https://docs.unity3d.com/Manual/class-Mesh.html) rendered by a [MeshRenderer](https://docs.unity3d.com/Manual/class-MeshRenderer.html).

- vertical trail generated from movement points  
- configurable lifetime and fade  
- no GC  

# Installation
- Place the script anywhere inside **`Project/Assets/`** in a Unity project
- Add the script to a GameObject.
- Assign a material to `_trailMaterial`.
- Move the object — the script generates a vertical trail mesh automatically.
