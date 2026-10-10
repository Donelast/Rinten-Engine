using Rinten.Navigation;

namespace Rinten.Engine.Resources;

/// <summary>Defines a navigation area resource for use in navigation meshes.</summary>
[AssetType( Name = "NavArea", Extension = "navarea", Category = "World", Icon = "map" )]
public sealed class NavMeshAreaDefinition : GameResource
{
	/// <summary>Debug color for this Area.</summary>
	public Color Color { get; set; } = Color.Random;

	internal const float CostMultiplierMax = 100f;
	internal const float CostMultiplierMin = 1f;

	/// <summary>How much costlier it is to cross this Area. Will be clamped.</summary>
	[Range( CostMultiplierMin, CostMultiplierMax )]
	public float CostMultiplier { get => _costMultiplier; set => _costMultiplier = MathX.Clamp( value, CostMultiplierMin, CostMultiplierMax ); }
	private float _costMultiplier = 1.0f;

	//Changing this value may trigger updates to the navigation mesh.
	/// <summary>Gets or sets the priority level for the area definition. Higher values take precedence if multiple areas overlap.</summary>
	public int Priority
	{
		get => _priority;
		set
		{
			_priority = value;
			IToolsDll.Current?.RunEvent<NavMesh.IEventListener>( x => x.OnAreaDefinitionChanged() );
			if ( Game.ActiveScene.IsValid() )
			{
				Game.ActiveScene.NavMesh?.UpdateAreaIds();
			}
		}
	}
	private int _priority = 0;

	protected override void PostLoad()
	{
		PostReload();
	}

	protected override void PostReload()
	{
		IToolsDll.Current?.RunEvent<NavMesh.IEventListener>( x => x.OnAreaDefinitionChanged() );
		if ( Game.ActiveScene.IsValid() )
		{
			Game.ActiveScene.NavMesh?.UpdateAreaIds();
		}
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "explore", width, height );
	}
}
