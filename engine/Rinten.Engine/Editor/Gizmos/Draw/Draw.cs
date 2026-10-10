namespace Rinten;

public static partial class Gizmo
{
	static GizmoDraw _draw = new();

	/// <summary>Draw a shape using the gizmo library</summary>
	public static GizmoDraw Draw => _draw;

	/// <summary>Adds gizmo draws to the active instance's DebugDrawList while recording; else only hitboxes</summary>
	public sealed partial class GizmoDraw
	{
		internal GizmoDraw()
		{

		}

		internal Color32 Color32 => Active.scope.Color32;

		/// <summary>The color to render the next object</summary>
		public Color Color
		{
			get => Active.scope.Color;
			set
			{
				if ( Active.scope.Color == value )
					return;

				Active.scope.Color = value;
				Active.scope.Color32 = value;
			}
		}

		/// <summary>Ignore depth when drawing, draw on top of everything</summary>
		public bool IgnoreDepth
		{
			get => Active.scope.IgnoreDepth;
			set => Active.scope.IgnoreDepth = value;
		}

		/// <summary>The thickness of line drawings, in pixels</summary>
		public float LineThickness
		{
			get => Active.scope.LineThickness;
			set => Active.scope.LineThickness = value;
		}

		/// <summary>Don't draw backfaces when drawing solids</summary>
		public bool CullBackfaces
		{
			get => Active.scope.CullBackfaces;
			set => Active.scope.CullBackfaces = value;
		}

		internal void Start()
		{
			IgnoreDepth = false;
			Color = Color.White;
			LineThickness = 1.0f;
		}

		//Where this frame's drawing goes, or nothing where the active instance is not recording one.
		internal static DebugDrawList Target => Active is { Recording: true } instance ? instance.Draws : null;

		//How long something drawn now stays - one recorded frame, or for a scene's instance the duration asked.
		internal static DebugDrawList.Life LifeFor( float duration ) => Active?.LifeFor( duration ) ?? DebugDrawList.Life.Frame;

		//A sink for lines in the scope's space: in the colour, lifetime and depth the call gave, the
		//brush's where it gave none. Every line is drawn, and inside a hitbox line scope can be aimed at.
		internal GizmoSink LineSink( Color? color = null, float duration = 0, bool? overlay = null )
			=> LineSink( Transform, color, duration, overlay );

		//The same, in a space of the caller's own inside the scope's.
		internal GizmoSink LineSink( in Transform space, Color? color = null, float duration = 0, bool? overlay = null )
		{
			var list = Target;
			list?.Begin( true, space, LineThickness, overlay ?? IgnoreDepth, false, LifeFor( duration ) );

			return new GizmoSink( list, color.HasValue ? (Color32)color.Value : Color32, LineThickness, Active.lineScope.Enabled );
		}

		//A sink for solid triangles, the same way.
		internal GizmoSink SolidSink()
		{
			var list = Target;
			list?.Begin( false, Transform, 1.0f, IgnoreDepth, CullBackfaces, LifeFor( 0.0f ) );

			return new GizmoSink( list, Color32, 0.0f, false );
		}

		/// <summary>Draw a model</summary>
		public SceneModel Model( string modelName, Transform localTransform )
		{
			var model = Rinten.Model.Load( modelName );
			if ( model == null ) model = Rinten.Model.Error;

			return Model( model, localTransform );
		}

		/// <summary>Draw a model</summary>
		public SceneModel Model( string modelName ) => Model( modelName, Transform.Zero );

		//A scene object kept from frame to frame, because callers pose it, tint it and put a material on it after it is handed back. It is drawn with the world, lit, through the immediate models, in whatever pose it was set to - see CoreSceneDrawer.AddGizmoModels.
		/// <summary>Draw a model</summary>
		public SceneModel Model( Model model, Transform localTransform, Color? color = null )
		{
			var tx = Transform.ToWorld( localTransform );
			var so = Active.FindOrCreate<SceneModel>( model.Name, () => new SceneModel( World, model, tx ) );
			so.Flags.CastShadows = false;
			so.Model = model;
			so.ColorTint = color ?? Color;
			so.Transform = tx;

			Target?.Add( so );

			return so;
		}

		/// <summary>Draw a model</summary>
		public SceneModel Model( Model modelName ) => Model( modelName, global::Transform.Zero );

		/// <summary>Draw particles. Control points will be set to the transform position.</summary>
		[Obsolete]
		public void Particles( string modelName, Transform localTransform, float? updateSpeed = null )
		{
		}

		/// <summary>Draw particles. Control point 0 will be set to the transform position.</summary>
		[Obsolete]
		public void Particles( string modelName, float? updateSpeed = null )
		{
		}

