namespace Editor.NodeEditor;

/// <summary>Dragging the canvas about under the pointer.</summary>
public sealed class PanInteraction : IGraphInteraction
{
	private readonly GraphViewport _viewport;
	private Vector2 _last;

	public PanInteraction( GraphViewport viewport, GraphPointer start )
	{
		_viewport = viewport;
		_last = start.View;
	}

	public void Move( GraphPointer pointer )
	{
		_viewport.Pan( pointer.View - _last );
		_last = pointer.View;
	}

	public void Finish( GraphPointer pointer ) => Move( pointer );

	public void Cancel() { }
}

/// <summary>The box you drag across empty canvas to catch what it covers.</summary>
public sealed class MarqueeInteraction : IGraphInteraction
{
	private readonly GraphView _graph;
	private readonly Vector2 _start;
	private readonly MarqueeItem _box;
	private readonly bool _add;

	public MarqueeInteraction( GraphView graph, GraphPointer start, bool add )
	{
		_graph = graph;
		_start = start.Scene;
		_add = add;

		_box = new MarqueeItem( graph ) { Position = start.Scene };

		graph.AddItem( _box );

		if ( !add ) graph.ClearSelection();
	}

	public void Move( GraphPointer pointer )
	{
		var min = Vector2.Min( _start, pointer.Scene );
		var max = Vector2.Max( _start, pointer.Scene );

		_box.Position = min;
		_box.Size = max - min;

		_graph.SelectInRect( new Rect( min, max - min ), _add );
	}

	public void Finish( GraphPointer pointer )
	{
		Move( pointer );
		Cancel();
	}

	public void Cancel() => _box.Destroy();
}

/// <summary>Nodes being carried across the graph.</summary>
public sealed class MoveItemsInteraction : IGraphInteraction
{
	private readonly GraphView _graph;
	private readonly Vector2 _start;
	private readonly List<(GraphItem Item, Vector2 From)> _carried = new();

	private bool _moved;

	public MoveItemsInteraction( GraphView graph, GraphPointer start, IEnumerable<GraphItem> items )
	{
		_graph = graph;
		_start = start.Scene;

		foreach ( var item in items )
		{
			if ( !item.IsValid() || !item.Movable ) continue;

			_carried.Add( (item, item.Position) );
		}
	}

	public bool IsCarrying => _carried.Count > 0;

	public void Move( GraphPointer pointer )
	{
		//Measured from the press rather than from the last event, because an item may not go exactly where it is put - a node snaps to
		//its grid, and step by step the snap threw the remainder away each time, so moving the pointer slowly moved a node not at all.
		var delta = pointer.Scene - _start;

		if ( !_moved )
		{
			if ( delta.Length < 1.0f ) return;

			_moved = true;
			_graph.PushLayoutUndo( "Move Item" );
		}

		foreach ( var (item, from) in _carried )
		{
			if ( !item.IsValid() ) continue;

			item.Position = from + delta;

			_graph.ItemDragged( item );
		}
	}

	public void Finish( GraphPointer pointer )
	{
		Move( pointer );

		if ( _moved ) _graph.PushRedo();

		foreach ( var (item, _) in _carried )
		{
			if ( item.IsValid() ) _graph.ItemDropped( item );
		}
	}

	public void Cancel()
	{
		foreach ( var (item, from) in _carried )
		{
			if ( item.IsValid() ) item.Position = from;
		}
	}
}

//Nothing happens until the pointer has actually gone somewhere. A plug that is only clicked is a plug that has been clicked, and taking a wire off it and putting it back would be a change the undo stack would have to carry.
/// <summary>A wire being pulled out of a plug, or off one.</summary>
public sealed class WireInteraction : IGraphInteraction
{
	//How far the pointer has to go before this is a drag.
	private const float Threshold = 2.0f;

	private readonly GraphView _graph;
	private readonly Vector2 _start;

	private Plug _plug;
	private Connection _source;
	private bool _dragging;
	private bool _takeOff = true;

	public WireInteraction( GraphView graph, Plug plug, GraphPointer start )
	{
		_graph = graph;
		_plug = plug;
		_start = start.View;
	}

	/// <summary>Taking hold of a wire that is already there, by the end that feeds it.</summary>
	public WireInteraction( GraphView graph, Connection connection, GraphPointer start )
		: this( graph, connection.Output, start )
	{
		_source = connection;
		_takeOff = false;
	}

	public void Move( GraphPointer pointer )
	{
		if ( !_dragging )
		{
			if ( pointer.View.Distance( _start ) < Threshold ) return;

			_dragging = true;

			TakeOffInput();
		}

		_graph.DraggingPlug( _plug, pointer.Scene, _source );
	}

	public void Finish( GraphPointer pointer )
	{
		if ( !_dragging )
		{
			_graph.CancelPlugDrag();
			return;
		}

		_graph.DroppedPlug( _plug, pointer.Scene, _source );
	}

	public void Cancel() => _graph.CancelPlugDrag();

	//Dragging an input that already has a wire takes the wire off it, and what is then being dragged is the other end.
	private void TakeOffInput()
	{
		if ( !_takeOff ) return;
		if ( _plug is not PlugIn plugIn ) return;
		if ( plugIn.Inner.ConnectedOutput is not { } output ) return;

		plugIn.Inner.ConnectedOutput = null;

		_source = _graph.Connections.FirstOrDefault( x => x.Input == plugIn && x.Output?.Inner == output );

		if ( _source?.Output.IsValid() is true ) _plug = _source.Output;
	}
}

/// <summary>One grip of a grid-drawn wire, being pushed along its axis.</summary>
public sealed class ConnectionHandleInteraction : IGraphInteraction
{
	private readonly GraphView _graph;
	private readonly ConnectionHandle _handle;

	private bool _moved;

	public ConnectionHandleInteraction( GraphView graph, ConnectionHandle handle )
	{
		_graph = graph;
		_handle = handle;
	}

	public void Move( GraphPointer pointer )
	{
		if ( !_handle.IsValid() ) return;

		if ( !_moved )
		{
			_moved = true;
			_graph.PushLayoutUndo( "Move Wire" );
		}

		_handle.DragTo( pointer.Scene );
	}

	public void Finish( GraphPointer pointer )
	{
		Move( pointer );

		if ( _moved ) _graph.PushRedo();
	}

	public void Cancel() { }
}

/// <summary>A comment box being pulled bigger or smaller by one of its edges.</summary>
public sealed class ResizeCommentInteraction : IGraphInteraction
{
	private readonly GraphView _graph;
	private readonly CommentUI _comment;
	private readonly ResizeEdges _edges;
	private readonly Vector2 _start;

	private bool _moved;

	public ResizeCommentInteraction( GraphView graph, CommentUI comment, ResizeEdges edges, GraphPointer start )
	{
		_graph = graph;
		_comment = comment;
		_edges = edges;
		_start = start.Scene;
	}

	public void Move( GraphPointer pointer )
	{
		if ( !_comment.IsValid() ) return;

		if ( !_moved )
		{
			if ( pointer.Scene.Distance( _start ) < 1.0f ) return;

			_moved = true;
			_graph.PushLayoutUndo( "Resize Comment" );
		}

		_comment.Resize( _edges, pointer.Scene );
	}

	public void Finish( GraphPointer pointer )
	{
		Move( pointer );

		if ( !_moved ) return;

		_comment.CommitSize();

		_graph.PushRedo();
	}

	public void Cancel() { }
}
