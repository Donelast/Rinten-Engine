using System.Collections.Generic;
using System.IO;
using System.Linq;
using Node = Rinten.AnimGraphBuilder.Node;

namespace Rinten.Porting.SBox;

//Translates their anim graph into ours, reading the fuller of the editor and compiled files
//Nodes without a counterpart are passed through and reported by name
internal sealed class SBoxAnimGraph
{
	private static readonly Logger log = new( "Porter" );

	//What their files write where a node id would go and there is none.
	private const long None = 4294967295;

	//Nothing is written: a graph is kept in the model it animates, so the document goes into that model's definition - see SBoxPorter.PortModel. There are no graph files on this side.
	/// <summary>Translate a model's graph into the graph editor's document, or null where there is none to translate.</summary>
	public static string Translate( string file, string relative, string skeleton, PortReport report,
		out string definition, Func<string, byte[]> find = null )
	{
		definition = null;

		SBoxAnimGraph graph = null;
		Node root = default;
		var most = -1;

		//Keep the candidate file with the most nodes; editor saves miss nested groups
		foreach ( var candidate in Candidates( file ) )
		{
			if ( Load( candidate ) is not { } data ) continue;
			if ( !IsGraph( data ) ) continue;

			var next = new SBoxAnimGraph( data, skeleton, candidate );
			next.ReadMasks( candidate, find, relative, report );
			var pose = next.Translate();
			if ( pose is null ) continue;

			var carried = next.clips + next.states + next.built.Count;
			if ( carried <= most ) continue;

			most = carried;
			graph = next;
			root = pose;
		}

		if ( graph is null || root is not { IsValid: true } )
		{
			report.Skipped.Add( $"{relative}: is not a graph this reads" );
			return null;
		}

		//What the graph comes to as ours, straight out of the translation. Not what the engine plays - that is
		//the document, compiled by the model editor when the project is opened - but the fallback source of one.
		definition = graph.builder.Build( root );

		//Written out of their own file, so the page keeps the positions their editor drew and the clips stay on it. Failing that, the definition
		//read back into a document - a page laid out here rather than the one its author drew, which is still a graph somebody can open.
		var document = SBoxAnimGraphDocument.Write( graph, graph.builder.Skeleton, graph.masks,
			out var missed, out var drawn, out var reached ) ?? SBoxAnimGraphEditor.FromRuntime( definition );

		graph.drawn = drawn;
		graph.reached = reached;


		foreach ( var (name, count) in missed )
			graph.passed[name] = graph.passed.GetValueOrDefault( name ) + count;

		graph.Say( relative, report );

		return document;
	}

	static bool IsGraph( CoreImportedKeyValues3 data )
	{
		var name = data.String( "_class" ) ?? "";
		return name is "CAnimationGraph" or "CAnimGraphDoc_Graph" or "CAnimGraph";
	}

	//The graph as its author left it. The compiled copy beside it used to be offered as well - see PortPaths.IsCompiled - and is not needed:
	//their editor save carries the whole graph once the groups in it are followed and the node their game plays into is passed through.
	static IEnumerable<string> Candidates( string file )
	{
		if ( string.IsNullOrWhiteSpace( file ) ) yield break;

		var editor = PortPaths.IsCompiled( file ) ? file[..^2] : file;

		if ( File.Exists( editor ) ) yield return editor;
	}

	static CoreImportedKeyValues3 Load( string file )
	{
		try
		{
			//The text their editor writes, and only that. A document read out of the DATA block of the file
			//beside it is the same document after their compiler has been over it - see PortPaths.IsCompiled.
			return CoreImportedKeyValues3.ReadText( File.ReadAllText( file ) );
		}
		catch ( IOException )
		{
			return null;
		}
	}

	/// <summary>Graph a compiled model references, found by scanning its null-terminated ASCII strings</summary>
	public static string NamedBy( string modelFile )
	{
		try
		{
			if ( !File.Exists( modelFile ) || new FileInfo( modelFile ).Length > 256 * 1024 * 1024 ) return null;

			var text = System.Text.Encoding.ASCII.GetString( File.ReadAllBytes( modelFile ) );

			var found = System.Text.RegularExpressions.Regex.Match( text,
				"[A-Za-z0-9_\\-./]+\\.vanmgrph" );

			return found.Success ? found.Value.Replace( '\\', '/' ) : null;
		}
		catch ( IOException )
		{
			return null;
		}
	}

	//-- what was read --------------------------------------------------------

	private readonly AnimGraphBuilder builder = AnimGraph.Builder;

	//Node maps per manager, since node ids are only unique within one manager
	private readonly List<Dictionary<long, CoreImportedKeyValues3>> scopes = new();

	//Which manager each node was drawn in. By the node itself rather than by its id, which
	//is the one thing about a node of theirs that does not tell it apart from another.
	private readonly Dictionary<CoreImportedKeyValues3, int> livesIn = new();

	//The manager a group holds inside itself.
	private readonly Dictionary<CoreImportedKeyValues3, int> holds = new();

	//Their nodes with the manager each was drawn in, for whatever writes the graph out - so there is one
	//reader of their format rather than one per writer, and it hands over ids that can still be told apart.
	internal IEnumerable<(int Scope, long Id, CoreImportedKeyValues3 Node)> Every
	{
		get
		{
			for ( var scope = 0; scope < scopes.Count; scope++ )
			{
				foreach ( var (id, node) in scopes[scope] ) yield return (scope, id, node);
			}
		}
	}

	//The node an id names, read the way a wire drawn in scope reads it: its own manager
	//first, and the outermost one after - which is where their shared data sits.
	internal CoreImportedKeyValues3 Find( long id, int scope )
	{
		if ( id == None ) return null;

		if ( scope >= 0 && scope < scopes.Count && scopes[scope].TryGetValue( id, out var here ) )
			return here;

		return scopes.Count > 0 && scopes[0].TryGetValue( id, out var outermost ) ? outermost : null;
	}

	//Which managers hold a node under this id, for a wire that found none.
	internal IEnumerable<int> ScopesWith( long id )
	{
		for ( var scope = 0; scope < scopes.Count; scope++ )
		{
			if ( scopes[scope].ContainsKey( id ) ) yield return scope;
		}
	}

	//Which manager a node was drawn in, or the outermost.
	internal int LivesIn( CoreImportedKeyValues3 node )
		=> node is not null && livesIn.TryGetValue( node, out var found ) ? found : 0;

	//The manager a group holds, or -1 where it holds none.
	internal int Holds( CoreImportedKeyValues3 group )
		=> group is not null && holds.TryGetValue( group, out var found ) ? found : -1;

	//The group a manager is the inside of, or null for the outermost.
	internal CoreImportedKeyValues3 GroupOf( int scope )
		=> groups.TryGetValue( scope, out var found ) ? found : null;

	private readonly Dictionary<long, Parameter> parameters = new();
	private readonly Dictionary<long, string> tags = new();

	//Their tag names by id, from wherever the file keeps them.
	internal IReadOnlyDictionary<long, string> TagNames => tags;

	//Their parameter list, from wherever the file keeps it.
	internal IReadOnlyList<CoreImportedKeyValues3> Declared => Parameters();

	//A node built once, however many things point at it - per output, because a group asked for two of its outputs is two different poses.
	private readonly Dictionary<Link, Node> built = new();

	//Which group each manager is the inside of, so a node drawn in one can be told what was wired into the group from outside. Kept by
	//manager rather than by the id of the group's input node, which is an id like any other of theirs and is reused across their groups.
	private readonly Dictionary<int, CoreImportedKeyValues3> groups = new();

	//What is being built, so a graph that loops does not hang.
	private readonly HashSet<Link> building = new();

