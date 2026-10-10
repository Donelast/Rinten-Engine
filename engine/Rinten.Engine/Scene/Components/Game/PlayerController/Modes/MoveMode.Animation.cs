namespace Rinten.Movement;

partial class MoveMode
{
	/// <summary>Update the animator which is available at Controller.Renderer.</summary>
	public virtual void UpdateAnimator( SkinnedModelRenderer renderer )
	{
		//Animation parameters

		OnUpdateAnimatorVelocity( renderer );
		OnUpdateAnimatorState( renderer );
		OnUpdateAnimatorLookDirection( renderer );

		UpdateRotationSpeed( renderer );

		//Renderer transform

		OnRotateRenderBody( renderer );
	}

	Vector3.SmoothDamped smoothedMove = new Vector3.SmoothDamped( 0, 0, 0.5f );
	Vector3.SmoothDamped smoothedWish = new Vector3.SmoothDamped( 0, 0, 0.5f );
	Vector3.SmoothDamped smoothedSkid = new Vector3.SmoothDamped( 0, 0, 0.5f );

	//Sets animation parameters on renderer based on the current PlayerController.Velocity and PlayerController.WishVelocity.
	protected virtual void OnUpdateAnimatorVelocity( SkinnedModelRenderer renderer )
	{
		var rot = renderer.WorldRotation;
		var vel = Controller.Velocity;
		var wishVel = Controller.WishVelocity;

		//skid
		{
			var skidAmount = 0.5f; //multiplier for moving skid
			var pushSkidAmount = 1.0f; //multiplier for sliding down a slope when standing still
			var skidDelay = 0.2f; //in seconds, longer means bigger gap between the velocities, more skidding

			//smooth version of our velocity
			var smoothed = vel;
			{
				smoothedMove.Target = smoothed;
				smoothedMove.SmoothTime = skidDelay;
				smoothedMove.Update( Time.Delta );
				smoothed = smoothedMove.Current;
			}

			//skid is the difference between our old velocity and our current velocity
			var skid = (smoothed - vel) * skidAmount;

			//if we're standing still, use our actual velity as the skid. Two and a half metres a second is the hundred inches a second this was written as.
			skid = Vector3.Lerp( skid, vel * pushSkidAmount, wishVel.Length.Remap( 2.54f, 0, 0, 1 ) );

			//smooth our skidders
			smoothedSkid.Target = skid;
			smoothedSkid.SmoothTime = 0.5f;
			smoothedSkid.Update( Time.Delta );
			skid = smoothedSkid.Current;

			//convert to model space
			skid = GetLocalVelocity( rot, skid );

			//Ten metres a second is a full skid - four hundred inches a second, as this was.
			renderer.Set( "skid_x", (skid.x / 10.16f) );
			renderer.Set( "skid_y", (skid.y / 10.16f) );
		}

		//move the legs
		{
			var smoothed = wishVel;
			{
				smoothedWish.Target = smoothed;
				smoothedWish.SmoothTime = 0.6f;
				smoothedWish.Update( Time.Delta );
				smoothed = smoothedWish.Current;

				//Stop walking if we're too slow - a quarter of a metre a second, which is the ten inches a second this asked for.
				smoothed = ApplyDeadZone( smoothed, 0.254f );
			}

			smoothed = GetLocalVelocity( rot, smoothed );

			renderer.Set( "move_direction", GetAngle( smoothed ) );
			renderer.Set( "move_speed", smoothed.Length );
			renderer.Set( "move_groundspeed", smoothed.WithZ( 0f ).Length );
			renderer.Set( "move_x", smoothed.x );
			renderer.Set( "move_y", smoothed.y );
			renderer.Set( "move_z", smoothed.z );
		}

		//the player's wish
		{
			var local = GetLocalVelocity( rot, wishVel );

			renderer.Set( "wish_direction", GetAngle( local ) );
			renderer.Set( "wish_speed", wishVel.Length );
			renderer.Set( "wish_groundspeed", wishVel.WithY( 0f ).Length );
			renderer.Set( "wish_x", local.x );
			renderer.Set( "wish_y", local.y );
			renderer.Set( "wish_z", local.z );
		}

	}

	#region OnUpdateAnimatorVelocity Helpers

	private static Vector3 ApplyDeadZone( Vector3 velocity, float minimum )
	{
		return velocity.IsNearlyZero( minimum ) ? 0f : velocity;
	}

	private static Vector3 GetLocalVelocity( Rotation rotation, Vector3 worldVelocity )
	{
		//TODO: this could be rotation.Inverse * worldVelocity

		var forward = rotation.Forward.Dot( worldVelocity );
		var sideward = rotation.Right.Dot( worldVelocity );

		return new Vector3( forward, sideward, worldVelocity.y );
	}

	private static float GetAngle( Vector3 localVelocity )
	{
		return MathF.Atan2( localVelocity.y, localVelocity.x ).RadianToDegree().NormalizeDegrees();
	}

