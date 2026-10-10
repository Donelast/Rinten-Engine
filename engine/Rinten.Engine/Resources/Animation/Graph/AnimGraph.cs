using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rinten;

//Shared description; run state is per character. Named by its model, not a file of its own
/// <summary>A compiled animation graph: what poses a model, and what decides between those poses.</summary>
public sealed class AnimGraph : Resource
{
	/// <summary>Somewhere to build a graph. A fresh one each time: a builder is used once and turned into a definition.</summary>
	public static AnimGraphBuilder Builder => new();

	private AnimGraph( Definition definition, string name )
	{
		Graph = definition;
		Name = name ?? "";
	}

	/// <summary>An empty one, for whatever needs to hold a graph before there is a graph to hold.</summary>
	public AnimGraph()
	{
		Graph = null;
		Name = "";
	}

	//What was compiled, ready to be read by whatever runs it.
	internal Definition Graph { get; }

	public string Name { get; }

	public override bool IsValid => Graph is not null;

	public override bool IsError => Graph is null;

	/// <summary>The skeleton the graph's clips were compiled against.</summary>
	public string ModelName => Graph?.Skeleton ?? "";

	/// <summary>Which variation of the graph this is, for graphs that come in several - one rig's version of the same movement.</summary>
	public string VariationId { get; internal set; } = "";

	/// <summary>The parameters a game can set by name. Only the declared ones: a graph makes parameters of its own where a node asks for one nobody declared, and those are its business rather than the game's.</summary>
	public IReadOnlyList<GraphParameter> Parameters => Graph?.Described ?? Array.Empty<GraphParameter>();

	/// <summary>One by name, or null.</summary>
	public GraphParameter Parameter( string name )
	{
		if ( Graph is null || string.IsNullOrEmpty( name ) ) return null;

		return Graph.ByName.GetValueOrDefault( name );
	}

	/// <summary>How many parameters a game can set.</summary>
	public int ParamCount => Parameters.Count;

	/// <summary>The name of one by position, or null past the end.</summary>
	public string GetParameterName( int index )
		=> index >= 0 && index < Parameters.Count ? Parameters[index].Name : null;

	/// <summary>The kind of value a parameter holds, as the type whoever sets it uses.</summary>
	public Type GetParameterType( string name ) => TypeOf( Parameter( name ) );

	/// <summary>The same by position.</summary>
	public Type GetParameterType( int index )
		=> TypeOf( index >= 0 && index < Parameters.Count ? Parameters[index] : null );

	private static Type TypeOf( GraphParameter parameter ) => parameter?.Kind switch
	{
		AnimGraphBuilder.NodeKind.Bool => typeof( bool ),
		AnimGraphBuilder.NodeKind.Float => typeof( float ),
		AnimGraphBuilder.NodeKind.Id => typeof( byte ),
		AnimGraphBuilder.NodeKind.Vector => typeof( Vector3 ),
		_ => null,
	};

	/// <summary>A graph from a definition a builder just produced, rather than from a file - for code that puts one together as it runs.</summary>
	public static AnimGraph FromDefinition( string definition, string name = null )
	{
		var parsed = Definition.Parse( definition );

		return parsed is null ? null : new AnimGraph( parsed, name ?? "procedural" );
	}

	/// <summary>The same, named - what the editor calls to play what it has just compiled.</summary>
	public static AnimGraph Create( string name, string definition )
		=> FromDefinition( definition, name );

	//There is nothing unmanaged behind a graph - the definition is managed and the poses belong to whatever is playing it. This is here because a tool that makes a graph a keystroke says so, and something that says so should not have to know that it did not need to.
	/// <summary>Let go of a graph made in code.</summary>
	public void Dispose() { }

