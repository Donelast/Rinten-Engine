using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Text;
using Rinten.Utility;

namespace Editor.NodeEditor;

public static class GraphClipboard
{
	public static void Copy( string ident, string value )
	{
		using var stream = new MemoryStream();

		using ( var compressed = new GZipStream( stream, CompressionMode.Compress ) )
		{
			var data = Encoding.UTF8.GetBytes( value );
			compressed.Write( data, 0, data.Length );
		}

		EditorUtility.Clipboard.Copy( $"{ident}:{Convert.ToBase64String( stream.ToArray() )}" );
	}

	public static bool CanPaste( string ident )
	{
		var value = EditorUtility.Clipboard.Paste();

		return value?.StartsWith( $"{ident}:", StringComparison.Ordinal ) == true;
	}

	public static bool TryPaste( string ident, out string value )
	{
		value = null;

		var buffer = EditorUtility.Clipboard.Paste();
		var prefix = $"{ident}:";

		if ( buffer?.StartsWith( prefix, StringComparison.Ordinal ) != true ) return false;

		try
		{
			using var stream = new MemoryStream( Convert.FromBase64String( buffer[prefix.Length..] ) );
			using var compressed = new GZipStream( stream, CompressionMode.Decompress );
			using var output = new MemoryStream();

			compressed.CopyTo( output );

			value = Encoding.UTF8.GetString( output.ToArray() );
			return true;
		}
		catch ( FormatException )
		{
			Log.Warning( "Clipboard data is not valid base64" );
			return false;
		}
		catch ( InvalidDataException )
		{
			Log.Warning( "Clipboard data is not valid compressed data" );
			return false;
		}
	}
}

partial class GraphView
{
	private IGraph _graph;

	public IGraph Graph
	{
		get => _graph;
		set
		{
			if ( _graph == value ) return;

			_graph = value;

			RebuildFromGraph();
		}
	}

	protected virtual INodeType RerouteNodeType => null;
	protected virtual INodeType CommentNodeType => null;

	//Whether this graph has a reroute node to offer.
	protected bool CanCreateReroute => RerouteNodeType is not null;

	protected virtual string ClipboardIdent => "graphview";

	protected virtual bool CanConnect( PlugOut output, PlugIn input ) => true;

	protected virtual bool CanDisconnectByDroppingConnection => true;

	//Undo

	internal IDisposable UndoScope( string name )
	{
		PushUndo( name );

		return new DisposeAction( PushRedo );
	}

	public virtual void PushUndo( string name )
	{
	}

	public virtual void PushLayoutUndo( string name ) => PushUndo( name );

	public virtual void PushRedo()
	{
	}

	//The clipboard

	public void CopySelection()
	{
		var nodes = SelectedItems.OfType<NodeUI>().Select( x => x.Node ).Where( x => x.CanClone ).ToArray();

		if ( !nodes.Any() ) return;

		GraphClipboard.Copy( ClipboardIdent, _graph.SerializeNodes( nodes ) );
	}

	public bool CanPasteSelection() => GraphClipboard.CanPaste( ClipboardIdent );

	public void PasteSelection()
	{
		if ( !GraphClipboard.TryPaste( ClipboardIdent, out var decompressed ) ) return;

		try
		{
			//Old clipboard data can hold nodes that can't be duplicated, like result nodes.
			var nodes = _graph.DeserializeNodes( decompressed ).Where( x => x.CanClone ).ToArray();

			if ( !nodes.Any() ) return;

			using var undoScope = UndoScope( "Paste Selection" );

			OnPaste( nodes );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Paste is not valid json: {e}" );
		}
	}

	public void DuplicateSelection()
	{
		var selected = SelectedItems.OfType<NodeUI>().Select( x => x.Node ).Where( x => x.CanClone ).ToArray();

		if ( !selected.Any() ) return;

		var pasted = _graph.DeserializeNodes( _graph.SerializeNodes( selected ) ).ToArray();

		using var undoScope = UndoScope( "Duplicate Selection" );

		OnPaste( pasted );
	}

	private void OnPaste( IReadOnlyList<INode> nodes )
	{
		var average = new Vector2( nodes.Average( x => x.Position.x ), nodes.Average( x => x.Position.y ) );

		ClearSelection();

		BuildFromNodes( nodes, true, -average + _lastScenePosition, true );
	}

	public void CutSelection()
	{
		if ( !SelectedItems.OfType<NodeUI>().Any() ) return;

		using var undoScope = UndoScope( "Cut Selection" );

		CopySelection();

		foreach ( var connection in SelectedItems.OfType<Connection>().ToArray() )
		{
			RemoveConnection( connection );
			connection.Destroy();
		}

		foreach ( var node in SelectedItems.OfType<NodeUI>().ToArray() )
		{
			RemoveNode( node );
		}

		ClearSelection();
	}

