using Rinten.Rendering;
using System.Numerics;

namespace Rinten;

/// <summary>A scene object which is used to draw lines</summary>
public class SceneLineObject : SceneCustomObject
{
	public enum CapStyle
	{
		None = 0,
		[Icon( "signal_cellular_4_bar" )]
		Triangle = 1,
		[Icon( "label_important" )]
		Arrow = 2,
		[Icon( "fiber_smart_record" )]
		Rounded = 3
	}

	public enum FaceMode
	{
		[Icon( "settings_ethernet" )]
		Camera = 0,
		[Icon( "rotate_90_degrees_cw" )]
		Normal = 1,
		[Icon( "circle" )]
		Cylinder = 2,
	}

	[Obsolete( "Use Material property instead", false )]
	public Texture LineTexture
	{
		get => Attributes.GetTexture( "BaseTexture" );
		set => Attributes.Set( "BaseTexture", value ?? Texture.White );
	}

	private static Material _defaultMaterial => field ??= Material.FromShader( "line" );
	public Material Material = _defaultMaterial;

	public CapStyle StartCap
	{
		get => (CapStyle)Attributes.GetInt( "StartCap" );
		set => Attributes.Set( "StartCap", (int)value );
	}

	public CapStyle EndCap
	{
		get => (CapStyle)Attributes.GetInt( "EndCap" );
		set => Attributes.Set( "EndCap", (int)value );
	}

	public FaceMode Face
	{
		get => (FaceMode)_csAttributes.GetInt( "FaceMode" );
		set => _csAttributes.Set( "FaceMode", (int)value );
	}

	public bool Wireframe
	{
		get => Attributes.GetComboBool( "D_WIREFRAME" );
		set => Attributes.SetCombo( "D_WIREFRAME", value );
	}

	public bool Lighting
	{
		get => Attributes.GetComboBool( "D_ENABLE_LIGHTING" );
		set => Attributes.SetCombo( "D_ENABLE_LIGHTING", value );
	}

	[Obsolete( "Use Sampler State property instead" )]
	public bool Clamped
	{
		get => _clamped;
		set
		{
			_clamped = value;
			if ( _clamped )
			{
				Attributes.Set( "SamplerIndex", SamplerState.GetBindlessIndex( ClampSampler ) );
			}
			else
			{
				Attributes.Set( "SamplerIndex", SamplerState.GetBindlessIndex( WrapSampler ) );
			}
		}
	}

	private bool _clamped = false;

	public SamplerState SamplerState
	{
		get => _samplerState;
		set
		{
			_samplerState = value;
			Attributes.Set( "SamplerIndex", SamplerState.GetBindlessIndex( _samplerState ) );
		}
	}

	private SamplerState _samplerState = new() { Filter = FilterMode.Anisotropic, MaxAnisotropy = 8, AddressModeU = TextureAddressMode.Wrap, AddressModeV = TextureAddressMode.Wrap };

	public int Smoothness
	{
		get => Attributes.GetInt( "Smoothness" );
		set => Attributes.Set( "Smoothness", value );
	}

	public bool Opaque
	{
		set
		{
			Attributes.SetCombo( "D_OPAQUE", value ? 1 : 0 );
			Flags.IsOpaque = value;
			Flags.IsTranslucent = !value;
		}
	}

	private int _roundedCapSegments = 8;
	private int _tessellationLevel = 1; //Number of subdivisions between left and right sides of each segment

	/// <summary>Number of tessellation subdivisions across the width of each line segment. 1 = no tessellation (just left and right), 2 = one subdivision in the middle, etc. Higher values create smoother curves and more detailed geometry but use more vertices.</summary>
	public int TessellationLevel
	{
		get => _tessellationLevel;
		set => _tessellationLevel = Math.Max( 1, value );
	}

	private struct LinePoint( uint offset, in Vector3 position, in Vector3 normal, Color color, float width, float textureCoord )
	{
		public uint Offset = offset;
		public Vector3 Position = position;
		public Vector3 Normal = normal;
		public Color Color = color;
		public float Width = width;
		public float TextureCoord = textureCoord;
	}

