using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Based on http://wiki.unity3d.com/index.php/OptimizedTrailRenderer
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class Trail : MonoBehaviour {
	[Tooltip("Must be a particle material that has the \"Tint Color\" property")]
	[SerializeField] private Material _material;
	private Material _instanceMaterial;

	// Emit
	[SerializeField] private bool _emit = true;
	
	[SerializeField] private float _segmentLifetime = 1;
	private float _lifeTimeRatio = 1;
	private float _fadeOutRatio;

	// Colors
	[SerializeField] private Color32[] _colors;

	// Widths
	[SerializeField] private float[] _widths;

	// Segment creation data
	[SerializeField] private float _maxAngle = 2;
	[SerializeField] private float _minVertexDistance = 0.1f;
	[SerializeField] private float _maxVertexDistance = 1f;

	// Components
	[SerializeField] private Transform _source;
	private Mesh _mesh;
	private MeshRenderer _renderer;

	// Points
	[SerializeField] int _maximumPoints = 500;
	private Point[] _points;
	private int _pointCount;
	private int _oldestPoint;

	// Mesh
	private Vector3[] _vertexBuffer;
	private Vector2[] _uvBuffer;
	private List<int> _triangleBuffer;
	private Color32[] _colorBuffer;


	private void Start()
	{
		_points = new Point[_maximumPoints];
		_vertexBuffer = new Vector3[_maximumPoints * 2];
		_uvBuffer = new Vector2[_maximumPoints * 2];
		_triangleBuffer = new List<int>(_maximumPoints * 6);
		_colorBuffer = new Color32[_maximumPoints * 2];
		_mesh = GetComponent<MeshFilter>().mesh;
		_instanceMaterial = new Material(_material);
		_fadeOutRatio = 1f / _instanceMaterial.GetColor("_TintColor").a;
		_renderer = GetComponent<MeshRenderer>();
		_renderer.material = _instanceMaterial;
	}

	private void Update()
	{
		// Remove expired points
		while (_pointCount > 0)
		{
			if (_points[_oldestPoint].TimeAlive > _segmentLifetime)
			{
				_oldestPoint++;
				_pointCount--;
				continue;
			}
			break;
		}

		// Do we add any new points?
		if (_emit)
		{
			// Make sure there are always at least 2 points when emitting
			if (_pointCount < 2)
			{
				if (_pointCount < 1)
					InsertPoint();
				InsertPoint();
			}
		}

		var newestPointIndex = _oldestPoint + _pointCount;
		// wrap around to the beginning
		if (newestPointIndex >= _points.Length)
			newestPointIndex -= _points.Length;

		var secondNewestPointIndex = newestPointIndex + 1;
		if (secondNewestPointIndex >= _points.Length)
			secondNewestPointIndex -= _points.Length;

		if (_emit) {
			var add = false;
			var sqrDistance = (_points[secondNewestPointIndex].Position - _source.transform.position).sqrMagnitude;
			if (sqrDistance > _minVertexDistance * _minVertexDistance)
			{
				if (sqrDistance > _maxVertexDistance * _maxVertexDistance)
					add = true;
				else if (Quaternion.Angle(_source.transform.rotation, _points[secondNewestPointIndex].Rotation) > _maxAngle)
					add = true;
			}
			if (add)
			{
				InsertPoint();
			}
			if (!add)
				_points[newestPointIndex].Update(_source.transform);
		}

		// Do we render this?
		if (_pointCount < 2)
		{
			_renderer.enabled = false;
			return;
		}
		_renderer.enabled = true;

		_lifeTimeRatio = 1 / _segmentLifetime;

		// Do we fade it out?
		if (!_emit)
		{
			if (_pointCount == 0)
				return;
			var color = _instanceMaterial.GetColor("_TintColor");
			color.a -= _fadeOutRatio * _lifeTimeRatio * Time.deltaTime;
			if (color.a > 0)
				_instanceMaterial.SetColor("_TintColor", color);
			return;
		}

		// Rebuild it
		_triangleBuffer.Clear();

		var uvMultiplier = 1 / (_points[_oldestPoint].TimeAlive - _points[newestPointIndex].TimeAlive);
		for (var i = 0; i < _pointCount; i++)
		{
			var v1 = i*2;
			var v2 = i*2 + 1;

			var wrappedIndex = i + _oldestPoint;
			if (wrappedIndex >= _points.Length)
				wrappedIndex -= _points.Length;

			var point = _points[wrappedIndex];
			var ratio = point.TimeAlive * _lifeTimeRatio;
			// Color
			Color32 color;
			if (_colors.Length == 0)
				color = Color32.Lerp(new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 0), ratio);
			else if (_colors.Length == 1)
				color = Color32.Lerp(_colors[0], new Color32(255, 255, 255, 0), ratio);
			else if (_colors.Length == 2)
				color = Color32.Lerp(_colors[0], _colors[1], ratio);
			else
			{
				var colorRatio = ratio * (_colors.Length - 1);
				var min = (int)Mathf.Floor(colorRatio);
				var lerp = Mathf.InverseLerp(min, min + 1, colorRatio);
				color = Color32.Lerp(_colors[min], _colors[min + 1], lerp);
			}
			_colorBuffer[v1] = color;
			_colorBuffer[v2] = color;

			// Width
			float width;
			if (_widths.Length == 0)
				width = 1;
			else if (_widths.Length == 1)
				width = _widths[0];
			else if (_widths.Length == 2)
				width = Mathf.Lerp(_widths[0], _widths[1], ratio);
			else
			{
				var widthRatio = ratio * (_widths.Length - 1);
				var min = (int)Mathf.Floor(widthRatio);
				var lerp = Mathf.InverseLerp(min, min + 1, widthRatio);
				width = Mathf.Lerp(_widths[min], _widths[min + 1], lerp);
			}
			transform.position = point.Position;
			transform.rotation = point.Rotation;
			_vertexBuffer[v1] = transform.TransformPoint(0, width * 0.5f, 0);
			_vertexBuffer[v2] = transform.TransformPoint(0, -width * 0.5f, 0);

			// UVs
			var uvRatio = (point.TimeAlive - _points[newestPointIndex].TimeAlive) * uvMultiplier;
			_uvBuffer[v1] = new Vector2(uvRatio, 0);
			_uvBuffer[v2] = new Vector2(uvRatio, 1);

			if (i > 0)
			{
				// Triangles
				var vertIndex = i * 2;
				_triangleBuffer.Add(vertIndex - 2);
				_triangleBuffer.Add(vertIndex - 1);
				_triangleBuffer.Add(vertIndex - 0);

				_triangleBuffer.Add(vertIndex + 1);
				_triangleBuffer.Add(vertIndex + 0);
				_triangleBuffer.Add(vertIndex - 1);
			}
		}
		transform.position = Vector3.zero;
		transform.rotation = Quaternion.identity;
		_mesh.Clear();
		_mesh.vertices = _vertexBuffer;
		_mesh.colors32 = _colorBuffer;
		_mesh.uv = _uvBuffer;
		_mesh.SetTriangles(_triangleBuffer, 0);
	}

	private void InsertPoint()
	{
		_pointCount++;
		var wrappedIndex = _oldestPoint + _pointCount;
		if (wrappedIndex >= _points.Length)
			wrappedIndex -= _points.Length;
		_points[wrappedIndex].Update(_source);
	}

	private struct Point
	{
		public float TimeCreated;
		public float TimeAlive
		{
			get { return Time.time - TimeCreated; }
		}
		
		public Vector3 Position;
		public Quaternion Rotation;
		public void Update(Transform trans)
		{
			Position = trans.position;
			Rotation = trans.rotation;
			TimeCreated = Time.time;
		}
	}
}
