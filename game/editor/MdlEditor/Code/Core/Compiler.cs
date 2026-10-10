using Editor.MdlEditor.Nodes;

namespace Editor.MdlEditor;

/// <summary>Translates the editor graph into an <see cref="AnimGraphBuilder"/> definition.</summary>
public sealed class Compiler
{
	public sealed record StateMachineDebug( int NodeIndex, IReadOnlyDictionary<StateMachineNode.State, int> StateNodes, IReadOnlyDictionary<TransitionNode, int> TransitionNodes );
	public sealed record LayerDebug( int NodeIndex, int LayerIndex );
	private sealed record CompiledStateMachine(
		AnimGraphBuilder.Node Node,
		IReadOnlyDictionary<StateMachineNode.State, int> StateNodes,
		IReadOnlyDictionary<TransitionNode, AnimGraphBuilder.Node> TransitionNodes );
	private sealed record CompiledLayer( AnimGraphBuilder.Node Node, int LayerIndex );

	/// <summary>How bad a compile message is - an error stops the graph compiling at all.</summary>
	public enum Severity
	{
		Warning,
		Error
	}

	/// <summary>A compile message, with the node it came from when one was being compiled.</summary>
	public sealed record Warning( string Message, Node Node, Severity Severity = Severity.Warning )
	{
		public override string ToString() => Message;

		public bool IsError => Severity == Severity.Error;
	}

	public sealed record Result(
		string Definition,
		int RootNodeIndex,
		IReadOnlyList<Warning> Warnings,
		IReadOnlyDictionary<StateMachineNode, StateMachineDebug> StateMachines,
		IReadOnlyDictionary<StateMachineLayerNode, LayerDebug> StateMachineLayers,
		IReadOnlyDictionary<Node, int> NodeIndices )
	{
		/// <summary>How long the compile took, in milliseconds.</summary>
		public double ElapsedMilliseconds { get; init; }

		/// <summary>Messages that stopped the graph compiling.</summary>
		public IEnumerable<Warning> Errors => Warnings.Where( warning => warning.IsError );

		/// <summary>True when the graph produced errors - the definition it built is not safe to run and shouldn't be written to a resource.</summary>
		public bool Failed => Warnings.Any( warning => warning.IsError );
	}

	public static Result Compile( Data graph ) => Compile( graph, Variation.DefaultVariationID );

	public static Result Compile( Data graph, string variationID )
	{
		var timer = Rinten.Diagnostics.FastTimer.StartNew();
		var result = new Compiler( graph, variationID ).Run();
		return result with { ElapsedMilliseconds = timer.ElapsedMilliSeconds };
	}

	private readonly Data _graph;
	private readonly AnimGraphBuilder _builder = AnimGraph.Builder;
	private readonly List<Warning> _warnings = new();
	private readonly List<string> _parameterNames = new();
	private readonly List<string> _virtualParameterIds = new();
	private readonly List<string> _externalGraphSlots = new();
	private readonly List<string> _externalPoseSlots = new();
	private readonly Dictionary<Node, AnimGraphBuilder.Node> _emitted = new();
	private readonly HashSet<Node> _emitting = new();
	private readonly List<Node> _compiling = new();
	private readonly Dictionary<StateMachineNode, CompiledStateMachine> _stateMachines = new();
	private readonly Dictionary<StateMachineLayerNode, CompiledLayer> _stateMachineLayers = new();
	private readonly HashSet<Node> _synchronizedNodesChecked = new();
	private readonly Dictionary<ParameterNode, IParameter> _resolvedParameters = new();
	private readonly Dictionary<IParameter, AnimGraphBuilder.Node> _controlParameterNodes = new();
	private readonly Dictionary<VirtualParameter, AnimGraphBuilder.Node> _compiledVirtualParameters = new();
	private readonly HashSet<VirtualParameter> _compilingVirtualParameters = new();
	private readonly Dictionary<Node, (AnimGraphBuilder.StateMachineBuilder.State, float, AnimGraphBuilder.Node)> _transitionScopes = new();

