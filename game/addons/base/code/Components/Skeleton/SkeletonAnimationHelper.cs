namespace Rinten;

/// <summary>Leans, a landing dip, planted feet and the one-off moves for the skeleton body under a PlayerController.</summary>
//The controller drives walking, running, jumping and ducking itself; its Body Follows Movement wants turning on - the clips only go forward
[Title( "Skeleton Animation Helper" )]
[Category( "Skeleton" )]
[Icon( "directions_run" )]
public sealed class SkeletonAnimationHelper : Component, PlayerController.IEvents
{
	/// <summary>The body the parameters are set on.</summary>
	[Property] public SkinnedModelRenderer Target { get; set; }

	/// <summary>Whose movement the body follows; the nearest one up the hierarchy when left empty.</summary>
	[Property] public PlayerController Controller { get; set; }

	/// <summary>Forward acceleration, metres a second squared, that makes a full lean.</summary>
	[Property, Group( "Weight" )] public float LeanAcceleration { get; set; } = 14.0f;

	/// <summary>Lean held for going flat out, at run speed.</summary>
	[Property, Group( "Weight" ), Range( 0, 1 )] public float RunLean { get; set; } = 0.35f;

	/// <summary>How far the upper body bends into a turn at a walk and at a run.</summary>
	[Property, Group( "Weight" ), Range( 0, 1 )] public float SideLeanWalk { get; set; } = 0.4f;

	/// <inheritdoc cref="SideLeanWalk"/>
	[Property, Group( "Weight" ), Range( 0, 1 )] public float SideLeanRun { get; set; } = 1.0f;

	/// <summary>How far the shoulders and head turn ahead into a turn.</summary>
	[Property, Group( "Weight" ), Range( 0, 1 )] public float TwistAmount { get; set; } = 0.8f;

	/// <summary>How deep a landing sinks the body, per metre a second it came down at.</summary>
	[Property, Group( "Weight" )] public float LandingDip { get; set; } = 0.12f;

	/// <summary>How quickly the lean follows - higher is snappier.</summary>
	[Property, Group( "Weight" )] public float WeightSmoothing { get; set; } = 6.0f;

	/// <summary>Whether a standing body puts its feet on the ground under them.</summary>
	[Property, Group( "Foot IK" )] public bool FootIk { get; set; } = true;

	/// <summary>Furthest the body drops, metres, for a foot to reach down a step.</summary>
	[Property, Group( "Foot IK" )] public float MaxDrop { get; set; } = 0.35f;

	//Forward acceleration, filtered, and the lean it asks for; read on the tick, when the velocity changes
	Vector3 lastVelocity;
	float accelFiltered, wantLean;

	float lean, side, twist;
	float dip, dipKick;

	//The animated feet relative to the undropped body, taken while the IK is off so it never feeds back into its own aim
	Transform footLeft, footRight;
	bool haveFeet;
	TimeSince ikOff;

	//How far the body is dropped, what of that is in its position, and the position it was left at
	float drop, dropApplied;
	Vector3 leftAt;

	/// <summary>Plays the roll.</summary>
	public void Roll() => Target?.Set( "roll", true );

	/// <summary>Plays the sword swing.</summary>
	public void AttackSword() => Target?.Set( "attack_sword", true );

	/// <summary>Plays the punch.</summary>
	public void AttackPunch() => Target?.Set( "attack_punch", true );

	/// <summary>Plays the hit reaction.</summary>
	public void Hit() => Target?.Set( "hit", true );

	/// <summary>Drops the body dead; it stays in the death pose until revived.</summary>
	public void Die()
	{
		Target?.Set( "dead", true );
		Target?.Set( "foot_ik", false );
	}

	/// <summary>Gets up from the death pose.</summary>
	public void Revive() => Target?.Set( "dead", false );

	protected override void OnStart()
	{
		Target ??= GetComponent<SkinnedModelRenderer>() ?? GetComponentInChildren<SkinnedModelRenderer>();
		Controller ??= GetComponentInParent<PlayerController>();
	}

	protected override void OnFixedUpdate()
	{
		if ( !Controller.IsValid() || IsProxy ) return;

		var dt = MathF.Max( Time.Delta, 0.0001f );
		var velocity = Controller.Velocity.WithY( 0 );
		var speed = velocity.Length;
		var runFraction = RunFraction( speed );
		var want = 0.0f;

		if ( Controller.IsOnGround )
		{
			var accel = (speed - lastVelocity.Length) / dt;
			accelFiltered = accelFiltered.LerpTo( accel, 0.15f );

			//A walk barely leans; the lean comes in with speed, and the lean held for pace grows with its square
			want = accelFiltered / LeanAcceleration * MathF.Max( runFraction, 0.15f ) + RunLean * runFraction * runFraction;
		}
		else
		{
			accelFiltered = 0;
		}

		wantLean = want.Clamp( -1, 1 );
		lastVelocity = velocity;
	}

