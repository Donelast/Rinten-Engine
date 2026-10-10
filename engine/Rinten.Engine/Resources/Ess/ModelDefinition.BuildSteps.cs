using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rinten;

partial class ModelDefinition
{
	//Custom compile steps from BuildStep; run after mesh import and morphs, before the model transform
	/// <summary>Steps a project adds to this model's compile, in order.</summary>
	[Property, Group( "Build" )]
	public List<BuildStep> Steps { get; set; } = new();

	/// <summary>One step of a model's compile, written by whoever needs it.</summary>
	[JsonConverter( typeof( BuildStepConverter ) )]
	public abstract class BuildStep
	{
		/// <summary>What this one is called, for the log and for somebody reading the file.</summary>
		[Property]
		public string Name { get; set; } = "";

		/// <summary>A step turned off is skipped, so one can be silenced to see what it was doing.</summary>
		[Property]
		public bool Enabled { get; set; } = true;

		/// <summary>Do the work. Anything thrown here fails this step and no other.</summary>
		public abstract void Run( ModelBuild build );

		/// <summary>Separately selectable pieces (points on a path, etc.), each with its own row and handle</summary>
		[JsonIgnore]
		public virtual IReadOnlyList<BuildStepPart> Parts => Array.Empty<BuildStepPart>();

		//A step with twenty points should not draw all twenty the same. This is the only thing about being drawn that a step cannot ask the gizmo itself, so it is the only thing handed to it.
		/// <summary>Which of this step's <see cref="Parts"/> is picked, or -1 for the step itself. Set by whatever is drawing it.</summary>
		[Hide, JsonIgnore]
		public int SelectedPart { get; set; } = -1;

		/// <summary>Draw this step where it will be.</summary>
		public virtual void DrawGizmos() { }

		//This rather than handles of the step's own: the editor shows one handle, the one its toolbar is set to, and everything about how a drag behaves lives there. part is -1 for the step itself.
		/// <summary>Where the step, or the part of it that is picked, can be dragged from.</summary>
		public virtual BuildStepPlacement Placement( int part ) => null;

		//A step that is a list of things has to be added to from somewhere, and the sheet cannot do it: a list of objects has no control of its own to edit with. So the outliner offers it, the same way it offers adding anything else.
		/// <summary>Whether another part can be appended.</summary>
		[JsonIgnore]
		public virtual bool CanAddPart => false;

		/// <summary>Append one, and say which row it became.</summary>
		public virtual int AddPart() => -1;

		/// <summary>Take one away. False for an index it will not remove.</summary>
		public virtual bool RemovePart( int index ) => false;
	}

	/// <summary>A step whose type is not loaded, kept as the text it arrived as.</summary>
	public sealed class UnknownStep : BuildStep
	{
		/// <summary>The type name the file asked for.</summary>
		public string TypeName { get; set; } = "";

		/// <summary>Everything the file said about it.</summary>
		public string Json { get; set; } = "{}";

		public override void Run( ModelBuild build )
		{
			build?.Warning( $"step '{TypeName}' is not a type this build knows - left alone." );
		}
	}
}

/// <summary>A selectable piece of a build step, e.g. one point of a path</summary>
public sealed class BuildStepPart
{
	/// <summary>What the row is called.</summary>
	public string Title { get; init; } = "";

	/// <summary>Its icon, or nothing for the step's own.</summary>
	public string Icon { get; init; } = "";

	/// <summary>What a property sheet edits when this row is picked. Usually the element itself, so its <c>[Property]</c> fields are the whole inspector.</summary>
	public object Target { get; init; }
}

//Polymorphic BuildStep JSON by runtime type; must sit on the type, property converters are ignored
internal sealed class BuildStepConverter : JsonConverter<ModelDefinition.BuildStep>
{
	private const string Discriminator = "__type";

	/// <summary>Only the base. A derived step is written by the ordinary serialiser.</summary>
	public override bool CanConvert( Type type ) => type == typeof( ModelDefinition.BuildStep );

	public override ModelDefinition.BuildStep Read( ref Utf8JsonReader reader,
		Type typeToConvert, JsonSerializerOptions options )
	{
		if ( reader.TokenType == JsonTokenType.Null ) return null;

		using var document = JsonDocument.ParseValue( ref reader );

		var root = document.RootElement;

		if ( root.ValueKind != JsonValueKind.Object ) return null;

		var raw = root.GetRawText();

		var named = root.TryGetProperty( Discriminator, out var found )
			? found.GetString()
			: null;

		if ( string.IsNullOrWhiteSpace( named ) || Resolve( named ) is not { } target )
			return Unknown( named, raw );

		try
		{
			if ( JsonSerializer.Deserialize( raw, target, options ) is ModelDefinition.BuildStep step )
				return step;
		}
		catch ( Exception )
		{
			//A step whose fields no longer match what the file says is the same problem as one whose type
			//is missing, and has the same answer: keep the text, so nothing is lost by opening the file.
		}

		return Unknown( named, raw );
	}

	public override void Write( Utf8JsonWriter writer,
		ModelDefinition.BuildStep value, JsonSerializerOptions options )
	{
		if ( value is null )
		{
			writer.WriteNullValue();
			return;
		}

		if ( value is ModelDefinition.UnknownStep unknown )
		{
			writer.WriteRawValue( string.IsNullOrWhiteSpace( unknown.Json ) ? "{}" : unknown.Json );
			return;
		}

		var type = value.GetType();

		using var document = JsonDocument.Parse( JsonSerializer.Serialize( value, type, options ) );

		writer.WriteStartObject();
		writer.WriteString( Discriminator, type.Name );

		foreach ( var property in document.RootElement.EnumerateObject() )
		{
			if ( property.NameEquals( Discriminator ) ) continue;

			property.WriteTo( writer );
		}

		writer.WriteEndObject();
	}

	private static ModelDefinition.UnknownStep Unknown( string named, string raw )
	{
		var name = string.IsNullOrWhiteSpace( named ) ? "unnamed" : named;

		return new ModelDefinition.UnknownStep { Name = name, TypeName = name, Json = raw };
	}

	//The step type of that name, or nothing if none is loaded.
	private static Type Resolve( string name )
	{
		try
		{
			return Game.TypeLibrary?.GetType<ModelDefinition.BuildStep>( name )?.TargetType;
		}
		catch ( Exception )
		{
			//Asked for during a static constructor, or before there is a type library at all. Unknown is the honest answer and it keeps the text.
			return null;
		}
	}
}
