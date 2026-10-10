using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

/// <summary>Dock listing the clips of the target skeleton; drag one onto the graph to make a clip node</summary>
public class ClipList : PanelDockHost
{
	private readonly ClipListPanel _panel;

	public ClipList( Window window ) : base( null )
	{
		Name = "AnimationClips";
		WindowTitle = "Animation Clips";
		SetWindowIcon( "movie" );

		_panel = new ClipListPanel( window ) { Dock = this };

		SetContent( _panel );
	}

	/// <summary>Run with the clip that was picked, so the properties can show it.</summary>
	public Action<string> OnClipSelected { get; set; }

	internal void Selected( string clip ) => OnClipSelected?.Invoke( clip );

	/// <summary>Read the catalogue again and lay the list out from the top.</summary>
	public void Rebuild() => _panel?.Rebuild();

	/// <summary>Whether this clip is used by a node somewhere in the graph.</summary>
	public bool IsReferenced( string clip ) => _panel?.IsReferenced( clip ) ?? false;

	//The graph changed. Only worth rebuilding for the two things this list reads: the skeleton it lists clips for, and which of them are used.
	internal void OnGraphEdited() => _panel?.OnGraphEdited();
}

//The filter and its two switches, and the clips under them.
[StyleSheet( "ClipList.cs.scss" )]
internal sealed class ClipListPanel : EditorPanel
{
	private readonly Window _window;
	private readonly TextField _filter;
	private readonly ClipRows _rows;

	private string _skeleton;
	private bool _ascending = true;
	private bool _referencedOnly;

	private HashSet<string> _referenced = new( StringComparer.OrdinalIgnoreCase );

	//What is listed now, after the filter and the sort.
	internal List<string> Clips { get; } = new();

	//Which of them are picked. Dragging one of these carries all of them.
	internal HashSet<string> Selection { get; } = new( StringComparer.OrdinalIgnoreCase );

	//The dock, for telling whoever is listening which clip was picked.
	internal ClipList Dock { get; init; }

	public ClipListPanel( Window window )
	{
		_window = window;

		AddClass( "clip-list" );

		var bar = AddChild<PanelToolbar>();

		_filter = bar.AddFilter( "Filter..", _ => Rebuild() );

		bar.AddToggle( "swap_vert", "Sort alphabetically", true, on =>
		{
			_ascending = on;
			Rebuild();
		} );

		bar.AddToggle( "link", "Only show animations used by this graph", false, on =>
		{
			_referencedOnly = on;
			Rebuild();
		} );

		_rows = AddChild( new ClipRows( this ) );
		_rows.AddClass( "well" );

		Rebuild();
	}

	/// <summary>Reads the catalogue, filters it, sorts it, and puts the list over it.</summary>
	public void Rebuild()
	{
		_skeleton = _window.Graph?.TargetSkeleton;
		_referenced = Referenced();

		var filter = _filter?.Value?.Trim() ?? string.Empty;

		var listed = ModelClipCatalog.Names( _window.Graph )
			.Where( name => string.IsNullOrEmpty( filter ) || name.Contains( filter, StringComparison.OrdinalIgnoreCase ) )
			.Where( name => !_referencedOnly || IsReferenced( name ) );

		var sorted = _ascending
			? listed.OrderBy( System.IO.Path.GetFileNameWithoutExtension ).ThenBy( name => name )
			: listed.OrderByDescending( System.IO.Path.GetFileNameWithoutExtension ).ThenByDescending( name => name );

		Clips.Clear();
		Clips.AddRange( sorted );

		//Whatever was picked and is no longer listed cannot stay picked - it is not there to unpick, and a drag would carry a name off a hidden row.
		Selection.RemoveWhere( name => !Clips.Contains( name ) );

		_rows.Invalidate();
	}

	/// <summary>Whether a clip is used by a node somewhere in the graph.</summary>
	public bool IsReferenced( string clip )
	{
		if ( string.IsNullOrWhiteSpace( clip ) ) return false;

		return _referenced.Contains( clip ) || _referenced.Contains( ModelClipCatalog.Key( clip ) );
	}

	/// <summary>The graph changed; rebuild only where what this list shows would differ.</summary>
	public void OnGraphEdited()
	{
		if ( _window.Graph?.TargetSkeleton != _skeleton )
		{
			Rebuild();
			return;
		}

		if ( !Referenced().SetEquals( _referenced ) ) Rebuild();
	}

	//Every clip named by a node in the graph, under both of its names.
	private HashSet<string> Referenced()
	{
		var set = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var node in _window.Graph?.AllNodes ?? [] )
		{
			var name = node switch
			{
				Nodes.AnimationClipNode clip => clip.AnimationName,
				Nodes.AnimationPoseNode pose => pose.AnimationName,
				Nodes.TimeControlledAnimationClipNode timed => timed.AnimationName,
				_ => null
			};

			if ( string.IsNullOrEmpty( name ) ) continue;

			set.Add( name );
			set.Add( ModelClipCatalog.Key( name ) );
		}

