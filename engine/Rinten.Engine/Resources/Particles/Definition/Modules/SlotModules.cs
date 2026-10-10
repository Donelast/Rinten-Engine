using System.Text.Json.Serialization;

namespace Rinten;

/// <summary>Something about a particle that can be written into a slot.</summary>
[Expose]
public enum ParticleFact
{
	/// <summary>Where it is, in the world.</summary>
	Position,

	/// <summary>Where it is, from the emitter.</summary>
	LocalPosition,

	/// <summary>Which way it is going, and how fast.</summary>
	Velocity,

	/// <summary>What colour it is, with alpha.</summary>
	Color,

	/// <summary>How big it is.</summary>
	Size,

	/// <summary>Its angles: pitch, yaw, roll.</summary>
	Angles,

	/// <summary>Four numbers that are its own, never changing, nought to one.</summary>
	Random,

	/// <summary>Its index, its index over the places, its age, and its life.</summary>
	Index,
}

/// <summary>Writes something the particle is at birth into one of the effect's slots, so it can be read back later however far the simulation has taken it - its home, the way it was thrown, the colour it started with.</summary>
[Title( "Remember" ), Category( "Initialize" ), Icon( "bookmark_add" )]
public sealed class RememberModule : ParticleModule
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Initialize;

	/// <summary>The slot written.</summary>
	[Property, ParticleSlotName]
	public string Slot { get; set; }

	//Which slot Slot is, found once - see ParticleSlotAt.
	private ParticleSlotAt resolvedSlot;

	/// <summary>What is written.</summary>
	[Property]
	public ParticleFact What { get; set; } = ParticleFact.Position;

	public override void Apply( ParticleModuleContext context )
	{
		var particle = context.Particle;
		var definition = context.Player?.Definition;

		if ( particle is null || definition is null ) return;

		var index = ParticleSlotAt.Resolve( ref resolvedSlot, definition, Slot );

		if ( index < 0 ) return;

		particle.SetSlot( index, definition.Slots.Count, Read( What, particle, context ) );
	}

	/// <summary>The fact as four numbers - the same for every writer.</summary>
	public static Vector4 Read( ParticleFact what, Particle particle, ParticleModuleContext context )
	{
		switch ( what )
		{
			case ParticleFact.Position: return new Vector4( particle.Position, 1.0f );
			case ParticleFact.LocalPosition:
			{
				var local = context.Player.IsValid() ? context.Player.WorldTransform.PointToLocal( particle.Position ) : particle.Position;
				return new Vector4( local, 1.0f );
			}
			case ParticleFact.Velocity: return new Vector4( particle.Velocity, particle.Velocity.Length );
			case ParticleFact.Color: return new Vector4( particle.Color.r, particle.Color.g, particle.Color.b, particle.Alpha );
			case ParticleFact.Size: return new Vector4( particle.Size, particle.Size.x );
			case ParticleFact.Angles: return new Vector4( particle.Angles.pitch, particle.Angles.yaw, particle.Angles.roll, 0.0f );
			case ParticleFact.Random: return new Vector4( particle.Rand( 11 ), particle.Rand( 12 ), particle.Rand( 13 ), particle.Rand( 14 ) );
			case ParticleFact.Index: return new Vector4( particle.Index, particle.IndexDelta, particle.Age, particle.DeathTime - particle.BornTime );
		}

		return Vector4.Zero;
	}
}

/// <summary>Writes a value into one of the effect's slots while the particle lives - a curve, a parameter, another slot scaled - so a later module reads what this one worked out.</summary>
[Title( "Set Slot" ), Category( "Update" ), Icon( "bookmark" )]
public sealed class SetSlotModule : ParticleModule, IParticleModuleStep
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Update;

	/// <summary>The slot written.</summary>
	[Property, ParticleSlotName]
	public string Slot { get; set; }

	//Which slot Slot is, found once - see ParticleSlotAt.
	private ParticleSlotAt resolvedSlot;

	/// <summary>Whether the value is something the particle is, or a number written here.</summary>
	[Property]
	public bool FromParticle { get; set; }

	[Property, ShowIf( nameof( FromParticle ), true )]
	public ParticleFact What { get; set; } = ParticleFact.Position;

	/// <summary>The value written, as x, y, z - read per particle, so a curve over life works.</summary>
	[Property, ShowIf( nameof( FromParticle ), false )]
	public ParticleVectorBinding Value { get; set; } = Vector3.Zero;

	/// <summary>The fourth number written.</summary>
	[Property, ShowIf( nameof( FromParticle ), false )]
	public ParticleFloatBinding W { get; set; } = 0.0f;

	/// <summary>Only on the first step, rather than every one.</summary>
	[Property]
	public bool Once { get; set; }

	public override void Apply( ParticleModuleContext context ) { }

	public void Step( ParticleModuleContext context, float delta )
	{
		var particle = context.Particle;
		var definition = context.Player?.Definition;

		if ( particle is null || definition is null ) return;

		var index = ParticleSlotAt.Resolve( ref resolvedSlot, definition, Slot );

		if ( index < 0 ) return;
		if ( Once && particle.Age > delta * 1.5f ) return;

		var value = FromParticle
			? RememberModule.Read( What, particle, context )
			: new Vector4( Value.Evaluate( particle, context.Player ), W.Evaluate( particle, context.Player ) );

		particle.SetSlot( index, definition.Slots.Count, value );
	}
}

