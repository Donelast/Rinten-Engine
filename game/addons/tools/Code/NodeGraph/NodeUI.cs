using Rinten.UI;
using System.Text.RegularExpressions;
using DisplayInfo = Rinten.DisplayInfo;
using UILabel = Rinten.UI.Label;

namespace Editor.NodeEditor;

/// <summary>One node as a panel: title bar, input and output columns, sized by flexbox to its content</summary>
public partial class NodeUI : GraphItem
{
	public INode Node { get; protected set; }

	public DisplayInfo DisplayInfo => Node.DisplayInfo;

	public Color SelectionOutline = Color.Parse( "#ff99c8" ) ?? default;
	public Color PrimaryColor = Color.Parse( "#ff99c8" ) ?? default;

	public List<PlugIn> Inputs { get; } = new();
	public List<PlugOut> Outputs { get; } = new();

	/// <summary>Every socket on the node, whichever way round it faces.</summary>
	public IEnumerable<Plug> AllPlugs => Inputs.Cast<Plug>().Concat( Outputs );

	//Sized by its contents, so the size is read back rather than written.
	protected override bool WritesSize => false;

	protected Panel TitleBar { get; set; }
	protected Panel Body { get; set; }
	protected Panel InputColumn { get; set; }
	protected Panel MiddleColumn { get; set; }
	protected Panel OutputColumn { get; set; }

	private Panel _titleInputs;
	private Panel _titleOutputs;
	private IconPanel _titleIcon;
	private UILabel _titleText;
	private IconPanel _bodyIcon;
	private UILabel _bodyName;
	private ThumbnailPanel _thumbnail;
	private UILabel _error;

	private bool _updatingPlugs;
	private int _lastGeometryHash;

	public NodeUI( GraphView graph, INode node ) : base( graph )
	{
		Node = node;

		AddClass( "node" );

		Selectable = true;
		Movable = true;
		ZIndex = 1.0f;

		Position = node.Position;

		BuildContent();
		UpdatePlugs();

		Node.Changed += MarkNodeChanged;
	}

	//Builds what the node is made of. A comment is a node with something else inside it, and this is where it says so.
	protected virtual void BuildContent()
	{
		TitleBar = AddChild<Panel>( "title" );
		_titleInputs = TitleBar.AddChild<Panel>( "title-plugs in" );
		_titleIcon = TitleBar.AddChild<IconPanel>( "icon" );
		_titleText = TitleBar.AddChild<UILabel>( "name" );
		_titleText.Tokenize = false;
		_titleOutputs = TitleBar.AddChild<Panel>( "title-plugs out" );

		Body = AddChild<Panel>( "body" );
		InputColumn = Body.AddChild<Panel>( "inputs" );
		MiddleColumn = Body.AddChild<Panel>( "middle" );
		OutputColumn = Body.AddChild<Panel>( "outputs" );

		_thumbnail = MiddleColumn.AddChild<ThumbnailPanel>( "thumb" );
		_bodyIcon = MiddleColumn.AddChild<IconPanel>( "body-icon" );
		_bodyName = MiddleColumn.AddChild<UILabel>( "body-name" );
		_bodyName.Tokenize = false;

		_error = AddChild<UILabel>( "error" );
		_error.Tokenize = false;
		_error.Text = "ERROR!!!";

		Node.BuildContent( MiddleColumn );
	}

	//Whether what the node shows is out of date with what it says.
	private bool _dirty = true;

	public override void OnDeleted()
	{
		base.OnDeleted();

		Node.Changed -= MarkNodeChanged;
	}

	public void Rebuild() => OnRebuild();

	protected virtual void OnRebuild()
	{
		Position = Node.Position;

		UpdatePlugs();
		Update();
	}

	public void MarkNodeChanged()
	{
		if ( !IsValid || _updatingPlugs ) return;

		_updatingPlugs = true;

		try
		{
			UpdatePlugs();
		}
		finally
		{
			_updatingPlugs = false;
		}

		Update();
	}

	/// <summary>Says the node needs re-reading; the reading happens on the next tick.</summary>
	public void Update() => _dirty = true;

	//What INode.IsReachable was on the last tick, so a change of it is seen.
	private bool _wasReachable = true;

	public static string FormatToolTip( string name, string description, Type type = null, string error = null )
	{
		var tooltip = name.WithColor( "#9CDCFE" );

		if ( type is not null ) tooltip += $": {type.ToRichText()}";

		var desc = description ?? "No description given.";

		tooltip += desc.StartsWith( "<br/>", StringComparison.OrdinalIgnoreCase ) ? desc : $"<br/>{desc}";

		foreach ( var message in error?.Split( Environment.NewLine, StringSplitOptions.RemoveEmptyEntries ) ?? Array.Empty<string>() )
		{
			tooltip += $"<br/><span style=\"font-size: 11px; color: {Theme.Red.Hex};\">{message}</span>";
		}

		return tooltip;
	}

