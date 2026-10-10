namespace Rinten;

/// <summary>Emits light in a specific direction in a cone shape.</summary>
[Expose]
[Title( "Spot Light" )]
[Category( "Light" )]
[Icon( "light_mode" )]
[EditorHandle( "materials/gizmo/spotlight.png" )]
[Alias( "SpotLightComponent" )]
public class SpotLight : Light
{
	SceneSpotLight _so;

	[Property]
	public float Radius
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _so.IsValid() )
				_so.Radius = value;
		}
	} = 6.0f;

	[Range( 0, 90 )]
	[Property]
	public float ConeOuter
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _so.IsValid() )
				_so.ConeOuter = value;
		}
	} = 45;

	[Range( 0, 90 )]
	[Property]
	public float ConeInner
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _so.IsValid() )
				_so.ConeInner = value;
		}
	} = 15;

	[Property]
	public Texture Cookie
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
		}
	}

	public SpotLight()
	{
		LightColor = "#E9FAFF";
	}

	protected override SceneLight CreateSceneObject()
	{
		return _so = new SceneSpotLight( Scene.SceneWorld, WorldPosition, LightColor )
		{
			Radius = Radius,
			ConeInner = ConeInner,
			ConeOuter = ConeOuter
		};
	}

	protected override void OnAwake()
	{
		Tags.Add( "light_spot" );

		base.OnAwake();
	}

	protected override void DrawGizmos()
	{
		using var scope = Gizmo.Scope( gizmoPath ??= $"light-{GetHashCode()}" );

		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		Gizmo.Draw.Color = LightColor.WithAlpha( Gizmo.IsSelected ? 0.9f : 0.4f );

		var coneAngle = MathX.DegreeToRadian( ConeOuter );
		var radius = Radius * MathF.Sin( coneAngle );
		var center = Vector3.Forward * Radius * MathF.Cos( coneAngle );
		var startPoint = Vector3.Zero;
		var lastPoint = Vector3.Zero;

		const int segments = 16;

		for ( var i = 0; i < segments; i++ )
		{
			var angle = MathF.PI * 2 * i / segments;
			var currentPoint = center + new Vector3( 0,
				MathF.Cos( angle ) * radius,
				MathF.Sin( angle ) * radius );

			Gizmo.Draw.Line( 0, currentPoint );

			if ( i > 0 )
			{
				Gizmo.Draw.Line( lastPoint, currentPoint );
			}
			else
			{
				startPoint = currentPoint;
			}

			lastPoint = currentPoint;
		}

		Gizmo.Draw.Line( lastPoint, startPoint );
	}

	//The name this component's gizmo scope goes by, made once - it was a new string every frame.
	string gizmoPath;
}