	//The variation being compiled - nodes resolve their variation data against it.
	internal string VariationID { get; }

	internal VariationHierarchy VariationHierarchy => _graph?.Variations;

	//What this variation animates, for a node checking a name the model has to know.
	internal Model TargetModel => _graph?.Variations.GetVariation( VariationID )?.TargetModel;

	internal float TransitionDuration { get; set; }
	internal AnimGraphBuilder.StateMachineBuilder.State SourceState { get; set; }
	internal AnimGraphBuilder.Node TransitionDurationOverride { get; set; }

	private Compiler( Data graph, string variationID )
	{
		_graph = graph;
		//Compile against the variation's own casing, so overrides keyed by ID still match
		VariationID = graph?.Variations.TryGetCaseCorrectVariationID( variationID ) ?? Variation.DefaultVariationID;
	}

	internal AnimGraphBuilder Builder => _builder;

	private Result Run()
	{
		BeginVariation();

		CollectParameters();
		CollectVirtualParameters();

		var output = _graph.Nodes.OfType<PoseResultNode>().FirstOrDefault();
		AnimGraphBuilder.Node root;

		if ( output is null )
		{
			Error( "Graph has no output node" );
			root = ReferencePose();
		}
		else
		{
			root = EmitPose( output, output.Pose, "Graph Output" );
		}

		var definition = _builder.Build( root );
		return new Result(
			definition,
			root.GetRuntimeIndex(),
			_warnings,
			_stateMachines.ToDictionary(
				entry => entry.Key,
				entry => new StateMachineDebug(
					entry.Value.Node.GetRuntimeIndex(),
					entry.Value.StateNodes,
					entry.Value.TransitionNodes.ToDictionary( transition => transition.Key, transition => transition.Value.GetRuntimeIndex() ) ) ),
			_stateMachineLayers.ToDictionary(
				entry => entry.Key,
				entry => new LayerDebug( entry.Value.Node.GetRuntimeIndex(), entry.Value.LayerIndex ) ),
			_emitted.ToDictionary( entry => entry.Key, entry => entry.Value.GetRuntimeIndex() ) );
	}

	//Point the builder at what this variation animates and report what it's missing. The model is what a renderer binds to, and
	//the skeleton is what the graph's clips are compiled against - a graph without one can't be created at all, so that's an error.
	private void BeginVariation()
	{
		if ( _graph?.Variations.GetVariation( VariationID ) is not { } variation )
			return;

		if ( string.IsNullOrWhiteSpace( variation.TargetModelPath ) )
			Warn( $"Variation '{VariationID}' has no model" );

		if ( string.IsNullOrWhiteSpace( variation.TargetSkeletonPath ) )
			Error( $"Variation '{VariationID}' has no skeleton" );

		_builder.Skeleton = variation.TargetSkeletonPath;
	}