	//Puts the sockets the node says it has against the ones it is showing, and makes up the difference.
	private void UpdatePlugs()
	{
		if ( !IsValid ) return;

		for ( var i = Inputs.Count - 1; i >= 0; --i )
		{
			var plugIn = Inputs[i];

			if ( Node.Inputs.Any( x => x == plugIn.Inner ) ) continue;

			Inputs.RemoveAt( i );

			var connection = plugIn.Connection;
			connection?.Disconnect();
			connection?.Destroy();

			plugIn.Delete();
		}

		for ( var i = Outputs.Count - 1; i >= 0; --i )
		{
			var plugOut = Outputs[i];

			if ( Node.Outputs.Any( x => x == plugOut.Inner ) ) continue;

			Outputs.RemoveAt( i );

			Graph.RemoveConnections( plugOut );

			plugOut.Delete();
		}

		var index = 0;

		foreach ( var plug in Node.Inputs )
		{
			var match = Inputs.FirstOrDefault( x => x.Inner == plug );

			if ( !match.IsValid() )
			{
				Inputs.Insert( index, CreateInput( plug ) );
			}
			else if ( Inputs.IndexOf( match ) != index )
			{
				Inputs.Remove( match );
				Inputs.Insert( index, match );
			}

			++index;
		}

		index = 0;

		foreach ( var plug in Node.Outputs )
		{
			var match = Outputs.FirstOrDefault( x => x.Inner == plug );

			if ( !match.IsValid() )
			{
				Outputs.Insert( index, CreateOutput( plug ) );
			}
			else if ( Outputs.IndexOf( match ) != index )
			{
				Outputs.Remove( match );
				Outputs.Insert( index, match );
			}

			++index;
		}

		Reparent();

		Update();
	}

	protected virtual PlugIn CreateInput( IPlugIn plug ) => new( this, plug );
	protected virtual PlugOut CreateOutput( IPlugOut plug ) => new( this, plug );

	//Puts each socket in the column it belongs to, in the order the node lists them.
	private void Reparent()
	{
		if ( InputColumn is null || OutputColumn is null ) return;

		foreach ( var plug in Inputs )
		{
			var wanted = plug.Inner.InTitleBar ? _titleInputs : InputColumn;

			if ( plug.Parent != wanted ) wanted.AddChild( plug );
		}

		foreach ( var plug in Outputs )
		{
			var wanted = plug.Inner.InTitleBar ? _titleOutputs : OutputColumn;

			if ( plug.Parent != wanted ) wanted.AddChild( plug );
		}

		//The columns are read down in the order the node lists its sockets, and a socket that appeared later was added to the end whatever its place.
		InputColumn.SortChildren<PlugIn>( Inputs.IndexOf );
		_titleInputs.SortChildren<PlugIn>( Inputs.IndexOf );
		OutputColumn.SortChildren<PlugOut>( Outputs.IndexOf );
		_titleOutputs.SortChildren<PlugOut>( Outputs.IndexOf );
	}

	//Room for a node that asked for more of it than its rows take.
	protected virtual void Layout()
	{
		if ( Body is null ) return;

		Body.Style.MinWidth = Node.ExpandSize.x > 0.0f ? Length.Pixels( Node.ExpandSize.x ) : (Length?)null;
		Body.Style.MinHeight = Node.ExpandSize.y > 0.0f ? Length.Pixels( Node.ExpandSize.y ) : (Length?)null;

		foreach ( var plug in AllPlugs )
		{
			plug.Layout();
		}
	}

	private static Regex WordWrapPointRegex { get; } = new( "[^A-Z][A-Z]|_[^_]" );

	//Lets a name wrap in more places, like after an underscore or between the words of a PascalCase name.
	private static string FixWordWrapping( string value )
	{
		return WordWrapPointRegex.Replace( value, x => $"{x.Value[0]}\x200B{x.Value[1]}" );
	}

