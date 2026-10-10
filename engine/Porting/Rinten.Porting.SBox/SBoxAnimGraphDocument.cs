using System.Globalization;
using System.Text.Json.Nodes;

namespace Rinten.Porting.SBox;

//Writes their anim graph as an editable document: one node each, same positions and wires
//Nodes without a counterpart pass the pose through and are reported by name
internal sealed class SBoxAnimGraphDocument
{
	//What their files write where a node id would go and there is none.
	private const long None = 4294967295;

	/// <summary>The document, as text, or null where their graph has no root to start from.</summary>
	public static string Write( SBoxAnimGraph read, string model,
		IReadOnlyDictionary<string, IReadOnlyList<(string Bone, float Weight)>> masks,
		out Dictionary<string, int> passed, out int drawn, out int reached )
	{
		var document = new SBoxAnimGraphDocument( read, model, masks );

		var text = document.Build();

		passed = document.passed;
		drawn = document.drawn;
		reached = document.reached;

		return text;
	}

	//Nodes placed vs nodes the output reaches; a low ratio means the graph is cut off
	private int drawn;
	private int reached;

	//-- what was read --------------------------------------------------------

	private readonly string model;
	private readonly IReadOnlyDictionary<string, IReadOnlyList<(string Bone, float Weight)>> masks;

	//Their graph as read. Held rather than copied out because their ids are only unique inside the manager
	//they were drawn in, and the reader is what knows which manager that was - see SBoxAnimGraph.Find.
	private readonly SBoxAnimGraph read;

	private readonly IReadOnlyDictionary<long, string> tags;

	//Their parameter list, as their file wrote it.
	private readonly IReadOnlyList<CoreImportedKeyValues3> declared;

	//Their parameters, under the identity ours gives them.
	private readonly Dictionary<long, Declared> parameters = new();

	//What each of their nodes became, and which output of it to take. Under the manager a node was
	//drawn in as well as its id, because their ids repeat across their groups - see SBoxAnimGraph.Find.
	private readonly Dictionary<(int Scope, long Id), Wire> made = new();

	//Nodes with no counterpart: what to read instead of them.
	private readonly Dictionary<(int Scope, long Id), Where> through = new();

	//Pose input box for nodes that became a chain of ours; output comes from the chain's end
	private readonly Dictionary<(int Scope, long Id), string> heads = new();

	//Which manager the node being written was drawn in, so the wires read off it are read in the same one.
	private int scope;

	//One end of a wire of theirs: a node, an output, and where both are read.
	private readonly record struct Where( long Node, long Output, int Scope )
	{
		public bool IsValid => Node != None;
	}

	private readonly Dictionary<string, int> passed = new();

	//The root page, and every node object on it by identifier.
	private readonly Page page = new();

	//Where the next node this invents goes, so two do not land on each other.
	private float invented;

	//Nothing is read out of their file here. Which of their files a graph came from, where the nodes were kept in it and what their classes are called
	//in that version of their format is one problem, solved once, in the thing that reads it - and read twice by two writers is read two different ways.
	private SBoxAnimGraphDocument( SBoxAnimGraph read, string model,
		IReadOnlyDictionary<string, IReadOnlyList<(string Bone, float Weight)>> masks )
	{
		this.model = model ?? "";
		this.masks = masks ?? new Dictionary<string, IReadOnlyList<(string, float)>>();

		this.read = read;
		tags = read.TagNames;
		declared = read.Declared;
	}

	//One of their parameters, as ours holds it.
	private readonly record struct Declared( string Identifier, string Name, string Kind,
		bool IsLength, IReadOnlyList<string> Options );

	//A connection: which node on the page, and which of its outputs.
	private readonly record struct Wire( string Identifier, string Output )
	{
		public bool IsValid => !string.IsNullOrEmpty( Identifier );
	}

	//-- the page -------------------------------------------------------------

	//A graph document's nodes, which is all a page is: the objects, and a way back to one that has already been written so it can be wired afterwards.
	private sealed class Page
	{
		public readonly JsonArray Nodes = new();

		private readonly Dictionary<string, JsonObject> objects = new();

		public string Add( string type, Vector2 at, JsonObject fields = null )
		{
			var identifier = Guid.NewGuid().ToString();

			var node = fields ?? new JsonObject();

			node["_class"] = type;
			node["Identifier"] = identifier;
			node["Position"] = Text( at );

			Nodes.Add( node );
			objects[identifier] = node;

			return identifier;
		}

		public JsonObject this[string identifier]
			=> identifier is not null && objects.TryGetValue( identifier, out var found ) ? found : null;
	}

	//What one of their nodes is, under the name the reader beside this settled on - their formats have called the same node three things.
	private static string Class( CoreImportedKeyValues3 node ) => SBoxAnimGraph.ClassName( node );

	//A position the way this engine's vectors are written down.
	private static string Text( Vector2 at )
		=> string.Create( CultureInfo.InvariantCulture, $"{at.x:G9},{at.y:G9}" );

	//A connection, as a node's input field holds one.
	private static JsonObject Link( Wire wire )
		=> wire.IsValid
			? new JsonObject { ["Identifier"] = wire.Identifier, ["Output"] = wire.Output }
			: null;

	//Write a connection onto a node, or leave the field off where there is nothing to connect. Off rather than written as nothing: an input is a
	//value type over there, and a field written null fails to read at all rather than reading as unconnected - which loses the whole node it was on.
	private static void Set( JsonObject node, string field, Wire wire )
	{
		if ( node is null || !wire.IsValid ) return;

		node[field] = Link( wire );
	}

	private void Connect( string identifier, string input, Wire wire )
	{
		if ( page[identifier] is not { } node || !wire.IsValid ) return;

		node[input] = Link( wire );
	}

	//-- reading their file ---------------------------------------------------

	private static long Id( CoreImportedKeyValues3 node )
		=> node is null ? None : (long)( node["m_id"]?.AsNumber() ?? node.AsNumber( None ) );

	private Where Wired( CoreImportedKeyValues3 connection ) => Wired( connection, scope );

	private static Where Wired( CoreImportedKeyValues3 connection, int scope )
		=> new( Id( connection?["m_nodeID"] ), Id( connection?["m_outputID"] ), scope );

	//Scale for their node positions; our boxes are bigger, so 1.5x on both axes avoids overlap
	private const float Spread = 1.5f;

	//One of their positions, at the distances ours wants.
	private static Vector2 Spaced( CoreImportedKeyValues3 at )
	{
		return new Vector2( at?[0]?.AsFloat() ?? 0.0f, at?[1]?.AsFloat() ?? 0.0f ) * Spread;
	}

	private static Vector2 At( CoreImportedKeyValues3 node ) => Spaced( node?["m_vecPosition"] );

	//-- the document ---------------------------------------------------------

	private string Build()
	{
		Declare();

		//Every node of theirs, in the order their file lists them, so a graph ported twice comes out the same both times.
		var ordered = read.Every.OrderBy( x => x.Scope ).ThenBy( x => x.Id ).ToList();

		foreach ( var (found, id, node) in ordered )
		{
			scope = found;
			Create( id, node );
		}

		foreach ( var (found, id, node) in ordered )
		{
			scope = found;
			Fill( id, node );
		}

		scope = 0;

		if ( !ordered.Any( x => Class( x.Node ) == "CRootAnimNode" ) ) return null;

		var document = new JsonObject
		{
			["Version"] = 1,
			["Model"] = model,
			["Skeleton"] = model,
			["parameters"] = Parameters(),
			["virtualParameters"] = new JsonArray(),
			["nodes"] = page.Nodes,
		};

		Count( document );

		return document.ToJsonString( new System.Text.Json.JsonSerializerOptions { WriteIndented = true } );
	}

	//-- parameters -----------------------------------------------------------

	//Every value the game sets by name, declared before anything reads one.
	private void Declare()
	{
		foreach ( var found in declared )
		{
			var id = Id( found["m_id"] );
			if ( id == None ) continue;

			var name = found.String( "m_name" ) ?? "";

			var kind = Class( found ) switch
			{
				"CFloatAnimParameter" => "Float",
				"CBoolAnimParameter" => "Bool",
				"CEnumAnimParameter" => "ID",
				"CIntAnimParameter" => "Float",
				"CVectorAnimParameter" => "Vector",

				//A rotation is not a value this graph format carries. Declared as a vector so the name still exists and setting it is not an error.
				"CQuaternionAnimParameter" => "Vector",

				_ => null,
			};

			if ( kind is null ) continue;

			var options = found.Array( "m_enumOptions" ).Select( x => x.AsString() ?? "" ).ToList();

			parameters[id] = new Declared( Guid.NewGuid().ToString(), name, kind,
				SBoxAnimGraph.IsLength( name ), options );
		}
	}

