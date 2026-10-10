using System.Text.Json.Serialization;

namespace Rinten;

/// <summary>What a Goal aims a particle at.</summary>
[Expose]
public enum ParticleGoalTarget
{
	/// <summary>One of the effect's shapes, sampled by index or by time.</summary>
	Shape,

	/// <summary>One of the effect's anchors - a bone, an object, the hand.</summary>
	Anchor,

	/// <summary>A position the particle remembered in a slot.</summary>
	Slot,
}

//The simulation keeps running underneath - forces, drag, collision - on a "free" pose the module carries on the particle, and what is shown is the blend. So a weight that goes 0 → 1 → 0 is a particle that is caught and let go, with its own motion still there when it is.
/// <summary>Pulls a particle from wherever the simulation has it towards a target pose, by a weight curve over its life: nought is the simulation alone, one is on the target. Sparks that fly out and then gather on a hand; shards that scatter and come home; anything that homes.</summary>
[Title( "Goal" ), Category( "Update" ), Icon( "my_location" )]
public sealed class GoalModule : ParticleModule, IParticleModuleStep
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Update;

	[Property]
	public ParticleGoalTarget Target { get; set; } = ParticleGoalTarget.Anchor;

	[Property, ShowIf( nameof( Target ), ParticleGoalTarget.Shape ), ParticleShapeName, Title( "Effect Shape" )]
	public string ShapeName { get; set; }

	/// <summary>How the shape is walked: by index, each particle to its own place; by time, to where Along says.</summary>
	[Property, ShowIf( nameof( Target ), ParticleGoalTarget.Shape )]
	public ParticleShapeSample Sample { get; set; } = ParticleShapeSample.ByIndex;

	[Property, ShowIf( nameof( Target ), ParticleGoalTarget.Shape )]
	public ParticleFloatBinding Along { get; set; } = 0.0f;

	[Property, ShowIf( nameof( Target ), ParticleGoalTarget.Anchor ), ParticleAnchorName]
	public string AnchorName { get; set; }

	/// <summary>Offset from the target, in its own space.</summary>
	[Property]
	public Vector3 Offset { get; set; } = Vector3.Zero;

	[Property, ShowIf( nameof( Target ), ParticleGoalTarget.Slot ), ParticleSlotName]
	public string SlotName { get; set; }

	//The shape and the slot this names, found once each - see ParticleShapeAt.
	private ParticleShapeAt resolvedShape;
	private ParticleSlotAt resolvedSlot;

	/// <summary>How much of the way to the target's position the particle is, over its life.</summary>
	[Property, Title( "Position Weight" )]
	public ParticleFloatBinding PositionWeight { get; set; } = new ParticleFloat
	{
		Type = ParticleFloat.ValueType.Curve,
		Evaluation = ParticleFloat.EvaluationType.Life,
		CurveA = new Curve( new Curve.Frame( 0.0f, 0.0f ), new Curve.Frame( 1.0f, 1.0f ) ),
		CurveB = new Curve( new Curve.Frame( 0.0f, 0.0f ), new Curve.Frame( 1.0f, 1.0f ) ),
	};

	/// <summary>How much the particle turns to the target's rotation - an anchor's, or the way a shape faces.</summary>
	[Property, Title( "Rotation Weight" )]
	public ParticleFloatBinding RotationWeight { get; set; } = 0.0f;

	/// <summary>How much the particle takes the anchor's scale, over its own size.</summary>
	[Property, ShowIf( nameof( Target ), ParticleGoalTarget.Anchor ), Title( "Scale Weight" )]
	public ParticleFloatBinding ScaleWeight { get; set; } = 0.0f;

	/// <summary>Turn the particle to face the way it is actually moving, after the blend.</summary>
	[Property]
	public bool AlignToMotion { get; set; }

	/// <summary>Whether the particle's velocity is set to the move it made, so a ribbon and a stretched sprite follow it.</summary>
	[Property]
	public bool WriteVelocity { get; set; } = true;

	public override void Apply( ParticleModuleContext context ) { }

	public void Step( ParticleModuleContext context, float delta )
	{
		var particle = context.Particle;
		var player = context.Player;

		if ( particle is null || !player.IsValid() ) return;
		if ( !Resolve( context, out var target, out var rotation, out var scale ) ) return;

		Drivers.Begin( particle );

		//Free pose: where simulation alone would put the particle; advanced by velocity while held
		if ( !particle.DriverHeld )
		{
			particle.DriverFree = particle.Position;
			particle.DriverFreeVelocity = particle.Velocity;
		}
		else
		{
			var own = particle.DriverFreeVelocity + ( particle.Velocity - particle.DriverShown );

			particle.DriverFree += own * delta;
			particle.DriverFreeVelocity = own;
		}

		var w = PositionWeight.Evaluate( particle, player ).Clamp( 0.0f, 1.0f );
		var next = Vector3.Lerp( particle.DriverFree, target, w );

		particle.GoalDistance = next.Distance( target );

		//Near full weight, snap the free pose to the target and zero its velocity; partial weights blend
		var hold = ( ( w - 0.9f ) * 10.0f ).Clamp( 0.0f, 1.0f );

		if ( hold > 0.0f )
		{
			particle.DriverFree = Vector3.Lerp( particle.DriverFree, next, hold );
			particle.DriverFreeVelocity = Vector3.Lerp( particle.DriverFreeVelocity, Vector3.Zero, hold );
		}

		if ( w <= 0.0f )
		{
			//Handed back to the simulation: the engine moves and collides it.
			particle.Position = next;
			particle.Velocity = particle.DriverFreeVelocity;
			particle.DriverHeld = false;
		}
		else
		{
			var shown = WriteVelocity && delta > 0.0f ? ( next - particle.Position ) / delta : particle.DriverFreeVelocity;

			particle.Position = next;
			particle.Velocity = shown;
			particle.DriverShown = shown;
			particle.DriverHeld = true;
			particle.Kinematic = true;
		}

		var rw = RotationWeight.Evaluate( particle, player ).Clamp( 0.0f, 1.0f );

		if ( rw > 0.0f )
			particle.Angles = Rotation.Slerp( particle.Angles.ToRotation(), rotation, rw ).Angles();

		if ( AlignToMotion && !particle.Velocity.IsNearlyZero() )
			particle.Angles = Rotation.LookAt( particle.Velocity.Normal ).Angles();

		if ( Target == ParticleGoalTarget.Anchor )
		{
			var sw = ScaleWeight.Evaluate( particle, player ).Clamp( 0.0f, 1.0f );

			if ( sw > 0.0f ) particle.Size = Vector3.Lerp( particle.Size, scale, sw );
		}
	}

	//Where the target is now, and which way it faces.
	bool Resolve( ParticleModuleContext context, out Vector3 position, out Rotation rotation, out Vector3 scale )
	{
		var particle = context.Particle;
		var player = context.Player;

		position = default;
		rotation = Rotation.Identity;
		scale = Vector3.One;

		switch ( Target )
		{
			case ParticleGoalTarget.Shape:
			{
				var shape = ParticleShapeAt.Resolve( ref resolvedShape, player.Definition, ShapeName );

				if ( shape is null ) return false;

				var places = Sample == ParticleShapeSample.ByIndex ? shape.Places( context.Emitter?.BurstCount ?? 1 ) : 1;
				var along = Sample == ParticleShapeSample.ByTime ? Along.Evaluate( particle, player ) : 0.0f;
				var pose = shape.SampleWorld( player, Sample == ParticleShapeSample.Random ? ParticleShapeSample.ByIndex : Sample, particle.Index, places, along );

				//A model's pieces go home the way the model faces; anything else faces the way the shape does where the particle lands.
				rotation = shape.Kind == ParticleShape.ShapeKind.Model || pose.Normal.IsNearlyZero()
					? shape.Frame( player ).Rotation * shape.Rotation.ToRotation()
					: Rotation.LookAt( pose.Normal );
				position = pose.Position + rotation * Offset;

				return true;
			}

			case ParticleGoalTarget.Anchor:
			{
				var tx = player.Anchor( AnchorName );

				rotation = tx.Rotation;
				scale = tx.Scale;
				position = tx.PointToWorld( Offset );

				return true;
			}

			case ParticleGoalTarget.Slot:
			{
				var index = ParticleSlotAt.Resolve( ref resolvedSlot, player.Definition, SlotName );

				if ( index < 0 ) return false;

				var slot = particle.Slot( index );

				position = new Vector3( slot.x, slot.y, slot.z ) + Offset;

				return true;
			}
		}

		return false;
	}
}