	//Re-reads everything the node shows.
	protected virtual void OnUpdate()
	{
		var display = DisplayInfo;
		var hasError = !string.IsNullOrWhiteSpace( Node.ErrorMessage );
		var hasThumb = Node.Thumbnail is not null;
		var hasTitle = Node.HasTitleBar;

		PrimaryColor = Node.GetPrimaryColor( Graph );

		if ( Selected ) PrimaryColor = SelectionOutline;
		else if ( !Node.IsReachable ) PrimaryColor = PrimaryColor.Desaturate( 0.5f ).Darken( 0.25f );

		SetClass( "no-title", !hasTitle );
		SetClass( "has-thumb", hasThumb );
		SetClass( "error", hasError );
		SetClass( "unreachable", !Node.IsReachable );

		TitleBar.SetClass( "hidden", !hasTitle );
		_error.SetClass( "hidden", !hasError );

		Style.BackgroundColor = PrimaryColor.Darken( hasTitle ? 0.5f : 0.6f );
		Body.Style.BackgroundColor = PrimaryColor.Darken( 0.6f );

		_titleIcon.SetClass( "hidden", display.Icon is null );
		_titleIcon.Text = display.Icon ?? string.Empty;
		_titleIcon.Style.FontColor = PrimaryColor.Lighten( 0.7f ).WithAlpha( 0.7f );

		_titleText.Text = display.Name ?? string.Empty;
		_titleText.Style.FontColor = PrimaryColor.Lighten( 0.8f );

		UpdateMiddle( display, hasTitle, hasThumb );
		UpdateOutline( hasError );

		Tooltip = FormatToolTip( display.Name, display.Description, null, Node.ErrorMessage );

		foreach ( var plug in AllPlugs )
		{
			plug.Update();
		}
	}

	//What sits between the two columns: a picture, an icon, or nothing.
	private void UpdateMiddle( DisplayInfo display, bool hasTitle, bool hasThumb )
	{
		_thumbnail.Pixmap = Node.Thumbnail;
		_thumbnail.SetClass( "hidden", !hasThumb );

		//A node with no title bar and no picture is an icon and nothing else - which is what an operator looks like.
		var iconOnly = !hasTitle && !hasThumb && display.Icon is not null;

		_bodyIcon.SetClass( "hidden", !iconOnly );
		_bodyIcon.Text = iconOnly ? display.Icon : string.Empty;

		//The name goes under the picture on a node that is a picture, because there is no title bar to put it in.
		var nameUnderThumb = !hasTitle && hasThumb && display.Name is not null;

		_bodyName.SetClass( "hidden", !nameUnderThumb );
		_bodyName.Text = nameUnderThumb ? FixWordWrapping( display.Name ) : string.Empty;

		MiddleColumn.SetClass( "hidden", !hasThumb && !iconOnly && !nameUnderThumb );
	}

	//The ring round the node, which says selected, or hovered, or wrong.
	private void UpdateOutline( bool hasError )
	{
		var highlight = Node is INodeHighlight { HighlightOutlineColor: { a: > 0.0f } colour } ? colour : (Color?)null;

		Style.BorderColor = hasError
			? Color.Red
			: Selected
				? SelectionOutline
				: highlight ?? SelectionOutline.Darken( 0.25f );

		SetClass( "highlight", highlight is not null );
	}

	protected override void OnSelectionChanged() => Update();

	protected override void OnPositionChanged()
	{
		//A node being put against its plug is not being moved to the grid, and is not being taken off the plug either - that is what dragging is.
		if ( Graph?.PlacingAttached != true )
		{
			Position = Position.SnapToGrid( Graph?.GridSize ?? 1.0f );
		}

		if ( Node is not null ) Node.Position = Position;

		Graph?.NodeGeometryChanged( this );
	}

	//Read from the boxes rather than worked out, because the boxes are what flexbox decided and a wire ends where the socket actually is.
	/// <summary>Notices when the node's own layout has settled somewhere new, so the wires that end on it can follow.</summary>
	public override void Tick()
	{
		base.Tick();

		//The wires are kept drawn (ConnectionLayer.RetainsDrawing) and their colour dims with
		//an unreachable node, so a change of it is a redraw of them - and of the node itself.
		var reachable = Node?.IsReachable ?? true;

		if ( reachable != _wasReachable )
		{
			_wasReachable = reachable;
			_dirty = true;

			Graph?.RedrawConnections();
		}

		if ( _dirty )
		{
			_dirty = false;

			Layout();
			OnUpdate();
		}

		ReadSizeFromBox();

		var hash = HashCode.Combine( Position, Size );

		foreach ( var plug in AllPlugs )
		{
			hash = HashCode.Combine( hash, plug.ConnectionPosition );
		}

		if ( hash == _lastGeometryHash ) return;

		_lastGeometryHash = hash;

		Graph?.NodeGeometryChanged( this );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( e.MouseButton != MouseButtons.Left ) return;

		e.StopPropagation();

		if ( GraphView.HasCtrl )
		{
			if ( Selected )
			{
				Graph.Deselect( this );
				return;
			}

			Graph.Select( this, alone: false );
		}
		else if ( !Selected )
		{
			Graph.Select( this, alone: true );
		}

		Graph.Begin( new MoveItemsInteraction( Graph, Graph.CurrentPointer,
			Graph.SelectedItems.OfType<GraphItem>() ), e );
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		Node.OnDoubleClick();

		e.StopPropagation();
	}

	protected override void OnMouseOver( MousePanelEvent e ) => Update();

	protected override void OnMouseOut( MousePanelEvent e ) => Update();
}