	private JsonArray Parameters()
	{
		var written = new JsonArray();

		foreach ( var found in declared )
		{
			var id = Id( found["m_id"] );
			if ( !parameters.TryGetValue( id, out var wanted ) ) continue;

			var scale = wanted.IsLength ? PortUnits.SBoxUnit : 1.0f;

			var parameter = new JsonObject
			{
				["Identifier"] = wanted.Identifier,
				["Type"] = wanted.Kind,
				["Name"] = wanted.Name,
				["Group"] = "",

				//Whether it goes back to its default once the graph has read it. Every kind of theirs declares this, and it is what
				//tells a parameter meaning a thing that happened - fired, jumped, deployed - from one meaning a thing that is so.
				["AutoReset"] = found.Bool( "m_bAutoReset" ),
			};

			switch ( Class( found ) )
			{
				case "CFloatAnimParameter":
					parameter["Min"] = found.Float( "m_fMinValue" ) * scale;
					parameter["Max"] = found.Float( "m_fMaxValue" ) * scale;
					parameter["DefaultFloat"] = found.Float( "m_fDefaultValue" ) * scale;
					break;

				case "CIntAnimParameter":
					parameter["Min"] = found.Int( "m_minValue" );
					parameter["Max"] = found.Int( "m_maxValue" );
					parameter["DefaultFloat"] = found.Int( "m_defaultValue" );
					break;

				case "CBoolAnimParameter":
					parameter["DefaultBool"] = found.Bool( "m_bDefaultValue" );
					break;

				case "CEnumAnimParameter":
				{
					var options = new JsonArray();
					foreach ( var option in wanted.Options ) options.Add( option );

					parameter["ExpectedValues"] = options;

					var chosen = found.Int( "m_defaultValue" );
					if ( chosen >= 0 && chosen < wanted.Options.Count )
						parameter["DefaultID"] = wanted.Options[chosen];

					break;
				}
			}

			written.Add( parameter );
		}

		return written;
	}

	//Parameter read node attached to the plug it feeds; finds the .enabled flag for an IK place
	private long Flag( long placed )
	{
		if ( !parameters.TryGetValue( placed, out var found ) ) return None;

		var name = found.Name ?? "";
		if ( !name.EndsWith( ".position", System.StringComparison.OrdinalIgnoreCase ) ) return None;

		var flag = name[..^".position".Length] + ".enabled";

		foreach ( var (id, parameter) in parameters )
		{
			if ( string.Equals( parameter.Name, flag, System.StringComparison.OrdinalIgnoreCase ) ) return id;
		}

		return None;
	}

	private Wire Value( Page onto, long parameterId, Vector2 at, CoreImportedKeyValues3 damping = null )
	{
		if ( !parameters.TryGetValue( parameterId, out var wanted ) ) return default;

		var type = wanted.Kind switch
		{
			"Bool" => "BoolParameterNode",
			"ID" => "IDParameterNode",
			"Vector" => "VectorParameterNode",
			_ => "FloatParameterNode",
		};

		//A spring between the read and its reader is a node of its own, so the read rides on the spring and the spring rides on whatever wanted it.
		var damped = wanted.Kind is not ( "Bool" or "ID" or "Vector" )
			&& Springs( damping );

		var identifier = onto.Add( type, at, new JsonObject
		{
			["ParameterIdentifier"] = wanted.Identifier,
			["ParameterName"] = wanted.Name,
			["Attached"] = true,
		} );

		var read = new Wire( identifier, "Value" );

		return damped ? Damped( onto, read, damping, at + new Vector2( 176.0f, 0.0f ) ) : read;
	}

	//Whether their damping is one that actually damps.
	private static bool Springs( CoreImportedKeyValues3 damping )
	{
		if ( damping is null ) return false;

		var how = damping.String( "m_speedFunction" ) ?? "NoDamping";

		return !how.Equals( "NoDamping", StringComparison.OrdinalIgnoreCase );
	}

	//A note their author left on the page, kept as one of ours. Sized as well as placed, and by the same factor: a comment is drawn around
	//the nodes it is about, so a box left at its old size after everything inside it has been spread apart no longer contains any of them.
	private void Comment( CoreImportedKeyValues3 node, Vector2 at )
	{
		var size = node["m_size"];

		var width = MathF.Max( ( size?[0]?.AsFloat() ?? 140.0f ) * Spread, 32.0f );
		var height = MathF.Max( ( size?[1]?.AsFloat() ?? 32.0f ) * Spread, 32.0f );

		//Their title, and the body underneath it. An empty title with a body is a note rather
		//than a heading, so the body is promoted rather than leaving a box labelled nothing.
		var title = ( node.String( "m_sName" ) ?? "" ).Trim();
		var body = ( node.String( "m_commentText" ) ?? "" ).Trim();

		if ( title.Length == 0 )
		{
			title = body;
			body = "";
		}

		if ( title.Length == 0 && body.Length == 0 ) return;

		page.Add( "CommentNode", at, new JsonObject
		{
			["Title"] = title,
			["Description"] = body,
			["Size"] = Text( new Vector2( width, height ) ),
			["Color"] = CommentColour( node["m_color"] ),
		} );
	}

	//Their comment colour as the nearest of the seven ours offers. Theirs is a free colour and ours is a list, so this is a nearest match by
	//hue. Black is what their editor leaves on a comment nobody coloured, and it means "no colour" rather than black - so it takes our default.
	private static string CommentColour( CoreImportedKeyValues3 colour )
	{
		var red = colour?[0]?.AsFloat() ?? 0.0f;
		var green = colour?[1]?.AsFloat() ?? 0.0f;
		var blue = colour?[2]?.AsFloat() ?? 0.0f;

		var brightest = MathF.Max( red, MathF.Max( green, blue ) );
		var dimmest = MathF.Min( red, MathF.Min( green, blue ) );

		//Nothing set, or a grey - neither of which names a colour.
		if ( brightest < 24.0f ) return "Green";
		if ( brightest - dimmest < 24.0f ) return "White";

		var hue = Hue( red, green, blue, brightest, brightest - dimmest );

		return hue switch
		{
			< 20.0f or >= 330.0f => "Red",
			< 45.0f => "Orange",
			< 70.0f => "Yellow",
			< 170.0f => "Green",
			< 260.0f => "Blue",
			_ => "Purple",
		};
	}

	//Where a colour sits on the wheel, in degrees.
	private static float Hue( float red, float green, float blue, float brightest, float spread )
	{
		var hue = brightest == red ? ( green - blue ) / spread
			: brightest == green ? 2.0f + ( ( blue - red ) / spread )
			: 4.0f + ( ( red - green ) / spread );

		hue *= 60.0f;

		return hue < 0.0f ? hue + 360.0f : hue;
	}

	//Their damping as our spring: freq = sqrt(tension), ratio = coefficient / (2 sqrt(tension))
	private static Wire Damped( Page onto, Wire value, CoreImportedKeyValues3 damping, Vector2 at )
	{
		if ( !value.IsValid || !Springs( damping ) ) return value;

		var tension = MathF.Max( damping.Float( "m_fTension", 100.0f ), 0.0001f );
		var coefficient = MathF.Max( damping.Float( "m_fDamping", 5.0f ), 0.0f );

		var root = MathF.Sqrt( tension );

		var spring = onto.Add( "FloatSpringNode", at, new JsonObject
		{
			["Hertz"] = root / ( 2.0f * MathF.PI ),
			["DampingRatio"] = Math.Clamp( coefficient / ( 2.0f * root ), 0.0f, 4.0f ),
			["Attached"] = true,
		} );

		Set( onto[spring], "Value", value );

		return new Wire( spring, "Result" );
	}

	private float Scaled( long parameterId, float value )
		=> parameters.TryGetValue( parameterId, out var found ) && found.IsLength
			? value * PortUnits.SBoxUnit
			: value;

	//-- their nodes, as ours -------------------------------------------------

