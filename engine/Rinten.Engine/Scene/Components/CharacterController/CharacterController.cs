namespace Rinten;

//All lengths and speeds are in metres (converted from the original inches)
/// <summary>Allows collision constrained movement without the need for a rigidbody. This is not affected by forces and will only move when you call the Move() method.</summary>
[Expose]
[Title( "Character Controller" )]
[Category( "Physics" )]
[Icon( "directions_walk" )]
[EditorHandle( "materials/gizmo/charactercontroller.png" )]
public class CharacterController : Component
{
	/// <summary>How wide the character is, in metres. Forty centimetres across the middle.</summary>
	[Range( 0, 5 )]
	[Property] public float Radius { get; set; } = 0.4f;

	/// <summary>How tall the character is, in metres.</summary>
	[Range( 0, 5 )]
	[Property] public float Height { get; set; } = 1.6f;

	/// <summary>The tallest thing it can walk up without jumping, in metres.</summary>
	[Range( 0, 1.5f )]
	[Property] public float StepHeight { get; set; } = 0.45f;

	/// <summary>The steepest ground it can stand on, in degrees.</summary>
	[Range( 0, 90 )]
	[Property] public float GroundAngle { get; set; } = 45.0f;

	//Not a length and not a speed: Vector3.WithAcceleration multiplies it by the speed being asked for, so it is a share of that per second and means the same whatever a unit is.
	/// <summary>How hard it gets up to the speed being asked for.</summary>
	[Range( 0, 64 )]
	[Property] public float Acceleration { get; set; } = 10.0f;

	/// <summary>When jumping into walls, should we bounce off or just stop dead?</summary>
	[Range( 0, 1 )]
	[Property] public float Bounciness { get; set; } = 0.3f;

	/// <summary>If enabled, determine what to collide with using current project's collision rules for the <see cref="GameObject.Tags"/> of the containing <see cref="GameObject"/>.</summary>
	[Property, Group( "Collision" ), Title( "Use Project Collision Rules" )] public bool UseCollisionRules { get; set; } = false;

	[Property, Group( "Collision" ), HideIf( nameof( UseCollisionRules ), true )]
	public TagSet IgnoreLayers { get; set; } = new();

	//Up is y. This box was left saying z when the engine's axes changed, which stood the character on its side - a metre and a half deep, eight hundred millimetres tall - and everything else in here already counts the other way: the ground test steps down y, the ceiling test looks up y.
	/// <summary>The hull that is traced with: as wide as the radius on both horizontal axes, and standing from the feet to <see cref="Height"/>.</summary>
	public BBox BoundingBox => new BBox( new Vector3( -Radius, 0, -Radius ), new Vector3( Radius, Height, Radius ) );

	[Sync]
	public Vector3 Velocity { get; set; }

	[Sync]
	public bool IsOnGround { get; set; }

	public GameObject GroundObject { get; set; }
	public Collider GroundCollider { get; set; }

	protected override void DrawGizmos()
	{
		Gizmo.Draw.LineBBox( BoundingBox );
	}

	/// <summary>Add acceleration to the current velocity. No need to scale by time delta - it will be done inside.</summary>
	public void Accelerate( Vector3 vector )
	{
		Velocity = Velocity.WithAcceleration( vector, Acceleration * Time.Delta );
	}

	/// <summary>Apply an amount of friction to the current velocity. No need to scale by time delta - it will be done inside.</summary>
	public void ApplyFriction( float frictionAmount, float stopSpeed = 3.5f )
	{
		var speed = Velocity.Length;

		//A quarter of a millimetre a second. Not stopped, but near enough that nothing below here would change where anything is.
		if ( speed < 0.00025f ) return;

		//Bleed off some speed, but if we have less than the bleed threshold, bleed the threshold amount.
		float control = (speed < stopSpeed) ? stopSpeed : speed;

		//Add the amount to the drop amount.
		var drop = control * Time.Delta * frictionAmount;

		//scale the velocity
		float newspeed = speed - drop;
		if ( newspeed < 0 ) newspeed = 0;
		if ( newspeed == speed ) return;

		newspeed /= speed;
		Velocity *= newspeed;
	}

	SceneTrace BuildTrace( Vector3 from, Vector3 to ) => BuildTrace( Scene.Trace.Ray( from, to ) );

	SceneTrace BuildTrace( SceneTrace source )
	{
		var trace = source.Size( BoundingBox ).IgnoreGameObjectHierarchy( GameObject );

		return UseCollisionRules ? trace.WithCollisionRules( Tags ) : trace.WithoutTags( IgnoreLayers );
	}

	/// <summary>Trace the controller's current position to the specified delta</summary>
	public SceneTraceResult TraceDirection( Vector3 direction )
	{
		return BuildTrace( GameObject.WorldPosition, GameObject.WorldPosition + direction ).Run();
	}