	private void CollectParameters()
	{
		foreach ( var parameter in _graph.Parameters )
		{
			if ( string.IsNullOrWhiteSpace( parameter.Name ) )
			{
				Warn( "A graph parameter has no name" );
				continue;
			}

			if ( _parameterNames.Contains( parameter.Name, StringComparer.OrdinalIgnoreCase ) )
			{
				Warn( $"Duplicate parameter '{parameter.Name}'" );
				continue;
			}

			_parameterNames.Add( parameter.Name );
			var node = AddControlParameter( parameter, describe: true );
			_controlParameterNodes[parameter] = node;
		}

		foreach ( var node in _graph.AllNodes.OfType<ParameterNode>() )
		{
			if ( string.IsNullOrWhiteSpace( node.ParameterName ) )
			{
				Warn( $"{node.DisplayInfo.Name} has no parameter name", node );
				_resolvedParameters[node] = AddImplicitParameter( node );
				continue;
			}

			IParameter parameter = null;

			if ( !string.IsNullOrWhiteSpace( node.ParameterIdentifier ) )
			{
				parameter = _graph.Parameters.FirstOrDefault( p => p.Identifier == node.ParameterIdentifier )
					?? (IParameter)_graph.VirtualParameters.FirstOrDefault( p => p.Identifier == node.ParameterIdentifier );
			}

			parameter ??= _graph.Parameters.FirstOrDefault( p => p.Type == node.ParameterType && p.Name.Equals( node.ParameterName, StringComparison.OrdinalIgnoreCase ) );
			parameter ??= _graph.VirtualParameters.FirstOrDefault( p => p.Type == node.ParameterType && p.Name.Equals( node.ParameterName, StringComparison.OrdinalIgnoreCase ) );

			if ( parameter is null )
			{
				Warn( $"'{node.ParameterName}' is not a graph parameter", node );
				_resolvedParameters[node] = AddImplicitParameter( node );
				continue;
			}

			if ( parameter.Type != node.ParameterType )
			{
				Warn( $"'{node.ParameterName}' node type doesn't match the parameter's type", node );
				_resolvedParameters[node] = AddImplicitParameter( node );
				continue;
			}

			node.ParameterIdentifier = parameter.Identifier;
			node.ParameterName = parameter.Name;
			_resolvedParameters[node] = parameter;
		}
	}

	private Parameter AddImplicitParameter( ParameterNode node )
	{
		if ( node.ParameterType == AnimParameterType.BoneMask )
			return null;

		var parameter = new Parameter
		{
			Identifier = node.ParameterIdentifier,
			Name = node.ParameterName ?? "",
			Type = node.ParameterType
		};

		_parameterNames.Add( node.ParameterName ?? "" );
		var parameterNode = AddControlParameter( parameter, describe: false );
		_controlParameterNodes[parameter] = parameterNode;
		return parameter;
	}

	private AnimGraphBuilder.Node AddControlParameter( Parameter parameter, bool describe )
	{
		return _builder.AddControlParameter(
			parameter.Name,
			parameter.Type switch
			{
				AnimParameterType.Bool => AnimGraphBuilder.NodeKind.Bool,
				AnimParameterType.ID => AnimGraphBuilder.NodeKind.Id,
				AnimParameterType.Float => AnimGraphBuilder.NodeKind.Float,
				AnimParameterType.Vector => AnimGraphBuilder.NodeKind.Vector,
				AnimParameterType.Target => AnimGraphBuilder.NodeKind.Target,
				_ => throw new InvalidOperationException( $"Unsupported control parameter type: {parameter.Type}" ),
			},
			new AnimGraphBuilder.ControlParameterSettings
			{
				Describe = describe,
				Min = parameter.HasRange ? parameter.Min : 0f,
				Max = parameter.HasRange ? parameter.Max : 0f,
				Default = StartValue( parameter ),
				AutoReset = parameter.AutoReset,
				Options = parameter.Options,
			} );
	}

	//Where a parameter stands before anything sets it, as one number. An id is kept as the position of its name among the options rather than
	//as the name, because that is what the graph a game runs carries - and a name that is not one of the options is no start value at all.
	private static float StartValue( Parameter parameter )
	{
		switch ( parameter.Type )
		{
			case AnimParameterType.Bool:
				return parameter.PreviewStartBool ? 1f : 0f;

			case AnimParameterType.ID:
			{
				var options = parameter.Options;
				if ( options is null ) return 0f;

				for ( var i = 0; i < options.Count; i++ )
				{
					if ( string.Equals( options[i], parameter.PreviewStartID, StringComparison.OrdinalIgnoreCase ) )
						return i;
				}

				return 0f;
			}

			case AnimParameterType.Float:
				return parameter.PreviewStartFloat;

			default:
				return 0f;
		}
	}