		/// <summary>Draw text on the screen, over a point in the world</summary>
		public void Text( string text, Transform tx, string font = "Roboto", float size = 12.0f, TextFlag flags = TextFlag.Center )
		{
			tx = Transform.ToWorld( tx );

			Target?.Add( new DebugDrawList.Label
			{
				Scope = new TextRendering.Scope( text, Color, size, font ),
				Placing = DebugDrawList.Placing.Projected,
				Position = tx.Position,
				Flags = flags,
				IgnoreDepth = true,
				Life = LifeFor( 0.0f ),
			} );
		}

		/// <summary>World text facing the viewer, rasterised at &gt;= 32px; one pixel is 1cm, matching TextRenderer</summary>
		public void Text( Vector3 position, string text, float size = 32, TextFlag flags = TextFlag.Center, Color? color = null, float duration = 0, bool? overlay = null )
		{
			var scope = new TextRendering.Scope( text, color ?? Color, size )
			{
				Shadow = new TextRendering.Shadow { Color = global::Color.Black, Enabled = true, Offset = 2, Size = 4 }
			};

			Text( position, scope, flags, duration, overlay );
		}

		/// <summary>Draw text standing in the world at a point, with a text rendering scope for more customization.</summary>
		public void Text( Vector3 position, TextRendering.Scope scope, TextFlag flags = TextFlag.Center, float duration = 0, bool? overlay = null )
		{
			var fontSize = scope.FontSize;
			var scale = TextRenderer.PixelsToWorld * ( fontSize < 32 ? fontSize.Remap( 0, 32, 0, 1 ) : 1.0f );
			scope.FontSize = MathF.Max( fontSize, 32 );

			Target?.Add( new DebugDrawList.Label
			{
				Scope = scope,
				Placing = DebugDrawList.Placing.Facing,
				Position = Transform.PointToWorld( position ),
				Scale = scale,
				Flags = flags,
				IgnoreDepth = overlay ?? IgnoreDepth,
				Life = LifeFor( duration ),
			} );
		}

		/// <summary>Draw text lying in the world, in the plane of the transform</summary>
		public void WorldText( string text, Transform tx, string font = "Roboto", float size = 12.0f, TextFlag flags = TextFlag.Center )
		{
			tx = Transform.ToWorld( tx );

			Target?.Add( new DebugDrawList.Label
			{
				Scope = new TextRendering.Scope( text, Color, size, font ),
				Placing = DebugDrawList.Placing.Plane,
				Position = tx.Position,
				Rotation = tx.Rotation,
				Scale = TextRenderer.PixelsToWorld * tx.UniformScale,
				Flags = flags,
				IgnoreDepth = IgnoreDepth,
				Life = LifeFor( 0.0f ),
			} );
		}

		/// <summary>Draw text</summary>
		public void ScreenText( string text, Vector2 pos, string font = "Roboto", float size = 12.0f, TextFlag flags = TextFlag.LeftTop, Color? color = null, float duration = 0 )
			=> ScreenText( new TextRendering.Scope( text, color ?? Color, size, font ), pos, flags, duration );

		/// <summary>Draw text with a text rendering scope for more text rendering customization.</summary>
		public void ScreenText( TextRendering.Scope text, Vector2 pos, TextFlag flags = TextFlag.LeftTop, float duration = 0 )
		{
			Target?.Add( new DebugDrawList.Label
			{
				Scope = text,
				Placing = DebugDrawList.Placing.Screen,
				Position = new Vector3( pos.x, pos.y, 0 ),
				Flags = flags,
				IgnoreDepth = true,
				Life = LifeFor( duration ),
			} );
		}

		/// <summary>Draw text on screen at a 3d position</summary>
		public void ScreenText( string text, Vector3 worldPos, Vector2 offset, string font = "Roboto", float size = 12.0f, TextFlag flags = TextFlag.LeftTop )
			=> ScreenText( new TextRendering.Scope( text, Color, size, font ), worldPos, offset, flags );

		/// <summary>Draw text on screen at a 3d position with a text rendering scope for more text rendering customization.</summary>
		public void ScreenText( TextRendering.Scope text, Vector3 worldPos, Vector2 offset, TextFlag flags = TextFlag.LeftTop )
		{
			Target?.Add( new DebugDrawList.Label
			{
				Scope = text,
				Placing = DebugDrawList.Placing.Projected,
				Position = worldPos,
				Offset = offset,
				Flags = flags,
				IgnoreDepth = true,
				Life = LifeFor( 0.0f ),
			} );
		}

