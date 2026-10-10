using System.Text.Json.Serialization;

namespace Rinten;

partial class ParticleDefinition
{
	/// <summary>One emitter; four module lists, one per stage, each run in its authored order</summary>
	public sealed class Emitter
	{
		/// <summary>What this emitter is called in the editor.</summary>
		[Property]
		public string Name { get; set; } = "Emitter";

		/// <summary>This emitter, whatever it has been renamed to.</summary>
		[Hide]
		public string Identifier { get; set; } = Guid.NewGuid().ToString();

		/// <summary>A disabled emitter is not built at all.</summary>
		[Property]
		public bool Enabled { get; set; } = true;

		/// <summary>How many particles this emitter may have alive at once.</summary>
		[Property]
		public int MaxParticles { get; set; } = 1000;

		/// <summary>How many places a particle's index counts through before it wraps - what a curve read By Index spans, and what a shape sampled By Index is divided into. Nought is the burst count, or the shape's own.</summary>
		[Property, Range( 0, 1000 )]
		public int Places { get; set; } = 0;

		/// <summary>Where this emitter sits, from the player - so a muzzle flash is at the muzzle and the shells come out of the side.</summary>
		[Property, Group( "Transform" )]
		public Vector3 Offset { get; set; } = Vector3.Zero;

		[Property, Group( "Transform" )]
		public Angles Rotation { get; set; } = Angles.Zero;

		[Property, Group( "Transform" )]
		public Vector3 Scale { get; set; } = Vector3.One;

		/// <summary>Which anchor this emitter rides, where it rides one instead of the player.</summary>
		[Property, Group( "Transform" ), ParticleAnchorName]
		public string Anchor { get; set; }

		//Goes straight onto ParticleEffect.StartDelay, so the wait is the engine's own rather than something the runner has to gate.
		/// <summary>Seconds to wait before this emitter starts, so one emitter of an effect can begin after another.</summary>
		[Property]
		public float Delay { get; set; } = 0.0f;

		/// <summary>How long this emitter runs for, where it is not the effect's own <see cref="ParticleDefinition.Duration"/>. Zero is "the effect's".</summary>
		[Property]
		public float Duration { get; set; } = 0.0f;

		/// <summary>How many seconds of the effect to run through before it is first seen, so it starts already going rather than building up from nothing.</summary>
		[Property]
		public float PreWarm { get; set; } = 0.0f;

		/// <summary>How fast this emitter simulates, against everything else.</summary>
		[Property, Range( 0.0f, 4.0f )]
		public float TimeScale { get; set; } = 1.0f;

		/// <summary>Whether this emitter follows game time or wall-clock time.</summary>
		[Property]
		public ParticleEffect.TimingMode Timing { get; set; } = ParticleEffect.TimingMode.GameTime;

		/// <summary>Modules that decide when and how many particles appear.</summary>
		[JsonConverter( typeof( ParticleModuleListConverter ) ), Hide]
		public List<ParticleModule> SpawnModules { get; set; } = new();

		/// <summary>Modules that run once, on a particle that has just been born.</summary>
		[JsonConverter( typeof( ParticleModuleListConverter ) ), Hide]
		public List<ParticleModule> InitializeModules { get; set; } = new();

		/// <summary>Modules that run every frame, on every particle alive.</summary>
		[JsonConverter( typeof( ParticleModuleListConverter ) ), Hide]
		public List<ParticleModule> UpdateModules { get; set; } = new();

		/// <summary>Modules that decide what a particle is drawn as.</summary>
		[JsonConverter( typeof( ParticleModuleListConverter ) ), Hide]
		public List<ParticleModule> RenderModules { get; set; } = new();

		/// <summary>The list a stage's modules are kept in, so a caller can hold one.</summary>
		public List<ParticleModule> Modules( ParticleModuleStage stage ) => stage switch
		{
			ParticleModuleStage.Spawn => SpawnModules,
			ParticleModuleStage.Initialize => InitializeModules,
			ParticleModuleStage.Update => UpdateModules,
			ParticleModuleStage.Render => RenderModules,
			_ => null
		};

		/// <summary>Every module of this emitter, in stage order.</summary>
		public IEnumerable<ParticleModule> AllModules()
		{
			foreach ( var module in SpawnModules ) yield return module;
			foreach ( var module in InitializeModules ) yield return module;
			foreach ( var module in UpdateModules ) yield return module;
			foreach ( var module in RenderModules ) yield return module;
		}

		/// <summary>Put a module where its own stage says it goes.</summary>
		public void Add( ParticleModule module )
		{
			if ( module is null ) return;

			Modules( module.Stage )?.Add( module );
		}

		/// <summary>Take a module out of whichever stage is holding it.</summary>
		public bool Remove( ParticleModule module )
		{
			if ( module is null ) return false;

			return SpawnModules.Remove( module )
				|| InitializeModules.Remove( module )
				|| UpdateModules.Remove( module )
				|| RenderModules.Remove( module );
		}
	}
}
