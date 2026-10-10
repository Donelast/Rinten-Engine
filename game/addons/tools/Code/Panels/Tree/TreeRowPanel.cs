namespace Editor;

/// <summary>The editor's shared tree row; trees differ only by TreeItemInfo and hooks, or derive to add</summary>
[StyleSheet( "TreeRowPanel.cs.scss" )]
public class TreeRowPanel<T> : Panel, IEditorDropTarget where T : class
{
	/// <summary>Pixels a level is stepped in by.</summary>
	public const float Indent = 16;

	/// <summary>Pixels before the first level's arrow.</summary>
	public const float IndentBase = 4;

	//The arrow's width and the gap after it - see .tree-row in TreeRowPanel.cs.scss.
	const float ExpanderWidth = 14;
	const float RowGap = 4;

	public T Node { get; }
	public TreeItemInfo Info { get; private set; }
	public int Depth { get; private set; }
	public int Row { get; private set; }

	/// <summary>Drop edge currently highlighted on this row.</summary>
	public TreeDropEdge DropEdge { get; private set; }

	protected TreePanel<T> Tree { get; }

	//Whether a left press may start a drag.
	protected bool PendingDrag { get; private set; }

	//Screen position where the left press started.
	protected Vector2? DragPressAt { get; private set; }

	//The fold arrow.
	protected Rinten.UI.Label Expander { get; }

	//Where the glyph stands; a row may put a picture here instead.
	protected Panel IconSlot { get; }

	//The glyph.
	protected Rinten.UI.Label Icon { get; }

	//The name.
	protected Rinten.UI.Label Title { get; }

	//The far end of the row, after the name: what a row adds of its own goes before the buttons.
	protected Panel Trailing { get; }

	//The plate a row inside a folder stands on, from its glyph to its far end - the indent and the guide lines stay on the ground.
	readonly Panel plate;

	readonly Rinten.UI.Label overlay;
	readonly Rinten.UI.Label count;
	readonly Rinten.UI.Label suffix;
	readonly Panel actions;
	readonly List<Panel> guides = new();

	//Which levels above this row have a line running past it, a bit a level, and whether this row is its parent's last.
	ulong guideLines;
	bool lastChild;

	public TreeRowPanel( TreePanel<T> tree, T node, TreeItemInfo info, int depth, int row )
	{
		Tree = tree;
		Node = node;

		AddClass( "tree-row" );

		//First, so everything else is drawn over it.
		plate = AddChild<Panel>( "plate" );

		Expander = AddChild<Rinten.UI.Label>( "expander material-icon" );

		IconSlot = AddChild<Panel>( "icon-slot" );
		Icon = IconSlot.AddChild<Rinten.UI.Label>( "icon material-icon" );
		overlay = IconSlot.AddChild<Rinten.UI.Label>( "overlay material-icon" );

		Title = AddChild<Rinten.UI.Label>( "title" );
		Title.Tokenize = false;

		count = AddChild<Rinten.UI.Label>( "count" );
		count.Tokenize = false;

		suffix = AddChild<Rinten.UI.Label>( "suffix" );
		suffix.Tokenize = false;

		Trailing = AddChild<Panel>( "trailing" );
		actions = Trailing.AddChild<Panel>( "actions" );

		UpdateLayout( depth, row, info );
	}

	//Where the guide lines run past this row - set by the tree before UpdateLayout.
	internal void SetGuides( ulong lines, bool last )
	{
		guideLines = lines;
		lastChild = last;
	}

	public virtual void UpdateLayout( int depth, int row, TreeItemInfo info )
	{
		var moved = depth != Depth || guides.Count != depth;

		Depth = depth;
		Row = row;
		Info = info;

		Style.PaddingLeft = IndentBase + depth * Indent;

		//From where the glyph starts: past the indent, the arrow and the gap after it.
		plate.Style.Left = IndentBase + depth * Indent + ExpanderWidth + RowGap - 3;

		Apply();
		LayGuides( moved );
		Decorate();
	}

