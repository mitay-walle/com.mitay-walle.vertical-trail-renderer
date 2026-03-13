using UnityEngine;

namespace mitaywalle
{
	[ExecuteAlways]
	public class VerticalTrailRenderer : MonoBehaviour
	{
		[SerializeField] private int _maxSegments = 250;
		[SerializeField] private float _minDistance = 0.2f;
		[SerializeField] private float _wallHeight = 5f;
		[SerializeField] private float _trailLifetime = 3f;
		[SerializeField] private bool _fadeTrail = true;
		[SerializeField] private AnimationCurve _fadeCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
		[SerializeField] private Material _trailMaterial;

		[SerializeField] private GameObject _trailObject;
		private MeshFilter _meshFilter;
		private MeshRenderer _meshRenderer;
		private Mesh _mesh;

		// Фиксированные массивы для избежания GC
		private Vector3[] _vertices;
		private Vector2[] _uvs;
		private Color[] _colors;
		private int[] _triangles;

		// Данные точек
		private Vector3[] _points;
		private float[] _pointTimes;
		private int _pointCount = 0;
		private Vector3 _lastPos;

		private void OnEnable()
		{
			CreateTrailObject();
			InitializeMesh();
			if (_trailObject != null)
				_trailObject.SetActive(true);
		}

		private void OnDisable()
		{
			if (_trailObject != null)
				_trailObject.SetActive(false);
		}

		private void CreateTrailObject()
		{
			// Создаем отдельный объект для рендеринга трейла
			if (_trailObject == null)
			{
				_trailObject = new GameObject("Trail_" + gameObject.name);
				_trailObject.transform.SetParent(null); // Независимый от родителя
				_trailObject.transform.position = Vector3.zero;
				_trailObject.transform.rotation = Quaternion.identity;
				_trailObject.transform.localScale = Vector3.one;

				_meshFilter = _trailObject.AddComponent<MeshFilter>();
				_meshRenderer = _trailObject.AddComponent<MeshRenderer>();

				if (_trailMaterial != null)
					_meshRenderer.material = _trailMaterial;
			}
			else
			{
				_meshFilter = _trailObject.GetComponent<MeshFilter>();
				_meshRenderer = _trailObject.GetComponent<MeshRenderer>();

				if (_trailMaterial != null)
					_meshRenderer.material = _trailMaterial;
			}
		}

		private void Validate()
		{
			if (_maxSegments > 0)
			{
				if (_trailObject == null)
					CreateTrailObject();

				InitializeMesh();
			}

			// Обновляем материал если он изменился
			if (_meshRenderer != null && _trailMaterial != null)
			{
				_meshRenderer.material = _trailMaterial;
			}
		}

		private void Update()
		{
			if (_trailObject == null)
				CreateTrailObject();

			if (_points == null)
				InitializeMesh();

			Vector3 pos = transform.position;
			if ((pos - _lastPos).sqrMagnitude >= _minDistance * _minDistance)
				AddPoint(pos);

			RemoveOldPoints();
			UpdateMesh();
		}

		private void InitializeMesh()
		{
			if (!_meshFilter) return;
			if (_maxSegments <= 0) return;

			// Создаем массивы фиксированного размера
			int maxVertices = (_maxSegments + 1) * 2;
			int maxTriangles = _maxSegments * 6;

			_vertices = new Vector3[maxVertices];
			_uvs = new Vector2[maxVertices];
			_colors = new Color[maxVertices];
			_triangles = new int[maxTriangles];

			_points = new Vector3[_maxSegments + 1];
			_pointTimes = new float[_maxSegments + 1];
			_pointCount = 0;

			// Создаем меш
			if (_mesh == null)
			{
				_mesh = new Mesh();
				_mesh.name = "VerticalTrail";
			}

			_mesh.Clear();
			_mesh.vertices = _vertices;
			_mesh.uv = _uvs;
			_mesh.colors = _colors;
			_mesh.triangles = _triangles;
			_mesh.RecalculateNormals();

			_meshFilter.sharedMesh = _mesh;

			if (Application.isPlaying)
				AddPoint(transform.position);
		}

