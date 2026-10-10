using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor.MdlEditor;

/// <summary>A model's animation graph without an editor window: load, compile and write it back to the model</summary>
public sealed class Session
{
	public Data Graph { get; private set; }
	public bool HasRepairs { get; private set; }

	/// <summary>The model this graph is kept in. Null for one with nowhere to be kept - made in code, or started on a model with no definition of ours.</summary>
	public Asset Model { get; private set; }

	/// <summary>Whether there is somewhere to write this graph.</summary>
	public bool HasHome => Model is not null;

	/// <summary>Whether the model has a graph in it, rather than an empty one started on it here.</summary>
	public bool Carried => _definition?.Graph is not null;

	/// <summary>The stamp of the graph as the model on disk has it written: what the compiled graph beside the model says it was built from. Set by a write, or by whoever read the model's text - see GraphBuild.</summary>
	public string Source { get; set; }

	//The definition the graph is written into. Held from the moment the graph was opened: it is the one object every editor
	//of that model shares, and asking the asset system for it again on every write asks the model whether it needs compiling.
	private ModelDefinition _definition;

	//What the logs call this graph.
	private string Where => Model?.Path ?? "unsaved graph";

	public string Revision
	{
		get
		{
			Normalize();
			var bytes = Encoding.UTF8.GetBytes( Graph.Serialize() );
			return Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant();
		}
	}

	private Session( Data graph )
	{
		Graph = graph;
	}

	/// <summary>A graph kept nowhere, for code that builds or compiles one on its own.</summary>
	public static Session Create() => Create( new Data() );

	public static Session Create( Data graph )
	{
		ArgumentNullException.ThrowIfNull( graph );
		EnsureOutputNode( graph );
		var session = new Session( graph );
		session.Normalize();
		return session;
	}

	//A model that carries none gets an empty graph, written into the model once there is something in it. Whatever model the graph was written against, it is this model's now: the preview shows this model and the compile names its skeleton.
	/// <summary>The graph a model carries in its own definition.</summary>
	public static Session ForModel( Asset model )
	{
		ArgumentNullException.ThrowIfNull( model );

		var definition = ModelGraphResolver.DefinitionAt( model );

		Data graph = null;

		try
		{
			if ( definition?.Graph?.Document is { } document )
				graph = DeserializeGraph( document );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"AnimGraph: the graph of '{model.Path}' would not read, starting empty" );
			graph = null;
		}

		graph ??= new Data();
		EnsureOutputNode( graph );

		var target = graph.DefaultVariation;

		if ( !SameModel( target.TargetModelPath, model.Path ) )
			target.TargetModelPath = model.Path;

		//A model with no definition of ours - a compiled one from somewhere else - has
		//nowhere to keep a graph. It gets a graph on it all the same, which goes nowhere.
		var session = new Session( graph )
		{
			Model = definition is null ? null : model,
			_definition = definition,
		};