	//Removing

	public void DeleteNode( NodeUI node )
	{
		if ( !node.IsValid() ) return;

		using var undoScope = UndoScope( "Delete Node" );

		RemoveNode( node );
		ClearSelection();
	}

	public void DeleteSelection()
	{
		var connections = SelectedItems.OfType<Connection>().ToArray();
		var nodes = SelectedItems.OfType<NodeUI>().ToArray();

		if ( connections.Length == 0 && nodes.Length == 0 ) return;

		using var undoScope = UndoScope( "Delete Selection" );

		foreach ( var connection in connections )
		{
			RemoveConnection( connection );
			connection.Disconnect();
			connection.Destroy();
		}

		foreach ( var node in nodes )
		{
			RemoveNode( node );
		}

		ClearSelection();
	}

	public virtual void RemoveNode( NodeUI node )
	{
		foreach ( var connection in _connections.Where( x => x.IsAttachedTo( node ) ).ToList() )
		{
			connection.Disconnect();
			connection.Destroy();
		}

		if ( !node.Node.CanRemove ) return;

		Graph?.RemoveNode( node.Node );
		node.Destroy();
	}

	/// <summary>Perform automated fixes and replace obsolete nodes.</summary>
	public virtual void CleanUp()
	{
	}

	//Making nodes

	public virtual CommentUI CreateNewComment( string text, CommentColor color, Vector2 position, Vector2 size )
	{
		using var undoScope = UndoScope( "Add Comment" );

		return (CommentUI)CreateNewNode( CommentNodeType, node =>
		{
			node.Position = position.SnapToGrid( GridSize );

			var comment = (ICommentNode)node;

			comment.Size = size;
			comment.Color = color;
			comment.Title = text;
		} );
	}

	public void CreateNewReroute( Vector2 position )
	{
		if ( !CanCreateReroute ) return;

		using var undoScope = UndoScope( "Add Reroute" );

		CreateNewNode( RerouteNodeType, position );
	}

	public virtual NodeUI CreateNewNode( INodeType type, Vector2 position )
	{
		return CreateNewNode( type, node => node.Position = position.SnapToGrid( GridSize ) );
	}

	public virtual NodeUI CreateNewNode( INodeType type, Action<INode> onCreated = null )
	{
		if ( type is null ) return null;

		var node = type.CreateNode( Graph );

		if ( node is null ) return null;

		onCreated?.Invoke( node );

		Graph?.AddNode( node );

		OnNodeCreated( node );

		return AddItem( node.CreateUI( this ) );
	}

	protected virtual NodeUI CreateNodeUI( INode node ) => AddItem( node.CreateUI( this ) );

	public virtual void CreateNewNode( INodeType type, Vector2 position, Plug targetPlug, bool selected = true )
	{
		using var undoScope = UndoScope( "Add Node" );

		var nodeUI = CreateNewNode( type, position );

		if ( !nodeUI.IsValid() ) return;

		if ( selected ) Select( nodeUI, alone: true );

		if ( !targetPlug.IsValid() ) return;

		if ( targetPlug is PlugIn plugIn )
		{
			if ( !type.TryGetOutput( plugIn.Inner.Type, out var targetName ) ) return;

			if ( nodeUI.Outputs.FirstOrDefault( x => x.Inner.Identifier == targetName ) is { } match )
			{
				CreateConnection( match, plugIn );
			}

			return;
		}

		if ( targetPlug is PlugOut plugOut )
		{
			if ( !type.TryGetInput( plugOut.Inner.Type, out var targetName ) ) return;

			if ( nodeUI.Inputs.FirstOrDefault( x => x.Inner.Identifier == targetName ) is { } match )
			{
				CreateConnection( plugOut, match );
			}
		}
	}

	protected virtual void OnNodeCreated( INode node )
	{
	}

	public NodeUI FindNode( INode node )
	{
		if ( node is null ) return null;

		return Items.OfType<NodeUI>().FirstOrDefault( x => x.Node == node );
	}

	public NodeUI SelectNode( INode node )
	{
		if ( node is null ) return null;

		var nodeUI = FindNode( node );

		Select( nodeUI, alone: true );

		return nodeUI;
	}

	public void UpdateNode( INode node )
	{
		if ( node is null ) return;

		if ( FindNode( node ) is { } nodeUI && nodeUI.IsValid() ) nodeUI.Update();
	}

	//Wires