	private void CollectVirtualParameters()
	{
		foreach ( var parameter in _graph.VirtualParameters )
		{
			if ( string.IsNullOrWhiteSpace( parameter.Name ) )
			{
				Warn( "A virtual parameter has no name" );
				continue;
			}

			if ( _parameterNames.Contains( parameter.Name, StringComparer.OrdinalIgnoreCase )
				|| _virtualParameterIds.Contains( parameter.Name, StringComparer.OrdinalIgnoreCase ) )
			{
				Warn( $"Duplicate parameter '{parameter.Name}'" );
				continue;
			}

			var node = CompileVirtualParameter( parameter );
			_virtualParameterIds.Add( parameter.Name );
			_builder.BindVirtualParameter( parameter.Name, node );
		}
	}

	private AnimGraphBuilder.Node CompileVirtualParameter( VirtualParameter parameter )
	{
		if ( _compiledVirtualParameters.TryGetValue( parameter, out var node ) )
			return node;

		if ( !_compilingVirtualParameters.Add( parameter ) )
		{
			Warn( $"Virtual parameter '{parameter.Name}' is part of a cycle" );
			return ConstDefault( ParameterLinkType( parameter.Type ) );
		}

		var result = parameter.Graph.Nodes.OfType<VirtualParameterResultNode>().FirstOrDefault();
		var source = result?.Inputs.FirstOrDefault()?.ConnectedOutput?.Node as Node;

		if ( source is null )
		{
			Warn( $"Virtual parameter '{parameter.Name}' has no result connected" );
			node = ConstDefault( ParameterLinkType( parameter.Type ) );
		}
		else
		{
			node = Emit( source );
		}

		_compilingVirtualParameters.Remove( parameter );
		_compiledVirtualParameters[parameter] = node;
		return node;
	}

	internal AnimGraphBuilder.Node EmitParameterReference( ParameterNode node )
	{
		if ( !_resolvedParameters.TryGetValue( node, out var parameter ) )
			return ConstDefault( node.Outputs.First().Type );

		if ( parameter is null )
			return ConstDefault( node.Outputs.First().Type );

		if ( parameter is VirtualParameter virtualParameter )
			return CompileVirtualParameter( virtualParameter );

		if ( _controlParameterNodes.TryGetValue( parameter, out var controlNode ) )
			return controlNode;

		Warn( $"Control parameter '{parameter.Name}' could not be compiled", node );
		return ConstDefault( node.Outputs.First().Type );
	}

	internal AnimGraphBuilder.Node EmitControlParameterReference(
		Node owner,
		string identifier,
		string name,
		AnimParameterType type )
	{
		IParameter parameter = null;

		if ( !string.IsNullOrWhiteSpace( identifier ) )
			parameter = _graph.Parameters.FirstOrDefault( candidate => candidate.Identifier == identifier );

		parameter ??= _graph.Parameters.FirstOrDefault( candidate =>
			candidate.Type == type && string.Equals( candidate.Name, name, StringComparison.OrdinalIgnoreCase ) );

		if ( parameter is null )
		{
			Warn( $"'{name}' is not a control parameter", owner );
			return null;
		}

		if ( parameter.Type != type )
		{
			Warn( $"'{parameter.Name}' parameter type doesn't match the selector's type", owner );
			return null;
		}

		if ( _controlParameterNodes.TryGetValue( parameter, out var controlNode ) )
			return controlNode;

		Warn( $"Control parameter '{parameter.Name}' could not be compiled", owner );
		return null;
	}

	private static Type ParameterLinkType( AnimParameterType type ) => type switch
	{
		AnimParameterType.Bool => typeof( BoolLink ),
		AnimParameterType.ID => typeof( IDLink ),
		AnimParameterType.Float => typeof( FloatLink ),
		AnimParameterType.Vector => typeof( VectorLink ),
		AnimParameterType.Target => typeof( TargetLink ),
		AnimParameterType.BoneMask => typeof( BoneMaskLink ),
		_ => typeof( FloatLink ),
	};