	#endregion

	//Sets animation parameters on renderer describing the movement style, like swimming, falling, or ducking.
	protected virtual void OnUpdateAnimatorState( SkinnedModelRenderer renderer )
	{
		renderer.Set( "sit", 0 );
		renderer.Set( "b_swim", Controller.IsSwimming );
		renderer.Set( "b_climbing", Controller.IsClimbing );
		renderer.Set( "b_grounded", Controller.IsOnGround || Controller.IsClimbing );
		renderer.Set( "b_ducked", Controller.IsDucking );

		//Crouch as the ceiling comes down over the last two thirds of a metre - twenty five inches, which is what this said.
		var duck = Controller.Headroom.Remap( 0.635f, 0, 0, 0.5f, true );

		if ( Controller.IsDucking )
		{
			duck *= 3.0f;
			duck += 1.0f;
		}

		renderer.Set( "duck", duck );
	}

	//Set animation parameters on renderer to look towards CalculateEyeTransform.
	protected virtual void OnUpdateAnimatorLookDirection( SkinnedModelRenderer renderer )
	{
		var eyesForward = Controller.EyeTransform.Forward;

		renderer.SetLookDirection( "aim_eyes", eyesForward, Controller.AimStrengthEyes );
		renderer.SetLookDirection( "aim_head", eyesForward, Controller.AimStrengthHead );
		renderer.SetLookDirection( "aim_body", eyesForward, Controller.AimStrengthBody );
	}

	//Updates the Component.WorldRotation of renderer.
	protected virtual void OnRotateRenderBody( SkinnedModelRenderer renderer )
	{
		if ( Controller.BodyFollowsMovement )
		{
			RotateBodyToMovement( renderer );
			return;
		}

		var eyeAngles = Controller.EyeTransform.Rotation.Angles();

		var targetAngle = Rotation.FromYaw( eyeAngles.yaw );
		var velocity = Controller.WishVelocity.WithY( 0 );

		var rotateDifference = renderer.WorldRotation.Distance( targetAngle );
		var oldRotation = renderer.WorldRotation;

		//We're over the limit - snap it
		if ( rotateDifference > Controller.RotationAngleLimit )
		{
			var delta = 0.999f - Controller.RotationAngleLimit / rotateDifference;
			var newRotation = Rotation.Lerp( renderer.WorldRotation, targetAngle, delta );

			renderer.WorldRotation = newRotation;
		}

		//Otherwise only rotate while moving. Six millimetres a second to count as moving, and full turning speed
		//by two and a half metres a second - a quarter of an inch and a hundred inches, as these were written.
		if ( velocity.Length > 0.00635f )
		{
			//TODO: frame rate dependent

			renderer.WorldRotation = Rotation.Slerp( renderer.WorldRotation, targetAngle,
				Time.Delta * 2.0f * Controller.RotationSpeed * velocity.Length.Remap( 0, 2.54f ) );
		}

		AddRotationSpeed( oldRotation, renderer.WorldRotation );
	}

	//Turns towards the way it is asked to go while it moves, and keeps that facing when it stops
	private void RotateBodyToMovement( SkinnedModelRenderer renderer )
	{
		var oldRotation = renderer.WorldRotation;
		var wish = Controller.WishVelocity.WithY( 0 );

		//Under a quarter of a metre a second is standing; a full turn rate by a walk
		if ( wish.Length > 0.25f )
		{
			var target = Rotation.LookAt( wish.Normal, Vector3.Up );
			var rate = 10.0f * Controller.RotationSpeed;

			renderer.WorldRotation = Rotation.Slerp( renderer.WorldRotation, target, 1.0f - MathF.Exp( -rate * Time.Delta ) );
		}

		AddRotationSpeed( oldRotation, renderer.WorldRotation );
	}

	private const float RotationSpeedUpdatePeriod = 0.1f;

	private float _animRotationSpeed;
	private TimeSince _timeSinceRotationSpeedUpdate;

	private void AddRotationSpeed( Rotation oldRotation, Rotation newRotation )
	{
		//TODO: this assumes we're always working with exactly one Renderer

		var oldYaw = oldRotation.Angles().yaw;
		var newYaw = newRotation.Angles().yaw;

		//TODO: why is this backwards?

		var deltaYaw = MathX.DeltaDegrees( newYaw, oldYaw );

		_animRotationSpeed = (_animRotationSpeed + deltaYaw).Clamp( -90f, 90f );
	}

	private void UpdateRotationSpeed( SkinnedModelRenderer renderer )
	{
		if ( _timeSinceRotationSpeedUpdate < RotationSpeedUpdatePeriod ) return;

		//TODO: why 5?

		renderer.Set( "move_rotationspeed", _animRotationSpeed * 5f );

		_timeSinceRotationSpeedUpdate = 0f;
		_animRotationSpeed = 0f;
	}

}
