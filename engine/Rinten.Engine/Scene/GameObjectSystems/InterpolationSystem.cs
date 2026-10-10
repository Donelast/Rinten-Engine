using Rinten.Utility;

namespace Rinten;

/// <summary>Updates interpolation for any <see cref="GameTransform"/> that needs it.</summary>
[Expose]
sealed class InterpolationSystem : GameObjectSystem<InterpolationSystem>
{
	HashSetEx<GameObject> _list { get; set; } = new();

	[ConVar( "debug_interp", ConVarFlags.Protected | ConVarFlags.Cheat )]
	static bool Debug { get; set; }

	public InterpolationSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.Interpolation, -1, Update, "UpdateInterpolation" );
	}

	//Add a GameObject to the interpolation list.
	internal void AddGameObject( GameObject go )
	{
		_list.Add( go );
	}

	//Remove a GameObject from the interpolation list.
	internal void RemoveGameObject( GameObject go )
	{
		_list.Remove( go );
	}

	private void Update()
	{
		var now = Time.NowDouble;
		float updateFreq = ProjectSettings.Physics.FixedUpdateFrequency.Clamp( 1, 1000 );

		//Keep more history to avoid culling data we might still need for interpolation
		var cullBefore = now - (1f / updateFreq) * 2f;

		foreach ( var go in _list.EnumerateLocked( true ) )
		{
			if ( go.IsValid() )
			{
				go.Transform.Update( now, cullBefore );

				if ( Debug )
				{
					DrawDebug( go );
				}
			}
		}
	}

	private void DrawDebug( GameObject go )
	{
		using var _ = Gizmo.Scope();
		var targetWorld = go.WorldTransform;
		var world = go.Transform.InterpolatedWorld;

		Gizmo.Draw.Color = Color.White;
		Gizmo.Draw.LineSphere( world.Position, 0.4f );

		Gizmo.Draw.Color = Color.Cyan;
		Gizmo.Draw.LineSphere( targetWorld.Position, 0.4f );
	}
}
