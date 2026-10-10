namespace Rinten.Movement;

/// <summary>The character is walking</summary>
[Icon( "transfer_within_a_station" ), Group( "Movement" ), Title( "MoveMode - Walk" ), Alias( "Rinten.PhysicsCharacterMode.PhysicsCharacterWalkMode" )]
public partial class MoveModeWalk : MoveMode
{
	[Property] public int Priority { get; set; } = 0;

	[Property] public float GroundAngle { get; set; } = 45.0f;
	//Forty-five centimetres, in metres - a tall kerb, a low stair.
	[Property] public float StepUpHeight { get; set; } = 0.45f;
	[Property] public float StepDownHeight { get; set; } = 0.45f;


	public override bool AllowGrounding => true;
	public override bool AllowFalling => true;

	public override int Score( PlayerController controller ) => Priority;

	public override void AddVelocity()
	{
		Controller.WishVelocity = Controller.WishVelocity.WithY( 0 );
		base.AddVelocity();
	}

	public override void PrePhysicsStep()
	{
		base.PrePhysicsStep();

		if ( StepUpHeight > 0 )
		{
			TrySteppingUp( StepUpHeight );
		}
	}

	public override void PostPhysicsStep()
	{
		base.PostPhysicsStep();

		StickToGround( StepDownHeight );
	}

	public override bool IsStandableSurface( in SceneTraceResult result )
	{
		if ( Vector3.GetAngle( Vector3.Up, result.Normal ) > GroundAngle )
			return false;

		return true;
	}

	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		// ignore pitch when walking
		eyes = eyes.Angles() with { pitch = 0 };

		return base.UpdateMove( eyes, input );
	}
}