	private struct LineVertex()
	{
		[VertexLayout.Position] public Vector3 Position = default;
		[VertexLayout.Normal] public Vector3 Normal = default;
		[VertexLayout.Tangent] public Vector3 Tangent = default;
		[VertexLayout.Color] public Color Color = default;
		[VertexLayout.TexCoord] public Vector2 TextureCoord = default;
	}

	private static ComputeShader _cs => field ??= new( "line_cs" );
	private RenderAttributes _csAttributes = new();
	private GpuBuffer<LinePoint> _pointBuffer;
	private GpuBuffer<LineVertex> _vertexBuffer;
	private GpuBuffer<uint> _indexBuffer;
	private static readonly SamplerState WrapSampler = new() { Filter = FilterMode.Anisotropic, MaxAnisotropy = 8, AddressModeU = TextureAddressMode.Wrap, AddressModeV = TextureAddressMode.Wrap };
	private static readonly SamplerState ClampSampler = new() { Filter = FilterMode.Anisotropic, MaxAnisotropy = 8, AddressModeU = TextureAddressMode.Clamp, AddressModeV = TextureAddressMode.Clamp };
	private LinePoint[] _points = [];
	private readonly CommandList _commandList = new( "SceneLine" );
	private BBox _bounds;

	private int _pointCount;
	private int _pointCapacity;

	private int _indexCapacity;
	private int _vertexCapacity;

