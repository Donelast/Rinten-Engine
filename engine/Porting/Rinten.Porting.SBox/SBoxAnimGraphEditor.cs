using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rinten.Porting.SBox;

//Builds an MDL editor AnimGraph document the graph view can open.
internal sealed class SBoxAnimGraphEditor
{
	static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = null,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Converters = { new JsonStringEnumConverter( null, true ) }
	};

	public sealed class Document
	{
		public string Model = "";
		public string Skeleton = "";
		public readonly List<Parameter> Parameters = new();
		public readonly RootGraph Root = new();
		int _layoutY;

		public string Serialize()
		{
			var doc = new JsonObject
			{
				["Version"] = 1,
				["Model"] = Model,
				["nodes"] = Root.WriteNodes()
			};

			if ( !string.IsNullOrWhiteSpace( Skeleton ) )
				doc["Skeleton"] = Skeleton;

			if ( Parameters.Count > 0 )
				doc["parameters"] = JsonSerializer.SerializeToNode( Parameters, Options );

			return doc.ToJsonString( Options );
		}

		internal Vector2 NextPosition( float x = 0 ) => new( x, _layoutY++ * 120f );
	}

	public static string FromRuntime( string runtimeDefinition )
	{
		using var parsed = JsonDocument.Parse( runtimeDefinition );
		var root = parsed.RootElement;
		var document = new Document
		{
			Model = root.TryGetProperty( "Skeleton", out var skeleton ) ? skeleton.GetString() ?? "" : "",
			Skeleton = root.TryGetProperty( "Skeleton", out skeleton ) ? skeleton.GetString() ?? "" : "",
		};

		var converter = new RuntimeConverter( document, root );
		var pose = converter.EmitPose( converter.RootIndex, document.Root );
		document.Root.AddPoseOutput( pose, new Vector2( 420, 0 ) );
		return document.Serialize();
	}

	public sealed class Parameter
	{
		public string Identifier { get; set; } = Guid.NewGuid().ToString();
		public AnimParameterType Type { get; set; }
		public string Name { get; set; } = "";
		public string Group { get; set; } = "";
		public float Min { get; set; }
		public float Max { get; set; } = 1f;
		public List<string> ExpectedValues { get; set; } = [];
	}

	public enum AnimParameterType
	{
		Float,
		Bool,
		ID,
		Vector,
		Target,
		BoneMask
	}

	public readonly record struct Handle( string Id )
	{
		public bool IsValid => !string.IsNullOrWhiteSpace( Id );
		public static Handle Invalid => default;
	}

	public sealed class RootGraph : Graph
	{
		public RootGraph() : base( isSubGraph: false ) { }
	}

	public class Graph
	{
		readonly bool _isSubGraph;
		readonly List<JsonObject> _nodes = new();
		protected Graph( bool isSubGraph ) => _isSubGraph = isSubGraph;

		public static Graph SubGraph() => new( isSubGraph: true );

		public JsonArray WriteNodes()
		{
			var array = new JsonArray();
			foreach ( var node in _nodes )
				array.Add( node.DeepClone() );
			return array;
		}

		public JsonObject WriteDocument()
		{
			var doc = new JsonObject
			{
				["Version"] = 1,
				["nodes"] = WriteNodes()
			};

			if ( _isSubGraph )
				doc["Kind"] = "BlendTree";

			return doc;
		}

		Handle Add( JsonObject node )
		{
			var id = node["Identifier"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
			node["Identifier"] = id;
			_nodes.Add( node );
			return new Handle( id );
		}

		public Handle ReferencePose( Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "ReferencePoseNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position )
			} );

		public Handle ZeroPose( Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "ZeroPoseNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position )
			} );

		public Handle Clip( string name, bool loop, float speed, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "AnimationClipNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["DefaultVariationData"] = new JsonObject
				{
					["AnimationName"] = name ?? "",
					["SpeedMultiplier"] = speed,
					["StartSyncEventOffset"] = 0
				},
				["Loop"] = loop,
				["SampleRootMotion"] = true
			} );

		public Handle Pose( string name, int frame, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "AnimationPoseNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["DefaultVariationData"] = new JsonObject { ["AnimationName"] = name ?? "" },
				["FixedTime"] = frame,
				["UseFramesAsInput"] = true
			} );

		public Handle ExternalPose( string slot, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "ExternalPoseNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["SlotName"] = slot ?? "External Pose"
			} );

		public Handle FloatParameter( Parameter parameter, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "FloatParameterNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["ParameterIdentifier"] = parameter.Identifier,
				["ParameterName"] = parameter.Name
			} );

		public Handle BoolParameter( Parameter parameter, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "BoolParameterNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["ParameterIdentifier"] = parameter.Identifier,
				["ParameterName"] = parameter.Name
			} );

		public Handle IdParameter( Parameter parameter, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "IDParameterNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["ParameterIdentifier"] = parameter.Identifier,
				["ParameterName"] = parameter.Name
			} );

		public Handle VectorParameter( Parameter parameter, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "VectorParameterNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["ParameterIdentifier"] = parameter.Identifier,
				["ParameterName"] = parameter.Name
			} );

		public Handle SpeedScale( Handle source, Handle speed, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "SpeedScaleNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Source"] = Link( source, "Result" ),
				["Speed"] = Link( speed, "Value" )
			} );

		public Handle Blend1D( Handle parameter, bool loop, IReadOnlyList<(float Value, Handle Pose)> points, Vector2 position )
		{
			var blendPoints = new JsonArray();
			var x = 0f;

			foreach ( var (value, pose) in points )
			{
				var sub = SubGraph();
				var output = sub.AddPoseOutput( pose, new Vector2( 400, 0 ) );
				blendPoints.Add( new JsonObject
				{
					["Name"] = "",
					["Value"] = value,
					["Graph"] = sub.WriteDocument(),
					["Input"] = Link( output, "Result" )
				} );
				x += 40f;
			}

			return Add( new JsonObject
			{
				["_class"] = "Blend1DNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Parameter"] = Link( parameter, "Value" ),
				["Loop"] = loop,
				["Points"] = blendPoints
			} );
		}

		public Handle Blend1DGraphs( Handle parameter, bool loop, IReadOnlyList<(float Value, JsonObject Graph)> points, Vector2 position )
		{
			var blendPoints = new JsonArray();

			foreach ( var (value, graph) in points )
			{
				blendPoints.Add( new JsonObject
				{
					["Name"] = "",
					["Value"] = value,
					["Graph"] = graph.DeepClone()
				} );
			}

			return Add( new JsonObject
			{
				["_class"] = "Blend1DNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Parameter"] = Link( parameter, "Value" ),
				["Loop"] = loop,
				["Points"] = blendPoints
			} );
		}

		public Handle Blend2D( Handle xParam, Handle yParam, bool loop, IReadOnlyList<(Vector2 At, Handle Pose)> points, Vector2 position )
		{
			var blendPoints = new JsonArray();

			foreach ( var (at, pose) in points )
			{
				var sub = SubGraph();
				var output = sub.AddPoseOutput( pose, new Vector2( 400, 0 ) );
				blendPoints.Add( new JsonObject
				{
					["Name"] = "",
					["Position"] = Vec( at ),
					["Graph"] = sub.WriteDocument(),
					["Input"] = Link( output, "Result" )
				} );
			}

			return Add( new JsonObject
			{
				["_class"] = "Blend2DNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["X"] = Link( xParam, "Value" ),
				["Y"] = Link( yParam, "Value" ),
				["Loop"] = loop,
				["Points"] = blendPoints
			} );
		}

		public Handle Blend2DGraphs( Handle xParam, Handle yParam, bool loop, IReadOnlyList<(Vector2 At, JsonObject Graph, Handle Output)> points, Vector2 position )
		{
			var blendPoints = new JsonArray();

			foreach ( var (at, graph, output) in points )
			{
				blendPoints.Add( new JsonObject
				{
					["Name"] = "",
					["Position"] = Vec( at ),
					["Graph"] = graph.DeepClone(),
					["Input"] = Link( output, "Result" )
				} );
			}

			return Add( new JsonObject
			{
				["_class"] = "Blend2DNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["X"] = Link( xParam, "Value" ),
				["Y"] = Link( yParam, "Value" ),
				["Loop"] = loop,
				["Points"] = blendPoints
			} );
		}

		public Handle LayerBlend( Handle basePose, IReadOnlyList<(Handle Layer, AnimGraphBuilder.LayerBlendMode Mode)> layers, Vector2 position )
		{
			var layerInputs = new JsonArray();
			foreach ( var _ in layers )
				layerInputs.Add( Link( default, "Layer" ) );

			if ( layerInputs.Count == 0 )
				layerInputs.Add( Link( default, "Layer" ) );

			for ( var i = 0; i < layers.Count; i++ )
				layerInputs[i] = Link( layers[i].Layer, "Layer" );

			return Add( new JsonObject
			{
				["_class"] = "LayerBlendNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Base"] = Link( basePose, "Result" ),
				["Layers"] = layerInputs,
				["OnlySampleBaseRootMotion"] = true
			} );
		}

		public Handle LocalLayer( Handle input, AnimGraphBuilder.LayerBlendMode mode, Handle weight, Handle mask, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "LocalLayerNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Input"] = Link( input, "Result" ),
				["Weight"] = weight.IsValid ? Link( weight, "Value" ) : null,
				["BoneMask"] = mask.IsValid ? Link( mask, "Mask" ) : null,
				["BlendMode"] = mode.ToString()
			} );

		public Handle BoneMask( string name, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "BoneMaskNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["BoneName"] = name ?? ""
			} );

		public Handle Selector( IReadOnlyList<(Handle Pose, Handle Condition)> options, Vector2 position )
		{
			var optionNodes = new JsonArray();
			var conditions = SubGraph();
			var labels = new JsonArray();
			var conditionInputs = new JsonArray();

			for ( var i = 0; i < options.Count; i++ )
			{
				optionNodes.Add( new JsonObject
				{
					["Label"] = $"Option {i + 1}",
					["Pose"] = Link( options[i].Pose, "Result" )
				} );

				labels.Add( $"Option {i + 1}" );
				var cond = options[i].Condition.IsValid
					? options[i].Condition
					: conditions.ConstBool( false, new Vector2( 0, i * 80f ) );
				conditionInputs.Add( Link( cond, "Result" ) );
			}

			var conditionGraph = new JsonObject
			{
				["Version"] = 1,
				["Kind"] = "SelectorConditions",
				["nodes"] = conditions.WriteConditionHub( labels, conditionInputs )
			};

			return Add( new JsonObject
			{
				["_class"] = "SelectorNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Options"] = optionNodes,
				["ConditionGraph"] = conditionGraph
			} );
		}

		public Handle IdSelector( Parameter parameter, IReadOnlyList<(string Id, Handle Pose)> options, Handle fallback, Vector2 position )
		{
			var optionNodes = new JsonArray();
			foreach ( var (id, pose) in options )
			{
				optionNodes.Add( new JsonObject
				{
					["Id"] = id ?? "",
					["Pose"] = Link( pose, "Result" )
				} );
			}

			return Add( new JsonObject
			{
				["_class"] = "IDBasedSelectorNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["ParameterIdentifier"] = parameter.Identifier,
				["ParameterName"] = parameter.Name,
				["Options"] = optionNodes,
				["Fallback"] = fallback.IsValid ? Link( fallback, "Result" ) : null
			} );
		}

		public Handle ParameterizedSelector( Handle parameter, IReadOnlyList<Handle> options, Vector2 position )
		{
			var optionNodes = new JsonArray();
			var weights = new JsonArray();

			foreach ( var pose in options )
			{
				optionNodes.Add( new JsonObject
				{
					["Label"] = "",
					["Pose"] = Link( pose, "Result" )
				} );
				weights.Add( 1 );
			}

			return Add( new JsonObject
			{
				["_class"] = "ParameterizedSelectorNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Parameter"] = Link( parameter, "Value" ),
				["Options"] = optionNodes,
				["DefaultVariationData"] = new JsonObject { ["OptionWeights"] = weights }
			} );
		}

		public Handle StateMachine( IReadOnlyList<StateMachineState> states, string defaultState, Vector2 position )
		{
			var stateNodes = new JsonArray();
			var conduits = new JsonArray();

			foreach ( var state in states )
			{
				stateNodes.Add( new JsonObject
				{
					["Name"] = state.Name,
					["Type"] = "BlendTree",
					["Events"] = new JsonArray(),
					["TimedEvents"] = new JsonArray(),
					["TransitionOrder"] = new JsonArray(),
					["Position"] = Vec( state.Layout ),
					["Graph"] = state.Graph,
					["LayerGraph"] = CreateLayerData().WriteDocument()
				} );
			}

			foreach ( var state in states )
			{
				foreach ( var transition in state.Transitions )
				{
					if ( !states.Any( s => s.Name == transition.To ) )
						continue;

					var conduitGraph = CreateTransitionConduit();
					conduitGraph.AddTransition( transition, states );

					conduits.Add( new JsonObject
					{
						["From"] = state.Name,
						["To"] = transition.To,
						["Graph"] = conduitGraph.WriteDocument()
					} );
				}
			}

			return Add( new JsonObject
			{
				["_class"] = "StateMachineNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["States"] = stateNodes,
				["Conduits"] = conduits,
				["DefaultState"] = defaultState ?? "",
				["GlobalGraph"] = CreateGlobalTransitions().WriteDocument(),
				["EntryGraph"] = CreateEntryOverrides().WriteDocument()
			} );
		}

		public Handle AddPoseOutput( Handle pose, Vector2 position )
		{
			var result = Add( new JsonObject
			{
				["_class"] = "PoseResultNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Pose"] = pose.IsValid ? Link( pose, "Result" ) : null
			} );

			return result;
		}

		public Handle ConstBool( bool value, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "ConstBoolNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Value"] = value
			} );

		public Handle Not( Handle value, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "NotNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Value"] = Link( value, "Result" )
			} );

		public Handle And( IReadOnlyList<Handle> values, Vector2 position )
		{
			var conditions = new JsonArray();
			foreach ( var value in values )
				conditions.Add( new JsonObject { ["Input"] = Link( value, "Result" ) } );

			return Add( new JsonObject
			{
				["_class"] = "AndNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Conditions"] = conditions
			} );
		}

		public Handle FloatCompare( Handle value, AnimGraphBuilder.FloatComparison comparison, float against, float epsilon, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "FloatComparisonNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Value"] = Link( value, "Value" ),
				["Comparison"] = comparison.ToString(),
				["ComparisonValue"] = against,
				["Epsilon"] = epsilon
			} );

		public Handle IdCompare( Handle value, string id, bool matches, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "IDComparisonNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Id"] = Link( value, "Value" ),
				["Comparison"] = matches ? "Matches" : "DoesntMatch",
				["Ids"] = new JsonArray { id ?? "" }
			} );

		public Handle TimeCondition( float time, AnimGraphBuilder.StateMachineBuilder.TimeComparison comparison, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "TimeConditionNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Type"] = "ElapsedTime",
				["Operator"] = comparison.ToString(),
				["Value"] = time
			} );

		public Handle StateCompleted( Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "StateCompletedConditionNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position )
			} );

		public Handle IdEventCondition( string tag, bool wanted, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "IDEventConditionNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Operator"] = wanted ? "Or" : "And",
				["SearchRule"] = "SearchAll",
				["EventIds"] = new JsonArray { tag ?? "" }
			} );

		public Handle VectorInfo( Handle vector, AnimGraphBuilder.VectorComponent component, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "VectorInfoNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Vector"] = Link( vector, "Value" ),
				["Info"] = component.ToString()
			} );

		public Handle TwoBoneIk( Handle input, string chain, string bone, Vector2 position )
			=> Add( new JsonObject
			{
				["_class"] = "TwoBoneIKNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( position ),
				["Input"] = Link( input, "Result" ),
				["DefaultVariationData"] = new JsonObject
				{
					["Chain"] = chain ?? "",
					["EffectorBone"] = bone ?? ""
				}
			} );

		JsonArray WriteConditionHub( JsonArray labels, JsonArray conditionInputs )
		{
			Add( new JsonObject
			{
				["_class"] = "SelectorConditionNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( new Vector2( 400, 0 ) ),
				["Labels"] = labels.DeepClone(),
				["Conditions"] = conditionInputs.DeepClone()
			} );

			return WriteNodes();
		}

		void AddTransition( StateMachineTransition transition, IReadOnlyList<StateMachineState> states )
		{
			var condition = transition.Condition;

			Add( new JsonObject
			{
				["_class"] = "TransitionNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( new Vector2( 0, _nodes.Count * 80f ) ),
				["TransitionName"] = "Transition",
				["Duration"] = transition.Duration,
				["Condition"] = condition.IsValid ? Link( condition, "Result" ) : null
			} );
		}

		static Graph CreateLayerData()
		{
			var graph = SubGraph();
			graph.Add( new JsonObject
			{
				["_class"] = "StateLayerDataNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( new Vector2( 400, 0 ) )
			} );
			return graph;
		}

		static Graph CreateGlobalTransitions()
		{
			var graph = SubGraph();
			graph.Add( new JsonObject
			{
				["_class"] = "GlobalTransitionNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( new Vector2( 400, 0 ) ),
				["StateName"] = ""
			} );
			return graph;
		}

		static Graph CreateEntryOverrides()
		{
			var graph = SubGraph();
			graph.Add( new JsonObject
			{
				["_class"] = "EntryStateOverrideConditionsNode",
				["Identifier"] = NewId(),
				["Position"] = Vec( new Vector2( 400, 0 ) )
			} );
			return graph;
		}

		static Graph CreateTransitionConduit() => SubGraph();

		static string NewId() => Guid.NewGuid().ToString();

		static JsonObject Link( Handle handle, string output )
		{
			if ( !handle.IsValid )
				return null;

			return new JsonObject
			{
				["Identifier"] = handle.Id,
				["Output"] = output
			};
		}

		static string Vec( Vector2 value )
			=> string.Create( CultureInfo.InvariantCulture, $"{value.x:G9},{value.y:G9}" );
	}

	public sealed class StateMachineState
	{
		public string Name { get; init; } = "State";
		public JsonObject Graph { get; init; }
		public Vector2 Layout { get; init; }
		public List<StateMachineTransition> Transitions { get; init; } = new();
	}

	public sealed class StateMachineTransition
	{
		public string To { get; init; } = "";
		public float Duration { get; init; }
		public Handle Condition { get; init; }
	}

	sealed class RuntimeConverter
	{
		readonly Document _document;
		readonly JsonArray _nodes;
		readonly List<Parameter> _parameters = new();
		readonly Dictionary<Graph, Dictionary<int, Handle>> _cache = new();
		readonly Dictionary<Graph, int> _layout = new();

		public RuntimeConverter( Document document, JsonElement root )
		{
			_document = document;
			RootIndex = root.TryGetProperty( "Root", out var rootNode ) ? rootNode.GetInt32() : -1;

			if ( root.TryGetProperty( "Nodes", out var nodes ) )
				_nodes = JsonNode.Parse( nodes.GetRawText() )?.AsArray() ?? new JsonArray();
			else
				_nodes = new JsonArray();

			if ( root.TryGetProperty( "Parameters", out var parameters ) )
			{
				foreach ( var parameter in parameters.EnumerateArray() )
				{
					var kind = parameter.TryGetProperty( "Kind", out var kindElement ) ? kindElement.GetString() : "";
					var made = new Parameter
					{
						Name = parameter.TryGetProperty( "Name", out var name ) ? name.GetString() ?? "" : "",
						Type = ParameterType( kind ),
						Min = parameter.TryGetProperty( "Min", out var min ) ? min.GetSingle() : 0f,
						Max = parameter.TryGetProperty( "Max", out var max ) ? max.GetSingle() : 0f,
					};

					if ( parameter.TryGetProperty( "Options", out var options ) && options.ValueKind == JsonValueKind.Array )
					{
						foreach ( var option in options.EnumerateArray() )
							made.ExpectedValues.Add( option.GetString() ?? "" );
					}

					_parameters.Add( made );
					_document.Parameters.Add( made );
				}
			}
		}

		public int RootIndex { get; }

		public Handle EmitPose( int index, Graph graph )
		{
			if ( index < 0 || index >= _nodes.Count )
				return graph.ReferencePose( NextPosition( graph ) );

			var cache = Cache( graph );
			if ( cache.TryGetValue( index, out var cached ) )
				return cached;

			var node = _nodes[index]?.AsObject();
			if ( node is null )
				return graph.ReferencePose( NextPosition( graph ) );

			var handle = EmitPoseUncached( index, node, graph );
			cache[index] = handle;
			return handle;
		}

		Handle EmitPoseUncached( int index, JsonObject node, Graph graph )
		{
			var kind = Text( node, "Kind" );
			var position = NextPosition( graph );

			switch ( kind )
			{
				case "ReferencePose":
					return graph.ReferencePose( position );
				case "ZeroPose":
					return graph.ZeroPose( position );
				case "Clip":
					return graph.Clip( Text( node, "Clip" ), Bool( node, "Looping" ), Float( node, "Speed", 1f ), position );
				case "Pose":
					return graph.Pose( Text( node, "Clip" ), Int( node, "FixedTime" ), position );
				case "ExternalPose":
					return graph.ExternalPose( Text( node, "Slot", "direct_playback" ), position );
				case "SpeedScale":
					return graph.SpeedScale( EmitPose( Int( node, "Pose" ), graph ), EmitValue( Int( node, "Scale" ), graph ), position );
				case "BlendSpace":
					return EmitBlendSpace( node, graph, position );
				case "BlendSpace2D":
					return EmitBlendSpace2D( node, graph, position );
				case "StateMachine":
					return EmitStateMachine( node, graph, position );
				case "Select":
					return EmitSelect( node, graph, position );
				case "ParameterSelect":
					return graph.ParameterizedSelector(
						EmitValue( Int( node, "Parameter" ), graph ),
						Array( node, "Options" ).Select( item => EmitPose( item.GetValue<int>(), graph ) ).ToList(),
						position );
				default:
					return graph.ReferencePose( position );
			}
		}

		Handle EmitStateMachine( JsonObject node, Graph graph, Vector2 position )
		{
			var states = new List<StateMachineState>();
			var runtimeStates = Array( node, "States" );
			var defaultIndex = Int( node, "Default" );

			for ( var i = 0; i < runtimeStates.Count; i++ )
			{
				var state = runtimeStates[i]?.AsObject();
				if ( state is null ) continue;

				var sub = Graph.SubGraph();
				var pose = EmitPose( Int( state, "Pose" ), sub );
				sub.AddPoseOutput( pose, new Vector2( 420, 0 ) );

				var transitions = new List<StateMachineTransition>();
				foreach ( var routeNode in Array( state, "Routes" ) )
				{
					if ( routeNode?.AsObject() is not { } route ) continue;

					var transition = Node( Int( route, "Transition" ) );
					transitions.Add( new StateMachineTransition
					{
						To = StateName( Int( route, "To" ) ),
						Duration = transition is null ? 0.2f : Float( transition, "Duration", 0.2f ),
					} );
				}

				states.Add( new StateMachineState
				{
					Name = StateName( i ),
					Graph = sub.WriteDocument(),
					Layout = new Vector2( 80 + (i % 4) * 180, 80 + (i / 4) * 120 ),
					Transitions = transitions,
				} );
			}

			return graph.StateMachine( states, StateName( defaultIndex >= 0 ? defaultIndex : 0 ), position );
		}

		Handle EmitBlendSpace( JsonObject node, Graph graph, Vector2 position )
		{
			var sources = Array( node, "Sources" );
			var positions = Array( node, "Positions" );
			var points = new List<(float Value, JsonObject Graph)>();

			for ( var i = 0; i < sources.Count; i++ )
			{
				var sub = Graph.SubGraph();
				var pose = EmitPose( sources[i].GetValue<int>(), sub );
				sub.AddPoseOutput( pose, new Vector2( 420, 0 ) );
				points.Add( (i < positions.Count ? positions[i].GetValue<float>() : 0f, sub.WriteDocument()) );
			}

			return graph.Blend1DGraphs( EmitValue( Int( node, "Parameter" ), graph ), Bool( node, "Looping" ), points, position );
		}

		Handle EmitBlendSpace2D( JsonObject node, Graph graph, Vector2 position )
		{
			var sources = Array( node, "Sources" );
			var positions = Array( node, "Positions" );
			var points = new List<(Vector2 At, JsonObject Graph, Handle Output)>();

			for ( var i = 0; i < sources.Count; i++ )
			{
				var sub = Graph.SubGraph();
				var pose = EmitPose( sources[i].GetValue<int>(), sub );
				var output = sub.AddPoseOutput( pose, new Vector2( 420, 0 ) );
				var at = new Vector2();

				if ( i < positions.Count && positions[i] is JsonArray pair )
				{
					if ( pair.Count > 0 ) at.x = pair[0].GetValue<float>();
					if ( pair.Count > 1 ) at.y = pair[1].GetValue<float>();
				}

				points.Add( (at, sub.WriteDocument(), output) );
			}

			return graph.Blend2DGraphs( EmitValue( Int( node, "X" ), graph ), EmitValue( Int( node, "Y" ), graph ), Bool( node, "Looping" ), points, position );
		}

		Handle EmitSelect( JsonObject node, Graph graph, Vector2 position )
		{
			var options = new List<(Handle Pose, Handle Condition)>();
			foreach ( var item in Array( node, "Options" ) )
			{
				if ( item?.AsObject() is not { } option ) continue;
				options.Add( (EmitPose( Int( option, "Pose" ), graph ), EmitValue( Int( option, "Condition" ), graph )) );
			}

			return options.Count > 0 ? graph.Selector( options, position ) : graph.ReferencePose( position );
		}

		Handle EmitValue( int index, Graph graph )
		{
			if ( index < 0 || index >= _nodes.Count )
				return Handle.Invalid;

			var cache = Cache( graph );
			if ( cache.TryGetValue( index, out var cached ) )
				return cached;

			var node = _nodes[index]?.AsObject();
			if ( node is null )
				return Handle.Invalid;

			var position = NextPosition( graph, -280 );
			Handle handle;

			switch ( Text( node, "Kind" ) )
			{
				case "Parameter":
					handle = EmitParameter( node, graph, position );
					break;
				case "ConstBool":
					handle = graph.ConstBool( Bool( node, "Value" ), position );
					break;
				case "Not":
					handle = graph.Not( EmitValue( Int( node, "Value" ), graph ), position );
					break;
				case "And":
					handle = graph.And( Array( node, "Values" ).Select( item => EmitValue( item.GetValue<int>(), graph ) ).ToList(), position );
					break;
				case "Compare":
					handle = graph.FloatCompare( EmitValue( Int( node, "Value" ), graph ),
						Enum.TryParse<AnimGraphBuilder.FloatComparison>( Text( node, "Comparison" ), out var comparison )
							? comparison
							: AnimGraphBuilder.FloatComparison.GreaterThanEqual,
						Float( node, "ComparandValue" ), Float( node, "Epsilon" ), position );
					break;
				default:
					handle = Handle.Invalid;
					break;
			}

			cache[index] = handle;
			return handle;
		}

		Handle EmitParameter( JsonObject node, Graph graph, Vector2 position )
		{
			var index = Int( node, "Parameter" );
			if ( index < 0 || index >= _parameters.Count )
				return Handle.Invalid;

			var parameter = _parameters[index];
			return parameter.Type switch
			{
				AnimParameterType.Bool => graph.BoolParameter( parameter, position ),
				AnimParameterType.ID => graph.IdParameter( parameter, position ),
				AnimParameterType.Vector => graph.VectorParameter( parameter, position ),
				_ => graph.FloatParameter( parameter, position ),
			};
		}

		JsonObject Node( int index )
			=> index >= 0 && index < _nodes.Count ? _nodes[index]?.AsObject() : null;

		Dictionary<int, Handle> Cache( Graph graph )
		{
			if ( !_cache.TryGetValue( graph, out var cache ) )
			{
				cache = new Dictionary<int, Handle>();
				_cache[graph] = cache;
			}

			return cache;
		}

		Vector2 NextPosition( Graph graph, float x = 0 )
		{
			_layout.TryGetValue( graph, out var row );
			_layout[graph] = row + 1;
			return new Vector2( x, row * 120f );
		}

		static string StateName( int index ) => index < 0 ? "" : $"State {index + 1}";

		static AnimParameterType ParameterType( string kind ) => kind switch
		{
			"Bool" => AnimParameterType.Bool,
			"Id" => AnimParameterType.ID,
			"Vector" => AnimParameterType.Vector,
			"Target" => AnimParameterType.Target,
			"BoneMask" => AnimParameterType.BoneMask,
			_ => AnimParameterType.Float,
		};

		static JsonArray Array( JsonObject node, string name )
			=> node[name] as JsonArray ?? new JsonArray();

		static string Text( JsonObject node, string name, string fallback = "" )
			=> node[name]?.GetValue<string>() ?? fallback;

		static int Int( JsonObject node, string name )
			=> node[name]?.GetValue<int>() ?? -1;

		static float Float( JsonObject node, string name, float fallback = 0f )
			=> node[name]?.GetValue<float>() ?? fallback;

		static bool Bool( JsonObject node, string name )
			=> node[name]?.GetValue<bool>() ?? false;
	}
}