		/// <summary>Draw text at an angle</summary>
		public void ScreenText( TextRendering.Scope text, Rect rect, float angle, TextFlag flags = TextFlag.LeftTop )
		{
			Target?.Add( new DebugDrawList.Label
			{
				Scope = text,
				Placing = DebugDrawList.Placing.Screen,
				Position = new Vector3( rect.Position.x, rect.Position.y, 0 ),
				Angle = angle,
				Flags = flags,
				IgnoreDepth = true,
				Life = LifeFor( 0.0f ),
			} );
		}

		/// <summary>Draw a plane</summary>
		public void Plane( Vector3 position, Vector3 normal )
		{
			using var scope = Scope( "plane", new Transform( position, Rotation.LookAt( normal ) ) );

			LineThickness = 1.0f;

			//Twenty five metres each way, which is as far as a plane is worth drawing. A thousand of
			//anything was a thousand inches; here it would be a kilometre of lines through the level.
			Line( Vector3.Left * 25.0f, Vector3.Left * -25.0f );
			Line( Vector3.Up * 25.0f, Vector3.Up * -25.0f );
			Line( (Vector3.Up + Vector3.Left).Normal * 25.0f, (Vector3.Up + Vector3.Left).Normal * -25.0f );
			Line( (Vector3.Down + Vector3.Left).Normal * 25.0f, (Vector3.Down + Vector3.Left).Normal * -25.0f );

			//Round the plane's own centre: the scope already stands there, and giving the position again put every ring that far off again.
			for ( float i = 0.0f; i < 25.0f; i += 1.25f )
			{
				LineCircle( Vector3.Zero, i, sections: 32 );
			}
		}

		/// <summary>Draw a line with an arrow on the end</summary>
		public void Arrow( Vector3 from, Vector3 to, float arrowLength = 0.3f, float arrowWidth = 0.125f )
		{
			var delta = to - from;
			var deltaNormal = delta.Normal;

			Line( from, to - deltaNormal * arrowLength );
			SolidCone( to - deltaNormal * arrowLength, deltaNormal * arrowLength, arrowWidth );
		}

		/// <summary>Draws a grid</summary>
		public void Grid( GridAxis axis, float spacing = 0.25f, float opacity = 1.0f, float minorLineWidth = 0.01f, float majorLineWidth = 0.02f )
		{
			Grid( axis, new Vector2( spacing, spacing ), opacity, minorLineWidth, majorLineWidth );
		}

		/// <summary>Draws a grid</summary>
		public void Grid( GridAxis axis, Vector2 spacing = default, float opacity = 1.0f, float minorLineWidth = 0.01f, float majorLineWidth = 0.02f )
		{
			Grid( 0, axis, spacing, opacity, minorLineWidth, majorLineWidth );
		}

		/// <summary>Draws a grid centered at a position</summary>
		public void Grid( Vector3 center, GridAxis axis, Vector2 spacing = default, float opacity = 1.0f, float minorLineWidth = 0.01f, float majorLineWidth = 0.02f )
		{
			//A quarter of a metre, the same default the scene view's grid uses.
			if ( spacing == default ) spacing = new Vector2( 0.25f, 0.25f );
			if ( spacing.x <= 0.0f || spacing.y <= 0.0f ) return;

			using var scope = Scope();

			LineThickness = 1.0f;

			Color = Color.White.WithAlpha( 0.25f * opacity );
			var minor = LineSink();
			DebugShapes.GridLines( ref minor, center, axis, spacing, 40, 16, major: false );

			Color = Color.White.WithAlpha( 0.6f * opacity );
			var major = LineSink();
			DebugShapes.GridLines( ref major, center, axis, spacing, 40, 16, major: true );
		}

		//Where a gizmo's shapes go: the recorded frame's list, when there is one, and the
		//hitboxes either way - a line a tool draws is also a line the pointer can land on.
		internal readonly struct GizmoSink : IDebugShapeSink
		{
			private readonly DebugDrawList list;
			private readonly Color32 color;
			private readonly float thickness;
			private readonly bool hitboxes;

			public GizmoSink( DebugDrawList list, Color32 color, float thickness, bool hitboxes )
			{
				this.list = list;
				this.color = color;
				this.thickness = thickness;
				this.hitboxes = hitboxes;
			}

			public void Line( in Vector3 a, in Vector3 b )
			{
				if ( list is not null )
				{
					list.Add( a, color );
					list.Add( b, color );
				}

				if ( hitboxes ) Hitbox.AddPotentialLine( a, b, thickness );
			}

			public void Triangle( in Vector3 a, in Vector3 b, in Vector3 c )
			{
				if ( list is null ) return;

				list.Add( a, color );
				list.Add( b, color );
				list.Add( c, color );
			}
		}
	}
}