	//Write what the row says from Info.
	void Apply()
	{
		Icon.Text = Info.Icon ?? string.Empty;
		Icon.SetClass( "hidden", string.IsNullOrEmpty( Info.Icon ) );
		Icon.Style.FontColor = Info.IconColor;

		overlay.Text = Info.Overlay ?? string.Empty;
		overlay.SetClass( "hidden", string.IsNullOrEmpty( Info.Overlay ) );

		Title.Text = Info.Label ?? string.Empty;
		Title.Style.FontColor = Info.TextColor;

		count.Text = Info.Count is { } n ? $"({n})" : string.Empty;
		count.SetClass( "hidden", Info.Count is null );
		count.Style.FontColor = Info.TextColor;

		suffix.Text = Info.Suffix ?? string.Empty;
		suffix.SetClass( "hidden", string.IsNullOrEmpty( Info.Suffix ) );
		suffix.SetClass( "material-icon", Info.SuffixIsIcon );

		Tooltip = Info.Tooltip;

		SetClass( "heading", Info.IsHeading );

		//Inside a folder rather than one of the tree's own branches: the only rows that stand on a plate - the root and its folders lie on the dock's ground.
		SetClass( "inner", Depth >= 2 && !Info.IsHeading && Tree.PlatesInnerRows );
		SetClass( "dim", Info.Dim );
		SetClass( "even", Row % 2 == 0 );

		if ( Info.Classes != appliedClasses )
		{
			if ( !string.IsNullOrEmpty( appliedClasses ) )
				foreach ( var c in appliedClasses.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) ) RemoveClass( c );

			if ( !string.IsNullOrEmpty( Info.Classes ) )
				foreach ( var c in Info.Classes.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) ) AddClass( c );