		return set;
	}

	//A row was pressed. Plain picks one, ctrl adds or removes one, shift takes everything between the last plain pick and this.
	internal void Pick( int index )
	{
		if ( index < 0 || index >= Clips.Count ) return;

		var clip = Clips[index];
		var modifiers = Application.KeyboardModifiers;

		if ( modifiers.HasFlag( KeyboardModifiers.Ctrl ) )
		{
			if ( !Selection.Add( clip ) ) Selection.Remove( clip );

			Anchor = index;
		}
		else if ( modifiers.HasFlag( KeyboardModifiers.Shift ) && Anchor >= 0 )
		{
			Selection.Clear();

			var from = Math.Min( Anchor, index );
			var to = Math.Max( Anchor, index );

			for ( var at = from; at <= to && at < Clips.Count; ++at ) Selection.Add( Clips[at] );
		}
		else
		{
			Selection.Clear();
			Selection.Add( clip );

			Anchor = index;
		}

		_rows.Invalidate();

		Dock?.Selected( clip );
	}

	//Where a shift-pick counts from.
	internal int Anchor { get; private set; } = -1;
}

//The clips, one row each.
internal sealed class ClipRows : PooledRowList<ClipRow>
{
	private readonly ClipListPanel _panel;

	//Which row the pointer went down on, and where, until it is carried or let go.
	private int _pressed = -1;
	private Vector2 _pressAt;
	private bool _carrying;

	public ClipRows( ClipListPanel panel )
	{
		_panel = panel;

		AddClass( "clip-rows" );
	}

	protected override float RowHeight => Theme.ListRowHeight;

	protected override int CellCount => _panel.Clips.Count;

	protected override ClipRow CreateCell() => new();

	protected override void BindCell( ClipRow cell, int index )
	{
		var clip = _panel.Clips[index];

		cell.Index = index;
		cell.Clicked = row => _panel.Pick( row.Index );
		cell.Pressed = Press;

		cell.Set( clip, _panel.IsReferenced( clip ), _panel.Selection.Contains( clip ) );
	}

	//A row says the pointer went down on it; a drag may be starting.
	private void Press( int index )
	{
		_pressed = index;
		_pressAt = Application.CursorPosition;
		_carrying = false;
	}

	//Here rather than on the row, because a row must not tick - it would count against every panel above it, for every row on screen. The list ticks anyway; see PooledCellList .
	/// <summary>Watches a press for the moment it becomes a drag.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( _pressed < 0 || _carrying ) return;

		if ( ( Application.MouseButtons & MouseButtons.Left ) == 0 )
		{
			_pressed = -1;
			return;
		}

		if ( Application.CursorPosition.Distance( _pressAt ) > 4.0f ) Carry();
	}

	//Picks the selection up, or the row under the pointer where it is not in it.
	private void Carry()
	{
		_carrying = true;

		if ( _pressed < 0 || _pressed >= _panel.Clips.Count ) return;

		var clip = ModelClipCatalog.Key( _panel.Clips[_pressed] );

		//Dragging a row that is part of the selection carries the whole of it; dragging one that is not carries only that row.
		var names = _panel.Selection
			.Select( ModelClipCatalog.Key )
			.Where( name => name.Length > 0 )
			.ToList();

		if ( !names.Contains( clip ) ) names = [clip];

		EditorDrag.Begin( this, new DragData
		{
			Text = string.Join( "\n", names ),
			Object = new ClipDragData( names )
		}, null );
	}
}

//One clip: whether the graph uses it, its name, and where it came from.
internal sealed class ClipRow : PanelPooledRow
{
	private readonly UILabel _icon;
	private readonly UILabel _name;
	private readonly UILabel _path;

	/// <summary>Run when the pointer goes down on this row, with the row it went down on.</summary>
	public Action<int> Pressed { get; set; }

	public ClipRow()
	{
		AddClass( "clip-row" );

		_icon = AddLabel( "icon material-icon" );
		_name = AddLabel( "name" );
		_path = AddLabel( "path grow-shrink" );

		PanelText.Set( _icon, "movie" );
	}

	//A label of this row, told not to read its text as tokens.
	private UILabel AddLabel( string classes )
	{
		var label = AddChild<UILabel>( classes );

		label.Tokenize = false;

		return label;
	}

	/// <summary>What this row is over.</summary>
	public void Set( string clip, bool referenced, bool selected )
	{
		SetClass( "referenced", referenced );
		SetClass( "selected", selected );

		//The band comes from the row's own index rather than a flag threaded in: PanelPooledRow works it out, and it is the same banding the hierarchy has.
		SetBand( Index );

		PanelText.Set( _name, System.IO.Path.GetFileNameWithoutExtension( clip ) );
		PanelText.Set( _path, clip );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		Pressed?.Invoke( Index );
	}

	public override void Write( System.Text.StringBuilder into ) => into.Append( _path.Text );
}

/// <summary>Marker object carried in drag data to identify animation clip drags.</summary>
public record ClipDragData( IReadOnlyList<string> AnimationNames );

/// <summary>Readonly info about an animation clip, displayed in the properties panel when a clip is selected in the animation list.</summary>
public class AnimationClipInfo
{
	private readonly AnimationClip _clip;

	[Property, Rinten.ReadOnly, Title( "Name" )]
	public string AnimationName { get; }

	[Property, Rinten.ReadOnly]
	public float Duration => _clip?.Duration ?? 0f;

	[Property, Rinten.ReadOnly, Title( "Frames" )]
	public int FrameCount => _clip?.FrameCount ?? 0;

	[Property, Rinten.ReadOnly, Title( "FPS" )]
	public float FrameRate => _clip?.FrameRate ?? 0f;

	[Property, Rinten.ReadOnly, Title( "Additive" )]
	public bool IsAdditive => _clip?.IsAdditive ?? false;

	public AnimationClipInfo( string name )
	{
		AnimationName = name;
		_clip = string.IsNullOrWhiteSpace( name ) ? null : AnimationClip.Load( name );
	}
}