	//Node classes with no counterpart here, and how many there were.
	private readonly Dictionary<string, int> passed = new();

	//The model's bone masks by name, read once for the whole graph.
	private readonly Dictionary<string, IReadOnlyList<(string Bone, float Weight)>> masks
		= new( StringComparer.OrdinalIgnoreCase );

	//Their name for a mask that reaches nothing, which is how one of their layers is switched off.
	//No model declares it, so it is answered here rather than counted as one that went missing.
	internal const string Nothing = "_empty_anim_weightlist";

	//How many layers named a mask that was not among them.
	private int missing;

	//Names of missing masks, so the report can say which ones
	private readonly SortedSet<string> unnamed = new( StringComparer.OrdinalIgnoreCase );

	private readonly CoreImportedKeyValues3 data;

	//Their file this was read from, so a subgraph it names can be found.
	private readonly string source;

	//Subgraphs being read, to stop recursion; a stack since each use is read separately
	private readonly HashSet<string> reading = new( StringComparer.OrdinalIgnoreCase );

	private int states;
	private int transitions;
	private int clips;

	//The page written, and how much of it its output reaches.
	private int drawn;
	private int reached;

	//One of the values a game sets by name.
	private readonly record struct Parameter( string Name, Node Value, bool IsLength );

	private SBoxAnimGraph( CoreImportedKeyValues3 data, string skeleton, string source = null )
	{
		this.data = data;
		this.source = source;

		builder.Skeleton = string.IsNullOrEmpty( skeleton )
			? Model( data )
			: skeleton;

		var outermost = NewScope();

		CollectNodes( data["m_nodeManager"]?["m_nodes"] ?? data["m_nodes"], outermost );
		CollectNodes( data["m_pSharedData"]?["m_nodes"], outermost );

		foreach ( var tag in Tags( data ) )
		{
			var id = Id( tag["m_tagID"] ?? tag["m_id"] ?? tag["m_nTagID"] );
			if ( id != None ) tags[id] = tag.String( "m_name" ) ?? $"tag{id}";
		}

		Declare();
	}

	//A manager of their own, and the number this graph knows it by.
	int NewScope()
	{
		scopes.Add( new Dictionary<long, CoreImportedKeyValues3>() );
		return scopes.Count - 1;
	}

	void CollectNodes( CoreImportedKeyValues3 raw, int scope )
	{
		if ( raw is null ) return;

		if ( raw.IsArray )
		{
			foreach ( var entry in raw.Items )
			{
				if ( entry is null ) continue;

				if ( entry["value"] is { } value )
				{
					Keep( Id( entry["key"] ), value, scope );
					continue;
				}

				Keep( NodeId( entry ), entry, scope );
			}

			return;
		}

		if ( !raw.IsObject ) return;

		foreach ( var key in raw.Keys )
		{
			var value = raw[key];
			if ( value is null ) continue;

			var id = ParseId( key );
			if ( id == None ) id = NodeId( value );

			Keep( id, value, scope );
		}
	}

	//One node, under the manager it was drawn in, and what it holds.
	void Keep( long id, CoreImportedKeyValues3 node, int scope )
	{
		if ( id != None ) scopes[scope][id] = node;

		livesIn[node] = scope;

		Nest( node, scope );
	}

	//Registers nodes nested inside a group, where nearly all animation logic lives
	void Nest( CoreImportedKeyValues3 node, int scope )
	{
		if ( node is null ) return;

		var kind = ClassName( node );

		if ( node["m_nodeManager"] is { } manager )
		{
			var inside = NewScope();

			holds[node] = inside;
			groups[inside] = node;

			CollectNodes( manager["m_nodes"] ?? manager, inside );
		}

		if ( kind == "CSubGraphAnimNode" ) Subgraph( node );
	}

	//Reads a subgraph's nodes from its own file; it's a group whose contents live elsewhere
	void Subgraph( CoreImportedKeyValues3 node )
	{
		var named = node.String( "m_subGraphFilename" );
		if ( string.IsNullOrWhiteSpace( named ) ) return;

		var relative = named.Replace( '\\', Path.DirectorySeparatorChar ).Replace( '/', Path.DirectorySeparatorChar );

		//Named from the root of whichever addon holds it, so the folders above the graph itself are tried until one of them has it.
		var root = string.IsNullOrWhiteSpace( source ) ? null : Path.GetDirectoryName( source );

		while ( !string.IsNullOrEmpty( root ) )
		{
			//Case-insensitive lookup, since references and folders differ in case
			if ( PortPaths.FindFile( Path.Combine( root, relative ) ) is { } candidate )
			{
				if ( !reading.Add( candidate ) ) return;

				try
				{
					//Read again for this node rather than shared with the last one that named the same file: a manager of its own,
					//filled with nodes of its own, because what the two were handed from outside is not the same - see reading.
					if ( Load( candidate ) is { } inside )
					{
						var scope = NewScope();

						holds[node] = scope;
						groups[scope] = node;

						CollectNodes( inside["m_nodeManager"]?["m_nodes"] ?? inside["m_nodes"], scope );
					}
				}
				finally
				{
					reading.Remove( candidate );
				}

				return;
			}

			root = Path.GetDirectoryName( root );
		}

		missingGraphs.Add( named );
	}

	//Subgraphs their graph names and this installation does not have.
	private readonly HashSet<string> missingGraphs = new( StringComparer.OrdinalIgnoreCase );

	static IReadOnlyList<CoreImportedKeyValues3> Tags( CoreImportedKeyValues3 data )
	{
		var listed = data["m_pTagManager"]?.Array( "m_tags" )
			?? System.Array.Empty<CoreImportedKeyValues3>();
		return listed.Count > 0 ? listed : data.Array( "m_localTags" );
	}

	static long NodeId( CoreImportedKeyValues3 node )
	{
		var id = Id( node["m_nNodeID"] );
		if ( id != None ) return id;

		id = Id( node["m_id"] );
		if ( id != None ) return id;

		return Id( node["m_nodeID"] );
	}

	//The model a graph was built against, under the name the port gives it.
	private static string Model( CoreImportedKeyValues3 data )
	{
		foreach ( var named in data.Array( "m_previewModels" ) )
		{
			var path = named.AsString();
			if ( string.IsNullOrWhiteSpace( path ) ) continue;

			return Path.ChangeExtension( path, ".mdl" ).Replace( '\\', '/' );
		}

		var remembered = data.String( "m_modelName" ) ?? data.String( "m_previewModelName" );
		if ( string.IsNullOrWhiteSpace( remembered ) ) return "";

		return Path.ChangeExtension( remembered, ".mdl" ).Replace( '\\', '/' );
	}

	//Reads the model's bone masks at port time so layers can embed weights instead of names
	private void ReadMasks( string file, Func<string, byte[]> find, string relative, PortReport report )
	{
		//Their name for the model, not ours: the port renames it to .mdl for the definition it writes, and what is on disk is still theirs.
		var model = Path.ChangeExtension( Model( data ), ".vmdl" )?.Replace( '\\', '/' );

		if ( string.IsNullOrWhiteSpace( model ) ) return;

		var document = Nearby( model );

		if ( document is null )
		{
			report.Notes.Add( $"{relative}: names the model {model} for its masks, which is not in the " +
				"project it was ported from - so its layers apply to whole bodies until the masks are " +
				"given weights here." );
			return;
		}

		foreach ( var list in SBoxModelLists.WeightLists( document ) )
		{
			if ( string.IsNullOrWhiteSpace( list.Name ) || list.Bones is not { Count: > 0 } ) continue;

			masks[list.Name] = list.Bones
				.ConvertAll( bone => (bone.Bone, bone.Weight) );
		}
	}

