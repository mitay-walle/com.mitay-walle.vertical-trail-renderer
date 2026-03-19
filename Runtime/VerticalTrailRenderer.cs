using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace mitaywalle
{
	[ExecuteAlways]
	public class VerticalTrailRenderer : MonoBehaviour
	{
		public enum eUpMode
		{
			World,
			Local,
			CachedPerPoint
		}

		private static readonly ProfilerMarker UpdateMarker = new("VerticalTrailRenderer.Update");
		private static readonly ProfilerMarker InitializeMeshMarker = new("VerticalTrailRenderer.InitializeMesh");
		private static readonly ProfilerMarker AddPointMarker = new("VerticalTrailRenderer.AddPoint");
		private static readonly ProfilerMarker RemoveOldPointsMarker = new("VerticalTrailRenderer.RemoveOldPoints");
		private static readonly ProfilerMarker UpdateMeshMarker = new("VerticalTrailRenderer.UpdateMesh");
		private static readonly ProfilerMarker ClearUnusedVerticesMarker = new("VerticalTrailRenderer.ClearUnusedVertices");
		private static readonly ProfilerMarker BuildVerticesMarker = new("VerticalTrailRenderer.BuildVertices");
		private static readonly ProfilerMarker ClearUnusedTrianglesMarker = new("VerticalTrailRenderer.ClearUnusedTriangles");
		private static readonly ProfilerMarker BuildTrianglesMarker = new("VerticalTrailRenderer.BuildTriangles");
		private static readonly ProfilerMarker UploadMeshMarker = new("VerticalTrailRenderer.UploadMesh");
		private static readonly ProfilerMarker RecalculateNormalsMarker = new("VerticalTrailRenderer.RecalculateNormals");
		private static readonly ProfilerMarker RecalculateBoundsMarker = new("VerticalTrailRenderer.RecalculateBounds");
		private static readonly ProfilerMarker RenderMeshMarker = new("VerticalTrailRenderer.RenderMesh");
		private static readonly ProfilerMarker ValidateMarker = new("VerticalTrailRenderer.Validate");

		[SerializeField] private int maxSegments = 250;

		public float MinDistance = 0.2f;
		public Vector3 WorldOffset;
		public Vector3 LocalOffset;
		public float WallHeight = 5f;
		public float TrailLifetime = 3f;
		public bool FadeTrail = true;
		public AnimationCurve FadeCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
		public Material TrailMaterial;
		public eUpMode UpMode = eUpMode.World;
		public bool DoubleSideGeometry;

		public ShadowCastingMode ShadowCastingMode = ShadowCastingMode.Off;
		public bool ReceiveShadows;
		public bool MotionVectors;
		public LightProbeUsage LightProbeUsage = LightProbeUsage.Off;
		public LightProbeProxyVolume LightProbeProxyVolumeOverride;
		[RenderingLayersMaskProperty] public uint RenderingLayerMask = 1;

		private Mesh mesh;

		private Vector3[] vertices;
		private Vector2[] uvs;
		private Color[] colors;
		private int[] triangles;

		private Vector3[] points;
		private Vector3[] pointUps;
		private float[] pointTimes;
		private int pointCount;
		private Vector3 lastPos;

		private Bounds worldBounds;

		private void OnEnable()
		{
			InitializeMesh();
		}

		private void OnValidate()
		{
			using ProfilerMarker.AutoScope _ = ValidateMarker.Auto();

			if (maxSegments < 1)
				maxSegments = 1;

			if (MinDistance < 0f)
				MinDistance = 0f;

			if (TrailLifetime < 0f)
				TrailLifetime = 0f;

			if (WallHeight < 0f)
				WallHeight = 0f;

			if (!isActiveAndEnabled)
				return;

			InitializeMesh();
		}

		private void Update()
		{
			using ProfilerMarker.AutoScope _ = UpdateMarker.Auto();

			if (points == null || mesh == null)
				InitializeMesh();

			Vector3 pos = WorldOffset + transform.TransformPoint(LocalOffset);

			if ((pos - lastPos).sqrMagnitude >= MinDistance * MinDistance)
				AddPoint(pos);

			RemoveOldPoints();
			UpdateMesh();

			if (mesh != null && TrailMaterial != null && pointCount >= 2)
				RenderMesh();
		}

		private void InitializeMesh()
		{
			using ProfilerMarker.AutoScope _ = InitializeMeshMarker.Auto();

			if (maxSegments <= 0)
				return;

			int sideMultiplier = DoubleSideGeometry ? 2 : 1;
			int maxVertices = (maxSegments + 1) * 2 * sideMultiplier;
			int maxTriangles = maxSegments * 6 * sideMultiplier;

			vertices = new Vector3[maxVertices];
			uvs = new Vector2[maxVertices];
			colors = new Color[maxVertices];
			triangles = new int[maxTriangles];

			points = new Vector3[maxSegments + 1];
			pointUps = new Vector3[maxSegments + 1];
			pointTimes = new float[maxSegments + 1];
			pointCount = 0;
			lastPos = transform.position;
			worldBounds = new Bounds(transform.position, Vector3.zero);

			if (mesh == null)
			{
				mesh = new Mesh
				{
					name = "VerticalTrail"
				};

				mesh.MarkDynamic();
			}
			else
			{
				mesh.Clear();
			}

			mesh.vertices = vertices;
			mesh.uv = uvs;
			mesh.colors = colors;
			mesh.triangles = triangles;

			if (Application.isPlaying)
				AddPoint(transform.position);
		}

		private void AddPoint(Vector3 basePos)
		{
			using ProfilerMarker.AutoScope _ = AddPointMarker.Auto();

			if (pointCount >= maxSegments + 1)
				return;

			points[pointCount] = basePos;
			pointUps[pointCount] = transform.up;
			pointTimes[pointCount] = Time.time;
			pointCount++;
			lastPos = basePos;
		}

		private void RemoveOldPoints()
		{
			using ProfilerMarker.AutoScope _ = RemoveOldPointsMarker.Auto();

			float currentTime = Time.time;
			int removeCount = 0;

			for (int i = 0; i < pointCount; i++)
			{
				if (currentTime - pointTimes[i] > TrailLifetime)
					removeCount++;
				else
					break;
			}

			if (removeCount <= 0)
				return;

			for (int i = 0; i < pointCount - removeCount; i++)
			{
				points[i] = points[i + removeCount];
				pointUps[i] = pointUps[i + removeCount];
				pointTimes[i] = pointTimes[i + removeCount];
			}

			pointCount -= removeCount;
		}

		private Vector3 GetUp(int index)
		{
			switch (UpMode)
			{
				case eUpMode.World:
					return Vector3.up;

				case eUpMode.Local:
					return transform.up;

				case eUpMode.CachedPerPoint:
					return pointUps[index];

				default:
					return transform.up;
			}
		}

		private void UpdateMesh()
		{
			using ProfilerMarker.AutoScope _ = UpdateMeshMarker.Auto();

			if (mesh == null)
				return;

			if (pointCount < 2)
			{
				mesh.Clear();
				worldBounds = new Bounds(transform.position, Vector3.zero);
				return;
			}

			float currentTime = Time.time;
			int sideMultiplier = DoubleSideGeometry ? 2 : 1;
			int usedVertexCount = pointCount * 2 * sideMultiplier;
			int usedTriangleCount = (pointCount - 1) * 6 * sideMultiplier;
			int backVertexOffset = pointCount * 2;

			{
				using ProfilerMarker.AutoScope __ = ClearUnusedVerticesMarker.Auto();

				for (int i = usedVertexCount; i < vertices.Length; i++)
				{
					vertices[i] = Vector3.zero;
					uvs[i] = Vector2.zero;
					colors[i] = Color.clear;
				}
			}

			{
				using ProfilerMarker.AutoScope __ = BuildVerticesMarker.Auto();

				for (int i = 0; i < pointCount; i++)
				{
					Vector3 worldBasePos = points[i];
					Vector3 worldTopPos = worldBasePos + GetUp(i) * WallHeight;
					int frontVertexIndex = i * 2;

					vertices[frontVertexIndex] = worldBasePos;
					vertices[frontVertexIndex + 1] = worldTopPos;

					float u = pointCount > 1 ? (float)i / (pointCount - 1) : 0f;
					uvs[frontVertexIndex] = new Vector2(u, 0f);
					uvs[frontVertexIndex + 1] = new Vector2(u, 1f);

					Color color = Color.white;
					if (FadeTrail)
					{
						float age = currentTime - pointTimes[i];
						float normalizedAge = TrailLifetime > 0f
							? Mathf.Clamp01(age / TrailLifetime)
							: 1f;

						color.a = FadeCurve.Evaluate(normalizedAge);
					}

					colors[frontVertexIndex] = color;
					colors[frontVertexIndex + 1] = color;

					if (!DoubleSideGeometry)
						continue;

					int backVertexIndex = backVertexOffset + i * 2;
					vertices[backVertexIndex] = worldBasePos;
					vertices[backVertexIndex + 1] = worldTopPos;
					uvs[backVertexIndex] = new Vector2(1f - u, 0f);
					uvs[backVertexIndex + 1] = new Vector2(1f - u, 1f);
					colors[backVertexIndex] = color;
					colors[backVertexIndex + 1] = color;
				}
			}

			{
				using ProfilerMarker.AutoScope __ = ClearUnusedTrianglesMarker.Auto();

				for (int i = usedTriangleCount; i < triangles.Length; i++)
					triangles[i] = 0;
			}

			{
				using ProfilerMarker.AutoScope __ = BuildTrianglesMarker.Auto();
				int triangleIndex = 0;

				for (int i = 0; i < pointCount - 1; i++)
				{
					int baseIndex = i * 2;

					triangles[triangleIndex] = baseIndex;
					triangles[triangleIndex + 1] = baseIndex + 1;
					triangles[triangleIndex + 2] = baseIndex + 2;

					triangles[triangleIndex + 3] = baseIndex + 1;
					triangles[triangleIndex + 4] = baseIndex + 3;
					triangles[triangleIndex + 5] = baseIndex + 2;

					triangleIndex += 6;
				}

				if (DoubleSideGeometry)
				{
					for (int i = 0; i < pointCount - 1; i++)
					{
						int baseIndex = backVertexOffset + i * 2;

						triangles[triangleIndex] = baseIndex;
						triangles[triangleIndex + 1] = baseIndex + 2;
						triangles[triangleIndex + 2] = baseIndex + 1;

						triangles[triangleIndex + 3] = baseIndex + 1;
						triangles[triangleIndex + 4] = baseIndex + 2;
						triangles[triangleIndex + 5] = baseIndex + 3;

						triangleIndex += 6;
					}
				}
			}

			{
				using ProfilerMarker.AutoScope __ = UploadMeshMarker.Auto();
				mesh.vertices = vertices;
				mesh.uv = uvs;
				mesh.colors = colors;
				mesh.triangles = triangles;
			}

			{
				using ProfilerMarker.AutoScope __ = RecalculateNormalsMarker.Auto();
				mesh.RecalculateNormals();
			}

			{
				using ProfilerMarker.AutoScope __ = RecalculateBoundsMarker.Auto();
				mesh.RecalculateBounds();
				worldBounds = mesh.bounds;
			}
		}

		private void RenderMesh()
		{
			using ProfilerMarker.AutoScope _ = RenderMeshMarker.Auto();

			RenderParams rp = new RenderParams(TrailMaterial)
			{
				layer = gameObject.layer,
				renderingLayerMask = RenderingLayerMask,
				worldBounds = worldBounds,
				shadowCastingMode = ShadowCastingMode,
				receiveShadows = ReceiveShadows,
				motionVectorMode = MotionVectors ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera,
				lightProbeUsage = LightProbeUsage,
				lightProbeProxyVolume = LightProbeProxyVolumeOverride,
			};

			Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
		}

		private void OnDisable()
		{
			if (mesh != null)
				mesh.Clear();
		}

		private void OnDestroy()
		{
			if (mesh == null)
				return;

			if (Application.isPlaying)
				Destroy(mesh);
			else
				DestroyImmediate(mesh);
		}

		private void OnDrawGizmosSelected()
		{
			Vector3 pos = WorldOffset + transform.TransformPoint(LocalOffset);
			Gizmos.DrawWireSphere(pos, .1f);
		}
	}

	public class RenderingLayersMaskPropertyAttribute : PropertyAttribute
	{
		public RenderingLayersMaskPropertyAttribute() { }
	}

	#if UNITY_EDITOR
	[CustomPropertyDrawer(typeof(RenderingLayersMaskPropertyAttribute))]
	public class RenderingLayersMaskPropertyDrawer : PropertyDrawer
	{
		private static string[] M_DEFAULT_RENDERING_LAYER_NAMES;

		private static string[] DefaultRenderingLayerNames
		{
			get
			{
				if (M_DEFAULT_RENDERING_LAYER_NAMES == null)
				{
					M_DEFAULT_RENDERING_LAYER_NAMES = new string[32];
					for (int i = 0; i < M_DEFAULT_RENDERING_LAYER_NAMES.Length; ++i)
					{
						M_DEFAULT_RENDERING_LAYER_NAMES[i] = string.Format("Layer{0}", i + 1);
					}
				}

				return M_DEFAULT_RENDERING_LAYER_NAMES;
			}
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			RenderPipelineAsset srpAsset = GraphicsSettings.currentRenderPipeline;
			bool usingSrp = srpAsset != null;
			if (!usingSrp) { return; }

			#if UNITY_6000_0_OR_NEWER
			string[] layerNames = RenderingLayerMask.GetDefinedRenderingLayerNames();
			#else
			string[] layerNames = srpAsset.renderingLayerMaskNames;
			#endif
			if (layerNames == null)
			{
				layerNames = DefaultRenderingLayerNames;
			}

			object owner = GetParent(property);
			uint mask = (uint)fieldInfo.GetValue(owner);

			EditorGUI.BeginProperty(position, label, property);
			Rect fieldRect = EditorGUI.PrefixLabel(position, new GUIContent(property.displayName));
			uint newMask = (uint)EditorGUI.MaskField(fieldRect, (int)mask, layerNames);
			if (newMask != mask)
			{
				property.uintValue = newMask;
			}

			EditorGUI.EndProperty();
		}

		//
		public static object GetParent(SerializedProperty prop)
		{
			string path = prop.propertyPath.Replace(".Array.data[", "[");
			object obj = prop.serializedObject.targetObject;
			string[] elements = path.Split('.');
			foreach (string element in elements.Take(elements.Length - 1))
			{
				if (element.Contains("["))
				{
					string elementName = element.Substring(0, element.IndexOf("["));
					int index = Convert.ToInt32(element.Substring(element.IndexOf("[")).Replace("[", "").Replace("]", ""));
					obj = GetValue(obj, elementName, index);
				}
				else
				{
					obj = GetValue(obj, element);
				}
			}

			return obj;
		}

		private static object GetValue(object source, string name)
		{
			if (source == null)
				return null;

			Type type = source.GetType();
			FieldInfo f = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
			if (f == null)
			{
				PropertyInfo p = type.GetProperty(name,
					BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

				if (p == null)
					return null;

				return p.GetValue(source, null);
			}

			return f.GetValue(source);
		}

		private static object GetValue(object source, string name, int index)
		{
			IEnumerable enumerable = GetValue(source, name) as IEnumerable;
			IEnumerator enm = enumerable.GetEnumerator();
			while (index-- >= 0)
				enm.MoveNext();

			return enm.Current;
		}
	}

	#endif
}
