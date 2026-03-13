using Unity.Profiling;
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
		[SerializeField] private uint _renderingLayerMask = 1;
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
			using var _ = ValidateMarker.Auto();

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
			using var _ = UpdateMarker.Auto();

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
			using var _ = InitializeMeshMarker.Auto();

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
			using var _ = AddPointMarker.Auto();

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
			using var _ = RemoveOldPointsMarker.Auto();

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
			using var _ = UpdateMeshMarker.Auto();

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

			Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
			Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

			{
				using var __ = ClearUnusedVerticesMarker.Auto();

				for (int i = _pointCount * 2; i < _vertices.Length; i++)
				{
					_vertices[i] = Vector3.zero;
					_uvs[i] = Vector2.zero;
					_colors[i] = Color.clear;
				}
			}

			{
				using var __ = BuildVerticesMarker.Auto();

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

					min = Vector3.Min(min, worldBasePos);
					min = Vector3.Min(min, worldTopPos);
					max = Vector3.Max(max, worldBasePos);
					max = Vector3.Max(max, worldTopPos);

					vertexIndex += 2;
				}
			}

			{
				using var __ = ClearUnusedTrianglesMarker.Auto();

				for (int i = (_pointCount - 1) * 6; i < _triangles.Length; i++)
					_triangles[i] = 0;
			}

			{
				using var __ = BuildTrianglesMarker.Auto();

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
				using var __ = UploadMeshMarker.Auto();
				_mesh.vertices = _vertices;
				_mesh.uv = _uvs;
				_mesh.colors = _colors;
				_mesh.triangles = _triangles;
			}

			{
				using var __ = RecalculateNormalsMarker.Auto();
				_mesh.RecalculateNormals();
			}

			{
				using var __ = RecalculateBoundsMarker.Auto();
				_mesh.RecalculateBounds();
			}

			_worldBounds.SetMinMax(min, max);
		}

		private void RenderMesh()
		{
			using var _ = RenderMeshMarker.Auto();

			var rp = new RenderParams(_trailMaterial)
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
}