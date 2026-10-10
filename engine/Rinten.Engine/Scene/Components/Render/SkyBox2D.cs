using System;

namespace Rinten;

/// <summary>Adds a 2D skybox to the world</summary>
[Title( "2D Skybox" )]
[Category( "Rendering" )]
[Icon( "visibility" )]
[EditorHandle( "materials/gizmo/2dskybox.png" )]
public class SkyBox2D : Component, Component.ExecuteInEditor
{
	[Property]
	public Color Tint
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			_sceneObject?.SkyTint = value;
		}
	} = Color.White;

	//The sky lights the world via the sky table in frame.slang; the old cubemap and toggle are gone

	//The material on a sky shader drives both the sky and ambient lighting; non-sky shaders are refused
	/// <summary>What the sky is drawn with: a material on a sky shader.</summary>
	[Property]
	public Material SkyMaterial
	{
		get;
		set
		{
			if ( value is null || value == field ) return;
			if ( !IsSkyMaterial( value ) ) return;

			field = value;

			_sceneObject?.SkyMaterial = value;
		}
	} = Material.Load( DefaultMaterial );

	/// <summary>The engine's own sky, which a SkyBox2D that names none draws with.</summary>
	public const string DefaultMaterial = "materials/skybox/procedural.mat";

	/// <summary>Whether a material may be the sky: the shader it wears is a sky.</summary>
	public static bool IsSkyMaterial( Material material )
		=> CoreResources.IsSkyShader( material?.core?.Shader );

	SceneSkyBox _sceneObject;

	protected override void OnAwake()
	{
		Tags.Add( "skybox" );

		base.OnAwake();
	}

	protected override void OnEnabled()
	{
		if ( SkyMaterial is null ) return;

		Assert.True( !_sceneObject.IsValid() );
		Assert.NotNull( Scene );

		_sceneObject = new SceneSkyBox( Scene.SceneWorld, SkyMaterial );
		_sceneObject.SkyTint = Tint;
		_sceneObject.Tags.SetFrom( Tags );

		OnTransformChanged();
		Transform.OnTransformChanged += OnTransformChanged;
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= OnTransformChanged;

		_sceneObject?.Delete();
		_sceneObject = null;
	}

	private void OnTransformChanged()
	{
		if ( _sceneObject.IsValid() )
			_sceneObject.Transform = WorldTransform.WithScale( 1.0f );
	}

	//Tags have been updated
	protected override void OnTagsChanged()
	{
		_sceneObject?.Tags.SetFrom( Tags );
	}
}