	//The node one of theirs becomes, made but not yet wired - so that anything pointing at it has something to point at whichever order they come in.
	private void Create( long id, CoreImportedKeyValues3 node )
	{
		var at = At( node );

		switch ( Class( node ) )
		{
			case "CRootAnimNode":
				made[(scope, id)] = new Wire( page.Add( "PoseResultNode", at ), "" );
				return;

			//Comments have no wires; they are just placed on the page
			case "CCommentAnimNode":
				Comment( node, at );
				return;

			case "CSequenceAnimNode":
				made[(scope, id)] = Result( page.Add( "AnimationClipNode", at, new JsonObject
				{
					["Loop"] = node.Bool( "m_bLoop" ),
					["DefaultVariationData"] = new JsonObject
					{
						["AnimationName"] = node.String( "m_sequenceName" ) ?? "",
						["SpeedMultiplier"] = node.Float( "m_playbackSpeed", 1.0f ),
					},
				} ) );
				return;

			case "CSingleFrameAnimNode":
				made[(scope, id)] = Result( page.Add( "AnimationPoseNode", at, new JsonObject
				{
					["FixedTime"] = node.Int( "m_nFrameIndex" ),
					["UseFramesAsInput"] = true,
					["DefaultVariationData"] = new JsonObject
					{
						["AnimationName"] = node.String( "m_sequenceName" ) ?? "",
					},
				} ) );
				return;

			//Pose difference, used to build additive layers
			case "CSubtractAnimNode":
				made[(scope, id)] = Result( page.Add( "PoseDifferenceNode", at ) );
				return;

			case "CStateMachineAnimNode":
				made[(scope, id)] = Result( page.Add( "StateMachineNode", at ) );
				return;

			case "CSelectorAnimNode":
				made[(scope, id)] = Selector( node, at );
				return;

			case "CBlendAnimNode":
				made[(scope, id)] = Result( page.Add( "Blend1DNode", at, new JsonObject
				{
					["Loop"] = node.Bool( "m_bLoop" ),

					//Theirs says on the node whether the cycles it blends are played as one stride. Forty-one of
					//the citizen's say yes, and read as no a walk blended out of an idle catches once a cycle.
					["Synchronized"] = node.Bool( "m_bSyncCycles" ),
				} ) );
				return;

			case "CBlend2DAnimNode":
				made[(scope, id)] = Result( page.Add( "Blend2DNode", at, new JsonObject
				{
					["Loop"] = node.Bool( "m_bLoop" ),

					//Their 2D blends always sync clips, or strides of different lengths drift apart
					["Synchronized"] = true,
				} ) );
				return;

			case "CAddAnimNode":
			case "CBoneMaskAnimNode":
			case "CAimMatrixAnimNode":
				made[(scope, id)] = Result( page.Add( "LayerBlendNode", at, new JsonObject
				{
					//Their layers move the root like any bone; holding it to the base breaks their adds
					["OnlySampleBaseRootMotion"] = false,
				} ) );
				return;

			case "CSpeedScaleAnimNode":
				made[(scope, id)] = Result( page.Add( "SpeedScaleNode", at ) );
				return;

			//Random choice; theirs crossfades and ours cuts, but all options stay reachable
			case "CChoiceAnimNode":
				made[(scope, id)] = Result( page.Add( "PoseVariationNode", at ) );
				return;

			case "CCycleControlAnimNode":
				made[(scope, id)] = Result( page.Add( "CycleControlNode", at ) );
				return;

			case "CHitReactAnimNode":
				made[(scope, id)] = Result( page.Add( "HitReactNode", at, new JsonObject
				{
					["BoneCount"] = node.Int( "m_nEffectedBoneCount", 3 ),
					["MinImpactForce"] = node.Float( "m_flMinImpactForce", 50.0f ),
					["MaxImpactForce"] = node.Float( "m_flMaxImpactForce", 250.0f ),
					["SpringStrength"] = node.Float( "m_flSpringStrength", 10.0f ),
					["CounterRotationScale"] = node.Float( "m_flCounterRotationScale", 0.6f ),
					["PropagationScale"] = node.Float( "m_flPropagationScale", 1.0f ),
					["MinDelayBetweenHits"] = node.Float( "m_flMinDelayBetweenHits" ),
				} ) );
				return;

			//Both of these can come to nothing - a solve that names no limb, a follow with nothing to pin. Left to the default below when
			//they do, which passes the pose along and owns up to the loss, rather than written onto the page as a box wired to nothing.
			case "CSolveIKChainAnimNode":
				if ( Solve( id, node, at ) is { IsValid: true } solved ) { made[(scope, id)] = solved; return; }
				goto default;

			case "CFollowAttachmentAnimNode":
				if ( Following( node, at ) is { IsValid: true } pinned ) { made[(scope, id)] = pinned; return; }
				goto default;

			case "CTwoBoneIKAnimNode":
				made[(scope, id)] = Result( page.Add( "TwoBoneIKNode", at ) );
				return;

			//Code-driven pose only if nothing is wired in; otherwise pass the input through
			case "CDirectPlaybackAnimNode":
			{
				var fed = Fed( node );

				if ( fed.IsValid )
				{
					through[(scope, id)] = fed;
					return;
				}

				made[(scope, id)] = Result( page.Add( "ExternalPoseNode", at, new JsonObject
				{
					["SlotName"] = "direct_playback",
				} ) );
				return;
			}

			case "CLookAtAnimNode":
				made[(scope, id)] = Result( page.Add( "LookAtNode", at, new JsonObject
				{
					["Chain"] = node.String( "m_lookatChainName" ) ?? "",

					//Attachment that gets aimed, usually not on the chain; without it the chain aims wrong
					["Attachment"] = node.String( "m_attachmentName" ) ?? "",

					//Theirs limits how far round and how far up on their own, and so does ours. Taking the tighter of the two for both is what a spine cannot
					//survive: forty-five degrees of turn and five of lean becomes five of each, and a body that should follow the camera round stands still instead.
					["YawLimit"] = node.Float( "m_flYawLimit", 180.0f ),
					["PitchLimit"] = node.Float( "m_flPitchLimit", 180.0f ),
				} ) );
				return;

			//Groups aren't placed as nodes; wires are resolved through them by Landing
			case "CGroupAnimNode":
			case "CSubGraphAnimNode":
			case "CGroupOutputAnimNode":
			case "CGroupInputAnimNode":
				return;

			//Everything else keeps whatever was feeding it, so the pose still arrives - it just arrives without what this node did to it.
			default:
				Count( Class( node ) is { Length: > 0 } named ? named : "unknown" );
				through[(scope, id)] = Fed( node );
				return;
		}
	}

	private static Wire Result( string identifier ) => new( identifier, "Result" );

	//Counts the page, and how much of it the graph output reaches - see drawn.
	private void Count( JsonObject document )
	{
		var nodes = new Dictionary<string, JsonObject>();
		var wires = new Dictionary<string, List<string>>();

		Gather( document, nodes, wires );

		drawn = nodes.Count;

		var start = document["nodes"]?.AsArray()
			.OfType<JsonObject>()
			.FirstOrDefault( node => (string)node["_class"] == "PoseResultNode" )?["Identifier"]?.GetValue<string>();

		if ( start is null ) return;

		var seen = new HashSet<string> { start };
		var waiting = new Queue<string>();
		waiting.Enqueue( start );

		while ( waiting.Count > 0 )
		{
			if ( !wires.TryGetValue( waiting.Dequeue(), out var onward ) ) continue;

			foreach ( var next in onward )
			{
				if ( nodes.ContainsKey( next ) && seen.Add( next ) ) waiting.Enqueue( next );
			}
		}

		reached = seen.Count;
	}

	//Every node on the page by identifier, and what each one points at.
	private static void Gather( JsonNode at, Dictionary<string, JsonObject> nodes,
		Dictionary<string, List<string>> wires, string owner = null )
	{
		switch ( at )
		{
			case JsonObject o:
			{
				var identifier = o["Identifier"]?.GetValue<string>();

				//A connection is an identifier and which output of it to take, and nothing else. A node has a class as well.
				if ( identifier is not null && o["_class"] is null && o.Count <= 2 )
				{
					if ( owner is not null )
					{
						if ( !wires.TryGetValue( owner, out var list ) ) wires[owner] = list = new List<string>();
						list.Add( identifier );
					}

					return;
				}

				if ( identifier is not null && o["_class"] is not null )
				{
					nodes[identifier] = o;
					owner = identifier;
				}

				foreach ( var (_, value) in o )
				{
					if ( value is not null ) Gather( value, nodes, wires, owner );
				}

				return;
			}

			case JsonArray a:
			{
				foreach ( var value in a )
				{
					if ( value is not null ) Gather( value, nodes, wires, owner );
				}

				return;
			}
		}
	}

