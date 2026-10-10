using System.Text.Json.Serialization;

namespace Rinten;

/// <summary>A particle effect in our own format: emitters of modules, played by <see cref="ParticlePlayer"/>.</summary>
[AssetType( Name = "Particle", Extension = "fx", Category = "Effects", Icon = "auto_awesome", IconColor = "#c66ad0" )]
public sealed partial class ParticleDefinition : GameResource
{
	/// <summary>Every emitter in this effect, in the order they are listed.</summary>
	[Property, Hide]
	public List<Emitter> Emitters { get; set; } = new();

	//An emitter with a Emitter.Duration of its own ignores this.
	/// <summary>How long the effect runs for, in seconds, before it is finished.</summary>
	[Property]
	public float Duration { get; set; } = 5.0f;

	/// <summary>Whether the effect starts again when it reaches its duration.</summary>
	[Property]
	public bool Looping { get; set; } = true;

	//The three lists are hidden from a property sheet rather than shown as list editors: the particle editor has a dock for them, with a row a parameter,
	//the way an animation graph lists its own - see ParameterList there. A sheet of three nested lists was the same information three clicks deeper.

	/// <summary>Where poses come from: the geometry a module samples - born on, aimed at, orbited around. Reusable by name, and drawn in the editor.</summary>
	[Hide]
	public List<ParticleShape> Shapes { get; set; } = new();

	/// <summary>What a particle can remember, by name. A module writes one with Remember or Set Slot, and any binding reads it back as its Slot source.</summary>
	[Property, Title( "Slots" )]
	public List<string> Slots { get; set; } = new();

	/// <summary>Named poses the effect is built around - see <see cref="ParticleAnchor"/>. A scene binds each to an object; a shape can hang from one, a Goal aim at one.</summary>
	[Hide]
	public List<ParticleAnchor> Anchors { get; set; } = new();

	/// <summary>The anchor called this, or null.</summary>
	public ParticleAnchor Anchor( string name )
		=> !string.IsNullOrEmpty( name ) && Lookup().Anchors.TryGetValue( name, out var anchor ) ? anchor : null;

	/// <summary>The shape called this, or null.</summary>
	public ParticleShape Shape( string name )
		=> !string.IsNullOrEmpty( name ) && Lookup().Shapes.TryGetValue( name, out var shape ) ? shape : null;

	/// <summary>Which slot a name is, or -1.</summary>
	public int SlotIndex( string name )
		=> !string.IsNullOrEmpty( name ) && Lookup().Slots.TryGetValue( name, out var index ) ? index : -1;

	//The names, hashed. Every step of every particle asks for a shape, a slot, an anchor or a parameter by name, and a walk down six lists
	//comparing strings is not a thing to do a hundred thousand times a second. Built again whenever the definition says it changed - see Revision.
	Names Lookup()
	{
		if ( _names is not null && _names.Revision == Revision ) return _names;

		var names = new Names { Revision = Revision };

		foreach ( var shape in Shapes )
		{
			if ( !string.IsNullOrEmpty( shape.Name ) ) names.Shapes.TryAdd( shape.Name, shape );
		}

		foreach ( var anchor in Anchors )
		{
			if ( !string.IsNullOrEmpty( anchor.Name ) ) names.Anchors.TryAdd( anchor.Name, anchor );
		}

		for ( var i = 0; i < Slots.Count; i++ )
		{
			if ( !string.IsNullOrEmpty( Slots[i] ) ) names.Slots.TryAdd( Slots[i], i );
		}

		//The parameters too. A binding that reads one reads it once a particle a frame - see ParticleBindings.Evaluate - and it used to
		//be a walk down the list from a LINQ predicate, which is a closure allocated for every one of those reads as well as the walk.
		foreach ( var parameter in FloatParameters )
		{
			if ( !string.IsNullOrEmpty( parameter.Name ) ) names.Floats.TryAdd( parameter.Name, parameter );
		}

		foreach ( var parameter in VectorParameters )
		{
			if ( !string.IsNullOrEmpty( parameter.Name ) ) names.Vectors.TryAdd( parameter.Name, parameter );
		}

		foreach ( var parameter in ColorParameters )
		{
			if ( !string.IsNullOrEmpty( parameter.Name ) ) names.Colors.TryAdd( parameter.Name, parameter );
		}

		return _names = names;
	}

	Names _names;

	sealed class Names
	{
		public int Revision;
		public readonly Dictionary<string, ParticleShape> Shapes = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, ParticleAnchor> Anchors = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, int> Slots = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, FloatParameter> Floats = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, VectorParameter> Vectors = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, ColorParameter> Colors = new( StringComparer.OrdinalIgnoreCase );
	}