//Kinematic: the position is written and the engine neither moves nor collides the particle while this runs; the velocity is set to the move so a sprite or ribbon can face the way it goes. Weight under one eases towards the shape rather than snapping.
/// <summary>Moves the particle along one of the effect's shapes: where the shape is at <see cref="Along"/> is where the particle is, every step. A snake down a spline, beads climbing a helix, a ring that is walked rather than thrown.</summary>
[Title( "Follow Shape" ), Category( "Update" ), Icon( "route" )]
public sealed class FollowShapeModule : ParticleModule, IParticleModuleStep, IParticleModuleFrame
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Update;

	[Property, ParticleShapeName, Title( "Effect Shape" )]
	public string ShapeName { get; set; }

	/// <summary>How the shape is walked: by time, where <see cref="Along"/> says; or by index, each particle parked at its own one of the shape's places, with Along turning the whole formation.</summary>
	[Property]
	public ParticleShapeSample Sample { get; set; } = ParticleShapeSample.ByTime;

	/// <summary>Where on the shape, nought to one, read per particle - a curve over life walks it, a slot parks each at its own place.</summary>
	[Property]
	public ParticleFloatBinding Along { get; set; } = new ParticleFloat
	{
		Type = ParticleFloat.ValueType.Range,
		Evaluation = ParticleFloat.EvaluationType.Life,
		ConstantA = 0.0f,
		ConstantB = 1.0f,
	};

	/// <summary>How far along each particle starts, by its index - so a rate of them is a train, not a pile.</summary>
	[Property]
	public ParticleFloatBinding Stagger { get; set; } = new ParticleFloat
	{
		Type = ParticleFloat.ValueType.Range,
		Evaluation = ParticleFloat.EvaluationType.Index,
		ConstantA = 0.0f,
		ConstantB = 0.0f,
	};

	/// <summary>How far along the shape the particle moves a second, on top of Along - motion by the clock rather than by the life, so one long-lived particle can go round and round. One is the whole shape a second.</summary>
	[Property]
	public ParticleFloatBinding Speed { get; set; } = 0.0f;

	/// <summary>Whether past one wraps to the start, or stops at the end.</summary>
	[Property]
	public bool Loop { get; set; } = true;

	/// <summary>How much of the shape's pose is taken: one is on it, less is drawn towards it.</summary>
	[Property]
	public ParticleFloatBinding Weight { get; set; } = 1.0f;

	/// <summary>Whether the particle is turned to face the way the shape goes.</summary>
	[Property]
	public bool AlignToShape { get; set; }

	public override void Apply( ParticleModuleContext context ) { }

	//Shape lookup cached per definition revision in one immutable object, so threads stay consistent
	private sealed record ShapeAt( ParticleDefinition Of, int Revision, ParticleShape Shape );

	private ShapeAt resolvedShape;

	private ParticleShape ShapeOf( ParticleDefinition definition )
	{
		if ( definition is null ) return null;

		var have = resolvedShape;
		var revision = definition.Revision;

		if ( have is not null && ReferenceEquals( have.Of, definition ) && have.Revision == revision )
			return have.Shape;

		var made = new ShapeAt( definition, revision, definition.Shape( ShapeName ) );

		resolvedShape = made;

		return made.Shape;
	}

	public void Step( ParticleModuleContext context, float delta )
	{
		var particle = context.Particle;

		if ( particle is null || context.Frame is not Frame held || held.Shape is null ) return;

		var shape = held.Shape;

		var along = Along.Evaluate( particle, context.Player )
			+ Stagger.Evaluate( particle, context.Player )
			+ particle.Age * Speed.Evaluate( particle, context.Player );

		along = Loop ? along - MathF.Floor( along ) : along.Clamp( 0.0f, 1.0f );

		var byIndex = Sample == ParticleShapeSample.ByIndex;
		var places = held.Places;

		//By index, Along is a turn of the whole formation: the k-th place, moved on by a fraction of the way round.
		var pose = byIndex
			? shape.SampleIn( held.Measured, context.Player, ParticleShapeSample.ByTime, particle.Index, places, Turned( particle.Index, places, along ) )
			: shape.SampleIn( held.Measured, context.Player, ParticleShapeSample.ByTime, particle.Index, 1, along );
		var position = pose.Position;
		var normal = pose.Normal;

		var weight = Weight.Evaluate( particle, context.Player ).Clamp( 0.0f, 1.0f );
		var next = Vector3.Lerp( particle.Position, position, weight );

		//Kinematic: the particle is where the shape says, and its velocity is the move it made, for a ribbon or a sprite facing its motion.
		if ( delta > 0.0f ) particle.Velocity = ( next - particle.Position ) / delta;

		particle.Position = next;
		particle.Kinematic = true;

		if ( AlignToShape && !normal.IsNearlyZero() )
			particle.Angles = Rotation.LookAt( normal ).Angles();
	}

	//Per-effect, per-frame shared state: shape, its frame, and place count (was per particle)
	private sealed class Frame
	{
		internal ParticleShape Shape;
		internal Transform Measured;
		internal int Places;
	}

	public object Begin( ParticleModuleContext context, float delta, object was )
	{
		var held = was as Frame ?? new Frame();

		held.Shape = ShapeOf( context.Player?.Definition );
		held.Measured = held.Shape is null ? Transform.Zero : held.Shape.Frame( context.Player );
		held.Places = held.Shape is not null && Sample == ParticleShapeSample.ByIndex
			? held.Shape.Places( context.Emitter?.BurstCount ?? 1 )
			: 1;

		return held;
	}

	//The k-th of N places, as a fraction, moved on by along and wrapped where it loops.
	float Turned( int index, int places, float along )
	{
		var place = places <= 1 ? 0.0f : ( index % places ) / (float)( places - 1 );
		var t = place + along;

		return Loop ? t - MathF.Floor( t ) : t.Clamp( 0.0f, 1.0f );
	}
}