	/// <summary>Load a model's compiled graph.</summary>
	public static AnimGraph Load( string path )
	{
		//The name a graph goes by, however it was spelled on the way in: the model, the graph and its compiled file are the same graph.
		//Used to remember it by and to name it, never to find it - this is lowercased, and a file on a case-sensitive disk is not.
		if ( ModelGraphName( path ) is not { } given ) return null;

		var wanted = FixPath( given );

		lock ( loaded )
		{
			if ( loaded.TryGetValue( wanted, out var cached ) ) return cached;
		}

		var full = CoreModel.ResolveOnDisk( ModelGraphFile( given ) );

		if ( full is null ) return null;

		AnimGraph graph;

		try
		{
			graph = FromDefinition( CompiledFile.TextOf( full ), wanted );
		}
		catch ( IOException )
		{
			return null;
		}

		if ( graph is null ) return null;

		//Where it came from, so it can be written back down. A resource with no path serializes to nothing, and a graph
		//dropped into a renderer that serializes to nothing is one that is gone by the next time the scene is opened.
		graph.RegisterWeakResourceId( wanted );

		lock ( loaded )
		{
			//Somebody may have loaded the same graph while this one was being read. One graph per path, so
			//that two renderers naming the same one hold the same object and comparing them means something.
			if ( loaded.TryGetValue( wanted, out var raced ) ) return raced;

			loaded[wanted] = graph;
		}

		return graph;
	}

	//One graph per path. A graph is read-only once compiled, so there is nothing to be gained by parsing the same file for every renderer
	//that names it - and a property that answers a different object each time is a property that looks changed every time it is compared.
	private static readonly Dictionary<string, AnimGraph> loaded = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Graph name for a model (thing.mdl_ag or thing+run.mdl_ag); accepts any of the model's names</summary>
	public static string ModelGraphName( string model, string variation = null )
	{
		if ( string.IsNullOrWhiteSpace( model ) ) return null;

		var path = model.Replace( '\\', '/' );
		var extension = Path.GetExtension( path );

		if ( !ModelExtensions.Contains( extension ) ) return null;

		var stem = path[..^extension.Length];

		return string.IsNullOrWhiteSpace( variation ) ? $"{stem}.mdl_ag" : $"{stem}+{variation}.mdl_ag";
	}

	//An extension no asset type claims, so the browser does not list it: it is the model's, the way its clips are.
	/// <summary>Where a model's graph is compiled to: beside the model, as <c>models/thing.mdl_ag_c</c> - the flat file the runtime reads.</summary>
	public static string ModelGraphFile( string model, string variation = null )
		=> ModelGraphName( model, variation ) is { } name ? name + "_c" : null;

	//Every name a model, or the graph it carries, goes by.
	private static readonly HashSet<string> ModelExtensions = new( StringComparer.OrdinalIgnoreCase )
	{
		".mdl", ".mdl_c", ".vmdl", ".vmdl_c", ".mdl_ag", ".mdl_ag_c",
	};

	//Counts forgotten graphs, so holders can tell a graph was recompiled and reload it
	internal static int Generation => System.Threading.Volatile.Read( ref generation );

	private static int generation;

	/// <summary>Drops a recompiled graph from the cache so the next load reads disk; matched by path suffix</summary>
	public static void Forget( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return;

		var name = FixPath( ModelGraphName( path ) ?? path.Replace( '\\', '/' ) );

		lock ( loaded )
		{
			foreach ( var key in new List<string>( loaded.Keys ) )
			{
				//On a folder boundary, so that the graph of "citizen.mdl" is not forgotten because somebody saved the one of "not_citizen.mdl".
				var same = name.Equals( key, StringComparison.OrdinalIgnoreCase )
					|| name.EndsWith( $"/{key}", StringComparison.OrdinalIgnoreCase )
					|| key.EndsWith( $"/{name}", StringComparison.OrdinalIgnoreCase );

				if ( same ) loaded.Remove( key );
			}
		}

		System.Threading.Interlocked.Increment( ref generation );
	}

	//Named for what it is rather than nested as Parameter , because the graph already has a method by that name and the two would collide.
	/// <summary>What a graph is driven by.</summary>
	public sealed class GraphParameter
	{
		public string Name { get; init; } = "";

		/// <summary>What kind of value it holds.</summary>
		public AnimGraphBuilder.NodeKind Kind { get; init; }

		public float Min { get; init; }
		public float Max { get; init; }

		/// <summary>Where it starts, and where it goes back to. For an id, the position in <see cref="Options"/> of the name it starts as.</summary>
		public float Default { get; init; }

		/// <summary>Whether it returns to its default once the graph has read it - a thing that happened rather than a thing that is so.</summary>
		public bool AutoReset { get; init; }

		/// <summary>The names an id parameter is allowed to take, if it says.</summary>
		public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();

