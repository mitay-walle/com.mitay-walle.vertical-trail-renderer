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
		public enum UpMode
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

		[SerializeField] private int _maxSegments = 250;
		[SerializeField] private float _minDistance = 0.2f;
		[SerializeField] private float _wallHeight = 5f;
		[SerializeField] private float _trailLifetime = 3f;
		[SerializeField] private bool _fadeTrail = true;
		[SerializeField] private AnimationCurve _fadeCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
		[SerializeField] private Material _trailMaterial;
		[SerializeField] private UpMode _upMode = UpMode.World;

		[SerializeField] private ShadowCastingMode _shadowCastingMode = ShadowCastingMode.Off;
		[SerializeField] private bool _receiveShadows;
		[SerializeField] private bool _motionVectors;
		[SerializeField, RenderingLayersMaskProperty] private uint _renderingLayerMask = 1;
		[SerializeField] private LightProbeUsage _lightProbeUsage = LightProbeUsage.Off;
		[SerializeField] private LightProbeProxyVolume _lightProbeProxyVolumeOverride;

		private Mesh _mesh;

		private Vector3[] _vertices;
		private Vector2[] _uvs;
		private Color[] _colors;
		private int[] _triangles;

		private Vector3[] _points;
		private Vector3[] _pointUps;
		private float[] _pointTimes;
		private int _pointCount;
		private Vector3 _lastPos;

		private Bounds _worldBounds;

		private void OnEnable()
		{
			InitializeMesh();
		}

		private void OnValidate()
		{
			using ProfilerMarker.AutoScope _ = ValidateMarker.Auto();

			if (_maxSegments < 1)
				_maxSegments = 1;

			if (_minDistance < 0f)
				_minDistance = 0f;

			if (_trailLifetime < 0f)
				_trailLifetime = 0f;

			if (_wallHeight < 0f)
				_wallHeight = 0f;

			if (!isActiveAndEnabled)
				return;

			InitializeMesh();
		}

		private void Update()
		{
			using ProfilerMarker.AutoScope _ = UpdateMarker.Auto();

			if (_points == null || _mesh == null)
				InitializeMesh();

			Vector3 pos = transform.position;
			if ((pos - _lastPos).sqrMagnitude >= _minDistance * _minDistance)
				AddPoint(pos);

			RemoveOldPoints();
			UpdateMesh();

			if (_mesh != null && _trailMaterial != null && _pointCount >= 2)
				RenderMesh();
		}

		private void InitializeMesh()
		{
			using ProfilerMarker.AutoScope _ = InitializeMeshMarker.Auto();

			if (_maxSegments <= 0)
				return;

			int maxVertices = (_maxSegments + 1) * 2;
			int maxTriangles = _maxSegments * 6;

			_vertices = new Vector3[maxVertices];
			_uvs = new Vector2[maxVertices];
			_colors = new Color[maxVertices];
			_triangles = new int[maxTriangles];

			_points = new Vector3[_maxSegments + 1];
			_pointUps = new Vector3[_maxSegments + 1];
			_pointTimes = new float[_maxSegments + 1];
			_pointCount = 0;
			_lastPos = transform.position;
			_worldBounds = new Bounds(transform.position, Vector3.zero);

			if (_mesh == null)
			{
				_mesh = new Mesh
				{
					name = "VerticalTrail"
				};

				_mesh.MarkDynamic();
			}
			else
			{
				_mesh.Clear();
			}

			_mesh.vertices = _vertices;
			_mesh.uv = _uvs;
			_mesh.colors = _colors;
			_mesh.triangles = _triangles;

			if (Application.isPlaying)
				AddPoint(transform.position);
		}

		private void AddPoint(Vector3 basePos)
		{
			using ProfilerMarker.AutoScope _ = AddPointMarker.Auto();

			if (_pointCount >= _maxSegments + 1)
				return;

			_points[_pointCount] = basePos;
			_pointUps[_pointCount] = transform.up;
			_pointTimes[_pointCount] = Time.time;
			_pointCount++;
			_lastPos = basePos;
		}

		private void RemoveOldPoints()
		{
			using ProfilerMarker.AutoScope _ = RemoveOldPointsMarker.Auto();

			float currentTime = Time.time;
			int removeCount = 0;

			for (int i = 0; i < _pointCount; i++)
			{
				if (currentTime - _pointTimes[i] > _trailLifetime)
					removeCount++;
				else
					break;
			}

			if (removeCount <= 0)
				return;

			for (int i = 0; i < _pointCount - removeCount; i++)
			{
				_points[i] = _points[i + removeCount];
				_pointUps[i] = _pointUps[i + removeCount];
				_pointTimes[i] = _pointTimes[i + removeCount];
			}

			_pointCount -= removeCount;
		}

		private Vector3 GetUp(int index)
		{
			switch (_upMode)
			{
				case UpMode.World:
					return Vector3.up;

				case UpMode.Local:
					return transform.up;

				case UpMode.CachedPerPoint:
					return _pointUps[index];

				default:
					return transform.up;
			}
		}

		private void UpdateMesh()
		{
			using ProfilerMarker.AutoScope _ = UpdateMeshMarker.Auto();

			if (_mesh == null)
				return;

			if (_pointCount < 2)
			{
				_mesh.Clear();
				_worldBounds = new Bounds(transform.position, Vector3.zero);
				return;
			}

			int vertexIndex = 0;
			int triangleIndex = 0;
			float currentTime = Time.time;

			{
				using ProfilerMarker.AutoScope __ = ClearUnusedVerticesMarker.Auto();

				for (int i = _pointCount * 2; i < _vertices.Length; i++)
				{
					_vertices[i] = Vector3.zero;
					_uvs[i] = Vector2.zero;
					_colors[i] = Color.clear;
				}
			}

			{
				using ProfilerMarker.AutoScope __ = BuildVerticesMarker.Auto();

				for (int i = 0; i < _pointCount; i++)
				{
					Vector3 worldBasePos = _points[i];
					Vector3 worldTopPos = worldBasePos + GetUp(i) * _wallHeight;

					_vertices[vertexIndex] = worldBasePos;
					_vertices[vertexIndex + 1] = worldTopPos;

					float u = _pointCount > 1 ? (float)i / (_pointCount - 1) : 0f;
					_uvs[vertexIndex] = new Vector2(u, 0f);
					_uvs[vertexIndex + 1] = new Vector2(u, 1f);

					Color color = Color.white;
					if (_fadeTrail)
					{
						float age = currentTime - _pointTimes[i];
						float normalizedAge = _trailLifetime > 0f
							? Mathf.Clamp01(age / _trailLifetime)
							: 1f;

						color.a = _fadeCurve.Evaluate(normalizedAge);
					}

					_colors[vertexIndex] = color;
					_colors[vertexIndex + 1] = color;

					vertexIndex += 2;
				}
			}

			{
				using ProfilerMarker.AutoScope __ = ClearUnusedTrianglesMarker.Auto();

				for (int i = (_pointCount - 1) * 6; i < _triangles.Length; i++)
					_triangles[i] = 0;
			}

			{
				using ProfilerMarker.AutoScope __ = BuildTrianglesMarker.Auto();

				for (int i = 0; i < _pointCount - 1; i++)
				{
					int baseIndex = i * 2;

					_triangles[triangleIndex] = baseIndex;
					_triangles[triangleIndex + 1] = baseIndex + 1;
					_triangles[triangleIndex + 2] = baseIndex + 2;

					_triangles[triangleIndex + 3] = baseIndex + 1;
					_triangles[triangleIndex + 4] = baseIndex + 3;
					_triangles[triangleIndex + 5] = baseIndex + 2;

					triangleIndex += 6;
				}
			}

			{
				using ProfilerMarker.AutoScope __ = UploadMeshMarker.Auto();
				_mesh.vertices = _vertices;
				_mesh.uv = _uvs;
				_mesh.colors = _colors;
				_mesh.triangles = _triangles;
			}

			{
				using ProfilerMarker.AutoScope __ = RecalculateNormalsMarker.Auto();
				_mesh.RecalculateNormals();
			}

			{
				using ProfilerMarker.AutoScope __ = RecalculateBoundsMarker.Auto();
				_mesh.RecalculateBounds();
				_worldBounds = _mesh.bounds;
			}
		}

		private void RenderMesh()
		{
			using ProfilerMarker.AutoScope _ = RenderMeshMarker.Auto();

			RenderParams rp = new RenderParams(_trailMaterial)
			{
				layer = gameObject.layer,
				renderingLayerMask = _renderingLayerMask,
				worldBounds = _worldBounds,
				shadowCastingMode = _shadowCastingMode,
				receiveShadows = _receiveShadows,
				motionVectorMode = _motionVectors ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera,
				lightProbeUsage = _lightProbeUsage,
				lightProbeProxyVolume = _lightProbeProxyVolumeOverride,
			};

			Graphics.RenderMesh(rp, _mesh, 0, Matrix4x4.identity);
		}

		private void OnDisable()
		{
			if (_mesh != null)
				_mesh.Clear();
		}

		private void OnDestroy()
		{
			if (_mesh == null)
				return;

			if (Application.isPlaying)
				Destroy(_mesh);
			else
				DestroyImmediate(_mesh);
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
		private static string[] m_DefaultRenderingLayerNames;

		private static string[] defaultRenderingLayerNames
		{
			get
			{
				if (m_DefaultRenderingLayerNames == null)
				{
					m_DefaultRenderingLayerNames = new string[32];
					for (int i = 0; i < m_DefaultRenderingLayerNames.Length; ++i)
					{
						m_DefaultRenderingLayerNames[i] = string.Format("Layer{0}", i + 1);
					}
				}

				return m_DefaultRenderingLayerNames;
			}
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			RenderPipelineAsset srpAsset = GraphicsSettings.currentRenderPipeline;
			bool usingSRP = srpAsset != null;
			if (!usingSRP) { return; }

			#if UNITY_6000_0_OR_NEWER
			string[] layerNames = RenderingLayerMask.GetDefinedRenderingLayerNames();
			#else
			string[] layerNames = srpAsset.renderingLayerMaskNames;
			#endif
			if (layerNames == null)
			{
				layerNames = defaultRenderingLayerNames;
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