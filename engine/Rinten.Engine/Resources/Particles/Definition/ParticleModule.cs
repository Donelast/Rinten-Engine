using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rinten;

/// <summary>When a module gets its say.</summary>
[Expose]
public enum ParticleModuleStage
{
	/// <summary>Decides when and how many particles appear.</summary>
	Spawn,

	/// <summary>Runs once, on a particle that has just been born.</summary>
	Initialize,

	/// <summary>Runs every frame, on every particle alive.</summary>
	Update,

	/// <summary>Decides what a particle is drawn as.</summary>
	Render
}

/// <summary>What a module gets when run; one instance per call, with Particle rewritten per particle</summary>
public sealed class ParticleModuleContext
{
	/// <summary>The particle this is about, where there is one.</summary>
	public Particle Particle { get; set; }

	/// <summary>The effect the particle belongs to.</summary>
	public ParticleEffect Effect { get; set; }

	/// <summary>The emitter component that is making particles for it.</summary>
	public ParticleEmitter Emitter { get; set; }

	/// <summary>The player, which is what a named parameter is read off.</summary>
	public ParticlePlayer Player { get; set; }

	/// <summary>The runner stepping this emitter's modules - what holds the emitter's data and its relay.</summary>
	public ParticleModuleRunner Runner { get; set; }

	/// <summary>The emitter's definition: the lists this module is one of.</summary>
	public ParticleDefinition.Emitter Data => Runner?.Data;

	/// <summary>What the module worked out for the whole effect this frame - see <see cref="IParticleModuleFrame"/>. Null for a module that has none.</summary>
	public object Frame { get; internal set; }

	/// <summary>The numbers a module draws: the runner's own, seeded from the player, so a seek plays the same again - or the shared one where there is no runner, as in the editor's sheets.</summary>
	public Random Random => Runner?.Random ?? Random.Shared;
}

//Most modules just configure the effect in Apply; [Expose] so saved modules resolve by name
/// <summary>One step of an effect's recipe - a thing that is done to particles, with the numbers it needs written on it.</summary>
[Expose]
public abstract class ParticleModule
{
	/// <summary>This module, whatever it has been renamed to.</summary>
	[Hide]
	public string Identifier { get; set; } = Guid.NewGuid().ToString();

	/// <summary>What this module is called in the editor's list.</summary>
	[Hide]
	public string Name { get; set; }

	/// <summary>A disabled module is built and kept, but never runs.</summary>
	[Hide]
	public bool Enabled { get; set; } = true;

	/// <summary>Which of the four moments this module belongs to.</summary>
	[JsonIgnore, Hide]
	public abstract ParticleModuleStage Stage { get; }

	/// <summary>Write this module's numbers onto the effect and its emitter.</summary>
	public abstract void Apply( ParticleModuleContext context );
}

/// <summary>A module that needs a component of its own beside the effect.</summary>
public interface IParticleModuleComponents
{
	/// <summary>Add what this module needs to the emitter's object.</summary>
	void CreateComponents( GameObject target );
}

/// <summary>A module that has to touch every particle, every frame.</summary>
public interface IParticleModuleStep
{
	/// <summary>Move one particle on by <paramref name="delta"/> seconds - the frame, scaled by the effect's and the particle's own time scale.</summary>
	void Step( ParticleModuleContext context, float delta );
}

/// <summary>Per-effect, per-frame work done once before particles step; state is owned by the runner</summary>
public interface IParticleModuleFrame
{
	/// <summary>Works out what the whole effect shares this frame and returns it.</summary>
	object Begin( ParticleModuleContext context, float delta, object was );
}

/// <summary>A module of a kind this build does not have, kept exactly as it was read.</summary>
public sealed class UnknownParticleModule : ParticleModule
{
	/// <summary>The type name that could not be found.</summary>
	[Hide]
	public string TypeName { get; set; }

	/// <summary>The object as it was read, to be written back as it was.</summary>
	[Hide, JsonIgnore]
	public JsonObject Raw { get; set; }

	/// <summary>Which stage's list it was found in, so it is written back into the same one.</summary>
	[Hide, JsonIgnore]
	public ParticleModuleStage StoredStage { get; set; }

	public override ParticleModuleStage Stage => StoredStage;

	public override void Apply( ParticleModuleContext context ) { }
}

/// <summary>Reads and writes a stage's module list, which is a list of things of different types.</summary>
public sealed class ParticleModuleListConverter : JsonConverter<List<ParticleModule>>
{
	const string TypeKey = "__type";

	public override List<ParticleModule> Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		var list = new List<ParticleModule>();

		if ( reader.TokenType != JsonTokenType.StartArray )
			return list;

		while ( reader.Read() && reader.TokenType != JsonTokenType.EndArray )
		{
			if ( JsonNode.Parse( JsonDocument.ParseValue( ref reader ).RootElement.GetRawText() ) is not JsonObject node )
				continue;

			if ( ReadModule( node, options ) is { } module )
				list.Add( module );
		}

		return list;
	}

	static ParticleModule ReadModule( JsonObject node, JsonSerializerOptions options )
	{
		var typeName = (string)node[TypeKey];

		if ( string.IsNullOrEmpty( typeName ) )
			return null;

		var type = Game.TypeLibrary?.GetType( typeName )?.TargetType;

		if ( type is null || !typeof( ParticleModule ).IsAssignableFrom( type ) || type.IsAbstract )
		{
			Log.Warning( $"Particle: no module type called '{typeName}' - it is kept as it is and not edited" );

			return new UnknownParticleModule { TypeName = typeName, Raw = node, Name = typeName };
		}

		try
		{
			return (ParticleModule)JsonSerializer.Deserialize( node.ToJsonString(), type, options );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Particle: could not read a '{typeName}'" );

			return null;
		}
	}

	public override void Write( Utf8JsonWriter writer, List<ParticleModule> value, JsonSerializerOptions options )
	{
		writer.WriteStartArray();

		foreach ( var module in value )
		{
			if ( module is null ) continue;

			if ( module is UnknownParticleModule unknown )
			{
				(unknown.Raw ?? new JsonObject()).WriteTo( writer, options );
				continue;
			}

			writer.WriteStartObject();
			writer.WriteString( TypeKey, module.GetType().FullName );

			//Written into the same object as the tag rather than nested under a value, so a
			//module reads as its own properties and a file stays something a person can follow.
			using ( var document = JsonDocument.Parse( JsonSerializer.Serialize( module, module.GetType(), options ) ) )
			{
				foreach ( var property in document.RootElement.EnumerateObject() )
				{
					if ( property.NameEquals( TypeKey ) ) continue;

					property.WriteTo( writer );
				}
			}

			writer.WriteEndObject();
		}

		writer.WriteEndArray();
	}
}