	/// <summary>Named floats a module can read instead of holding its own value.</summary>
	[Hide]
	public List<FloatParameter> FloatParameters { get; set; } = new();

	/// <summary>Named vectors a module can read instead of holding its own value.</summary>
	[Hide]
	public List<VectorParameter> VectorParameters { get; set; } = new();

	/// <summary>Named colours a module can read instead of holding its own value.</summary>
	[Hide]
	public List<ColorParameter> ColorParameters { get; set; } = new();

	//A counter rather than an event, because a player is only interested once a frame and there is no ordering to get wrong: a number that differs from the one it last built on is a rebuild, whoever moved it and whenever. Never serialized - it counts edits in this session, not versions on disk.
	/// <summary>Bumped whenever this definition has changed shape, so a player in a scene knows to build its emitters again.</summary>
	[Hide, JsonIgnore]
	public int Revision { get; private set; }

	/// <summary>Say that something in here was edited.</summary>
	public void NoteChanged() => Revision++;

	//A recompile from disk is an edit as far as anything playing it is concerned.
	protected override void PostReload() => NoteChanged();

	/// <summary>What a named float is worth where nobody has overridden it.</summary>
	public float FloatDefault( string name )
		=> !string.IsNullOrEmpty( name ) && Lookup().Floats.TryGetValue( name, out var found ) ? found.DefaultValue : 0.0f;

	/// <summary>What a named vector is worth where nobody has overridden it.</summary>
	public Vector3 VectorDefault( string name )
		=> !string.IsNullOrEmpty( name ) && Lookup().Vectors.TryGetValue( name, out var found ) ? found.DefaultValue : Vector3.Zero;

	/// <summary>What a named colour is worth where nobody has overridden it.</summary>
	public ParticleGradient ColorDefault( string name )
		=> !string.IsNullOrEmpty( name ) && Lookup().Colors.TryGetValue( name, out var found ) ? found.DefaultValue : Color.White;

	/// <summary>Whether a float of this name is declared at all - see ParticlePlayer.GetFloat.</summary>
	public bool HasFloat( string name )
		=> !string.IsNullOrEmpty( name ) && Lookup().Floats.ContainsKey( name );

	/// <summary>Whether a vector of this name is declared at all - see ParticlePlayer.GetVector.</summary>
	public bool HasVector( string name )
		=> !string.IsNullOrEmpty( name ) && Lookup().Vectors.ContainsKey( name );

	/// <summary>Whether a colour of this name is declared at all.</summary>
	public bool HasColor( string name )
		=> !string.IsNullOrEmpty( name ) && Lookup().Colors.ContainsKey( name );

	//Through JSON, which is the only deep copy that is guaranteed to agree with what the file will hold - the modules are polymorphic and a hand-written clone would have to know every one of them.
	/// <summary>A copy of this definition that shares nothing with it.</summary>
	public ParticleDefinition Duplicate()
	{
		var copy = new ParticleDefinition();

		copy.CopyFrom( this );

		return copy;
	}

	/// <summary>Something with a name, that a module can point at.</summary>
	public abstract class Parameter
	{
		/// <summary>What a module names to read this.</summary>
		[Property]
		public string Name { get; set; } = "Parameter";

		//Nothing reads it yet - a module follows a parameter by name, the way the inspector's overrides are keyed. It is written so that renaming can one day carry the modules with it without the file having to change shape first.
		/// <summary>What this parameter is, whatever it is called at the moment.</summary>
		[Hide]
		public string Identifier { get; set; } = Guid.NewGuid().ToString();
	}

	/// <summary>A named float.</summary>
	public sealed class FloatParameter : Parameter
	{
		[Property]
		public float DefaultValue { get; set; } = 1.0f;

		/// <summary>The least it is meant to be set to. With <see cref="Max"/> under it, there is no range.</summary>
		[Property]
		public float Min { get; set; } = 0.0f;

		/// <summary>The most it is meant to be set to - what a slider runs up to.</summary>
		[Property]
		public float Max { get; set; } = 2.0f;

		/// <summary>Whether the two ends make a range a slider can run between.</summary>
		[Hide, JsonIgnore]
		public bool HasRange => Min < Max;
	}

	/// <summary>A named vector.</summary>
	public sealed class VectorParameter : Parameter
	{
		[Property]
		public Vector3 DefaultValue { get; set; } = Vector3.One;
	}

	/// <summary>A named colour, which may be a gradient.</summary>
	public sealed class ColorParameter : Parameter
	{
		[Property]
		public ParticleGradient DefaultValue { get; set; } = Color.White;
	}
}