		private void AddPoint(Vector3 basePos)
		{
			if (_pointCount >= _maxSegments + 1)
				return;

			_points[_pointCount] = basePos;
			_pointTimes[_pointCount] = Time.time;
			_pointCount++;
			_lastPos = basePos;
		}

		private void RemoveOldPoints()
		{
			float currentTime = Time.time;
			int removeCount = 0;

			// Подсчитываем сколько старых точек нужно удалить
			for (int i = 0; i < _pointCount; i++)
			{
				if (currentTime - _pointTimes[i] > _trailLifetime)
					removeCount++;
				else
					break;
			}

			if (removeCount > 0)
			{
				// Сдвигаем массивы без создания новых
				for (int i = 0; i < _pointCount - removeCount; i++)
				{
					_points[i] = _points[i + removeCount];
					_pointTimes[i] = _pointTimes[i + removeCount];
				}

				_pointCount -= removeCount;
			}
		}

		private void UpdateMesh()
		{
			if (_pointCount < 2)
			{
				_mesh.Clear();
				return;
			}

			int vertexIndex = 0;
			int triangleIndex = 0;
			float currentTime = Time.time;

			// Очищаем неиспользуемые вертексы
			for (int i = _pointCount * 2; i < _vertices.Length; i++)
			{
				_vertices[i] = Vector3.zero;
				_colors[i] = Color.clear;
			}

			// Генерируем вертексы в мировых координатах (трейл-объект имеет identity transform)
			for (int i = 0; i < _pointCount; i++)
			{
				Vector3 worldBasePos = _points[i];
				Vector3 worldTopPos = worldBasePos + transform.up * _wallHeight;

				// Поскольку трейл-объект находится в identity transform, используем мировые координаты напрямую
				_vertices[vertexIndex] = worldBasePos;
				_vertices[vertexIndex + 1] = worldTopPos;

				// UV координаты
				float u = _pointCount > 1 ? (float)i / (_pointCount - 1) : 0f;
				_uvs[vertexIndex] = new Vector2(u, 0f);
				_uvs[vertexIndex + 1] = new Vector2(u, 1f);

				// Цвет с альфой для фейдинга
				Color color = Color.white;
				if (_fadeTrail)
				{
					float age = currentTime - _pointTimes[i];
					float normalizedAge = Mathf.Clamp01(age / _trailLifetime);
					float alpha = _fadeCurve.Evaluate(1f - normalizedAge); // Инвертируем для правильного фейда
					color.a = alpha;
				}

				_colors[vertexIndex] = color;
				_colors[vertexIndex + 1] = color;

				vertexIndex += 2;
			}

			// Очищаем неиспользуемые треугольники
			for (int i = (_pointCount - 1) * 6; i < _triangles.Length; i++)
			{
				_triangles[i] = 0;
			}

			// Генерируем треугольники
			for (int i = 0; i < _pointCount - 1; i++)
			{
				int baseIndex = i * 2;

				// Первый треугольник
				_triangles[triangleIndex] = baseIndex;
				_triangles[triangleIndex + 1] = baseIndex + 1;
				_triangles[triangleIndex + 2] = baseIndex + 2;

				// Второй треугольник
				_triangles[triangleIndex + 3] = baseIndex + 1;
				_triangles[triangleIndex + 4] = baseIndex + 3;
				_triangles[triangleIndex + 5] = baseIndex + 2;

				triangleIndex += 6;
			}

			// Обновляем меш без создания GC
			_mesh.vertices = _vertices;
			_mesh.uv = _uvs;
			_mesh.colors = _colors;
			_mesh.triangles = _triangles;
			_mesh.RecalculateNormals();
			_mesh.RecalculateBounds();
		}

		private void OnDestroy()
		{
			if (_mesh != null)
			{
				if (Application.isPlaying)
					Destroy(_mesh);
				else
					DestroyImmediate(_mesh);
			}

			if (_trailObject != null)
			{
				if (Application.isPlaying)
					Destroy(_trailObject);
				else
					DestroyImmediate(_trailObject);
			}
		}
	}
}