	//The plug a wire being dragged is over, which lights up either end.
	internal Plug DropTarget { get; set; }

	//The wire drawn while one is being pulled about.
	internal Connection Preview { get; set; }

	protected virtual Connection CreateConnection( PlugOut nodeOutput, PlugIn dropTarget, bool uiOnly = false )
	{
		ArgumentNullException.ThrowIfNull( nodeOutput );
		ArgumentNullException.ThrowIfNull( dropTarget );

		if ( !uiOnly )
		{
			dropTarget.Inner.ConnectedOutput = nodeOutput.Inner;
		}

		if ( !nodeOutput.Inner.ShowConnection || !dropTarget.Inner.ShowConnection ) return null;

		var connection = new Connection( this, nodeOutput, dropTarget );

		_connections.Add( connection );

		connection.Layout();

		return connection;
	}

	public virtual void RemoveConnection( Connection c )
	{
		if ( c.Input.IsValid() && c.Input.Inner.ConnectedOutput == c.Output?.Inner )
		{
			c.Input.Inner.ConnectedOutput = null;
		}

		_connections.Remove( c );

		RedrawConnections();
	}

	internal void RemoveConnections( PlugIn plugIn )
	{
		foreach ( var connection in _connections.Where( x => x.Input == plugIn ).ToArray() )
		{
			connection.Disconnect();
			connection.Destroy();
		}
	}

	internal void RemoveConnections( PlugOut plugOut )
	{
		foreach ( var connection in _connections.Where( x => x.Output == plugOut ).ToArray() )
		{
			connection.Disconnect();
			connection.Destroy();
		}
	}

	internal void RerouteConnection( Connection c, Vector2 scenePosition )
	{
		if ( !CanCreateReroute ) return;

		using var undoScope = UndoScope( "Reroute Connection" );

		var nodeUI = CreateNewNode( RerouteNodeType, scenePosition );

		CreateConnection( c.Output, nodeUI.Inputs.FirstOrDefault() );
		CreateConnection( nodeUI.Outputs.FirstOrDefault(), c.Input );

		_connections.Remove( c );
		c.Destroy();
	}

	//A node's box has changed, so the wires that end on it have moved.
	internal void NodeGeometryChanged( NodeUI node )
	{
		foreach ( var connection in _connections )
		{
			if ( !connection.IsAttachedTo( node ) ) continue;

			connection.Layout();
		}

		PlaceAttached( node );
	}

	internal void DraggingPlug( Plug plug, Vector2 scenePosition, Connection source )
	{
		var dropTarget = PlugAt( scenePosition );

		dropTarget = plug is PlugIn ? dropTarget as PlugOut : dropTarget as PlugIn;

		if ( dropTarget?.Node == plug.Node ) dropTarget = null;

		var output = plug as PlugOut ?? dropTarget as PlugOut;
		var input = plug as PlugIn ?? dropTarget as PlugIn;
		var isInvalid = output.IsValid() && input.IsValid() && !CanConnect( output, input );

		DropTarget?.Update();
		DropTarget = dropTarget;
		DropTarget?.Update();

		Preview ??= new Connection( this, plug );

		Preview.IsInvalidPreview = isInvalid;
		Preview.LayoutForPreview( plug, scenePosition, DropTarget );
	}

	internal void CancelPlugDrag()
	{
		DropTarget?.Update();
		DropTarget = null;

		Preview = null;

		RedrawConnections();
	}

	internal void DroppedPlug( Plug plug, Vector2 scenePosition, Connection source )
	{
		var disconnected = false;
		var connected = false;

		if ( source.IsValid() && !DropTarget.IsValid() && !CanDisconnectByDroppingConnection )
		{
			CancelPlugDrag();
			return;
		}

		if ( source.IsValid() )
		{
			//Dropped back on the connection it already had: nothing has changed.
			if ( source.Input == DropTarget )
			{
				CancelPlugDrag();
				return;
			}

			disconnected = true;

			PushUndo( DropTarget.IsValid() ? "Change Connection" : "Drop Connection" );

			source.Disconnect();
			source.Destroy();
		}

		var output = plug as PlugOut ?? DropTarget as PlugOut;
		var input = plug as PlugIn ?? DropTarget as PlugIn;

		if ( DropTarget.IsValid() && DropTarget.Node != plug.Node && CanConnect( output, input ) )
		{
			connected = true;

			if ( !disconnected ) PushUndo( "Create Connection" );

			foreach ( var connection in _connections.Where( x => x.Input == DropTarget ).ToList() )
			{
				RemoveConnection( connection );
				connection.Destroy();
			}

			CreateConnection( output, input );
		}

		DropTarget?.Update();
		DropTarget = null;

		if ( disconnected || connected ) PushRedo();

		if ( !disconnected && !connected )
		{
			if ( _createReroute && CanCreateReroute )
			{
				CreateNewNode( RerouteNodeType, scenePosition, plug, false );
			}
			else
			{
				OpenContextMenu( Application.CursorPosition, scenePosition, plug, onClose: CancelPlugDrag );
				return;
			}
		}

		Preview = null;

		RedrawConnections();
	}