	//A file their document names, found by walking up from the graph. Their paths are written from the root of whichever addon
	//holds them, and nothing here knows where that root is, so the folders above the graph are tried until one of them has the file.
	private string Nearby( string named )
	{
		if ( string.IsNullOrWhiteSpace( named ) ) return null;

		var relative = named.Replace( '\\', Path.DirectorySeparatorChar ).Replace( '/', Path.DirectorySeparatorChar );

		var root = string.IsNullOrWhiteSpace( source ) ? null : Path.GetDirectoryName( source );

		while ( !string.IsNullOrEmpty( root ) )
		{
			//However it is spelled - see Subgraph, which looks for its files the same way and for the same reason.
			if ( PortPaths.FindFile( Path.Combine( root, relative ) ) is { } candidate ) return candidate;

			root = Path.GetDirectoryName( root );
		}

		return null;
	}

	//The mask a layer names, with the weights in it - or the name on its own where the model did not have one under that name.
	private Node Mask( string name )
	{
		if ( string.IsNullOrEmpty( name ) ) return null;

		//Their name for no mask at all. Naming one the model does not have would weigh the layer at nothing, which is the opposite of what it means.
		if ( name == "Everything" ) return null;

		if ( masks.TryGetValue( name, out var weights ) ) return builder.BoneMask( name, weights );

		//Their engine's own name for a mask that reaches nothing. A model never declares it, so counting it
		//as absent is a report that cries wolf on every graph that switches a layer off the way theirs does.
		if ( name == Nothing ) return builder.BoneMask( 0.0f );

		++missing;
		unnamed.Add( name );

		return builder.BoneMask( name );
	}

	//An id out of the little object they wrap every id in.
	private static long Id( CoreImportedKeyValues3 node )
	{
		if ( node is null ) return None;

		if ( node["m_id"] is { } inner ) return Id( inner );

		var number = node.AsNumber( double.NaN );
		if ( !double.IsNaN( number ) && number != None ) return (long)number;

		return ParseId( node.AsString() );
	}

	static long ParseId( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return None;

		var part = text;
		var colon = text.LastIndexOf( ':' );
		if ( colon >= 0 && colon + 1 < text.Length ) part = text[( colon + 1 )..];

		return long.TryParse( part, out var id ) ? id : None;
	}

	//Wire source: node, output (groups have several) and scope (ids are unique per manager)
	private readonly record struct Link( long Node, long Output, int Scope );

	//Which manager the wires being read right now are drawn in, which is the one the node being built was drawn in.
	private int scope;

	//What a connection points at, in the manager it is drawn in.
	private Link Wire( CoreImportedKeyValues3 connection ) => Wire( connection, scope );

	//What a connection points at, read as a wire drawn in scope.
	private static Link Wire( CoreImportedKeyValues3 connection, int scope )
	{
		if ( connection is null ) return new Link( None, None, scope );

		var id = Id( connection["m_nodeID"] );
		if ( id == None ) id = NodeId( connection );
		if ( id == None ) id = Id( connection );

		return new Link( id, Id( connection["m_outputID"] ), scope );
	}

	//-- parameters -----------------------------------------------------------

	//Every value the game can set, declared before anything reads one.
	private void Declare()
	{
		var listed = data["m_pParameterList"]?.Array( "m_Parameters" )
			?? System.Array.Empty<CoreImportedKeyValues3>();
		if ( listed.Count == 0 ) listed = data.Array( "m_localParameters" );

		foreach ( var found in listed )
		{
			var id = Id( found["m_id"] );
			if ( id == None ) continue;

			var name = found.String( "m_name" ) ?? "";
			var length = IsLength( name );

			var scale = length ? PortUnits.SBoxUnit : 1.0f;

			//Whether it goes back to what it started as once the graph has read it, which every kind of theirs declares. This is what makes
			//a parameter a thing that happened rather than a thing that is so, and it is the author's decision rather than ours to guess at.
			var reset = found.Bool( "m_bAutoReset" );

			var options = found.Array( "m_enumOptions" ).Select( x => x.AsString() ?? "" ).ToArray();

			var value = ClassName( found ) switch
			{
				"CFloatAnimParameter" => Control( name, AnimGraphBuilder.NodeKind.Float, reset,
					found.Float( "m_fMinValue" ) * scale, found.Float( "m_fMaxValue" ) * scale,
					found.Float( "m_fDefaultValue" ) * scale ),

				"CBoolAnimParameter" => Control( name, AnimGraphBuilder.NodeKind.Bool, reset,
					0.0f, 0.0f, found.Bool( "m_bDefaultValue" ) ? 1.0f : 0.0f ),

				"CEnumAnimParameter" => Control( name, AnimGraphBuilder.NodeKind.Id, reset,
					0.0f, 0.0f, found.Int( "m_defaultValue" ), options ),

				"CIntAnimParameter" => Control( name, AnimGraphBuilder.NodeKind.Float, reset,
					found.Int( "m_minValue" ), found.Int( "m_maxValue" ), found.Int( "m_defaultValue" ) ),

				"CVectorAnimParameter" => Control( name, AnimGraphBuilder.NodeKind.Vector, reset ),

				//A rotation is not something this graph format carries as a value of its own.
				//Declared as a vector so the name still exists and setting it is not an error.
				"CQuaternionAnimParameter" => Control( name, AnimGraphBuilder.NodeKind.Vector, reset ),

				_ => null,
			};

			if ( value is null ) continue;

			parameters[id] = new Parameter( name, value, length );
		}
	}

	//Creates a parameter node with its default, reset flag, range and enum options
	private Node Control( string name, AnimGraphBuilder.NodeKind kind, bool reset,
		float min = 0.0f, float max = 0.0f, float fallback = 0.0f, IReadOnlyList<string> options = null )
		=> builder.AddControlParameter( name, kind, new AnimGraphBuilder.ControlParameterSettings
		{
			Describe = true,
			Min = min,
			Max = max,
			Default = fallback,
			AutoReset = reset,
			Options = options,
		} );

	//Parameters measured in inches that must convert to metres; excludes rotation speeds and scales
	internal static bool IsLength( string name )
	{
		if ( string.IsNullOrEmpty( name ) ) return false;

		var lower = name.ToLowerInvariant();

		//Turning rates, and the numbers that scale a speed rather than being one.
		if ( lower.Contains( "rotationspeed" ) ) return false;
		if ( lower.Contains( "speed_scale" ) || lower.Contains( "speed_reload" ) ) return false;
		if ( lower.Contains( "scale_" ) ) return false;

		if ( lower.Contains( "speed" ) ) return true;
		if ( lower.Contains( "offset_height" ) || lower.Contains( "height_offset" ) ) return true;
		if ( lower.EndsWith( ".position" ) ) return true;
		if ( lower == "hit_offset" ) return true;

		//move_x, wish_y and the rest: how fast, along an axis.
		foreach ( var prefix in new[] { "move_", "wish_" } )
		{
			if ( lower.Length == prefix.Length + 1 && lower.StartsWith( prefix ) && lower[^1] is 'x' or 'y' or 'z' )
				return true;
		}

		return false;
	}

	private float Scaled( long parameterId, float value )
		=> parameters.TryGetValue( parameterId, out var found ) && found.IsLength
			? value * PortUnits.SBoxUnit
			: value;

	private Node Value( long parameterId )
		=> parameters.TryGetValue( parameterId, out var found ) ? found.Value : null;