		/// <summary>Whether the graph offers this to whoever plays it. An implicit parameter - one a node asked for and nobody declared - is not.</summary>
		public bool Described { get; init; }

		public override string ToString() => $"{Kind} {Name}";
	}

	//A compiled graph as it is read back: a flat list of nodes referring to each other by index, the parameters, and where the final pose comes from.
	internal sealed class Definition
	{
		public int Version { get; private init; }
		public string Skeleton { get; private init; } = "";

		/// <summary>Which node produces the pose, or -1 if the graph is empty.</summary>
		public int Root { get; private init; } = -1;

		public IReadOnlyList<JsonObject> Nodes { get; private init; } = Array.Empty<JsonObject>();

		public IReadOnlyList<GraphParameter> All { get; private init; } = Array.Empty<GraphParameter>();

		/// <summary>The ones a game is meant to set.</summary>
		public IReadOnlyList<GraphParameter> Described { get; private init; } = Array.Empty<GraphParameter>();

		public IReadOnlyDictionary<string, GraphParameter> ByName { get; private init; }

		/// <summary>Names bound to nodes the graph works out for itself, so a node can read one as though it were set from outside.</summary>
		public IReadOnlyDictionary<string, int> Virtual { get; private init; }

		private static readonly Logger log = new( "Animation" );

		public static Definition Parse( string json )
		{
			if ( string.IsNullOrWhiteSpace( json ) ) return null;

			try
			{
				if ( JsonNode.Parse( json ) is not JsonObject root ) return null;

				var nodes = new List<JsonObject>();

				if ( root["Nodes"] is JsonArray list )
				{
					foreach ( var node in list )
					{
						if ( node is JsonObject entry ) nodes.Add( entry );
					}
				}

				var parameters = new List<GraphParameter>();

				if ( root["Parameters"] is JsonArray declared )
				{
					foreach ( var entry in declared )
					{
						if ( entry is not JsonObject parameter ) continue;

						var options = new List<string>();

						if ( parameter["Options"] is JsonArray names )
						{
							foreach ( var name in names ) options.Add( name?.GetValue<string>() ?? "" );
						}

						parameters.Add( new GraphParameter
						{
							Name = parameter["Name"]?.GetValue<string>() ?? "",
							Kind = Enum.TryParse<AnimGraphBuilder.NodeKind>( parameter["Kind"]?.GetValue<string>(), out var kind )
								? kind
								: AnimGraphBuilder.NodeKind.Float,
							Min = Number( parameter["Min"] ),
							Max = Number( parameter["Max"] ),
							Default = Number( parameter["Default"] ),
							AutoReset = parameter["AutoReset"]?.GetValue<bool>() ?? false,
							Options = options,
							Described = parameter["Describe"]?.GetValue<bool>() ?? false,
						} );
					}
				}

				//Editor source graph, not compiled: warn, since it would otherwise load silently with no output
				if ( root["Root"] is null && root["nodes"] is not null )
				{
					log.Warning( "this is the editor's source graph rather than a compiled one - open it in the model editor and save it" );
					return null;
				}

				var bound = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );

				if ( root["Virtual"] is JsonObject virtuals )
				{
					foreach ( var (name, index) in virtuals )
					{
						bound[name] = (int)Number( index );
					}
				}

				//The last declaration of a name wins, which is also what the compiler that wrote this warned about.
				var byName = new Dictionary<string, GraphParameter>( StringComparer.OrdinalIgnoreCase );
				foreach ( var parameter in parameters ) byName[parameter.Name] = parameter;

				return new Definition
				{
					Version = (int)Number( root["Version"] ),
					Skeleton = root["Skeleton"]?.GetValue<string>() ?? "",
					Root = (int)Number( root["Root"], -1.0f ),
					Nodes = nodes,
					All = parameters,
					Described = parameters.Where( x => x.Described ).ToList(),
					ByName = byName,
					Virtual = bound,
				};
			}
			catch ( JsonException )
			{
				return null;
			}
		}

		private static float Number( JsonNode node, float fallback = 0.0f )
		{
			if ( node is null ) return fallback;

			return node.GetValueKind() == JsonValueKind.Number ? node.GetValue<float>() : fallback;
		}
	}
}
