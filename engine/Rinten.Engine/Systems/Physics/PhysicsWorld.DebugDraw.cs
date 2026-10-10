using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Rinten;

partial class PhysicsWorld
{
	internal Scene Scene { get; set; }

	/// <summary>A SceneWorld where debug SceneObjects exist.</summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	public SceneWorld DebugSceneWorld
	{
		get => Backend.GetDebugScene();
		set => Backend.SetDebugScene( value );
	}

	/// <summary>Draws the physics debug overlays enabled via the physics_debug_draw convars, updating the SceneObjects in the <see cref="DebugSceneWorld"/>. Call once per tick or frame.</summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	public unsafe void DebugDraw()
	{
		Backend.Draw( onDebugDraw ??= OnDebugDrawInternal );
	}

	DebugDrawCallback onDebugDraw;

	internal unsafe struct DebugDrawArgs
	{
		public DrawStringArgs* String;
		public DrawSegmentArgs* Segment;
		public DrawPointArgs* Point;
		public DrawBoundsArgs* Bounds;
		public DrawTransformArgs* Transform;
	}

	internal unsafe struct DrawStringArgs
	{
		public Vector3 Position;
		public sbyte* String;
		public Color Color;
	}

	internal unsafe struct DrawSegmentArgs
	{
		public Vector3 P1;
		public Vector3 P2;
		public Color Color;
	}

	internal unsafe struct DrawPointArgs
	{
		public Vector3 Position;
		public float Size;
		public Color Color;
	}

	internal unsafe struct DrawBoundsArgs
	{
		public BBox Box;
		public Color Color;
	}

	internal unsafe struct DrawTransformArgs
	{
		public Vector3 Position;
		public Rotation Rotation;
	}

	//What the backend hands each piece of debug geometry to. A managed call: the simulation is C# too,
	//and a native pointer to an instance's method is something a page's runtime cannot make at all.
	internal unsafe delegate void DebugDrawCallback( DebugDrawArgs* args );

	private unsafe void OnDebugDrawInternal( DebugDrawArgs* args )
	{
		if ( args == null )
			return;

		if ( args->String != null ) OnDrawStringInternal( args->String );
		else if ( args->Segment != null ) OnDrawSegmentInternal( args->Segment );
		else if ( args->Point != null ) OnDrawPointInternal( args->Point );
		else if ( args->Bounds != null ) OnDrawBoundsInternal( args->Bounds );
		else if ( args->Transform != null ) OnDrawTransformInternal( args->Transform );
	}

	private unsafe void OnDrawStringInternal( DrawStringArgs* args )
	{
		if ( args == null )
			return;

		var position = args->Position;
		var color = args->Color;
		var text = Marshal.PtrToStringAnsi( (IntPtr)args->String );
		var scope = new TextRendering.Scope( text, color, 11, "Roboto Mono", 600 ) { Outline = new TextRendering.Outline { Color = Color.Black, Size = 3, Enabled = true } };

		Gizmo.Draw.ScreenText( scope, position, Vector2.Zero, TextFlag.Center );
	}

	private unsafe void OnDrawSegmentInternal( DrawSegmentArgs* args )
	{
		if ( args == null )
			return;

		var p1 = args->P1;
		var p2 = args->P2;
		var color = args->Color;

		Gizmo.Draw.Line( p1, p2, color );
	}

	private unsafe void OnDrawPointInternal( DrawPointArgs* args )
	{
		if ( args == null )
			return;

		var position = args->Position;
		var size = args->Size;
		var color = args->Color;

		Gizmo.Draw.Sprite( position, new Vector2( size * 0.1f ), null, true, 0.0f, color, overlay: true );
	}

	private unsafe void OnDrawBoundsInternal( DrawBoundsArgs* args )
	{
		if ( args == null )
			return;

		var box = args->Box;
		var color = args->Color;

		Gizmo.Draw.LineBBox( box, color );
	}

	private unsafe void OnDrawTransformInternal( DrawTransformArgs* args )
	{
		if ( args == null )
			return;

		var position = args->Position;
		var rotation = args->Rotation;

		const float axisLength = 5.0f;

		Gizmo.Draw.Line( position, position + rotation.Forward * axisLength, Color.Red, overlay: true );
		Gizmo.Draw.Line( position, position + rotation.Right * axisLength, Color.Green, overlay: true );
		Gizmo.Draw.Line( position, position + rotation.Up * axisLength, Color.Blue, overlay: true );
	}
}