	//Their input damper as our spring: freq = sqrt(tension)/2pi, ratio = damping/(2 sqrt(tension))
	private Node Damped( Node value, CoreImportedKeyValues3 node )
	{
		if ( value is null ) return null;

		if ( node?["m_damping"] is not { } damping ) return value;

		var tension = damping.Float( "m_fTension", 0.0f );
		if ( tension <= 0.0f ) return value;

		var rate = MathF.Sqrt( tension );

		return builder.Spring( value, new AnimGraphBuilder.SpringSettings
		{
			Hertz = rate / ( 2.0f * MathF.PI ),
			DampingRatio = damping.Float( "m_fDamping", rate ) / ( 2.0f * rate ),
		} );
	}

	//-- the graph ------------------------------------------------------------

	//The root node's pose, which is the whole graph.
	private Node Translate()
	{
		foreach ( var (found, _, node) in Every )
		{
			if ( ClassName( node ) != "CRootAnimNode" ) continue;

			scope = found;

			return Pose( Wire( node["m_inputConnection"] ) ) ?? builder.ReferencePose();
		}

		return null;
	}

	//One node, and everything under it. Null where the connection points at nothing, which their graphs are full of.
	private Node Pose( long id ) => Pose( new Link( id, None, scope ) );

	private Node Pose( Link link )
	{
		var node = Find( link.Node, link.Scope );
		if ( node is null ) return null;

		var kind = ClassName( node );

		//Only a group answers differently depending on which output was asked for. Everything else has one pose
		//to give, so wires that happen to carry an output id must not build the same node twice under two keys.
		if ( kind is not ("CGroupAnimNode" or "CGroupOutputAnimNode" or "CGroupInputAnimNode") )
			link = link with { Output = None };

		if ( built.TryGetValue( link, out var already ) ) return already;

		//A graph that points back at itself would otherwise never finish. Their editor allows it while a graph is half drawn.
		if ( !building.Add( link ) ) return builder.ReferencePose();

		//The wires this node draws are drawn in the manager this node was found in, and are read as such until it is done with.
		var outer = scope;
		scope = link.Scope;

		try
		{
			//The three group kinds are answered here rather than in Build, because what they come
			//to depends on which output was asked for and Build is handed a node and nothing else.
			var made = kind switch
			{
				"CGroupAnimNode" => Grouped( node, link.Output ),
				"CSubGraphAnimNode" => Grouped( node, link.Output ),
				"CGroupOutputAnimNode" => Proxied( node, link.Output ),
				"CGroupInputAnimNode" => Outside( node, link.Output ),
				_ => Build( node ),
			};

			if ( made is null ) return null;

			built[link] = made;
			return made;
		}
		finally
		{
			scope = outer;
			building.Remove( link );
		}
	}

	//-- groups ---------------------------------------------------------------

	//A group evaluates to whatever its output node evaluates to
	private Node Grouped( CoreImportedKeyValues3 group, long output )
	{
		var outputs = Id( group["m_outputNodeID"] );
		var inside = Holds( group );

		return outputs == None || inside < 0
			? Through( group )
			: Pose( new Link( outputs, output, inside ) );
	}

	//The way out of a group: one of the outputs drawn on it, each with its own name and its own wire back into the group.
	private Node Proxied( CoreImportedKeyValues3 node, long output )
	{
		var items = node.Array( "m_proxyItems" );
		if ( items.Count == 0 ) return null;

		return Pose( Wire( Picked( items, "m_outputID", output )["m_inputConnection"] ) );
	}

	//The way into a group, from the inside: whatever the group was handed from outside, found through the map the group keeps of its own inputs.
	private Node Outside( CoreImportedKeyValues3 node, long output )
	{
		if ( GroupOf( scope ) is not { } group ) return null;

		var map = group.Array( "m_inputConnectionMap" );
		if ( map.Count == 0 ) return null;

		//The map hangs on the group itself, so what it names is drawn out where the group is and not in here.
		return Pose( Wire( Picked( map, "key", output )["value"], LivesIn( group ) ) );
	}

	//The entry of a list whose id matches, or the first one where nothing was asked for. Falling back to the first is deliberate. A wire drawn before
	//somebody added a second output carries no output id at all, and answering it with the only output there used to be is what their editor does too.
	private static CoreImportedKeyValues3 Picked(
		IReadOnlyList<CoreImportedKeyValues3> items, string key, long output )
	{
		if ( output == None ) return items[0];

		foreach ( var item in items )
		{
			if ( Id( item[key] ) == output ) return item;
		}

		return items[0];
	}

	private Node Build( CoreImportedKeyValues3 node )
	{
		switch ( ClassName( node ) )
		{
			case "CSequenceAnimNode":
				++clips;
				return builder.Clip( node.String( "m_sequenceName" ), new AnimGraphBuilder.ClipSettings
				{
					Looping = node.Bool( "m_bLoop" ),
					Speed = node.Float( "m_playbackSpeed", 1.0f ),
				} );

			case "CSingleFrameAnimNode":
				++clips;
				return builder.Pose( node.String( "m_sequenceName" ), new AnimGraphBuilder.PoseSettings
				{
					FixedTime = node.Int( "m_nFrameIndex" ),
					UseFramesAsInput = true,
				} );

			case "CBlendAnimNode": return Blend( node );
			case "CBlend2DAnimNode": return Blend2D( node );
			case "CSelectorAnimNode": return Selector( node );
			case "CChoiceAnimNode": return Choice( node );
			case "CStateMachineAnimNode": return Machine( node );
			case "CAddAnimNode": return Additive( node );
			case "CBoneMaskAnimNode": return Masked( node );

			case "CSpeedScaleAnimNode":
			{
				var pose = Pose( Wire( node["m_inputConnection"] ) );
				var scale = Damped( Value( Id( node["m_param"] ) ), node );

				return pose is null ? null : scale is null ? pose : builder.SpeedScale( pose, scale );
			}

			case "CHitReactAnimNode": return Hit( node );
			case "CTwoBoneIKAnimNode": return TwoBone( node );
			case "CSolveIKChainAnimNode": return Chains( node );
			case "CCycleControlAnimNode": return Cycle( node );
			case "CFollowAttachmentAnimNode": return Follow( node );
			case "CAimMatrixAnimNode": return AimMatrix( node );

			case "CLookAtAnimNode":
			{
				var under = Pose( Wire( node["m_inputConnection"] ) );
				if ( under is null ) return Through( node );

				if ( Damped( Value( Id( node["m_param"] ) ), node ) is not { } target ) return Through( node );

				//Keep separate yaw and pitch limits, and the aimed attachment, or the upper body aims wrong
				return builder.LookAt( under, node.String( "m_lookatChainName" ), target,
					new AnimGraphBuilder.LookAtSettings
					{
						Weight = Damped( Value( Id( node["m_weightParam"] ) ), node ),
						Attachment = node.String( "m_attachmentName" ) ?? "",
						YawLimit = node.Float( "m_flYawLimit", 180.0f ),
						PitchLimit = node.Float( "m_flPitchLimit", 180.0f ),
					} );
			}

			case "CSubtractAnimNode": return Difference( node );

			case "CDirectPlaybackAnimNode":
			{
				var under = Pose( Wire( node["m_inputConnection"] ) );
				return under ?? builder.ExternalPose( "direct_playback" );
			}

			default: return Through( node );
		}
	}

	//A node with no counterpart: pass its input along and count it, by class, for the port to own up to afterwards.
	private Node Through( CoreImportedKeyValues3 node )
	{
		var name = node.String( "_class" ) ?? "unknown";
		passed[name] = passed.GetValueOrDefault( name ) + 1;

		foreach ( var key in new[]
		{
			"m_inputConnection", "m_baseInput", "m_baseInputConnection", "m_inputConnection1",
			"m_pInputConnection", "m_inputPose",
		} )
		{
			if ( node[key] is null ) continue;

			if ( Pose( Wire( node[key] ) ) is { } pose ) return pose;
		}

		return null;
	}

