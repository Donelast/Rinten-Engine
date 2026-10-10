namespace Rinten;

/// <summary>Dictates where players will spawn when they join the game when using a NetworkHelper.</summary>
[Expose]
[Title( "Spawn Point" )]
[Category( "Game" )]
[Icon( "accessibility_new" )]
[EditorHandle( "materials/gizmo/spawnpoint.png" )]
public sealed class SpawnPoint : Component
{
	[Property] public Color Color { get; set; } = "#E3510D";
	protected override void DrawGizmos()
	{
		base.DrawGizmos();

		var spawnpointModel = Model.Load( "models/skeleton/skeleton.mdl" );

		Gizmo.Hitbox.Model( spawnpointModel );
		Gizmo.Draw.Color = Color.WithAlpha( (Gizmo.IsHovered || Gizmo.IsSelected) ? 0.7f : 0.5f );
		var so = Gizmo.Draw.Model( spawnpointModel );
		if ( so is not null )
		{
			so.Flags.CastShadows = true;

			//Standing as the player will, not in the rig's T-pose: the first frame of the idle, held
			so.UseAnimGraph = false;
			if ( so.CurrentSequence.Name != "Idle_Loop" ) so.CurrentSequence.Name = "Idle_Loop";
			so.Update( 0 );
		}
	}

}