	void Move( bool step )
	{
		if ( step && IsOnGround )
		{
			Velocity = Velocity.WithY( 0 );
		}

		//Thousandths of a millimetre a second: moving, but not in any sense a trace would report.
		if ( Velocity.Length < 0.000025f )
		{
			Velocity = Vector3.Zero;
			return;
		}

		var pos = GameObject.WorldPosition;

		var mover = new CharacterControllerHelper( BuildTrace( pos, pos ), pos, Velocity );
		mover.Bounce = Bounciness;
		mover.MaxStandableAngle = GroundAngle;

		if ( step && IsOnGround )
		{
			mover.TryMoveWithStep( Time.Delta, StepHeight );
		}
		else
		{
			mover.TryMove( Time.Delta );
		}

		WorldPosition = mover.Position;
		Velocity = mover.Velocity;
	}

	void CategorizePosition()
	{
		var Position = WorldPosition;
		//Five centimetres down, to find the floor we are presumably standing on.
		var point = Position + Vector3.Down * 0.05f;
		var vBumpOrigin = Position;
		var wasOnGround = IsOnGround;

		//We're flying upwards too fast, never land on ground
		if ( !IsOnGround && Velocity.y > 1.0f )
		{
			ClearGround();
			return;
		}

		//Step down by StepHeight when grounded, otherwise probe 2.5mm to find a floor without snapping
		point.y -= wasOnGround ? StepHeight : 0.0025f;


		var pm = BuildTrace( vBumpOrigin, point ).Run();

		//we didn't hit - or the ground is too steep to be ground
		if ( !pm.Hit || Vector3.GetAngle( Vector3.Up, pm.Normal ) > GroundAngle )
		{
			ClearGround();
			return;
		}

		//we are on ground
		IsOnGround = true;
		GroundObject = pm.GameObject;
		GroundCollider = pm.Shape?.Collider;

		//move to this ground position, if we moved, and hit
		if ( wasOnGround && !pm.StartedSolid && pm.Fraction > 0.0f && pm.Fraction < 1.0f )
		{
			WorldPosition = pm.EndPosition;
		}
	}

	/// <summary>Disconnect from ground and punch our velocity. This is useful if you want the player to jump or something.</summary>
	public void Punch( in Vector3 amount )
	{
		ClearGround();
		Velocity += amount;
	}

	void ClearGround()
	{
		IsOnGround = false;
		GroundObject = default;
		GroundCollider = default;
	}

	/// <summary>Move a character, with this velocity</summary>
	public void Move()
	{
		if ( TryUnstuck() )
			return;

		if ( IsOnGround )
		{
			Move( true );
		}
		else
		{
			Move( false );
		}

		CategorizePosition();
	}

	/// <summary>Move from our current position to this target position, but using tracing an sliding. This is good for different control modes like ladders and stuff.</summary>
	public void MoveTo( Vector3 targetPosition, bool useStep )
	{
		if ( TryUnstuck() )
			return;

		var pos = WorldPosition;
		var delta = targetPosition - pos;

		var mover = new CharacterControllerHelper( BuildTrace( pos, pos ), pos, delta );
		mover.MaxStandableAngle = GroundAngle;

		if ( useStep )
		{
			mover.TryMoveWithStep( 1.0f, StepHeight );
		}
		else
		{
			mover.TryMove( 1.0f );
		}

		WorldPosition = mover.Position;
	}

	int _stuckTries;

	bool TryUnstuck()
	{
		var result = BuildTrace( WorldPosition, WorldPosition ).Run();

		//Not stuck, we cool
		if ( !result.StartedSolid )
		{
			_stuckTries = 0;
			return false;
		}

		//using ( Gizmo.Scope( "unstuck", WorldTransform ) )
		//{
		//Gizmo.Draw.Color = Gizmo.Colors.Red;
		//Gizmo.Draw.LineBBox( BoundingBox );
		//}

		int AttemptsPerTick = 20;

		for ( int i = 0; i < AttemptsPerTick; i++ )
		{
			//Feeling further out with every tick spent stuck: a bit over a centimetre a try.
			var pos = WorldPosition + Vector3.Random.Normal * (_stuckTries * 0.0127f);

			//First try the up direction for moving platforms
			if ( i == 0 )
			{
				pos = WorldPosition + Vector3.Up * 0.05f;
			}

			result = BuildTrace( pos, pos ).Run();

			if ( !result.StartedSolid )
			{
				//Log.Info( $"unstuck after {_stuckTries} tries ({_stuckTries * AttemptsPerTick} tests)" );
				WorldPosition = pos;
				return false;
			}
		}

		_stuckTries++;

		return true;
	}

}
