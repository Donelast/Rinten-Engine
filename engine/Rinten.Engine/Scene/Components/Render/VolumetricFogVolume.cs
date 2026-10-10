namespace Rinten;

/// <summary>Adds a volumetric fog volume to the scene.</summary>
[Title( "VolumetricFogVolume" )]
[Category( "Rendering" )]
[Icon( "visibility" )]
[EditorHandle( "materials/gizmo/VolumetricFogVolume.png" )]
public class VolumetricFogVolume : Component, Component.ExecuteInEditor
{
	SceneFogVolume sceneObject;

	//Seven and a half metres a side, which is the three hundred units this came with said in metres. Left
	//as it was it fogged a box three hundred metres across - the whole level and everything past it.
	[Property] public BBox Bounds { get; set; } = BBox.FromPositionAndSize( 0, 7.6f );
	[Property, Range( 0, 1 )] public float Strength { get; set; } = 1.0f;
	[Property, Range( 0, 1 )] public float FalloffExponent { get; set; } = 1.0f;
	[Property] public Color Color { get; set; } = Color.White;

	protected override void DrawGizmos()
	{
		using var scope = Gizmo.Scope( gizmoPath ??= $"volumetricfogvolume-{GetHashCode()}" );

		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		Gizmo.Draw.Color = Color.White.WithAlpha( Gizmo.IsSelected ? 0.9f : 0.4f );
		Gizmo.Draw.LineBBox( Bounds );
	}

	//The name this component's gizmo scope goes by, made once - it was a new string every frame.
	string gizmoPath;

	protected override void OnEnabled()
	{
		Assert.True( !sceneObject.IsValid() );
		Assert.NotNull( Scene );

		sceneObject = new SceneFogVolume( Scene.SceneWorld, WorldTransform, Bounds, Strength, FalloffExponent );
	}

	protected override void OnDisabled()
	{
		sceneObject?.Delete();
		sceneObject = null;
	}

	protected override void OnPreRender()
	{
		if ( !sceneObject.IsValid() )
			return;

		//Scaled by the scene's controller where there is one. There used to be a flag saying this volume came out of a compiled
		//map, because only a map ever made a controller; now anything can, and a scene without one multiplies by nothing.
		var strength = Strength * (Scene.GetAll<VolumetricFogController>().FirstOrDefault()?.GlobalScale ?? 1.0f);

		sceneObject.Transform = WorldTransform;
		sceneObject.BoundingBox = Bounds;
		sceneObject.FogStrength = strength;
		sceneObject.FalloffExponent = FalloffExponent;
		sceneObject.Color = Color;
	}

}
