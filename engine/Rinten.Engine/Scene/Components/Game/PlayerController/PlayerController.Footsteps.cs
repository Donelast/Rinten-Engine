namespace Rinten;

public sealed partial class PlayerController : Component
{
	/// <summary>Draw debug overlay on footsteps</summary>
	public bool DebugFootsteps;

	TimeSince _timeSinceStep;

	private void OnFootstepEvent( SceneModel.FootstepEvent e )
	{
		if ( !IsOnGround ) return;
		if ( !EnableFootstepSounds ) return;
		if ( _timeSinceStep < 0.2f ) return;

		_timeSinceStep = 0;

		//Ten metres a second for a full-volume step, which is the four hundred inches a second this asked for said in metres.
		float volume = e.Volume * WishVelocity.Length.Remap( 0, 10.16f, 0, 1 );
		if ( volume <= 0.1f ) return;

		PlayFootstepSound( e.Transform.Position, volume, e.FootId );
	}

	/// <summary>Play a footstep sound at the given world position. Will only play if the player has a GroundSurface.</summary>
	public void PlayFootstepSound( Vector3 worldPosition, float volume, int foot )
	{
		if ( !GroundSurface.IsValid() ) return;

		var soundEvent = foot == 0 ? GroundSurface.SoundCollection.FootLeft : GroundSurface.SoundCollection.FootRight;
		if ( soundEvent is null )
		{
			if ( DebugFootsteps )
			{
				Gizmo.Draw.LineSphere( new Sphere( worldPosition, volume ), color: Color.Orange, duration: 10, overlay: true );
			}

			return;
		}

		var handle = GameObject.PlaySound( soundEvent, 0 );
		if ( !handle.IsValid() ) return;

		handle.FollowParent = false;
		handle.TargetMixer = FootstepMixer.GetOrDefault();
		handle.Volume *= volume * FootstepVolume;

		if ( DebugFootsteps )
		{
			Gizmo.Draw.LineSphere( new Sphere( worldPosition, volume ), color: Color.White, duration: 10, overlay: true );
			Gizmo.Draw.Text( worldPosition, $"{soundEvent.ResourceName}", size: 14, flags: TextFlag.LeftTop, color: Color.White, duration: 10, overlay: true );
		}
	}
}