	//-- blends ---------------------------------------------------------------

	//Poses along a line, picked out by one value.
	private Node Blend( CoreImportedKeyValues3 node )
	{
		var parameter = Id( node["m_param"] );
		var value = Value( parameter );

		if ( value is null ) return Through( node );

		var space = builder.BlendSpace( value, node.Bool( "m_bLoop" ) );
		var added = 0;

		foreach ( var child in node.Array( "m_children" ) )
		{
			var pose = Pose( Wire( child["m_inputConnection"] ) );
			if ( pose is null ) continue;

			space.Add( Scaled( parameter, child.Float( "m_blendValue" ) ), pose );
			++added;
		}

		return added > 0 ? space.Node : null;
	}

	//Poses across a plane, picked out by two.
	private Node Blend2D( CoreImportedKeyValues3 node )
	{
		var x = Id( node["m_paramX"] );
		var y = Id( node["m_paramY"] );

		if ( Value( x ) is not { } across || Value( y ) is not { } along ) return Through( node );

		var space = builder.BlendSpace2D( across, along, node.Bool( "m_bLoop" ) );
		var added = 0;

		foreach ( var item in node.Array( "m_items" ) )
		{
			var clip = item.String( "m_sequenceName" );
			if ( string.IsNullOrEmpty( clip ) ) continue;

			var at = item["m_blendValue"];

			space.Add( new Vector2(
					Scaled( x, at?[0]?.AsFloat() ?? 0.0f ),
					Scaled( y, at?[1]?.AsFloat() ?? 0.0f ) ),
				builder.Clip( clip, new AnimGraphBuilder.ClipSettings { Looping = node.Bool( "m_bLoop" ) } ) );

			++clips;
			++added;
		}

		return added > 0 ? space.Node : null;
	}

	//One of several, by what a value says.
	private Node Selector( CoreImportedKeyValues3 node )
	{
		var options = new List<Node>();

		foreach ( var child in node.Array( "m_children" ) )
			options.Add( Pose( Wire( child["m_inputConnection"] ?? child ) ) ?? builder.ReferencePose() );

		if ( options.Count == 0 ) return null;

		var source = node.String( "m_selectionSource" ) ?? "";

		//Enum selector: children labelled by option name at their index, chosen by name
		if ( source.EndsWith( "Enum" ) && Value( Id( node["m_enumParamID"] ) ) is { } chosen )
		{
			var named = Options( Id( node["m_enumParamID"] ) );

			return builder.IdBasedSelector( chosen,
				options.Select( ( pose, at ) => (at < named.Count ? named[at] : at.ToString(), pose) ) );
		}

		if ( source.EndsWith( "Int" ) && Value( Id( node["m_intParamID"] ) ) is { } counted )
			return builder.ParameterizedSelector( counted, options );

		//Bool selector: false picks the first child, true the second; crossfades like theirs
		if ( Value( Id( node["m_boolParamID"] ) ) is { } flag && options.Count >= 2 )
		{
			var across = builder.Ease( builder.Switch( flag, 1.0f, 0.0f ),
				new AnimGraphBuilder.EaseSettings { Time = node.Float( "m_blendDuration", 0.2f ) } );

			return builder.BlendSpace( across )
				.Add( 0.0f, options[0] )
				.Add( 1.0f, options[1] );
		}

		return options[0];
	}

	//One of several, at random, weighted.
	private Node Choice( CoreImportedKeyValues3 node )
	{
		var variation = builder.Variation();
		var added = 0;

		foreach ( var child in node.Array( "m_children" ) )
		{
			var pose = Pose( Wire( child["m_inputConnection"] ) );
			if ( pose is null ) continue;

			//Their weights are fractions and ours are counts, and a weight of nothing still means the option can come up.
			var weight = System.Math.Max( 1, (int)System.Math.Round( child.Float( "m_weight" ) * 100.0f ) );

			variation.Add( pose, weight );
			++added;
		}

		return added > 0 ? variation.Node : null;
	}

	//One pose measured against another, which is the difference an additive layer is laid out of. This used to hand back nothing at all - a pose of
	//zeroes - which is a character that never leans, never aims off centre and never flinches, because every one of those is authored as a difference.
	private Node Difference( CoreImportedKeyValues3 node )
	{
		var pose = Pose( Wire( node["m_baseInputConnection"] ) )
			?? Pose( Wire( node["m_baseInput"] ) );

		var reference = Pose( Wire( node["m_subtractInputConnection"] ) )
			?? Pose( Wire( node["m_subtractInput"] ) );

		if ( pose is null ) return reference is null ? Through( node ) : builder.ZeroPose();
		if ( reference is null ) return Through( node );

		return builder.PoseDifference( pose, reference );
	}

	//A difference laid over a pose.
	private Node Additive( CoreImportedKeyValues3 node )
	{
		var under = Pose( Wire( node["m_baseInput"] ) );
		var over = Pose( Wire( node["m_additiveInput"] ) );

		if ( under is null ) return over;
		if ( over is null ) return under;

		var layers = builder.Layers( under );

		layers.AddLayer( over, new AnimGraphBuilder.LayerSettings
		{
			BlendMode = AnimGraphBuilder.LayerBlendMode.Additive,

			//Theirs says so on eighty four of the citizen's eighty six adds: the difference goes on a channel at a time rather than
			//being composed, so an offset is added as it was authored instead of being turned first by whatever the base was doing.
			ChannelsApart = node.Bool( "m_bApplyChannelsSeparately" ),

			//And where it was authored. Fourteen of the citizen's adds say model space, and every clip whose name ends in deltams is one of them.
			ModelSpace = node.Bool( "m_bModelSpace" ),
		} );

		return layers.Node;
	}

	//Masked blend; the blend space (often model space, rotation only) matters for hold types
	private Node Masked( CoreImportedKeyValues3 node )
	{
		var under = Pose( Wire( node["m_inputConnection1"] ) );
		var over = Pose( Wire( node["m_inputConnection2"] ) );

		if ( under is null ) return over;
		if ( over is null ) return under;

		var mask = node.String( "m_weightListName" );
		var space = node.String( "m_blendSpace" ) ?? "";

		var layers = builder.Layers( under );

		layers.AddLayer( over, new AnimGraphBuilder.LayerSettings
		{
			BoneMask = Mask( mask ),
			Weight = node.Bool( "m_bUseBlendScale" ) ? Damped( Value( Id( node["m_blendParameter"] ) ), node ) : null,

			ModelSpace = space.StartsWith( "BlendSpace_Model" ),

			//Rotation only says which way bones point and nothing about how far apart they sit, so a body keeps its proportions through the blend.
			RotationOnly = space.EndsWith( "RotationOnly" ),
		} );

		return layers.Node;
	}