		session.Normalize();
		return session;
	}

	//Whether two paths name the same model, whichever of its names each uses.
	private static bool SameModel( string a, string b )
	{
		if ( string.IsNullOrWhiteSpace( a ) || string.IsNullOrWhiteSpace( b ) ) return false;

		return ModelGraphResolver.SamePath(
			System.IO.Path.ChangeExtension( a.Replace( '\\', '/' ), ".mdl" ),
			System.IO.Path.ChangeExtension( b.Replace( '\\', '/' ), ".mdl" ) );
	}

	public void ReplaceGraph( Data graph )
	{
		ArgumentNullException.ThrowIfNull( graph );
		EnsureOutputNode( graph );
		Graph = graph;
		Normalize();
	}

	public Compiler.Result Compile() => Compile( Graph?.SelectedVariationID ?? Variation.DefaultVariationID );

	public Compiler.Result Compile( string variationID )
	{
		Normalize();
		return Compiler.Compile( Graph, variationID );
	}

	/// <summary>Every variation of a graph, each compiled into its own definition. A variation is its own resource, so playing one is loading the resource it compiled into.</summary>
	public sealed record CompiledGraph( IReadOnlyDictionary<string, Compiler.Result> Variations )
	{
		/// <summary>The result for a variation, falling back to the default one when it doesn't exist.</summary>
		public Compiler.Result Get( string variationID )
		{
			return Variations.TryGetValue( variationID ?? Variation.DefaultVariationID, out var result )
				? result
				: Variations.GetValueOrDefault( Variation.DefaultVariationID );
		}

		/// <summary>Every error from every variation.</summary>
		public IEnumerable<Compiler.Warning> Errors => Variations.Values.SelectMany( result => result.Errors );

		/// <summary>True when any variation failed to compile.</summary>
		public bool Failed => Variations.Values.Any( result => result.Failed );
	}

	/// <summary>The last compile of every variation, from <see cref="CompileAll"/> or <see cref="Save"/>.</summary>
	public CompiledGraph LastCompile { get; private set; }

	/// <summary>Compile every variation into its own definition, tagged with the variation it came from.</summary>
	public CompiledGraph CompileAll()
	{
		Normalize();

		var results = new Dictionary<string, Compiler.Result>();

		foreach ( var variation in Graph.Variations.All )
		{
			//Each variation compiles on its own - nodes resolve their variation data as they go,
			//so the slot arrays and node definitions are the ones that variation actually needs
			var result = Compiler.Compile( Graph, variation.ID );

			var definition = JsonNode.Parse( result.Definition ).AsObject();
			definition["m_variationID"] = variation.ID;
			definition["m_model"] = variation.TargetModelPath;

			results[variation.ID] = result with { Definition = definition.ToJsonString() };
		}

		return LastCompile = new CompiledGraph( results );
	}

	/// <summary>Puts the graph into the model it is kept in, and writes the model - nothing else.</summary>
	public void Write()
	{
		if ( Model is null ) return;

		_definition ??= ModelGraphResolver.DefinitionAt( Model )
			?? throw new InvalidOperationException( $"'{Model.Path}' has no definition to keep a graph in" );

		var document = Graph.Serialize();

		_definition.Graph = new ModelDefinition.ModelGraph( document );

		if ( !Model.SaveToDiskUnbuilt( _definition ) )
			throw new InvalidOperationException( $"Could not write the graph into '{Model.Path}'" );

		//The definition writes the document as it is, so this is the stamp of what is now on the disk.
		Source = StampOf( document );
	}

	//A graph that will not compile leaves whatever was there before. The last one that worked is better than none while somebody is mid-edit.
	/// <summary>Builds the compiled graph beside the model, one file per variation.</summary>
	public void WriteCompiled() => WriteCompiled( CompileAll() );

	private void WriteCompiled( CompiledGraph compiled )
	{
		if ( Model?.GetSourceFile( true ) is not { Length: > 0 } model || compiled.Failed ) return;

		foreach ( var (variationID, result) in compiled.Variations )
		{
			if ( result.Definition is not { Length: > 2 } definition ) continue;

			var variation = variationID == Variation.DefaultVariationID ? null : variationID;

			if ( AnimGraph.ModelGraphFile( model, variation ) is not { } file ) continue;

			try
			{
				System.IO.File.WriteAllText( file, Stamped( definition, Source ) );

				//What was cached under this name is the version just replaced.
				AnimGraph.Forget( file );
			}
			catch ( System.IO.IOException )
			{
				//Somebody has it open, or the disk said no. The graph is in the model either way, and the next edit tries again.
			}
		}
	}

	/// <summary>Compiles the graph, writes it into the model, and writes the compiled graph beside the model.</summary>
	public Compiler.Result Save( string expectedRevision = null )
	{
		if ( Model is null )
			throw new InvalidOperationException( "This graph has no model to be kept in" );

		if ( !string.IsNullOrWhiteSpace( expectedRevision ) && RevisionInModel() is { } current
			&& !string.Equals( current, expectedRevision, StringComparison.OrdinalIgnoreCase ) )
		{
			throw new InvalidOperationException( $"The graph changed before it could be saved. Expected {expectedRevision}, current revision is {current}" );
		}

		var compiled = CompileAll();

		Write();
		WriteCompiled( compiled );

		HasRepairs = false;
		return compiled.Get( Graph?.SelectedVariationID );
	}

	//The revision of the graph the model holds now, or null where it holds none.
	private string RevisionInModel()
	{
		if ( ModelGraphResolver.DefinitionAt( Model )?.Graph?.Document is not { } document ) return null;

		return Create( DeserializeGraph( document ) ).Revision;
	}

	/// <summary>What a graph's text is known by - see <see cref="Source"/>.</summary>
	public static string StampOf( string document )
		=> string.IsNullOrEmpty( document ) ? null : document.FastHash64().ToString( "x16" );

	//Written first in the file, so asking costs the first few bytes of it rather than megabytes of a character's graph.
	/// <summary>The stamp a compiled graph says it was built from, or null where it says none or is not there.</summary>
	public static string StampIn( string compiled )
	{
		if ( string.IsNullOrWhiteSpace( compiled ) || !System.IO.File.Exists( compiled ) ) return null;

		try
		{
			var head = new byte[256];
			int read;

			using ( var stream = System.IO.File.OpenRead( compiled ) )
				read = stream.Read( head, 0, head.Length );

			var reader = new Utf8JsonReader( head.AsSpan( 0, read ), isFinalBlock: false, state: default );

			if ( !reader.Read() || reader.TokenType != JsonTokenType.StartObject ) return null;
			if ( !reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals( StampName ) ) return null;
			if ( !reader.Read() || reader.TokenType != JsonTokenType.String ) return null;

			return reader.GetString();
		}
		catch ( Exception e ) when ( e is System.IO.IOException or JsonException )
		{
			return null;
		}
	}

	private const string StampName = "m_source";

	//A compiled definition with the stamp of its source put first in it.
	private static string Stamped( string definition, string stamp )
	{
		if ( string.IsNullOrEmpty( stamp ) || !definition.StartsWith( '{' ) ) return definition;

		return $"{{\"{StampName}\":{JsonSerializer.Serialize( stamp )},{definition[1..]}";
	}

	public void Normalize()
	{
		var root = Graph.Root;
		var visited = new HashSet<Data>( ReferenceEqualityComparer.Instance );
		var identifiers = new HashSet<string>( StringComparer.Ordinal );
		var repaired = false;
		var migratedBlendSpaces = false;
		var culledDuplicateResults = false;

		void NormalizeGraph( Data graph )
		{
			if ( graph is null || !visited.Add( graph ) )
				return;

			graph.SetRoot( root );

			//Copy/paste mishaps could leave duplicate result nodes - keep the wired one
			var results = graph.Nodes.Where( node => node is Nodes.PoseResultNode or Nodes.VirtualParameterResultNode ).ToArray();
			if ( results.Length > 1 )
			{
				var keep = results.FirstOrDefault( node => node.Inputs.Any( input => input.ConnectedOutput is not null ) ) ?? results[0];

				foreach ( var stray in results.Where( node => node != keep ) )
					graph.RemoveNode( stray );

				culledDuplicateResults = true;
			}

			foreach ( var machine in graph.Nodes.OfType<Nodes.StateMachineNode>() )
				machine.SyncStates();

			foreach ( var selector in graph.Nodes.OfType<Nodes.SelectorNode>() )
				_ = selector.SecondaryGraph;

			foreach ( var blend in graph.Nodes.OfType<Nodes.Blend2DNode>().ToArray() )
				migratedBlendSpaces |= blend.MigrateLegacyPoints( graph );

			foreach ( var blend in graph.Nodes.OfType<Nodes.Blend1DNode>() )
				blend.InitializePointGraphs();

			var nodes = graph.Nodes.OfType<Node>().ToArray();
			if ( nodes.Any( node => identifiers.Contains( node.Identifier ) ) )
			{
				graph.RegenerateNodeIdentifiers();
				nodes = graph.Nodes.OfType<Node>().ToArray();
				repaired = true;
			}

			identifiers.UnionWith( nodes.Select( node => node.Identifier ) );

			foreach ( var node in nodes )
			{
				if ( node is Nodes.ParameterNode parameterNode )
				NormalizeParameterReference( root, parameterNode );

				foreach ( var child in node.SubGraphs )
					NormalizeGraph( child );
			}
		}

		NormalizeGraph( root );

		foreach ( var parameter in root.VirtualParameters )
			NormalizeGraph( parameter.Graph );

		if ( repaired || migratedBlendSpaces || culledDuplicateResults )
			HasRepairs = true;

		if ( repaired )
		{
			Log.Warning( $"AnimGraph: regenerated duplicate node identifiers in '{Where}'" );
		}

		if ( culledDuplicateResults )
		{
			Log.Warning( $"AnimGraph: removed duplicate result nodes in '{Where}'" );
		}

		if ( migratedBlendSpaces )
			Log.Info( $"AnimGraph: migrated Blend 2D points in '{Where}'" );
	}

	//The graph editor's document, read into a graph - as a model carries it, or under the __editor key of the oldest saves.
	internal static Data DeserializeGraph( string source )
	{
		using var document = JsonDocument.Parse( source, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, MaxDepth = 512 } );
		var root = document.RootElement;
		var editorSource = source;

		if ( !root.TryGetProperty( "nodes", out _ ) )
		{
			if ( !root.TryGetProperty( "__editor", out var legacyEditorDocument )
				|| !legacyEditorDocument.TryGetProperty( "nodes", out _ ) )
			{
				throw new InvalidOperationException( "AnimGraph source has no editor graph" );
			}

			editorSource = legacyEditorDocument.GetRawText();
		}

		var graph = new Data();
		graph.Deserialize( editorSource );
		return graph;
	}

	private static void NormalizeParameterReference( Data root, Nodes.ParameterNode node )
	{
		IParameter parameter = null;

		if ( !string.IsNullOrWhiteSpace( node.ParameterIdentifier ) )
			parameter = root.AllParameters.FirstOrDefault( candidate => candidate.Identifier == node.ParameterIdentifier );

		parameter ??= root.AllParameters.FirstOrDefault( candidate =>
			candidate.Type == node.ParameterType
			&& string.Equals( candidate.Name, node.ParameterName, StringComparison.OrdinalIgnoreCase ) );

		if ( parameter is null )
			return;

		node.ParameterIdentifier = parameter.Identifier;
		node.ParameterName = parameter.Name;
	}

	private static void EnsureOutputNode( Data graph )
	{
		if ( graph.IsSubGraph || graph.Kind != GraphKind.BlendTree )
			return;

		if ( graph.Nodes.OfType<Nodes.PoseResultNode>().Any() )
			return;

		graph.AddNode( new Nodes.PoseResultNode { Position = new Vector2( 400, 0 ) } );
	}
}