	//What a node with no counterpart was being fed by.
	private Where Fed( CoreImportedKeyValues3 node )
	{
		foreach ( var key in new[]
		{
			"m_inputConnection", "m_baseInput", "m_baseInputConnection", "m_inputConnection1",
		} )
		{
			if ( node[key] is null ) continue;

			var wired = Wired( node[key] );
			if ( wired.IsValid ) return wired;
		}

		//A node that chooses between several and is not one of ours keeps the first of them, which is the one their editor draws at the top.
		foreach ( var child in node.Array( "m_children" ) )
		{
			var wired = Wired( child["m_inputConnection"] ?? child );
			if ( wired.IsValid ) return wired;
		}

		return default;
	}

	//What a connection of theirs points at here, following the nodes that were passed through rather than built.
	private Wire Resolve( Where where )
	{
		var asked = where;
		var seen = new HashSet<(int, long)>();

		while ( true )
		{
			where = Landing( where );

			if ( !where.IsValid || !seen.Add( (where.Scope, where.Node) ) ) { Lost( asked, where, "landing" ); return default; }

			if ( made.TryGetValue( (where.Scope, where.Node), out var found ) ) return found;
			if ( !through.TryGetValue( (where.Scope, where.Node), out var next ) ) { Lost( asked, where, "unmade" ); return default; }

			where = next;
		}
	}

	private static readonly bool trace = System.Environment.GetEnvironmentVariable( "RINTEN_AG_TRACE" ) == "1";

	private void Lost( Where asked, Where at, string why )
	{
		if ( !trace ) return;

		var from = read.Find( asked.Node, asked.Scope );
		var landed = at.IsValid ? read.Find( at.Node, at.Scope ) : null;

		var elsewhere = from is null
			? string.Join( ",", read.ScopesWith( asked.Node ) )
			: "";

		System.Console.WriteLine( $"LOST {why} asked={asked.Node}/{asked.Scope} ({Class( from ) ?? "-"} {from?.String( "m_sName" )}) " +
			$"landed={at.Node}/{at.Scope} ({Class( landed ) ?? "-"} {landed?.String( "m_sName" )})" +
			( elsewhere.Length > 0 ? $"  but that id lives in manager(s) {elsewhere}" : from is null ? "  and no manager has it" : "" ) );
	}

	//Resolves a wire through groups to the real source node via output proxies and input maps
	private Where Landing( Where where )
	{
		var seen = new HashSet<(int, long)>();

		while ( where.IsValid && seen.Add( (where.Scope, where.Node) ) )
		{
			if ( read.Find( where.Node, where.Scope ) is not { } node ) return default;

			switch ( Class( node ) )
			{
				case "CGroupAnimNode":
				case "CSubGraphAnimNode":
				{
					var inside = read.Holds( node );
					var outputs = Id( node["m_outputNodeID"] );

					if ( inside >= 0 && outputs != None )
					{
						where = new Where( outputs, where.Output, inside );
						continue;
					}

					//A group whose contents this does not have - a subgraph in a file nobody installed. What it was handed still goes
					//on through it, which is the pose arriving without whatever the group did to it, rather than the wire ending here.
					var handed = node.Array( "m_inputConnectionMap" );
					if ( handed.Count == 0 ) return default;

					where = Wired( Picked( handed, "key", where.Output )["value"], where.Scope );
					continue;
				}

				case "CGroupOutputAnimNode":
				{
					var items = node.Array( "m_proxyItems" );
					if ( items.Count == 0 ) return default;

					where = Wired( Picked( items, "m_outputID", where.Output )["m_inputConnection"], where.Scope );
					continue;
				}

				case "CGroupInputAnimNode":
				{
					if ( read.GroupOf( where.Scope ) is not { } group ) return default;

					var map = group.Array( "m_inputConnectionMap" );
					if ( map.Count == 0 ) return default;

					where = Wired( Picked( map, "key", where.Output )["value"], read.LivesIn( group ) );
					continue;
				}

				default:
					return where;
			}
		}

		return default;
	}

	//The entry of a list whose id matches, or the first one where nothing was asked for -
	//which is what their editor does with a wire drawn before somebody added a second output.
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

	private Wire Resolve( CoreImportedKeyValues3 connection ) => Resolve( Wired( connection ) );

	private void Count( string name )
		=> passed[name] = passed.GetValueOrDefault( name ) + 1;

	//-- wiring ---------------------------------------------------------------

	private void Fill( long id, CoreImportedKeyValues3 node )
	{
		if ( !made.TryGetValue( (scope, id), out var mine ) ) return;

		switch ( Class( node ) )
		{
			case "CRootAnimNode":
				Connect( mine.Identifier, "Pose", Resolve( node["m_inputConnection"] ) );
				return;

			case "CStateMachineAnimNode":
				Machine( mine.Identifier, node );
				return;

			case "CSubtractAnimNode":
				Connect( mine.Identifier, "Pose",
					Resolve( node["m_baseInputConnection"] ?? node["m_baseInput"] ) );

				Connect( mine.Identifier, "Base",
					Resolve( node["m_subtractInputConnection"] ?? node["m_subtractInput"] ) );

				return;

			case "CSelectorAnimNode":
				FillSelector( mine.Identifier, node );
				return;

			case "CBlendAnimNode":
				FillBlend( mine.Identifier, node );
				return;

			case "CBlend2DAnimNode":
				FillBlend2D( mine.Identifier, node );
				return;

			case "CAddAnimNode":
				FillLayer( mine.Identifier, node, "m_baseInput", "m_additiveInput", additive: true );
				return;

			case "CBoneMaskAnimNode":
				FillLayer( mine.Identifier, node, "m_inputConnection1", "m_inputConnection2", additive: false );
				return;

			case "CAimMatrixAnimNode":
				FillAimMatrix( mine.Identifier, node );
				return;

			case "CSpeedScaleAnimNode":
				Connect( mine.Identifier, "Source", Resolve( node["m_inputConnection"] ) );
				Connect( mine.Identifier, "Speed", Value( page, Id( node["m_param"] ), Beside( node, 0 ), node["m_damping"] ) );
				return;

			case "CLookAtAnimNode":
				Connect( mine.Identifier, "Input", Resolve( node["m_inputConnection"] ) );
				Connect( mine.Identifier, "Direction", Value( page, Id( node["m_param"] ), Beside( node, 0 ), node["m_damping"] ) );
				Connect( mine.Identifier, "Weight", Value( page, Id( node["m_weightParam"] ), Beside( node, 1 ), node["m_weightDamping"] ) );
				return;

			case "CChoiceAnimNode":
				FillChoice( mine.Identifier, node );
				return;

			case "CCycleControlAnimNode":
				Connect( mine.Identifier, "Pose", Resolve( node["m_inputConnection"] ) );
				Connect( mine.Identifier, "Cycle",
					Value( page, Id( node["m_param"] ), Beside( node, 0 ), node["m_damping"] ) );
				return;

			case "CHitReactAnimNode":
				Connect( mine.Identifier, "Pose", Resolve( node["m_inputConnection"] ) );
				Connect( mine.Identifier, "Trigger", Value( page, Id( node["m_triggerParam"] ), Beside( node, 0 ) ) );
				Connect( mine.Identifier, "HitBone", Value( page, Id( node["m_hitBoneParam"] ), Beside( node, 1 ) ) );
				Connect( mine.Identifier, "HitOffset", Value( page, Id( node["m_hitOffsetParam"] ), Beside( node, 2 ) ) );
				Connect( mine.Identifier, "HitDirection", Value( page, Id( node["m_hitDirectionParam"] ), Beside( node, 3 ) ) );
				Connect( mine.Identifier, "HitStrength", Value( page, Id( node["m_hitStrengthParam"] ), Beside( node, 4 ) ) );
				return;

			//The front of the row, not the box the wires out of it come from - see heads.
			case "CSolveIKChainAnimNode":
			case "CFollowAttachmentAnimNode":
				Connect( heads.TryGetValue( (scope, id), out var front ) ? front : mine.Identifier,
					"Input", Resolve( node["m_inputConnection"] ) );
				return;

			case "CTwoBoneIKAnimNode":
			{
				Connect( mine.Identifier, "Input", Resolve( node["m_inputConnection"] ) );

				//Where it reaches: a bone of the model, or wherever the game has put a parameter. Their node keeps the bone name filled
				//either way, and on the citizen the leftover beside a parameter target names a hand on the nodes that solve the legs.
				if ( ( node.String( "m_targetType" ) ?? "" ).Contains( "Parameter" ) )
				{
					var placed = Id( node["m_targetParam"] );

					if ( Value( page, placed, Beside( node, 0 ) ) is { IsValid: true } where )
					{
						var target = page.Add( "PointTargetNode", Beside( node, 1 ) );

						Connect( target, "Position", where );
						Connect( mine.Identifier, "EffectorTarget", new Wire( target, "Target" ) );
					}

					if ( Flag( placed ) is var flag && flag != None && Value( page, flag, Beside( node, 2 ) ) is { IsValid: true } on )
						Connect( mine.Identifier, "Enable", on );

					if ( page[mine.Identifier] is { } manual )
					{
						manual["DefaultVariationData"] = new JsonObject
						{
							["Chain"] = node.String( "m_ikChainName" ) ?? "",
						};
					}

					return;
				}

				var bone = node.String( "m_targetBoneName" );

				if ( !string.IsNullOrWhiteSpace( bone ) )
				{
					var target = page.Add( "ConstBoneTargetNode", Beside( node, 0 ), new JsonObject
					{
						["Bone"] = bone,
					} );

					Connect( mine.Identifier, "EffectorTarget", new Wire( target, "Target" ) );
				}

				if ( page[mine.Identifier] is { } ik )
				{
					ik["DefaultVariationData"] = new JsonObject
					{
						["Chain"] = node.String( "m_ikChainName" ) ?? "",
					};
				}

				return;
			}
		}
	}