	//Building from the graph

	public void RebuildFromGraph()
	{
		CancelInteraction();

		Preview = null;

		DropTarget?.Update();
		DropTarget = null;

		foreach ( var connection in _connections.ToArray() )
		{
			connection.Destroy();
		}

		_connections.Clear();

		OnClear();

		DeleteAllItems();

		if ( _graph is not null ) BuildFromNodes( _graph.Nodes, true );

		OnRebuild();
		RestoreViewFromCookie();
	}

	protected virtual void OnClear()
	{
	}

	protected virtual void OnRebuild()
	{
	}

	/// <summary>Create or update the panels for a set of nodes and the wires between them.</summary>
	public virtual void BuildFromNodes( IEnumerable<INode> nodes, bool insert, Vector2 offset = default, bool selectNew = false )
	{
		var nodesSet = nodes.ToImmutableHashSet();

		if ( !insert )
		{
			foreach ( var nodeUi in Items.OfType<NodeUI>().Where( x => !nodesSet.Contains( x.Node ) ).ToArray() )
			{
				nodeUi.Destroy();
			}
		}

		foreach ( var node in nodesSet )
		{
			if ( !insert && FindNode( node ) is { } existing )
			{
				existing.Rebuild();
				continue;
			}

			node.Position += offset;

			var nodeUi = node.CreateUI( this );

			if ( !nodeUi.IsValid() ) continue;

			AddItem( nodeUi );

			nodeUi.Position = node.Position;

			if ( selectNew ) Select( nodeUi, alone: false );
		}

		UpdateConnections( nodesSet, selectNew );

		//Last, because a node cannot be put against a plug until both it and the thing it is
		//plugged into exist - and they are made in whatever order the graph happens to list them.
		PlaceAllAttached();
	}

	public void UpdateConnections( IEnumerable<INode> nodes, bool selectNew = false )
	{
		var nodeDict = new Dictionary<INode, NodeUI>();
		var connectionSet = new HashSet<(IPlug, IPlug)>();

		foreach ( var connection in _connections.ToArray() )
		{
			if ( connection.Input?.Inner.ConnectedOutput != connection.Output?.Inner
				|| connection.Input?.Inner.ShowConnection is not true
				|| connection.Output?.Inner.ShowConnection is not true )
			{
				connection.Destroy();
				_connections.Remove( connection );
			}
			else
			{
				connectionSet.Add( (connection.Output.Inner, connection.Input.Inner) );
			}
		}

		foreach ( var nodeUi in Items.OfType<NodeUI>() )
		{
			nodeDict[nodeUi.Node] = nodeUi;
		}

		var nodeSet = new HashSet<INode>( nodes );

		//Anything wired to one of these has moved too, so it is part of the set.
		foreach ( var node in Graph.Nodes )
		{
			if ( nodeSet.Contains( node ) ) continue;

			foreach ( var input in node.Inputs )
			{
				if ( !nodeSet.Contains( input.ConnectedOutput?.Node ) ) continue;

				nodeSet.Add( node );
				break;
			}
		}

		foreach ( var node in nodeSet )
		{
			foreach ( var input in node.Inputs )
			{
				if ( input.ConnectedOutput is not { } output ) continue;
				if ( !input.ShowConnection || !output.ShowConnection ) continue;
				if ( !connectionSet.Add( (output, input) ) ) continue;

				nodeDict.TryGetValue( node, out var a );
				nodeDict.TryGetValue( output.Node, out var b );

				var dropTarget = a?.Inputs.FirstOrDefault( x => x.Inner == input );
				var nodeOutput = b?.Outputs.FirstOrDefault( x => x.Inner == output );

				if ( !dropTarget.IsValid() || !nodeOutput.IsValid() ) continue;

				var connection = CreateConnection( nodeOutput, dropTarget, true );

				if ( selectNew && connection.IsValid() ) Select( connection, alone: false );
			}
		}

		foreach ( var connection in _connections.ToArray() )
		{
			if ( connectionSet.Contains( (connection.Output?.Inner, connection.Input?.Inner) ) )
			{
				connection.Layout();
			}
		}
	}
}