	protected override void OnUpdate()
	{
		if ( !Target.IsValid() || !Controller.IsValid() || IsProxy ) return;

		var t = 1.0f - MathF.Exp( -WeightSmoothing * Time.Delta );
		var velocity = Controller.Velocity.WithY( 0 );
		var runFraction = RunFraction( velocity.Length );

		//How far the body is behind the way it is asked to go - a sharp change of direction throws its weight into catching up
		var intent = 0.0f;
		if ( Controller.IsOnGround && velocity.Length > 0.3f )
			intent = (AngleTo( Target.WorldRotation, Controller.WishVelocity ) / 90.0f).Clamp( -1, 1 );

		lean = lean.LerpTo( wantLean, t );
		side = side.LerpTo( intent * SideLeanWalk.LerpTo( SideLeanRun, runFraction ), t );
		twist = twist.LerpTo( intent * TwistAmount, t );

		//The kick fades on its own; the dip chases it quickly, so it sinks and comes back up
		dipKick = dipKick.LerpTo( 0, 1.0f - MathF.Exp( -5.0f * Time.Delta ) );
		dip = dip.LerpTo( dipKick, 1.0f - MathF.Exp( -18.0f * Time.Delta ) );

		Target.Set( "lean", lean );
		Target.Set( "lean_side", side );
		Target.Set( "turn", twist );
		Target.Set( "dip", dip );

		UpdateFeet( velocity );
	}

	//The drop goes on after the controller has placed the body for the frame; a body placed anew since carries none of it
	protected override void OnPreRender()
	{
		if ( !Target.IsValid() ) return;

		var position = Target.LocalPosition;
		if ( position != leftAt ) dropApplied = 0;

		Target.LocalPosition = position + Vector3.Up * (drop - dropApplied);
		dropApplied = drop;
		leftAt = Target.LocalPosition;
	}

	void PlayerController.IEvents.OnLanded( float distance, Vector3 impactVelocity )
	{
		//Whichever says it came down harder - the speed it hit at, or the height it fell
		var hit = MathF.Max( MathF.Abs( impactVelocity.y ), MathF.Sqrt( 2.0f * 9.81f * MathF.Max( distance, 0 ) ) );
		dipKick = MathF.Max( dipKick, (hit * LandingDip).Clamp( 0, 1 ) );
	}

	float RunFraction( float speed ) => (speed / MathF.Max( Controller.RunSpeed, 0.1f )).Clamp( 0, 1 );

	//Standing still, each foot goes on the ground under it and the body drops so the lower one reaches
	void UpdateFeet( Vector3 velocity )
	{
		var standing = FootIk && Controller.IsOnGround && velocity.Length < 0.25f;
		var root = new Transform( Controller.WorldPosition, Target.WorldRotation );

		if ( !standing )
		{
			ikOff = 0;
			Target.Set( "foot_ik", false );
			drop = drop.LerpTo( 0, (Time.Delta * 10.0f).Clamp( 0, 1 ) );
			return;
		}

		//The animated feet, once the IK has been off long enough to blend out
		if ( ikOff < 0.6f )
		{
			haveFeet = TakeFoot( "foot_l", root, out footLeft ) & TakeFoot( "foot_r", root, out footRight );
			return;
		}

		if ( !haveFeet ) return;

		var left = Plant( root, footLeft, out var dropLeft );
		var right = Plant( root, footRight, out var dropRight );

		Target.Set( "foot_l_target", left );
		Target.Set( "foot_r_target", right );
		Target.Set( "foot_ik", true );

		var wantDrop = MathF.Min( 0, MathF.Min( dropLeft, dropRight ) ).Clamp( -MaxDrop, 0 );
		drop = drop.LerpTo( wantDrop, (Time.Delta * 10.0f).Clamp( 0, 1 ) );
	}

	bool TakeFoot( string bone, Transform root, out Transform local )
	{
		local = default;
		if ( !Target.TryGetBoneTransform( bone, out var world ) ) return false;

		world.Position -= Vector3.Up * drop;
		local = root.ToLocal( world );
		return true;
	}

	//Where a foot goes: over the ground beneath it, turned to lie on it
	Transform Plant( Transform root, Transform local, out float groundOffset )
	{
		var foot = root.ToWorld( local );
		var height = local.Position.y;

		var tr = Scene.Trace.Ray( foot.Position.WithY( root.Position.y + 0.5f ), foot.Position.WithY( root.Position.y - MaxDrop - 0.1f ) )
			.IgnoreGameObjectHierarchy( Controller.GameObject )
			.Run();

		if ( !tr.Hit || tr.StartedSolid )
		{
			groundOffset = 0;
			return foot;
		}

		groundOffset = tr.HitPosition.y - root.Position.y;

		var tilt = Rotation.FromToRotation( Vector3.Up, tr.Normal );
		return new Transform( tr.HitPosition + Vector3.Up * height, tilt * foot.Rotation );
	}

	//Degrees from a rotation's forward round to a direction, positive to the right
	static float AngleTo( Rotation from, Vector3 direction )
	{
		direction = direction.WithY( 0 );
		if ( direction.IsNearZeroLength ) return 0;

		var forward = from.Forward;
		var right = forward.Cross( Vector3.Up );
		var to = direction.Normal;

		return MathF.Atan2( to.Dot( right ), to.Dot( forward ) ).RadianToDegree();
	}
}