	//Where a node this invents goes: to the left of the node it serves, and each one below the last so that two never land on each other.
	private Vector2 Beside( CoreImportedKeyValues3 node, int index )
	{
		invented += 1.0f;

		return At( node ) + new Vector2( -320.0f, ( index * 120.0f ) + ( invented % 3.0f ) * 8.0f );
	}

	//-- reaching, picking, scrubbing -----------------------------------------

	//Their multi-chain IK as one solve node per chain in a row; bone targets kept, else SetIk name
	private Wire Solve( long id, CoreImportedKeyValues3 node, Vector2 at )
	{
		var chains = node.Array( "m_IkChains" );

		var made = new List<string>();
		var beside = 0;

		foreach ( var chain in chains )
		{
			var name = Chain( chain );
			if ( string.IsNullOrWhiteSpace( name ) ) continue;

			var solve = page.Add( "IkNode", at + new Vector2( made.Count * 220.0f, 0.0f ), new JsonObject
			{
				["DefaultVariationData"] = new JsonObject
				{
					["Chain"] = name,
				},
			} );

			//Their override only counts where the node says it is using one: the block is written out
			//either way, and read unconditionally it is a hand reaching for whatever was in the box last.
			var overridden = ( chain.String( "m_TargetSettingSource" ) ?? "" )
				.EndsWith( "Override", System.StringComparison.OrdinalIgnoreCase );

			var bone = overridden ? Bone( chain ) : null;

			if ( !string.IsNullOrWhiteSpace( bone ) )
			{
				var target = page.Add( "ConstBoneTargetNode", Beside( node, beside++ ), new JsonObject
				{
					["Bone"] = bone,
				} );

				Connect( solve, "Target", new Wire( target, "Target" ) );
			}
			else
			{
				var target = page.Add( "TargetParameterNode", Beside( node, beside++ ), new JsonObject
				{
					["ParameterName"] = name,
				} );

				var set = page.Add( "IsTargetSetNode", Beside( node, beside++ ) );

				Connect( set, "Target", new Wire( target, "Value" ) );
				Connect( solve, "Target", new Wire( target, "Value" ) );
				Connect( solve, "Enable", new Wire( set, "Result" ) );
			}

			if ( made.Count > 0 ) Connect( solve, "Input", Result( made[^1] ) );

			made.Add( solve );
		}

		if ( made.Count == 0 ) return default;

		heads[(scope, id)] = made[0];

		return Result( made[^1] );
	}

	//A bone of theirs pinned to an attachment, which here is a solve reaching for it.
	private Wire Following( CoreImportedKeyValues3 node, Vector2 at )
	{
		var bone = node.String( "m_boneName" );
		var attachment = node.String( "m_attachmentName" );

		//Nothing to pin, or nothing to pin it to. Passed through rather than drawn as
		//a solve with no target, which is a limb that quietly stops following anything.
		if ( string.IsNullOrWhiteSpace( bone ) || string.IsNullOrWhiteSpace( attachment ) ) return default;

		var follow = page.Add( "TwoBoneIKNode", at, new JsonObject
		{
			["DefaultVariationData"] = new JsonObject
			{
				["EffectorBone"] = bone,
			},
		} );

		var target = page.Add( "ConstBoneTargetNode", Beside( node, 0 ), new JsonObject
		{
			["Bone"] = attachment,
		} );

		Connect( follow, "EffectorTarget", new Wire( target, "Target" ) );

		return Result( follow );
	}

	//The options a choice picks between, each with how often it comes up.
	private void FillChoice( string identifier, CoreImportedKeyValues3 node )
	{
		var options = new JsonArray();

		foreach ( var child in node.Array( "m_children" ) )
		{
			var option = new JsonObject
			{
				//Theirs are fractions against each other and ours are counts, and a weight of nothing still
				//means the option can come up - their editor writes one on a child somebody never touched.
				["Weight"] = System.Math.Max( 1,
					(int)System.Math.Round( child.Float( "m_weight", 1.0f ) * 100.0f ) ),
			};

			Set( option, "Pose", Resolve( child["m_inputConnection"] ?? child ) );

			options.Add( option );
		}

		if ( page[identifier] is { } variation ) variation["Options"] = options;
	}

	//The limb one entry of their chain list names.
	private static string Chain( CoreImportedKeyValues3 chain )
	{
		foreach ( var key in new[] { "m_IkChain", "m_ikChain", "m_chainName", "m_name" } )
		{
			if ( chain?[key]?.AsString( null ) is { Length: > 0 } found ) return found;
		}

		return null;
	}

	//The bone their override names to reach for, or nothing. m_Name with the capital, which is how their bone reference
	//is written. Spelled the other way it is never found, and every chain that names a bone reads as one that names none.
	private static string Bone( CoreImportedKeyValues3 chain )
	{
		var settings = chain?["m_OverrideTargetSettings"];
		var bone = settings?["m_Bone"];

		return bone?.String( "m_Name" ) ?? bone?.String( "m_name" ) ?? bone?.AsString( null );
	}

	//-- choosing between poses -----------------------------------------------

	//Selector; node type depends on what it reads (a flag becomes a crossfading pose switch)
	private Wire Selector( CoreImportedKeyValues3 node, Vector2 at )
	{
		var source = node.String( "m_selectionSource" ) ?? "";

		if ( source.EndsWith( "Enum" ) && parameters.ContainsKey( Id( node["m_enumParamID"] ) ) )
			return Result( page.Add( "IDBasedSelectorNode", at ) );

		if ( source.EndsWith( "Int" ) && parameters.ContainsKey( Id( node["m_intParamID"] ) ) )
			return Result( page.Add( "ParameterizedSelectorNode", at ) );

		return Result( page.Add( "PoseSwitchNode", at, new JsonObject
		{
			["BlendTime"] = node.Float( "m_blendDuration", 0.2f ),
			["SyncCycles"] = node.Bool( "m_bSyncCycles", true ),
		} ) );
	}

	private void FillSelector( string identifier, CoreImportedKeyValues3 node )
	{
		var children = node.Array( "m_children" )
			.Select( child => Resolve( child["m_inputConnection"] ?? child ) )
			.ToList();

		switch ( page[identifier]?["_class"]?.GetValue<string>() )
		{
			case "IDBasedSelectorNode":
			{
				var parameterId = Id( node["m_enumParamID"] );
				if ( !parameters.TryGetValue( parameterId, out var wanted ) ) return;

				var options = new JsonArray();

				for ( var i = 0; i < children.Count; i++ )
				{
					var option = new JsonObject
					{
						["Id"] = i < wanted.Options.Count ? wanted.Options[i] : i.ToString(),
					};

					Set( option, "Pose", children[i] );

					options.Add( option );
				}

				if ( page[identifier] is { } selector )
				{
					selector["ParameterIdentifier"] = wanted.Identifier;
					selector["ParameterName"] = wanted.Name;
					selector["Options"] = options;
				}

				return;
			}

			case "ParameterizedSelectorNode":
			{
				var options = new JsonArray();

				for ( var i = 0; i < children.Count; i++ )
				{
					var option = new JsonObject { ["Label"] = $"Option {i + 1}" };

					Set( option, "Pose", children[i] );

					options.Add( option );
				}

				if ( page[identifier] is { } selector ) selector["Options"] = options;

				Connect( identifier, "Parameter", Value( page, Id( node["m_intParamID"] ), Beside( node, 0 ) ) );

				return;
			}

			default:
			{
				//A flag indexes the children: false is child 0, true is child 1
				Connect( identifier, "WhenFalse", children.ElementAtOrDefault( 0 ) );
				Connect( identifier, "WhenTrue", children.ElementAtOrDefault( 1 ) );
				Connect( identifier, "Condition",
					Value( page, Id( node["m_boolParamID"] ), Beside( node, 1 ) ) );

				return;
			}
		}
	}