	//Aim matrix as a blend space: 3x3 frames bottom-to-top, left-to-right, frame 4 is center
	//Range comes from the node; pole frames are added only if they don't overlap the grid
	private Node AimMatrix( CoreImportedKeyValues3 node )
	{
		var under = Pose( Wire( node["m_inputConnection"] ) );

		var clip = node.String( "m_sequenceName" );
		if ( string.IsNullOrEmpty( clip ) ) return under ?? Through( node );

		if ( Damped( Value( Id( node["m_param"] ) ), node ) is not { } aim ) return under ?? Through( node );

		var step = node.Float( "m_fAngleIncrement", 90.0f );
		if ( step <= 0.0f ) step = 90.0f;

		//Which way the character is being asked to aim, as two angles.
		var across = builder.VectorInfo( aim, AnimGraphBuilder.VectorComponent.AngleHorizontal );
		var up = builder.VectorInfo( aim, AnimGraphBuilder.VectorComponent.AngleVertical );

		var space = builder.BlendSpace2D( across, up );

		Node Frame( int frame ) => builder.Pose( clip, new AnimGraphBuilder.PoseSettings
		{
			FixedTime = frame,
			UseFramesAsInput = true,
		} );

		//Row nought is the bottom one, so it aims down by a step, not up.
		for ( var frame = 0; frame < 9; frame++ )
		{
			space.Add( new Vector2( ( 1 - ( frame % 3 ) ) * step, ( ( frame / 3 ) - 1 ) * step ), Frame( frame ) );
		}

		if ( step < 90.0f )
		{
			space.Add( new Vector2( 0.0f, 90.0f ), Frame( 9 ) );

			if ( node.Bool( "m_bCanLookStraightDown" ) )
				space.Add( new Vector2( 0.0f, -90.0f ), Frame( 10 ) );
		}

		++clips;

		var aimed = space.Node;

		if ( under is null ) return aimed;

		var mask = node.String( "m_boneMaskName" );

		var layers = builder.Layers( under );

		//Theirs says which of three; an aim grid is nearly always a difference laid over whatever the body is already doing,
		//and one of the three says that difference was authored against the model rather than against each bone's parent.
		var blend = node.String( "m_blendMode" ) ?? "";
		var additive = blend.Contains( "Additive" );

		layers.AddLayer( aimed, new AnimGraphBuilder.LayerSettings
		{
			BlendMode = additive
				? AnimGraphBuilder.LayerBlendMode.Additive
				: AnimGraphBuilder.LayerBlendMode.Overlay,

			ModelSpace = additive && blend.Contains( "ModelSpace" ),

			BoneMask = Mask( mask ),
		} );

		return layers.Node;
	}

	//Two-bone IK; target is a bone or a parameter as the node says, ignoring leftover fields
	private Node TwoBone( CoreImportedKeyValues3 node )
	{
		var pose = Pose( Wire( node["m_inputConnection"] ) );
		if ( pose is null ) return null;

		var chain = node.String( "m_ikChainName" ) ?? "";

		if ( ( node.String( "m_targetType" ) ?? "" ).Contains( "Parameter" ) )
		{
			var target = Id( node["m_targetParam"] );

			if ( Value( target ) is not { } where ) return Through( node );

			return builder.TwoBoneIK( pose, chain, builder.PointTarget( where ),
				new AnimGraphBuilder.IkSettings { Enabled = Enabled( target ) } );
		}

		var bone = node.String( "m_targetBoneName" );
		if ( string.IsNullOrEmpty( bone ) ) return Through( node );

		return builder.TwoBoneIK( pose, chain, builder.BoneTarget( bone ) );
	}

	//The .enabled flag beside an IK position parameter; without one the target decides
	private Node Enabled( long placed )
	{
		if ( !parameters.TryGetValue( placed, out var found ) ) return null;

		var name = found.Name ?? "";
		if ( !name.EndsWith( ".position", StringComparison.OrdinalIgnoreCase ) ) return null;

		var flag = name[..^".position".Length] + ".enabled";

		foreach ( var (_, parameter) in parameters )
		{
			if ( string.Equals( parameter.Name, flag, StringComparison.OrdinalIgnoreCase ) ) return parameter.Value;
		}

		return null;
	}

	//Multi-chain IK as a stack of single-chain solves; chains without a target are left alone
	private Node Chains( CoreImportedKeyValues3 node )
	{
		var pose = Pose( Wire( node["m_inputConnection"] ) );
		if ( pose is null ) return null;

		var solved = 0;

		foreach ( var chain in node.Array( "m_IkChains" ) )
		{
			var name = ChainName( chain );
			if ( string.IsNullOrEmpty( name ) ) continue;

			var settings = chain["m_OverrideTargetSettings"];

			var bone = Text( settings?["m_Bone"] );
			var named = Text( settings?["m_AnimgraphParameterNamePosition"] );

			//A bone to follow where their file names one, a target the game sets otherwise - under
			//the parameter name they gave it, or under the chain's own name, which is what SetIk uses.
			var target = !string.IsNullOrEmpty( bone )
				? builder.BoneTarget( bone )
				: builder.TargetParameter( string.IsNullOrEmpty( named ) ? name : named );

			pose = builder.TwoBoneIK( pose, "", target, new AnimGraphBuilder.IkSettings
			{
				//Named as a chain rather than as a bone, because that is what it is: the model says which three bones the limb is.
				Chain = name,

				//A chain nothing is reaching for is left alone rather than dragged to the origin.
				Enabled = string.IsNullOrEmpty( bone ) ? builder.IsTargetSet( target ) : null,
			} );

			++solved;
		}

		if ( solved == 0 ) return Through( node );

		return pose;
	}

	//A name out of their file, whether it is written as a string or wrapped in a block with the name inside.
	private static string Text( CoreImportedKeyValues3 found )
	{
		if ( found is null ) return null;

		var text = found.AsString( null );
		if ( !string.IsNullOrEmpty( text ) ) return text;

		//Their bone reference key is m_Name (capital N); keys here match exactly
		return found.String( "m_name" ) ?? found.String( "m_Name" ) ?? found.String( "m_sName" );
	}

	//What a chain is called, wherever their file happens to keep the name. A chain is named directly on older nodes and through a
	//settings block on newer ones, and both shapes are in the graphs that ship - so both are read rather than one being assumed.
	private static string ChainName( CoreImportedKeyValues3 chain )
	{
		foreach ( var key in new[] { "m_IkChain", "m_ikChain", "m_chainName", "m_name" } )
		{
			if ( Text( chain[key] ) is { Length: > 0 } found ) return found;
		}

		return null;
	}

	//Procedural hit reaction driven by parameters CitizenAnimationHelper already sets
	private Node Hit( CoreImportedKeyValues3 node )
	{
		var pose = Pose( Wire( node["m_inputConnection"] ) );
		if ( pose is null ) return null;

		var trigger = Value( Id( node["m_triggerParam"] ) );
		var bone = Value( Id( node["m_hitBoneParam"] ) );
		var direction = Value( Id( node["m_hitDirectionParam"] ) );

		//Which bone, which way, and that it happened at all. Without those three there is no hit to react
		//to, and a node that reacted to a hit it could not place would shove whatever bone came first.
		if ( trigger is null || bone is null || direction is null ) return Through( node );

		return builder.HitReact( pose, trigger, bone,
			Value( Id( node["m_hitOffsetParam"] ) ),
			direction,
			Value( Id( node["m_hitStrengthParam"] ) ),
			new AnimGraphBuilder.HitReactSettings
			{
				BoneCount = node.Int( "m_nEffectedBoneCount", 3 ),
				MinImpactForce = node.Float( "m_flMinImpactForce", 50.0f ),
				MaxImpactForce = node.Float( "m_flMaxImpactForce", 250.0f ),
				SpringStrength = node.Float( "m_flSpringStrength", 10.0f ),
				CounterRotationScale = node.Float( "m_flCounterRotationScale", 0.6f ),
				PropagationScale = node.Float( "m_flPropagationScale", 1.0f ),
				MinDelayBetweenHits = node.Float( "m_flMinDelayBetweenHits" ),
			} );
	}

	private Node Cycle( CoreImportedKeyValues3 node )
	{
		var pose = Pose( Wire( node["m_inputConnection"] ) );
		if ( pose is null ) return null;

		var value = Damped( Value( Id( node["m_param"] ) ), node );
		if ( value is null ) return Through( node );

		return builder.CycleControl( pose, value );
	}

