using System;

namespace Rinten;

//What the sky is made of, as the scene describes it: the material the sky pass draws with - see SkyBox2D.SkyMaterial - and the tint the renderer reads through the component.
/// <summary>Renders a skybox within a <see cref="SceneWorld"/>.</summary>
public class SceneSkyBox : SceneObject
{
	public enum FogType
	{
		None,
		Distance,
		Angular
	}

	public struct FogParamInfo
	{
		public FogType FogType;
		public float FogMinStart;
		public float FogMinEnd;
		public float FogMaxStart;
		public float FogMaxEnd;
	}

	public struct SkyLightInfo
	{
		public Vector3 LightColor;
		public Vector3 LightDirection;
	}

	internal SceneSkyBox() { }
	internal SceneSkyBox( HandleCreationData _ ) { }

	public SceneSkyBox( SceneWorld world, Material skyMaterial )
	{
		Assert.NotNull( skyMaterial );
		Assert.IsValid( world );

		_material = skyMaterial;

		JoinWorld( world );
	}

	private Material _material;

	/// <summary>The skybox material, on a sky shader - see SkyBox2D.SkyMaterial.</summary>
	public Material SkyMaterial
	{
		get => _material;
		set => _material = value;
	}

	/// <summary>Skybox color tint.</summary>
	public Color SkyTint { get; set; } = Color.White;

	/// <summary>Controls the skybox specific fog.</summary>
	public FogParamInfo FogParams { get; set; }

	[Obsolete]
	public void SetSkyLighting( Vector3 ConstantSkyLight )
	{
	}
}