	//-- blends ---------------------------------------------------------------

	private void FillBlend( string identifier, CoreImportedKeyValues3 node )
	{
		var parameterId = Id( node["m_param"] );
		var points = new JsonArray();

		foreach ( var child in node.Array( "m_children" ) )
		{
			//A point with nothing wired into it is not a point. Ours compiles an empty one to the model at rest, and a rest pose reached through
			//an additive layer is a skeleton added to itself - so the point goes rather than the blend passing through a whole character.
			var pose = Resolve( child["m_inputConnection"] );
			if ( !pose.IsValid ) continue;

			var point = new JsonObject { ["Value"] = Scaled( parameterId, child.Float( "m_blendValue" ) ) };

			Set( point, "Pose", pose );

			points.Add( point );
		}

		//A blend of one is not a blend, and ours refuses to compile as one. The point added to make one carries the same pose rather than nothing:
		//an empty point compiles to the model at rest, and the rest pose reached through an additive layer is a character built on top of itself.
		while ( points.Count is > 0 and < 2 )
		{
			var first = points[0].AsObject();
			var again = new JsonObject { ["Value"] = first["Value"].GetValue<float>() + 1.0f };

			if ( first["Pose"] is { } carried ) again["Pose"] = carried.DeepClone();

			points.Add( again );
		}

		if ( page[identifier] is { } blend ) blend["Points"] = points;

		Connect( identifier, "Parameter", Value( page, parameterId, Beside( node, 0 ), node["m_damping"] ) );
	}

	private void FillBlend2D( string identifier, CoreImportedKeyValues3 node )
	{
		var x = Id( node["m_paramX"] );
		var y = Id( node["m_paramY"] );

		var placed = new List<(float X, float Y, Wire Pose)>();
		var index = 0;

		//Theirs names a clip per corner rather than wiring one in, so each becomes a clip of ours drawn beside the blend.
		foreach ( var item in node.Array( "m_items" ) )
		{
			var clip = item.String( "m_sequenceName" );
			if ( string.IsNullOrEmpty( clip ) ) continue;

			var at = item["m_blendValue"];

			var made = page.Add( "AnimationClipNode", Beside( node, index++ ), new JsonObject
			{
				["Loop"] = node.Bool( "m_bLoop" ),
				["DefaultVariationData"] = new JsonObject
				{
					["AnimationName"] = clip,
					["SpeedMultiplier"] = 1.0f,
				},
			} );

			placed.Add( (
				Scaled( x, at?[0]?.AsFloat() ?? 0.0f ),
				Scaled( y, at?[1]?.AsFloat() ?? 0.0f ),
				Result( made ) ) );
		}

		//A 2D blend with collinear points is written as a 1D blend along the axis they vary on
		if ( !Triangulable( placed ) )
		{
			OneDimensional( identifier, node, placed, x, y, index );
			return;
		}

		var points = new JsonArray();

		foreach ( var (px, py, pose) in placed )
		{
			var point = new JsonObject { ["Position"] = Text( new Vector2( px, py ) ) };

			Set( point, "Pose", pose );

			points.Add( point );
		}

		if ( page[identifier] is { } blend ) blend["Points"] = points;

		//With the damping their node puts on it, the same as a one dimensional blend: the citizen's movement space springs move_x and move_y at
		//a tension of a hundred, and read without it the blend jumps to wherever the game last set the parameter instead of leaning into it.
		Connect( identifier, "X", Value( page, x, Beside( node, index ), node["m_damping"] ) );
		Connect( identifier, "Y", Value( page, y, Beside( node, index + 1 ), node["m_damping"] ) );
	}

	//True if the points contain a non-degenerate triangle, needed for a 2D blend
	private static bool Triangulable( List<(float X, float Y, Wire Pose)> points )
	{
		if ( points.Count < 3 ) return false;

		for ( var a = 0; a < points.Count; a++ )
		{
			for ( var b = a + 1; b < points.Count; b++ )
			{
				for ( var c = b + 1; c < points.Count; c++ )
				{
					var area = ( points[b].X - points[a].X ) * ( points[c].Y - points[a].Y )
						- ( points[b].Y - points[a].Y ) * ( points[c].X - points[a].X );

					if ( Math.Abs( area ) > 1e-6f ) return true;
				}
			}
		}

		return false;
	}

	//A two dimensional blend of theirs whose points lie along one axis, written as a blend along that axis.
	private void OneDimensional( string identifier, CoreImportedKeyValues3 node,
		List<(float X, float Y, Wire Pose)> placed, long x, long y, int index )
	{
		//Whichever axis the points move along. Neither, and it is one pose repeated - the axis does not matter and X is as good as Y.
		var across = 0.0f;
		var up = 0.0f;

		foreach ( var (px, py, _) in placed )
		{
			across = Math.Max( across, Math.Abs( px - placed[0].X ) );
			up = Math.Max( up, Math.Abs( py - placed[0].Y ) );
		}

		var vertical = up > across;

		var points = new JsonArray();
		var values = new HashSet<float>();

		foreach ( var (px, py, pose) in placed )
		{
			var value = vertical ? py : px;

			//Two points at the same value is a blend with nowhere to go, and ours refuses to
			//compile one. The second says nothing the first has not already said, so it goes.
			if ( !values.Add( value ) ) continue;

			var point = new JsonObject { ["Value"] = value };

			Set( point, "Pose", pose );

			points.Add( point );
		}

		//And a blend of one is not a blend either: the same pose at a value it can reach, so
		//whatever the parameter says the answer is that pose rather than the model at rest.
		while ( points.Count is > 0 and < 2 )
		{
			var first = points[0].AsObject();
			var again = new JsonObject { ["Value"] = first["Value"].GetValue<float>() + 1.0f };

			if ( first["Pose"] is { } pose ) again["Pose"] = pose.DeepClone();

			points.Add( again );
		}

		//The node is already on the page as a two dimensional blend, so it becomes a one dimensional one
		//where it stands rather than being made again: everything already wired to it stays wired to it.
		if ( page[identifier] is { } blend )
		{
			blend["_class"] = "Blend1DNode";
			blend["Points"] = points;
		}

		Connect( identifier, "Parameter", Value( page, vertical ? y : x, Beside( node, index ) ) );
	}

	//-- layers ---------------------------------------------------------------

	//One pose laid over another, which theirs writes as a node with two inputs and ours as a blend with a layer wired into it.
	private void FillLayer( string identifier, CoreImportedKeyValues3 node,
		string baseKey, string overKey, bool additive )
	{
		var under = Resolve( node[baseKey] );
		var over = Resolve( node[overKey] );

		//With one side missing, pass the pose through; an empty input would add a whole skeleton
		if ( !under.IsValid || !over.IsValid )
		{
			Connect( identifier, "Base", under.IsValid ? under : over );
			return;
		}

		Connect( identifier, "Base", under );

		var space = node.String( "m_blendSpace" ) ?? "";

		//Where a mask blend was authored. Their hold types are ramps up the body blended in model space,
		//and a ramp read as parent space compounds down the arm instead of landing the hand on the weapon.
		var model = additive ? node.Bool( "m_bModelSpace" ) : space.StartsWith( "BlendSpace_Model" );

		var layer = page.Add( "LocalLayerNode", Beside( node, 0 ), new JsonObject
		{
			["BlendMode"] = additive ? "Additive" : "Overlay",
			["BlendInModelSpace"] = model,
			["RotationOnly"] = !additive && space.EndsWith( "RotationOnly" ),
			["ChannelsApart"] = additive && node.Bool( "m_bApplyChannelsSeparately" ),
			["IsSynchronized"] = false,
			["IgnoreEvents"] = false,
		} );

		Connect( layer, "Input", over );

		if ( !additive )
		{
			if ( Mask( node.String( "m_weightListName" ), Beside( node, 1 ) ) is { IsValid: true } mask )
				Connect( layer, "BoneMask", mask );

			if ( node.Bool( "m_bUseBlendScale" ) )
				Connect( layer, "Weight", Value( page, Id( node["m_blendParameter"] ), Beside( node, 2 ), node["m_damping"] ) );
		}

		if ( page[identifier] is { } blend )
			blend["Layers"] = new JsonArray { Link( new Wire( layer, "Layer" ) ) };
	}

