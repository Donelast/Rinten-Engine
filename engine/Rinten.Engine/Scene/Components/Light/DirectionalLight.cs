namespace Rinten;

/// <summary>A directional light that casts shadows, like the sun.</summary>
[Expose]
[Title( "Directional Light" )]
[Category( "Light" )]
[Icon( "light_mode" )]
[EditorHandle( "materials/gizmo/directionallight.png" )]
[Alias( "DirectionalLightComponent" )]
public class DirectionalLight : Light
{
	SceneDirectionalLight _so;

	/// <summary>Color of the ambient sky color This is kept for long term support, the recommended way to do this is with an Ambient Light component.</summary>
	[Property]
	public Color SkyColor { get; set; }

	//Texels per metre of the nearest sun shadow pages; each ring further out is half as fine
	/// <summary>How finely its shadow is kept, in texels a metre.</summary>
	[Property, Group( "Shadows" ), Range( 4.0f, 256.0f ), Title( "Shadow Detail" )]
	public float ShadowDetail { get; set; } = 256.0f;

	protected override SceneLight CreateSceneObject()
	{
		return _so = new SceneDirectionalLight( Scene.SceneWorld, WorldRotation, LightColor );
	}

	protected override void OnAwake()
	{
		Tags.Add( "light_directional" );

		base.OnAwake();
	}

	protected override void DrawGizmos()
	{
		using var scope = Gizmo.Scope( gizmoPath ??= $"light-{GetHashCode()}" );
		Gizmo.Draw.Color = LightColor;

		var segments = 12;
		for ( var i = 0; i < segments; i++ )
		{
			var angle = MathF.PI * 2 * i / segments;
			var off = (MathF.Sin( angle ) * Vector3.Left + MathF.Cos( angle ) * Vector3.Up) * 0.125f;
			Gizmo.Draw.Line( off, off + Vector3.Forward * 0.75f );
		}
	}

	//The name this component's gizmo scope goes by, made once - it was a new string every frame.
	string gizmoPath;
}