	//A bone made to follow an attachment, which ours does by reaching for it. Only where there is a bone to move and somewhere to move it to. Theirs
	//can follow position, rotation or both; ours places the bone, so a node asking for neither is left as a pass-through rather than pretended at.
	private Node Follow( CoreImportedKeyValues3 node )
	{
		var pose = Pose( Wire( node["m_inputConnection"] ) );
		if ( pose is null ) return null;

		var bone = node.String( "m_boneName" );
		var attachment = node.String( "m_attachmentName" );

		if ( string.IsNullOrEmpty( bone ) || string.IsNullOrEmpty( attachment ) ) return Through( node );
		if ( !node.Bool( "m_bMatchTranslation" ) && !node.Bool( "m_bMatchRotation" ) ) return Through( node );

		return builder.TwoBoneIK( pose, bone, builder.BoneTarget( attachment ) );
	}

	//-- state machines -------------------------------------------------------

	//States and what moves between them, which is where most of a character's behaviour is written.
	private Node Machine( CoreImportedKeyValues3 node )
	{
		var machine = builder.StateMachine();

		var made = new Dictionary<long, AnimGraphBuilder.StateMachineBuilder.State>();
		var start = default( AnimGraphBuilder.StateMachineBuilder.State );

		foreach ( var state in node.Array( "m_states" ) )
		{
			var id = Id( state["m_stateID"] );
			if ( id == None ) continue;

			var pose = Pose( Wire( state["m_inputConnection"] ) );

			//A tag on a state is a name that is set for as long as the machine is in it, and conditions elsewhere read it. Ours has no tags, but it has
			//events - and an event announced from no time onwards is announced every frame the state is current, which is the same thing said differently.
			var held = new List<(string, float, AnimGraphBuilder.StateMachineBuilder.TimedEventComparison)>();

			foreach ( var tag in state.Array( "m_tags" ) )
			{
				if ( tags.TryGetValue( Id( tag ), out var name ) )
					held.Add( (name, 0.0f, AnimGraphBuilder.StateMachineBuilder.TimedEventComparison.GreaterThanEqual) );
			}

			var settings = new AnimGraphBuilder.StateMachineBuilder.StateSettings
			{
				IsOffState = pose is null,
				ElapsedEvents = held.Count > 0 ? held : null,
			};

			var here = machine.AddState( pose ?? builder.ReferencePose(), settings );

			made[id] = here;
			++states;

			if ( state.Bool( "m_bIsStartState" ) ) start = here;
		}

		if ( made.Count == 0 ) return null;

		if ( start is not null ) machine.SetDefaultState( start );

		foreach ( var state in node.Array( "m_states" ) )
		{
			var from = Id( state["m_stateID"] );
			if ( !made.TryGetValue( from, out var here ) ) continue;

			foreach ( var route in state.Array( "m_transitions" ) )
			{
				if ( route.Bool( "m_bDisabled" ) ) continue;

				var to = Id( route["m_destState"] );
				if ( !made.TryGetValue( to, out var there ) ) continue;

				var transition = machine.CreateTransition( there, new AnimGraphBuilder.TransitionSettings
				{
					//The same default the document half uses. Left to fall to zero here, every transition of theirs that does not write the field
					//snapped in the graph that plays while blending in the graph an artist opens - two different characters out of one file.
				Duration = route.Float( "m_blendDuration", 0.2f ),

					//Nearest named easing to their bezier, and whether the target state keeps the cycle
					BlendEasing = Curve( route["m_blendCurve"] ),

					TimeMode = route.Bool( "m_bBlendCycle" )
						? AnimGraphBuilder.TransitionTimeMode.Synchronized
						: route.String( "m_resetCycleOption" ) == "SameCycleAsSource"
							? AnimGraphBuilder.TransitionTimeMode.MatchSourceTime
							: AnimGraphBuilder.TransitionTimeMode.None,
				} );

				machine.AddTransition( here, there, transition,
					new AnimGraphBuilder.StateMachineBuilder.TransitionRouteSettings
					{
						Condition = Conditions( route.Array( "m_conditions" ), machine, here ),
					} );

				++transitions;
			}
		}

		return machine.Build();
	}

	//Maps their bezier blend curve to our nearest named easing by where it is flat
	private static AnimGraphBuilder.Easing Curve( CoreImportedKeyValues3 curve )
	{
		if ( curve is null ) return AnimGraphBuilder.Easing.Linear;

		var first = curve["m_vControlPoint1"];
		var second = curve["m_vControlPoint2"];

		if ( first is null || second is null ) return AnimGraphBuilder.Easing.Linear;

		var startsFlat = first.Float( "1", first.Float( "y", 0.0f ) ) < 0.25f;
		var endsFlat = second.Float( "1", second.Float( "y", 1.0f ) ) > 0.75f;

		if ( startsFlat && endsFlat ) return AnimGraphBuilder.Easing.InOutQuad;
		if ( startsFlat ) return AnimGraphBuilder.Easing.InQuad;
		if ( endsFlat ) return AnimGraphBuilder.Easing.OutQuad;

		return AnimGraphBuilder.Easing.Linear;
	}

	//Everything a transition waits for, as one value. All of them at once: their conditions are a list and a list is an and.
	private Node Conditions( IReadOnlyList<CoreImportedKeyValues3> list,
		AnimGraphBuilder.StateMachineBuilder machine,
		AnimGraphBuilder.StateMachineBuilder.State from )
	{
		var all = new List<Node>();

		foreach ( var condition in list )
		{
			if ( Condition( condition, machine, from ) is { } made ) all.Add( made );
		}

		return all.Count switch
		{
			0 => null,
			1 => all[0],
			_ => builder.And( all.ToArray() ),
		};
	}

	private Node Condition( CoreImportedKeyValues3 condition,
		AnimGraphBuilder.StateMachineBuilder machine,
		AnimGraphBuilder.StateMachineBuilder.State from )
	{
		switch ( ClassName( condition ) )
		{
			case "CParameterAnimCondition":
			{
				var id = Id( condition["m_paramID"] );
				if ( Value( id ) is not { } value ) return null;

				var against = condition["m_comparisonValue"];
				var number = (float)( against?["m_data"]?.AsNumber() ?? 0.0 );

				//A flag is read as itself rather than compared with one.
				if ( value.Kind == AnimGraphBuilder.NodeKind.Bool )
					return number != 0.0f ? value : builder.Not( value );

				if ( value.Kind == AnimGraphBuilder.NodeKind.Id )
				{
					var options = Options( id );
					var name = number >= 0 && number < options.Count ? options[(int)number] : number.ToString();

					var same = builder.IdCompare( value, name );
					return condition.Int( "m_comparisonOp" ) == 1 ? builder.Not( same ) : same;
				}

				return Compare( value, condition.Int( "m_comparisonOp" ), Scaled( id, number ) );
			}

			case "CTagCondition":
			{
				if ( !tags.TryGetValue( Id( condition["m_tagID"] ), out var name ) ) return null;

				var set = builder.IdEventCondition( new[] { name } );

				var wanted = condition.Int( "m_comparisonValue", 1 ) != 0;
				if ( condition.Int( "m_comparisonOp" ) == 1 ) wanted = !wanted;

				return wanted ? set : builder.Not( set );
			}

			case "CTimeCondition":
				return builder.TimeCondition( from, new AnimGraphBuilder.StateMachineBuilder.TimeConditionSettings
				{
					Type = AnimGraphBuilder.StateMachineBuilder.TimeComparisonType.ElapsedTime,
					Comparison = Timing( condition.Int( "m_comparisonOp" ) ),
					Value = condition.Float( "m_comparisonValue" ),
				} );

			case "CFinishedCondition":
				return machine.Completed( from );

			case "CCycleCondition":
				//How far through what the state is playing, rather than how long it has been playing: the same question asked of a clip that may be any length.
				return builder.TimeCondition( from, new AnimGraphBuilder.StateMachineBuilder.TimeConditionSettings
				{
					Type = AnimGraphBuilder.StateMachineBuilder.TimeComparisonType.PercentageThroughState,
					Comparison = Timing( condition.Int( "m_comparisonOp" ) ),
					Value = condition.Float( "m_comparisonValue" ),
				} );

			case "CControlValueCondition":
			{
				//One parameter against another, or against a number. Theirs says which by a field rather
				//than by which of the two it filled in, so both are read and the fixed one is the fallback.
				var id = Id( condition["m_comparisonParamID"] );
				if ( Value( id ) is not { } value ) return null;

				if ( Value( Id( condition["m_sourceControlValue"] ) ) is { } other )
					return Compare( value, condition.Int( "m_comparisonOp" ), other );

				return Compare( value, condition.Int( "m_comparisonOp" ),
					Scaled( id, condition.Float( "m_comparisonFixedValue" ) ) );
			}

			default:
			{
				var name = condition.String( "_class" ) ?? "unknown condition";
				passed[name] = passed.GetValueOrDefault( name ) + 1;

				return null;
			}
		}
	}

