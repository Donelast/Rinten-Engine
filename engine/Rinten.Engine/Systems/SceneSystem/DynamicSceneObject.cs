using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rinten;

/// <summary>Scene object whose vertices are supplied every frame it changes: debug lines, gizmos and the like</summary>
public class SceneDynamicObject : SceneObject
{
	/// <summary>What it is drawn with. Nothing is the engine's own line and triangle drawing, which is what nearly all of these want.</summary>
	public Material Material { get; set; }

	/// <summary>Whether these vertices are lines, triangles, or a strip.</summary>
	public Graphics.PrimitiveType Primitive { get; private set; } = Graphics.PrimitiveType.Triangles;

	//What to draw. Written between Init and the end of the frame, read by the renderer.
	internal List<Vertex> Geometry { get; } = new();

	//Drawn procedurally by its shader (the editor grid); skipped where that shader can't run
	internal bool ShaderDriven { get; set; }

	public SceneDynamicObject( SceneWorld sceneWorld )
	{
		Assert.IsValid( sceneWorld );

		//Infinite until something says otherwise, so an object whose bounds nobody set is drawn rather than culled.
		Bounds = new BBox( new Vector3( float.MinValue * 0.5f ), new Vector3( float.MaxValue * 0.5f ) );

		JoinWorld( sceneWorld );
	}

	public void Clear() => Geometry.Clear();

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void AddVertex( in Vertex v ) => Geometry.Add( v );

	public void AddVertex( in Span<Vertex> v )
	{
		foreach ( var vertex in v ) Geometry.Add( vertex );
	}

	internal unsafe void AddVertex( in Vertex* ptr, int count )
	{
		for ( var i = 0; i < count; i++ ) Geometry.Add( ptr[i] );
	}

	public IDisposable Write( Graphics.PrimitiveType type, int vertices, int indices )
	{
		Init( type );

		if ( vertices > 0 ) Geometry.Capacity = Math.Max( Geometry.Capacity, vertices );

		return null;
	}

	public void Init( Graphics.PrimitiveType type )
	{
		Primitive = type;
		Geometry.Clear();
	}
}