			appliedClasses = Info.Classes;
		}

		BuildActions();
		SyncState();
	}

	string appliedClasses;

	//The buttons at the far end, made once for as many as there are and written over after.
	void BuildActions()
	{
		var list = Info.Actions ?? Array.Empty<TreeRowAction>();

		if ( actions.ChildrenCount != list.Length )
		{
			actions.DeleteChildren( true );

			for ( var i = 0; i < list.Length; i++ )
			{
				var at = i;
				var button = actions.AddChild<Rinten.UI.Label>( "action material-icon" );

				//The action is read when it is pressed, not when the button was made: a row
				//keeps its buttons while what they do is handed over again with every new look.
				button.AddEventListener( "onmousedown", e =>
				{
					e.StopPropagation();

					if ( Info.Actions is { } now && at < now.Length )
					{
						if ( now[at].Pressed is { } pressed ) pressed();
						else Tree.OnRowAction( Node, at );
					}

					Tree.Bump();
				} );
			}
		}

		for ( var i = 0; i < list.Length; i++ )
		{
			var action = list[i];
			var button = (Rinten.UI.Label)actions.GetChild( i );

			button.Text = action.On ? action.Icon : action.OffIcon ?? action.Icon;
			button.Tooltip = action.Tooltip;
			button.SetClass( "on", action.On );
		}
	}

	//What a row looks like, as one number: everything it draws from its info, and not what its buttons
	//do - a tree hands out new ones every time it is asked, and a row is not drawn again for that.
	static int Look( TreeItemInfo info )
	{
		var hash = new HashCode();

		hash.Add( info.Label );
		hash.Add( info.Icon );
		hash.Add( info.HasChildren );
		hash.Add( info.IconColor );
		hash.Add( info.TextColor );
		hash.Add( info.IsHeading );
		hash.Add( info.Count );
		hash.Add( info.Overlay );
		hash.Add( info.Suffix );
		hash.Add( info.SuffixIsIcon );
		hash.Add( info.Dim );
		hash.Add( info.Tooltip );
		hash.Add( info.Classes );

		if ( info.Actions is { } list )
		{
			foreach ( var action in list )
			{
				hash.Add( action.Icon );
				hash.Add( action.On );
				hash.Add( action.Tooltip );
			}
		}

		return hash.ToHashCode();
	}

	//The lines down the left that join a row to its parent: one for each level above it whose run goes on
	//past it, and for its own level a line that turns in to it - stopping halfway on its parent's last child.
	void LayGuides( bool moved )
	{
		if ( moved )
		{
			foreach ( var guide in guides ) guide.Delete( true );
			guides.Clear();

			for ( var level = 0; level < Depth; level++ )
				guides.Add( AddChild<Panel>( "guide" ) );

			if ( Depth > 0 )
				guides.Add( AddChild<Panel>( "guide tick" ) );
		}

		for ( var level = 0; level < Depth; level++ )
		{
			var guide = guides[level];
			var own = level == Depth - 1;
			var runs = own || (guideLines & (1UL << level)) != 0;

			guide.Style.Left = IndentBase + level * Indent + Indent * 0.5f - 0.5f;
			guide.SetClass( "hidden", !runs );
			guide.SetClass( "half", own && lastChild );
		}

		if ( Depth > 0 )
			guides[Depth].Style.Left = IndentBase + (Depth - 1) * Indent + Indent * 0.5f;
	}

	//Add what this kind of row shows beyond the shared parts - a thumbnail, a meter. Called after every layout.
	protected virtual void Decorate() { }

	//Whether the row can be opened, which is what the arrow says.
	protected virtual bool CanExpand => Info.HasChildren;

	//Rebuilds only when selected/open changes, or every frame for trees using TreePanel.LiveInfo
	protected override int BuildHash()
	{
		var live = Tree.LiveInfo( Node, Info );

		return HashCode.Combine( base.BuildHash(), Tree.IsSelected( Node ), CanExpand && Tree.IsOpen( Node ),
			live is { } info ? Look( info ) : 0 );
	}

	protected override void OnBuildHashChanged()
	{
		base.OnBuildHashChanged();

		if ( Tree.LiveInfo( Node, Info ) is { } live )
		{
			var changed = Look( live ) != Look( Info );

			Info = live;

			if ( changed )
			{
				Apply();
				Decorate();
				return;
			}
		}

		SyncState();
	}

	void SyncState()
	{
		SetClass( "selected", Tree.IsSelected( Node ) );

		var expandable = CanExpand;
		Expander.SetClass( "invisible", !expandable );
		Expander.Text = expandable ? (Tree.IsOpen( Node ) ? "expand_more" : "chevron_right") : string.Empty;
	}

	public void ScrollIntoView()
	{
		var body = Parent;
		if ( body is null ) return;

		//Rects are on screen, so the current scroll is added back to land in the body's content space.
		var top = Box.Rect.Top - body.Box.Rect.Top + body.ScrollOffset.y;
		body.ScrollOffset = new Vector2( 0, top.Clamp( 0, body.ScrollSize.y ) );
	}

	//Tick runs only when asked, i.e. while a press might be turning into a drag
	protected override bool TickWhenAsked => true;

	public override void Tick()
	{
		base.Tick();

		if ( !PendingDrag ) return;

		TryStartDrag();

		//Watched for as long as the button is held. The release, wherever it happens, is the row's own mouse-up to deal with, as it always was.
		if ( PendingDrag && ( Application.MouseButtons & MouseButtons.Left ) != 0 ) RequestTick();
	}

	//Arms a left press so it can become a drag.
	protected void ArmDrag( MousePanelEvent e )
	{
		PendingDrag = true;
		DragPressAt = Application.CursorPosition;

		//The drag is watched for from the next frame on.
		RequestTick();
	}

	//Clears a pending drag without starting one.
	protected void DisarmDrag()
	{
		PendingDrag = false;
		DragPressAt = null;
	}

	//Completes a pending press as a click.
	protected void CompletePendingClick( MousePanelEvent e )
	{
		if ( !PendingDrag ) return;

		DisarmDrag();
		Tree.RowClicked( this, e );
	}

	//Updates drop-edge classes on the row.
	internal void SetDropEdge( TreeDropEdge edge )
	{
		if ( DropEdge == edge ) return;

		DropEdge = edge;
		SetClass( "drop-top", edge == TreeDropEdge.Top );
		SetClass( "drop-bottom", edge == TreeDropEdge.Bottom );
		SetClass( "drop-inside", edge == TreeDropEdge.Inside );
	}

	bool IEditorDropTarget.AcceptEditorDrops => Tree.SupportsDragDrop;

	DropAction IEditorDropTarget.OnEditorDragHover( Widget.DragEvent ev ) => Tree.RowDragHover( this, ev );

	void IEditorDropTarget.OnEditorDragDrop( Widget.DragEvent ev ) => Tree.RowDragDrop( this, ev );

	void IEditorDropTarget.OnEditorDragLeave() => Tree.RowDragLeave();

	void TryStartDrag()
	{
		if ( !PendingDrag || DragPressAt is null ) return;
		if ( (Application.MouseButtons & MouseButtons.Left) == 0 ) return;
		if ( DragPressAt.Value.Distance( Application.CursorPosition ) < 4 ) return;

		DisarmDrag();

		if ( Tree.SupportsDragDrop )
			Tree.StartRowDrag( this );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		Tree.TryCancelRename( e.Target as Panel ?? this );
		base.OnMouseDown( e );

		if ( Tree.OnRowPressed( Node, e ) )
		{
			e.StopPropagation();
			return;
		}

		if ( e.MouseButton == MouseButtons.Right )
		{
			Tree.RowContextMenu( this );
			e.StopPropagation();
			return;
		}

		if ( e.MouseButton != MouseButtons.Left ) return;

		//A heading is a fold rather than a thing: pressing it anywhere folds it.
		if ( CanExpand && ( e.Target == Expander || Info.IsHeading ) )
		{
			Tree.ToggleOpen( Node, Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) );
			e.StopPropagation();
			return;
		}

		//Pressed rather than released: a press that turns into a drag is not a click, and one that does not is completed on the way up.
		ArmDrag( e );
		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		CompletePendingClick( e );
		DisarmDrag();
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		base.OnDoubleClick( e );

		if ( e.MouseButton != MouseButtons.Left || e.Target == Expander ) return;

		if ( Tree.OnRowActivated( Node ) )
		{
			e.StopPropagation();
			return;
		}

		if ( CanExpand )
		{
			Tree.ToggleOpen( Node );
			e.StopPropagation();
		}
	}
}