	private List<string> Options( long parameterId )
	{
		foreach ( var found in Parameters() )
		{
			if ( Id( found["m_id"] ) != parameterId ) continue;

			return found.Array( "m_enumOptions" ).Select( x => x.AsString() ?? "" ).ToList();
		}

		return new List<string>();
	}

	IReadOnlyList<CoreImportedKeyValues3> Parameters()
	{
		var listed = data["m_pParameterList"]?.Array( "m_Parameters" )
			?? System.Array.Empty<CoreImportedKeyValues3>();
		return listed.Count > 0 ? listed : data.Array( "m_localParameters" );
	}

	internal static string ClassName( CoreImportedKeyValues3 node )
	{
		var name = node?.String( "_class" ) ?? "";

		if ( name.StartsWith( "CAnimGraphDoc_", StringComparison.Ordinal ) )
		{
			var rest = name["CAnimGraphDoc_".Length..];
			if ( rest.EndsWith( "AnimNode", StringComparison.Ordinal ) ) return "C" + rest;
			if ( rest.EndsWith( "Node", StringComparison.Ordinal ) )
				return "C" + rest[..^4] + "AnimNode";
			if ( rest.EndsWith( "Condition", StringComparison.Ordinal ) )
				return rest.StartsWith( "Parameter", StringComparison.Ordinal ) ? "CParameterAnimCondition" : "C" + rest;
			return "C" + rest;
		}

		if ( name.EndsWith( "UpdateNode", StringComparison.Ordinal ) )
			return name[..^"UpdateNode".Length] + "AnimNode";

		return name;
	}

	//How two numbers are compared. Their operators in the order their files use them, worked out from the graph rather than from documentation: equality
	//is written with the value carrying which way round, and the four inequalities appear against speeds and fractions where only one reading makes sense.
	private Node Compare( Node value, int op, float against ) => op switch
	{
		0 => builder.Compare( value, AnimGraphBuilder.FloatComparison.NearEqual, against, 0.001f ),
		1 => builder.Not( builder.Compare( value, AnimGraphBuilder.FloatComparison.NearEqual, against, 0.001f ) ),
		2 => builder.Compare( value, AnimGraphBuilder.FloatComparison.GreaterThan, against ),
		3 => builder.Compare( value, AnimGraphBuilder.FloatComparison.GreaterThanEqual, against ),
		4 => builder.Compare( value, AnimGraphBuilder.FloatComparison.LessThan, against ),
		5 => builder.Compare( value, AnimGraphBuilder.FloatComparison.LessThanEqual, against ),
		_ => null,
	};

	//The same comparison against another value rather than against a number.
	private Node Compare( Node value, int op, Node against ) => op switch
	{
		0 => builder.Compare( value, AnimGraphBuilder.FloatComparison.NearEqual, against, 0.001f ),
		1 => builder.Not( builder.Compare( value, AnimGraphBuilder.FloatComparison.NearEqual, against, 0.001f ) ),
		2 => builder.Compare( value, AnimGraphBuilder.FloatComparison.GreaterThan, against ),
		3 => builder.Compare( value, AnimGraphBuilder.FloatComparison.GreaterThanEqual, against ),
		4 => builder.Compare( value, AnimGraphBuilder.FloatComparison.LessThan, against ),
		5 => builder.Compare( value, AnimGraphBuilder.FloatComparison.LessThanEqual, against ),
		_ => null,
	};

	private static AnimGraphBuilder.StateMachineBuilder.TimeComparison Timing( int op ) => op switch
	{
		2 => AnimGraphBuilder.StateMachineBuilder.TimeComparison.GreaterThan,
		3 => AnimGraphBuilder.StateMachineBuilder.TimeComparison.GreaterThanEqual,
		4 => AnimGraphBuilder.StateMachineBuilder.TimeComparison.LessThan,
		5 => AnimGraphBuilder.StateMachineBuilder.TimeComparison.LessThanEqual,
		_ => AnimGraphBuilder.StateMachineBuilder.TimeComparison.GreaterThanEqual,
	};

	//-- what it could not do -------------------------------------------------

	private void Say( string resource, PortReport report )
	{
		report.Notes.Add( $"{resource}: {states} state(s), {transitions} transition(s) and {clips} clip(s) " +
			$"came across from their graph, driven by the {parameters.Count} parameter(s) the game already sets." );

		if ( masks.Count > 0 )
			report.Notes.Add( $"{resource}: {masks.Count} bone mask(s) came over with their weights." );

		if ( drawn > 0 )
		{
			report.Notes.Add( $"{resource}: the page carries {drawn} node(s), {reached} of them reachable " +
				"from its output - a graph plays what its output reaches and nothing else." );
		}

		//A layer whose mask went missing applies to nothing, which reads on screen as the layer not being there at all.
		if ( missing > 0 )
		{
			report.Skipped.Add( $"{resource}: {missing} layer(s) name a bone mask the model does not have - " +
				string.Join( ", ", unnamed ) + ". Those layers reach every bone until the model declares them." );
		}

		if ( passed.Count == 0 ) return;

		var left = string.Join( ", ", passed.OrderByDescending( x => x.Value ).Select( x => $"{Plain( x.Key )} ({x.Value})" ) );

		report.Notes.Add( $"{resource}: what has no counterpart here was passed through - {left}. " +
			"The pose still arrives; what those nodes did to it does not." );

		log.Info( $"{resource}: {states} states, {transitions} transitions, {passed.Count} kind(s) passed through" );
	}

	//Their class name, as something to read in a report.
	private static string Plain( string name )
	{
		var trimmed = name.StartsWith( "C" ) ? name[1..] : name;

		trimmed = trimmed.Replace( "AnimNode", "" ).Replace( "AnimCondition", "" ).Replace( "Condition", "" );

		var words = new System.Text.StringBuilder();

		for ( var i = 0; i < trimmed.Length; i++ )
		{
			if ( i > 0 && char.IsUpper( trimmed[i] ) ) words.Append( ' ' );

			words.Append( i == 0 ? trimmed[i] : char.ToLowerInvariant( trimmed[i] ) );
		}

		return words.ToString();
	}
}