	//The mask a layer names, with the weights the model holds for it - or the name on its own where the model had none under that name.
	private Wire Mask( string name, Vector2 at )
	{
		if ( string.IsNullOrEmpty( name ) ) return default;

		//Their name for no mask at all. Naming one the model does not have would weigh the layer at nothing, which is the opposite of what it means.
		if ( name == "Everything" ) return default;

		//Their built-in empty mask, used to switch a layer off; not declared by any model
		if ( name == SBoxAnimGraph.Nothing )
		{
			return new Wire( page.Add( "BoneMaskNode", at, new JsonObject
			{
				["BoneName"] = "",
				["DefaultWeight"] = 0.0f,
			} ), "Mask" );
		}

		var weights = new JsonArray();

		if ( masks.TryGetValue( name, out var found ) )
		{
			foreach ( var (bone, weight) in found )
				weights.Add( new JsonObject { ["Bone"] = bone, ["Weight"] = weight } );
		}

		//The name on its own where the model holds no list under it. Written as a weights node with an empty list it would be a mask that reaches no
		//bone at all, which is the layer switched off rather than the layer unmasked - and a mask the model gains later would never be picked up.
		if ( weights.Count == 0 )
		{
			return new Wire( page.Add( "BoneMaskNode", at, new JsonObject
			{
				["BoneName"] = name,
			} ), "Mask" );
		}

		var identifier = page.Add( "BoneMaskWeightsNode", at, new JsonObject
		{
			["MaskName"] = name,
			["Weights"] = weights,
		} );

		return new Wire( identifier, "Mask" );
	}

	//Aim matrix as a blend space over the input: 3x3 frames bottom-to-top, left-to-right, frame 4 center
	//Range comes from the node; pole frames are added only if they don't overlap the grid
	private void FillAimMatrix( string identifier, CoreImportedKeyValues3 node )
	{
		Connect( identifier, "Base", Resolve( node["m_inputConnection"] ) );

		var clip = node.String( "m_sequenceName" );
		if ( string.IsNullOrEmpty( clip ) ) return;

		var aim = Id( node["m_param"] );
		if ( !parameters.ContainsKey( aim ) ) return;

		var step = node.Float( "m_fAngleIncrement", 90.0f );
		if ( step <= 0.0f ) step = 90.0f;

		var at = At( node );
		var points = new JsonArray();

		void Point( int frame, Vector2 aimed, Vector2 where )
		{
			var pose = page.Add( "AnimationPoseNode", at + where, new JsonObject
			{
				["FixedTime"] = frame,
				["UseFramesAsInput"] = true,
				["DefaultVariationData"] = new JsonObject { ["AnimationName"] = clip },
			} );

			var point = new JsonObject { ["Position"] = Text( aimed ) };

			Set( point, "Pose", Result( pose ) );

			points.Add( point );
		}

		for ( var frame = 0; frame < 9; frame++ )
		{
			var column = frame % 3;
			var row = frame / 3;

			//Row nought is the bottom one, so it aims down by a step, not up, and it is drawn at the bottom of the page for the same reason.
			Point( frame, new Vector2( ( 1 - column ) * step, ( row - 1 ) * step ),
				new Vector2( -640.0f + ( column * 200.0f ), 60.0f - ( row * 90.0f ) ) );
		}

		if ( step < 90.0f )
		{
			Point( 9, new Vector2( 0.0f, 90.0f ), new Vector2( -440.0f, -300.0f ) );

			if ( node.Bool( "m_bCanLookStraightDown" ) )
				Point( 10, new Vector2( 0.0f, -90.0f ), new Vector2( -440.0f, 330.0f ) );
		}

		var grid = page.Add( "Blend2DNode", at + new Vector2( -320.0f, -160.0f ), new JsonObject
		{
			["Loop"] = false,
			["Points"] = points,
		} );

		//Which way the character is being asked to aim, as two angles off the one vector their node reads.
		var wanted = Value( page, aim, at + new Vector2( -960.0f, 40.0f ) );

		if ( wanted.IsValid )
		{
			var across = page.Add( "VectorInfoNode", at + new Vector2( -800.0f, 0.0f ), new JsonObject
			{
				["Info"] = "AngleHorizontal",
			} );

			var up = page.Add( "VectorInfoNode", at + new Vector2( -800.0f, 90.0f ), new JsonObject
			{
				["Info"] = "AngleVertical",
			} );

			Connect( across, "Vector", wanted );
			Connect( up, "Vector", wanted );

			Connect( grid, "X", new Wire( across, "Result" ) );
			Connect( grid, "Y", new Wire( up, "Result" ) );
		}

		//Both additive modes (local and model space) layer over the pose; only one mode replaces it
		var mode = node.String( "m_blendMode" ) ?? "";

		var layer = page.Add( "LocalLayerNode", at + new Vector2( -160.0f, -80.0f ), new JsonObject
		{
			["BlendMode"] = mode.Contains( "Additive" )
				? mode.Contains( "ModelSpace" ) ? "ModelSpace" : "Additive"
				: "Overlay",

			["IsSynchronized"] = false,
			["IgnoreEvents"] = false,
		} );

		Connect( layer, "Input", new Wire( grid, "Result" ) );

		if ( Mask( node.String( "m_boneMaskName" ), at + new Vector2( -320.0f, 60.0f ) ) is { IsValid: true } mask )
			Connect( layer, "BoneMask", mask );

		if ( page[identifier] is { } blend )
			blend["Layers"] = new JsonArray { Link( new Wire( layer, "Layer" ) ) };
	}

	//-- state machines -------------------------------------------------------

	//State machine; each state's pose comes in on a pin, keeping the graph on one page
	private void Machine( string identifier, CoreImportedKeyValues3 node )
	{
		var states = new JsonArray();
		var conduits = new JsonArray();

		var names = new Dictionary<long, string>();
		var taken = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		var start = "";

		foreach ( var state in node.Array( "m_states" ) )
		{
			var id = Id( state["m_stateID"] );
			if ( id == None ) continue;

			var name = Unique( state.String( "m_name" ), taken );

			names[id] = name;

			if ( state.Bool( "m_bIsStartState" ) ) start = name;
		}

		foreach ( var state in node.Array( "m_states" ) )
		{
			var id = Id( state["m_stateID"] );
			if ( !names.TryGetValue( id, out var name ) ) continue;

			var pose = Resolve( state["m_inputConnection"] );

			var written = new JsonObject
			{
				["Name"] = name,
				["Type"] = pose.IsValid ? "BlendTree" : "Off",
				["Position"] = Text( Spaced( state["m_position"] ) ),
				["Graph"] = EmptyPose(),
			};

			Set( written, "Pose", pose );

			//A tag on a state is a name that is set for as long as the machine is in it, and conditions elsewhere read it. Ours has no tags but it
			//has events, and an event announced from no time onwards is announced every frame the state is current - the same thing said differently.
			var events = new JsonArray();

			foreach ( var tag in state.Array( "m_tags" ) )
			{
				if ( !tags.TryGetValue( Id( tag ), out var called ) ) continue;

				events.Add( new JsonObject
				{
					["Id"] = called,
					["Type"] = "TimeElapsed",
					["Operator"] = "GreaterThanEqual",
					["Time"] = 0.0f,
				} );
			}

			if ( events.Count > 0 ) written["TimedEvents"] = events;

			states.Add( written );
		}

		//Their transitions hang off the state they leave; ours are grouped by the pair
		//of states they run between, because that is what an arrow on the page is.
		var routes = new Dictionary<(string From, string To), List<CoreImportedKeyValues3>>();
		var order = new List<(string From, string To)>();

		foreach ( var state in node.Array( "m_states" ) )
		{
			var from = Id( state["m_stateID"] );
			if ( !names.TryGetValue( from, out var leaving ) ) continue;

			foreach ( var route in state.Array( "m_transitions" ) )
			{
				if ( route.Bool( "m_bDisabled" ) ) continue;

				if ( !names.TryGetValue( Id( route["m_destState"] ), out var arriving ) ) continue;

				var key = (leaving, arriving);

				if ( !routes.TryGetValue( key, out var written ) )
				{
					routes[key] = written = new List<CoreImportedKeyValues3>();
					order.Add( key );
				}

				written.Add( route );
			}
		}

		foreach ( var (from, to) in order )
			conduits.Add( Conduit( from, to, routes[(from, to)] ) );

		if ( page[identifier] is not { } machine ) return;

		machine["States"] = states;
		machine["Conduits"] = conduits;
		machine["DefaultState"] = start;
		machine["GlobalGraph"] = Empty( "GlobalTransitions" );
		machine["EntryGraph"] = Entry();
	}