	//Emits a node's runtime definition. Already emitted nodes return their cached handle so shared subtrees dedupe.
	internal AnimGraphBuilder.Node Emit( Node node )
	{
		if ( node is ITransitionScopedNode )
			CheckTransitionScope( node );

		if ( _emitted.TryGetValue( node, out var cached ) )
			return cached;

		if ( !_emitting.Add( node ) )
		{
			Warn( $"{node.DisplayInfo.Name} is part of a cycle", node );
			return Fallback( node );
		}

		_compiling.Add( node );
		var emitted = node.Compile( this );
		_compiling.RemoveAt( _compiling.Count - 1 );

		_emitting.Remove( node );
		_emitted[node] = emitted;

		return emitted;
	}

	//A transition-scoped node compiles against the transition it sits in (its source state and blend duration), and the emit
	//cache means it's only compiled once - so sharing one between transitions silently binds every use to the first one's scope.
	private void CheckTransitionScope( Node node )
	{
		var scope = (SourceState, TransitionDuration, TransitionDurationOverride);

		if ( !_transitionScopes.TryGetValue( node, out var previous ) )
		{
			_transitionScopes[node] = scope;
			return;
		}

		if ( !previous.Equals( scope ) )
			Error( $"{node.DisplayInfo.Name} is used by more than one transition - give each transition its own node", node );
	}

	//Registers an external graph slot - a referenced graph node filled at runtime by game code using the slot id.
	internal bool RegisterExternalGraphSlot( string slotId, Node node )
	{
		return RegisterExternalSlot( _externalGraphSlots, slotId, node );
	}

	//Registers an external pose slot - a pose node driven at runtime by game code using the slot id.
	internal bool RegisterExternalPoseSlot( string slotId, Node node )
	{
		return RegisterExternalSlot( _externalPoseSlots, slotId, node );
	}

	private bool RegisterExternalSlot( List<string> slots, string slotId, Node node )
	{
		if ( string.IsNullOrWhiteSpace( slotId ) )
		{
			Warn( $"{node.DisplayInfo.Name} has no slot name", node );
			return false;
		}

		if ( slots.Contains( slotId, StringComparer.OrdinalIgnoreCase ) )
		{
			Warn( $"Duplicate external slot name '{slotId}'", node );
			return false;
		}

		slots.Add( slotId );
		return true;
	}

	internal AnimGraphBuilder.Node EmitPose( Node owner, NodeInput input, string title = null, bool synchronized = false )
	{
		if ( owner.GetConnectedNode( input ) is { } node )
		{
			var output = node.Outputs.FirstOrDefault( x => x.Identifier == input.Output );
			if ( output?.Type != typeof( PoseLink ) )
			{
				Warn( $"{title ?? owner.DisplayInfo.Name} is connected to a non-pose output", owner );
				return ReferencePose();
			}

			if ( synchronized )
				WarnSynchronizedReverse( node );

			return Emit( node );
		}

		Warn( $"{title ?? owner.DisplayInfo.Name} has no pose connected", owner );
		return ReferencePose();
	}

	private void WarnSynchronizedReverse( Node node )
	{
		var pending = new Stack<Node>();
		pending.Push( node );

		while ( pending.TryPop( out var current ) )
		{
			if ( !_synchronizedNodesChecked.Add( current ) )
				continue;

			if ( current is TimeControlledAnimationClipNode timed && timed.GetConnectedNode( timed.PlayInReverse ) is not null )
				Warn( "Play In Reverse is ignored because this clip is driven by a synchronized blend or layer; use an unsynchronized branch or layer", timed );

			foreach ( var input in current.Inputs.Where( input => input.Type == typeof( PoseLink ) ) )
			{
				if ( input.ConnectedOutput?.Node is Node source )
					pending.Push( source );
			}

			foreach ( var graph in current.SubGraphs.Where( graph => graph.Kind == GraphKind.BlendTree ) )
			{
				foreach ( var output in graph.Nodes.OfType<PoseResultNode>() )
				{
					if ( output.GetConnectedNode( output.Pose ) is { } source )
						pending.Push( source );
				}
			}
		}
	}

