namespace Editor.NodeEditor;

//Nodes that ride on the input they answer, instead of sitting beside it with a wire in between.
partial class GraphView
{
	//How far off the host's edge an attached node sits.
	private const float AttachedGap = 2.0f;

	//How near a node's output has to come to an input to take it, in graph units - so it is a fixed distance in the graph
	//rather than on the screen. A snap that got easier as you zoomed in would grab things you were only passing over.
	private const float AttachReach = 40.0f;

	//What is being seated right now. A set rather than a flag because they chain - a parameter
	//rides on a spring which rides on a condition - and seating one moves it, which seats the next.
	private readonly HashSet<NodeUI> _placing = new();

	//Whether a node is being put where its plug is rather than dragged there.
	internal bool PlacingAttached => _placing.Count > 0;

	//The plug a node being dragged would land on if let go now.
	private PlugIn _attachTarget;

	//The dragged node's own plug, when it has found somewhere to land - so both ends light up rather than only the one being landed on.
	internal PlugOut AttachSource { get; private set; }

	//Whether this drag has taken something off a plug.
	private bool _detached;

	/// <summary>The node riding on a plug, if the thing feeding it asked to.</summary>
	public NodeUI AttachedTo( PlugIn plug )
	{
		if ( plug?.Inner?.ConnectedOutput?.Node is not IAttachableNode { Attached: true } attached ) return null;

		return Items.OfType<NodeUI>().FirstOrDefault( x => ReferenceEquals( x.Node, attached ) );
	}

	/// <summary>Puts every node attached to this one against the plug it answers: on the same row as that plug, and just off the left edge.</summary>
	public void PlaceAttached( NodeUI host )
	{
		if ( !host.IsValid() || !_placing.Add( host ) ) return;

		try
		{
			foreach ( var plug in host.Inputs )
			{
				if ( AttachedTo( plug ) is not { } rider ) continue;

				//The rider's own output plug is what has to land on this one, so the two rows line up however tall either node happens to be.
				var seat = rider.Outputs.FirstOrDefault();
				var offset = seat is not null ? seat.SceneRect.Center.y - rider.Position.y : 0.0f;
				var row = plug.SceneRect.Center.y;

				rider.Position = new Vector2(
					host.Position.x - rider.Size.x - AttachedGap,
					row - offset );
			}
		}
		finally
		{
			_placing.Remove( host );
		}
	}

	/// <summary>Puts every node that rides on a plug against the plug it answers.</summary>
	public void PlaceAllAttached()
	{
		var all = Items.OfType<NodeUI>().ToArray();

		//A passenger answers one plug. Anything wired to a second one has to come off and be an ordinary
		//node again, or it would sit on one of them and reach the others by wires it is not drawing.
		var readers = new Dictionary<INode, int>();

		foreach ( var node in all )
		{
			foreach ( var plug in node.Inputs )
			{
				if ( plug.Inner?.ConnectedOutput?.Node is not { } source ) continue;

				readers[source] = readers.GetValueOrDefault( source ) + 1;
			}
		}

		foreach ( var node in all )
		{
			if ( node.Node is not IAttachableNode { Attached: true } attached ) continue;
			if ( readers.GetValueOrDefault( node.Node ) == 1 ) continue;

			attached.Attached = false;
		}

		foreach ( var host in all )
		{
			PlaceAttached( host );
		}
	}

	//An item is being carried across the graph.
	internal void ItemDragged( GraphItem item )
	{
		if ( item is not NodeUI node ) return;

		//Dragged by hand, so it is not riding on anything any more. Decided here rather than where the position
		//changes: that runs whenever anything moves a node, the graph seating it against a plug included.
		if ( node.Node is IAttachableNode { Attached: true } attachable )
		{
			attachable.Attached = false;
			_detached = true;
		}

		NodeGeometryChanged( node );

		var wanted = AttachCandidate( node );

		if ( ReferenceEquals( wanted, _attachTarget ) ) return;

		//The same highlight a wire being dragged puts on the plug it is over, because it means the same thing: let go and these two are joined.
		DropTarget?.Update();
		DropTarget = wanted;
		DropTarget?.Update();

		_attachTarget = wanted;

		AttachSource?.Update();
		AttachSource = wanted is null ? null : node.Outputs.FirstOrDefault();
		AttachSource?.Update();
	}

	//An item has been let go.
	internal void ItemDropped( GraphItem item )
	{
		var target = _attachTarget;

		DropTarget?.Update();
		DropTarget = null;
		_attachTarget = null;

		AttachSource?.Update();
		AttachSource = null;

		var came = _detached;
		_detached = false;

		if ( target is null || item is not NodeUI node || node.Outputs.FirstOrDefault() is not { } source )
		{
			//Nothing to land on. If it came off a plug on the way here the graph still has to be redrawn - the wire it was doing without is back.
			if ( came ) RebuildFromGraph();
			return;
		}

		PushUndo( "Attach Node" );

		target.Inner.ConnectedOutput = source.Inner;

		if ( node.Node is IAttachableNode attachable ) attachable.Attached = true;

		PushRedo();

		RebuildFromGraph();
	}

	//The input a node held here would attach to: the nearest one its output can answer, close enough to count and not on the node itself.
	private PlugIn AttachCandidate( NodeUI node )
	{
		if ( node?.Node is not IAttachableNode ) return null;

		//One answer to give. Whether it has inputs of its own does not come into it - a condition
		//reading a parameter is exactly the sort of thing that wants to ride on the input it answers.
		if ( node.Outputs.Count != 1 ) return null;

		var source = node.Outputs[0];
		var from = source.ConnectionPosition;

		PlugIn best = null;
		var nearest = AttachReach;

		foreach ( var other in Items.OfType<NodeUI>() )
		{
			if ( ReferenceEquals( other, node ) ) continue;

			foreach ( var plug in other.Inputs )
			{
				if ( !CanConnect( source, plug ) ) continue;

				//Taken, unless it is this very node that has it: dragging a passenger off a plug leaves the
				//wire behind, so the plug it came from is still its own and putting it back has to be allowed.
				var held = plug.Inner?.ConnectedOutput;
				if ( held is not null && !ReferenceEquals( held, source.Inner ) ) continue;

				var distance = from.Distance( plug.ConnectionPosition );
				if ( distance >= nearest ) continue;

				nearest = distance;
				best = plug;
			}
		}

		return best;
	}
}