	private static string Unique( string wanted, HashSet<string> taken )
	{
		var name = string.IsNullOrWhiteSpace( wanted ) ? "State" : wanted;

		if ( taken.Add( name ) ) return name;

		for ( var i = 2; ; i++ )
		{
			var tried = $"{name} {i}";
			if ( taken.Add( tried ) ) return tried;
		}
	}

	//An empty page of the kind a node owns.
	private static JsonObject Empty( string kind )
	{
		var graph = new JsonObject
		{
			["Version"] = 1,
			["nodes"] = new JsonArray(),
		};

		if ( kind is not null ) graph["Kind"] = kind;

		return graph;
	}

	//A state's own page, which a ported state does not use.
	private static JsonObject EmptyPose()
	{
		var graph = Empty( null );

		graph["nodes"].AsArray().Add( new JsonObject
		{
			["_class"] = "PoseResultNode",
			["Identifier"] = Guid.NewGuid().ToString(),
			["Position"] = Text( new Vector2( 400.0f, 0.0f ) ),
		} );

		return graph;
	}

	private static JsonObject Entry()
	{
		var graph = Empty( "EntryOverrides" );

		graph["nodes"].AsArray().Add( new JsonObject
		{
			["_class"] = "EntryStateOverrideConditionsNode",
			["Identifier"] = Guid.NewGuid().ToString(),
			["Position"] = Text( new Vector2( 400.0f, 0.0f ) ),
		} );

		return graph;
	}

	//One arrow between two states, and every way of taking it. A conduit is its own little page: a transition node per way of taking it, with what each
	//one waits for drawn behind it. Theirs is a list of conditions and a list is an and, so several become an And node with the conditions feeding it.
	private JsonObject Conduit( string from, string to, List<CoreImportedKeyValues3> theirs )
	{
		var graph = new Page();
		var index = 0;

		foreach ( var route in theirs )
		{
			var top = index * 260.0f;

			var transition = graph.Add( "TransitionNode", new Vector2( 320.0f, top ), new JsonObject
			{
				["TransitionName"] = theirs.Count > 1 ? $"{from} → {to} ({index + 1})" : $"{from} → {to}",
				["Duration"] = route.Float( "m_blendDuration", 0.2f ),
				["CanBeForced"] = false,
			} );

			var condition = Conditions( graph, route.Array( "m_conditions" ), new Vector2( -40.0f, top ) );

			Set( graph[transition], "Condition", condition );

			++index;
		}

		return new JsonObject
		{
			["From"] = from,
			["To"] = to,
			["Graph"] = new JsonObject
			{
				["Version"] = 1,
				["Kind"] = "TransitionConduit",
				["nodes"] = graph.Nodes,
			},
		};
	}

	//Everything a transition waits for, as one value. All of them at once: their conditions are a list and a list is an and.
	private Wire Conditions( Page graph, IReadOnlyList<CoreImportedKeyValues3> list, Vector2 at )
	{
		var all = new List<Wire>();

		for ( var i = 0; i < list.Count; i++ )
		{
			var made = Condition( graph, list[i], at + new Vector2( -320.0f, i * 130.0f ) );

			if ( made.IsValid ) all.Add( made );
		}

		if ( all.Count == 0 ) return default;
		if ( all.Count == 1 ) return all[0];

		var conditions = new JsonArray();

		foreach ( var one in all )
		{
			var input = new JsonObject();

			Set( input, "Input", one );

			conditions.Add( input );
		}

		var and = graph.Add( "AndNode", at, new JsonObject { ["Conditions"] = conditions } );

		return new Wire( and, "Result" );
	}

	private Wire Condition( Page graph, CoreImportedKeyValues3 condition, Vector2 at )
	{
		switch ( Class( condition ) )
		{
			case "CParameterAnimCondition":
			{
				var id = Id( condition["m_paramID"] );
				if ( !parameters.TryGetValue( id, out var wanted ) ) return default;

				var value = Value( graph, id, at + new Vector2( -280.0f, 0.0f ) );
				if ( !value.IsValid ) return default;

				var against = condition["m_comparisonValue"];
				var number = (float)( against?["m_data"]?.AsNumber() ?? 0.0 );

				switch ( wanted.Kind )
				{
					//A flag is read as itself rather than compared with one, and read the other way round where they compare it against false.
					case "Bool":
					{
						var set = number != 0.0f;
						if ( condition.Int( "m_comparisonOp" ) == 1 ) set = !set;

						if ( !set && graph[value.Identifier] is { } flag ) flag["Invert"] = true;

						return value;
					}

					case "ID":
					{
						var name = number >= 0 && number < wanted.Options.Count
							? wanted.Options[(int)number]
							: number.ToString( CultureInfo.InvariantCulture );

						var ids = new JsonArray { name };

						var compare = graph.Add( "IDComparisonNode", at, new JsonObject
						{
							["Comparison"] = condition.Int( "m_comparisonOp" ) == 1 ? "DoesntMatch" : "Matches",
							["Ids"] = ids,
							["Attached"] = true,
						} );

						Set( graph[compare], "Id", value );

						return new Wire( compare, "Result" );
					}

					default:
					{
						//Theirs writes "not equal" as an operator of its own. Ours has the equality and a flag beside it, which is one node rather than two.
						var compare = graph.Add( "FloatComparisonNode", at, new JsonObject
						{
							["Comparison"] = Comparison( condition.Int( "m_comparisonOp" ) ),
							["ComparisonValue"] = Scaled( id, number ),
							["Epsilon"] = 0.001f,
							["Invert"] = condition.Int( "m_comparisonOp" ) == 1,
							["Attached"] = true,
						} );

						Set( graph[compare], "Value", value );

						return new Wire( compare, "Result" );
					}
				}
			}

			case "CFinishedCondition":
			{
				//Theirs fires either as the clip ends or early enough that the blend finishes with it; ours is the same pair of answers.
				var early = ( condition.String( "m_option" ) ?? "" ).EndsWith( "OnAlmostFinished" );

				return new Wire( graph.Add( "StateCompletedConditionNode", at, new JsonObject
				{
					["AtClipEnd"] = !early,
					["Invert"] = !condition.Bool( "m_bIsFinished", true ),
					["Attached"] = true,
				} ), "Result" );
			}

			case "CTimeCondition":
				return new Wire( graph.Add( "TimeConditionNode", at, new JsonObject
				{
					["Type"] = "ElapsedTime",
					["Operator"] = Timing( condition.Int( "m_comparisonOp" ) ),
					["Value"] = condition.Float( "m_comparisonValue" ),
					["Attached"] = true,
				} ), "Result" );

			case "CTagCondition":
			{
				if ( !tags.TryGetValue( Id( condition["m_tagID"] ), out var name ) ) return default;

				var ids = new JsonArray { name };

				var wanted = condition.Int( "m_comparisonValue", 1 ) != 0;
				if ( condition.Int( "m_comparisonOp" ) == 1 ) wanted = !wanted;

				return new Wire( graph.Add( "IDEventConditionNode", at, new JsonObject
				{
					["Operator"] = "Or",
					["SearchRule"] = "SearchAll",
					["EventIds"] = ids,
					["Invert"] = !wanted,
					["Attached"] = true,
				} ), "Result" );
			}

			default:
				Count( Class( condition ) is { Length: > 0 } named ? named : "unknown condition" );
				return default;
		}
	}

	//How two numbers are compared. Their operators in the order their files use them, worked out from the graph rather than from documentation.
	private static string Comparison( int op ) => op switch
	{
		0 or 1 => "NearEqual",
		2 => "GreaterThan",
		3 => "GreaterThanEqual",
		4 => "LessThan",
		5 => "LessThanEqual",
		_ => "NearEqual",
	};

	private static string Timing( int op ) => op switch
	{
		2 => "GreaterThan",
		3 => "GreaterThanEqual",
		4 => "LessThan",
		5 => "LessThanEqual",
		_ => "GreaterThanEqual",
	};
}