//Only the change since the last step is applied, so a curve is what the particle has at that moment in its life rather than something that piles up every frame.
/// <summary>Moves, turns and scales a particle by curves over its life, on top of whatever the simulation does: a puff that drifts up as it fades, a glyph that grows and turns, a sheet that stretches out from its hilt.</summary>
[Title( "Transform Over Life" ), Category( "Update" ), Icon( "open_with" )]
public sealed class TransformOverLifeModule : ParticleModule, IParticleModuleStep
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Update;

	/// <summary>Whether the offset is in the emitter's space, or the world's.</summary>
	[Property]
	public ParticleSpace Space { get; set; } = ParticleSpace.Local;

	/// <summary>Where the particle is moved to, from where it would be, over its life.</summary>
	[Property]
	public ParticleVectorBinding Offset { get; set; } = Vector3.Zero;

	/// <summary>Degrees of pitch, yaw and roll added over its life.</summary>
	[Property]
	public ParticleVectorBinding Rotation { get; set; } = Vector3.Zero;

	/// <summary>What its size is multiplied by over its life.</summary>
	[Property]
	public ParticleFloatBinding Scale { get; set; } = 1.0f;

	public override void Apply( ParticleModuleContext context ) { }

	public void Step( ParticleModuleContext context, float delta )
	{
		var particle = context.Particle;
		var player = context.Player;

		if ( particle is null ) return;

		Drivers.Begin( particle );

		var offset = Offset.Evaluate( particle, player );

		if ( Space == ParticleSpace.Local && player.IsValid() )
			offset = player.WorldRotation * offset;

		particle.Position += offset - particle.DriverOffset;
		particle.DriverFree += offset - particle.DriverOffset;
		particle.DriverOffset = offset;

		var turn = Rotation.Evaluate( particle, player );
		var angles = new Angles( turn.x, turn.y, turn.z );

		particle.Angles += angles - particle.DriverAngles;
		particle.DriverAngles = angles;

		var scale = MathF.Max( Scale.Evaluate( particle, player ), 1e-4f );

		particle.Size *= scale / particle.DriverScale;
		particle.DriverScale = scale;
	}
}

//The bookkeeping the drivers share on a particle.
static class Drivers
{
	/// <summary>The first step of a particle: the free pose is where it was born.</summary>
	public static void Begin( Particle particle )
	{
		if ( particle.DriverStarted ) return;

		particle.DriverStarted = true;
		particle.DriverHeld = false;
		particle.DriverFree = particle.Position;
		particle.DriverFreeVelocity = particle.Velocity;
		particle.DriverShown = particle.Velocity;
		particle.DriverOffset = Vector3.Zero;
		particle.DriverAngles = Angles.Zero;
		particle.DriverScale = 1.0f;
	}
}