	internal AnimGraphBuilder.Node EmitMask( Node owner, NodeInput input, string title = null )
	{
		if ( owner.GetConnectedNode( input ) is { } node )
		{
			var output = node.Outputs.FirstOrDefault( output => output.Identifier == input.Output );
			if ( output?.Type != typeof( BoneMaskLink ) )
			{
				Warn( $"{title ?? owner.DisplayInfo.Name} is connected to a non-bone-mask output", owner );
				return FixedWeightBoneMask( 1.0f );
			}

			return Emit( node );
		}

		Warn( $"{title ?? owner.DisplayInfo.Name} has no bone mask connected", owner );
		return FixedWeightBoneMask( 1.0f );
	}

	internal AnimGraphBuilder.Node EmitValue( Node owner, NodeInput input )
	{
		if ( owner.GetConnectedNode( input ) is not { } node )
			return default;

		var output = node.Outputs.FirstOrDefault( output => output.Identifier == input.Output );
		var isCompatible = owner.Inputs
			.OfType<AnimPlugIn>()
			.Any( candidate => candidate.Info.GetInput( owner ) == input && candidate.Type == output?.Type );

		if ( isCompatible )
			return Emit( node );

		Warn( $"{owner.DisplayInfo.Name} has an incompatible value connection", owner );
		return default;
	}

	internal AnimGraphBuilder.Node ConstFloat( float value ) => _builder.Constant( value );
	internal AnimGraphBuilder.Node ConstBool( bool value ) => _builder.Constant( value );
	internal AnimGraphBuilder.Node ConstID( string id = null ) => _builder.ConstantId( id );
	internal AnimGraphBuilder.Node ConstVector( Vector3 value ) => _builder.Constant( value );
	internal AnimGraphBuilder.Node NullTarget() => _builder.NullTarget();
	internal AnimGraphBuilder.Node ReferencePose() => _builder.ReferencePose();
	internal AnimGraphBuilder.Node FixedWeightBoneMask( float weight ) => _builder.BoneMask( weight );

	private AnimGraphBuilder.Node Fallback( Node node )
	{
		var outputType = node.Outputs.OfType<AnimPlugOut>().FirstOrDefault()?.Type;
		return outputType is null ? ReferencePose() : ConstDefault( outputType );
	}

	//The default constant for a connection type, used when a required input is missing.
	internal AnimGraphBuilder.Node ConstDefault( Type linkType )
	{
		if ( linkType == typeof( FloatLink ) ) return ConstFloat( 0.0f );
		if ( linkType == typeof( BoolLink ) ) return ConstBool( false );
		if ( linkType == typeof( IDLink ) ) return ConstID();
		if ( linkType == typeof( VectorLink ) ) return ConstVector( Vector3.Zero );
		if ( linkType == typeof( TargetLink ) ) return NullTarget();
		if ( linkType == typeof( BoneMaskLink ) ) return FixedWeightBoneMask( 1.0f );
		return ReferencePose();
	}

	internal void SetEmitted( Node node, AnimGraphBuilder.Node emitted ) => _emitted[node] = emitted;
	internal void RegisterLayerDebug( StateMachineLayerNode node, AnimGraphBuilder.Node emitted, int layerIndex ) => _stateMachineLayers[node] = new( emitted, layerIndex );
	internal void RegisterStateMachine(
		StateMachineNode node,
		AnimGraphBuilder.Node emitted,
		IReadOnlyDictionary<StateMachineNode.State, int> states,
		IReadOnlyDictionary<TransitionNode, AnimGraphBuilder.Node> transitions )
		=> _stateMachines[node] = new( emitted, states, transitions );

	internal void Warn( string message, Node node = null ) => AddMessage( message, node, Severity.Warning );

	//Reports a problem that makes the compiled graph wrong rather than just suspect. The
	//compile carries on so every error surfaces at once, but the result is marked failed.
	internal void Error( string message, Node node = null ) => AddMessage( message, node, Severity.Error );

	private void AddMessage( string message, Node node, Severity severity )
	{
		if ( _warnings.All( w => w.Message != message || w.Severity != severity ) )
			_warnings.Add( new Warning( message, node ?? (_compiling.Count > 0 ? _compiling[^1] : null), severity ) );
	}
}