	public SceneLineObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
#pragma warning disable CS0618
		LineTexture = Texture.White;
#pragma warning restore CS0618
	}

	public void StartLine()
	{
		_pointCount = 0;

		_bounds = BBox.FromPositionAndSize( Transform.Position, 10 );
	}

	public void AddLinePoint( in Vector3 pos, Color color, float width )
	{
		AddLinePoint( pos, color, width, _pointCount / 20.0f );
	}

	public void AddLinePoint( in Vector3 pos, Color color, float width, float textureCoord )
	{
		AddLinePoint( pos, Vector3.Up, color, width, textureCoord );
	}

	public void AddLinePoint( in Vector3 pos, in Vector3 normal, Color color, float width, float textureCoord )
	{
		if ( _pointCount > 0 && _points[_pointCount - 1].Position.DistanceSquared( pos ) < 0.01f )
			return;

		if ( _pointCount >= _points.Length )
		{
			Array.Resize( ref _points, Math.Max( 8, _points.Length * 2 ) );
		}

		_points[_pointCount] = new LinePoint( 0, pos, normal, color, width, textureCoord );

		_pointCount++;
		_bounds = _bounds.AddPoint( pos );
	}

	//Calculate the number of vertices needed for a specific cap type
	private int GetCapVertexCount( CapStyle capStyle )
	{
		return capStyle switch
		{
			CapStyle.None => 0,
			CapStyle.Triangle => 1, //1 additional vertex (center)
			CapStyle.Arrow => 3, //Additional triangle
			CapStyle.Rounded => _roundedCapSegments + 1 + 1, //Multiple vertices for the rounded cap
			_ => 0
		};
	}

	//Calculate the number of indices needed for a specific cap type
	private int GetCapIndexCount( CapStyle capStyle )
	{
		return capStyle switch
		{
			CapStyle.None => 0,
			CapStyle.Triangle => 3, //One triangle (3 indices)
			CapStyle.Arrow => 3,    //One triangle (3 indices)
			CapStyle.Rounded => _roundedCapSegments * 3, //Multiple triangles (3 indices each)
			_ => 0
		};
	}

	public void EndLine()
	{
		Bounds = _bounds;

		if ( _pointCount == 0 )
			return;

		var startCap = StartCap;
		var endCap = EndCap;

		if ( _pointCount > _pointCapacity )
		{
			MainThread.QueueDispose( _pointBuffer );

			_pointCapacity = (int)(_pointCount * 1.5);
			_pointBuffer = new GpuBuffer<LinePoint>( _pointCapacity, GpuBuffer.UsageFlags.Structured );
		}

		if ( CalculateIndexCount() > _indexCapacity )
		{
			MainThread.QueueDispose( _indexBuffer );

			_indexCapacity = (int)(CalculateIndexCount() * 1.5);
			_indexBuffer = new GpuBuffer<uint>( _indexCapacity, GpuBuffer.UsageFlags.Index | GpuBuffer.UsageFlags.Structured );
		}

		if ( CalculateVertexCount() > _vertexCapacity )
		{
			MainThread.QueueDispose( _vertexBuffer );

			_vertexCapacity = (int)(CalculateVertexCount() * 1.5);
			_vertexBuffer = new GpuBuffer<LineVertex>( _vertexCapacity, GpuBuffer.UsageFlags.Vertex | GpuBuffer.UsageFlags.Structured );
		}

		if ( Face == FaceMode.Cylinder )
		{
			//Unsupported on Cylinder
			_csAttributes.Set( "StartCap", 0 );
			_csAttributes.Set( "EndCap", 0 );
		}
		else
		{
			_csAttributes.Set( "StartCap", (int)startCap );
			_csAttributes.Set( "EndCap", (int)endCap );
		}

		_csAttributes.Set( "TessellationLevel", _tessellationLevel );
		_csAttributes.Set( "RoundedCapSegments", _roundedCapSegments );

		//Nothing to build here any more: the ribbon is made from the points when the view draws it - see RenderSceneObject.
	}

	public void Clear()
	{
		_pointCount = 0;

		_pointCapacity = 0;
		_vertexCapacity = 0;
		_indexCapacity = 0;

		MainThread.QueueDispose( _pointBuffer );
		MainThread.QueueDispose( _vertexBuffer );
		MainThread.QueueDispose( _indexBuffer );

		_pointBuffer = default;
		_vertexBuffer = default;
		_indexBuffer = default;

		_commandList.Reset();
	}

	private int CalculateIndexCount()
	{
		//With tessellation, each segment creates tessellationLevel * 2 triangles (tessellationLevel quads, each quad = 2 triangles, each triangle = 3 indices)
		int indicesPerSegment = _tessellationLevel * 2 * 3;
		int lineIndices = (_pointCount - 1) * indicesPerSegment;

		//Add indices for caps
		int capIndices = GetCapIndexCount( StartCap ) + GetCapIndexCount( EndCap );

		return lineIndices + capIndices;
	}

	private int CalculateVertexCount()
	{
		//Basic line vertices (tessellationLevel + 1 vertices per point to create tessellationLevel segments across width)
		int lineVertices = _pointCount * (_tessellationLevel + 1);

		//Add vertices for caps
		int capVertices = GetCapVertexCount( StartCap ) + GetCapVertexCount( EndCap );

		return lineVertices + capVertices;
	}

	private void BuildCommandList()
	{
		_commandList.Reset();

		var indexCount = CalculateIndexCount();

		if ( indexCount < 6 )
			return;

		if ( !_pointBuffer.IsValid() || !_vertexBuffer.IsValid() || !_indexBuffer.IsValid() )
			return;

		//Upload point data to GPU (deferred to render thread)
		_commandList.SetBufferData( _pointBuffer, _points, 0, _pointCount );

		//Compute shader attributes
		_commandList.Attributes.Set( "PointBuffer", (GpuBuffer)_pointBuffer );
		_commandList.Attributes.Set( "VertexBuffer", (GpuBuffer)_vertexBuffer );
		_commandList.Attributes.Set( "IndexBuffer", (GpuBuffer)_indexBuffer );
		_commandList.Attributes.Set( "PointCount", _pointCount );
		_commandList.Attributes.Set( "FaceMode", _csAttributes.GetInt( "FaceMode" ) );
		_commandList.Attributes.Set( "StartCap", _csAttributes.GetInt( "StartCap" ) );
		_commandList.Attributes.Set( "EndCap", _csAttributes.GetInt( "EndCap" ) );
		_commandList.Attributes.Set( "TessellationLevel", _csAttributes.GetInt( "TessellationLevel" ) );
		_commandList.Attributes.Set( "RoundedCapSegments", _csAttributes.GetInt( "RoundedCapSegments" ) );
		_commandList.DispatchCompute( _cs, _pointCount, 1, 1 );

		_commandList.ResourceBarrierTransition( (GpuBuffer)_vertexBuffer, ResourceState.UnorderedAccess, ResourceState.VertexOrIndexBuffer );
		_commandList.ResourceBarrierTransition( (GpuBuffer)_indexBuffer, ResourceState.UnorderedAccess, ResourceState.VertexOrIndexBuffer );

		//Setup and draw using sceneobjects attributes... this needs a good refactor.
		if ( Lighting )
		{
			_commandList.SetupLighting( this, Attributes );
		}

		_commandList.DrawIndexed( _vertexBuffer, (GpuBuffer)_indexBuffer, Material, 0, indexCount, Attributes );
	}

	public override void RenderSceneObject()
	{
		base.RenderSceneObject();

		//Ribbon is built on the CPU per view and drawn via the immediate path; command lists are stubs here
		DrawRibbon();
	}

	private static Vertex[] _ribbonVertices = new Vertex[256];
	private static ushort[] _ribbonIndices = new ushort[768];

	private void DrawRibbon()
	{
		if ( _pointCount < 2 ) return;

		//Sixteen-bit indices: a ribbon longer than that is not a trail.
		var count = Math.Min( _pointCount, 32000 );

		var vertexCount = count * 2;
		var indexCount = ( count - 1 ) * 6;

		if ( _ribbonVertices.Length < vertexCount ) _ribbonVertices = new Vertex[vertexCount * 2];
		if ( _ribbonIndices.Length < indexCount ) _ribbonIndices = new ushort[indexCount * 2];

		var eye = Graphics.CameraPosition;
		var face = Face;

		for ( var i = 0; i < count; i++ )
		{
			ref readonly var point = ref _points[i];

			//Along the line: towards the next point, or from the last one at the very end, so the ribbon does not pinch where it stops.
			var along = i + 1 < count
				? _points[i + 1].Position - point.Position
				: point.Position - _points[i - 1].Position;

			if ( along.IsNearlyZero() ) along = Vector3.Forward;

			//Across it: perpendicular to the line and to what it faces - the eye for a trail, its own normal for a line laid on something.
			var facing = face == FaceMode.Normal
				? point.Normal
				: eye - point.Position;

			var across = Vector3.Cross( along, facing );

			if ( across.IsNearlyZero() ) across = Vector3.Cross( along, Vector3.Up );
			if ( across.IsNearlyZero() ) across = Vector3.Cross( along, Vector3.Right );

			//A point's width is its half-extent - what the trail hands over having halved it already, and what their compute pass extruded by.
			across = across.Normal * point.Width;

			var colour = point.Color.ToColor32();
			var u = point.TextureCoord;

			_ribbonVertices[i * 2] = new Vertex( point.Position - across, new Vector4( u, 0.0f, 0.0f, 0.0f ), colour );
			_ribbonVertices[i * 2 + 1] = new Vertex( point.Position + across, new Vector4( u, 1.0f, 0.0f, 0.0f ), colour );
		}

		for ( var i = 0; i < count - 1; i++ )
		{
			var a = (ushort)( i * 2 );
			var at = i * 6;

			_ribbonIndices[at + 0] = a;
			_ribbonIndices[at + 1] = (ushort)( a + 1 );
			_ribbonIndices[at + 2] = (ushort)( a + 2 );
			_ribbonIndices[at + 3] = (ushort)( a + 1 );
			_ribbonIndices[at + 4] = (ushort)( a + 3 );
			_ribbonIndices[at + 5] = (ushort)( a + 2 );
		}

		//In the vertex colours alone. Their line material was a shader of its own with a colour map in it, and the immediate path has
		//no way to run that; a ribbon coloured along its length by the trail's gradient is nearly all of what the texture did anyway.
		Graphics.DrawColoured( _ribbonVertices.AsSpan(), vertexCount, _ribbonIndices.AsSpan(), indexCount );
	}
}
