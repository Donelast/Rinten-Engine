namespace Editor.Inspectors;

using Rinten.Clutter;
using Rinten.UI;

//One clutter entry in the grid: what it places, and how often.
sealed class ClutterEntryTile : Panel, IEditorDropTarget
{
	readonly ClutterEntriesGridControl _owner;
	readonly Rinten.UI.Image _thumb;
	readonly Rinten.UI.Label _glyph;
	readonly Rinten.UI.Label _name;
	readonly Rinten.UI.Label _weight;

	Texture _texture;
	string _textureKey;
	float _shownWeight = float.NaN;

	bool _pressPending;
	bool _dragStarted;
	Vector2 _pressStart;

	public ClutterEntry Entry { get; }

	public int Index { get; }

	public ClutterEntryTile( ClutterEntriesGridControl owner, ClutterEntry entry, int index )
	{
		_owner = owner;
		Entry = entry;
		Index = index;

		AddClass( "tile" );

		var frame = AddChild<Panel>( "frame" );

		_thumb = frame.AddChild<Rinten.UI.Image>( "thumb" );

		_glyph = frame.AddChild<Rinten.UI.Label>( "glyph" );
		_glyph.Tokenize = false;

		//The button is the innermost thing under the pointer, so pressing it neither selects the tile nor starts dragging it.
		var remove = frame.AddChild( new Rinten.UI.Button( null, "close" ) );
		remove.AddClass( "remove" );
		remove.Tooltip = "Remove Entry";
		remove.AddEventListener( "onclick", () => _owner.Remove( Entry ) );

		_name = AddChild<Rinten.UI.Label>( "name" );
		_name.Tokenize = false;

		_weight = AddChild<Rinten.UI.Label>( "weight" );
		_weight.Tokenize = false;

		Sync();
	}

	public override void Tick()
	{
		base.Tick();

		UpdateDrag();
		DropOutside();

		if ( _shownWeight != Entry.Weight )
			Sync();
	}

	//Letting a tile go outside the grid throws the entry away. Only a tile that survived the drag gets here: dropping on another tile
	//reorders the list, which rebuilds every tile, so this one is gone before its next frame. What is left is a drag that landed nowhere.
	void DropOutside()
	{
		if ( !_dragStarted || EditorDrag.Active ) return;

		_dragStarted = false;

		if ( _owner.ContainsCursor() ) return;

		_owner.Remove( Entry );
	}

	void Sync()
	{
		var asset = ClutterEntriesGridControl.AssetOf( Entry );

		_shownWeight = Entry.Weight;
		_name.Text = asset?.Name ?? "Empty";
		_weight.Text = $"Weight: {Entry.Weight:F2}";

		var pixmap = asset?.GetAssetThumb( true );

		if ( pixmap?.picture is { IsValid: true } )
		{
			var key = pixmap.picture.GetHashCode().ToString();

			if ( _textureKey != key || _texture is null || !_texture.IsValid )
			{
				_textureKey = key;
				_texture = pixmap.picture.ToTexture( mips: false );
			}

			_thumb.Texture = _texture;
			SetClass( "has-thumb", true );
			return;
		}

		_thumb.Texture = null;
		SetClass( "has-thumb", false );
		_glyph.Text = asset is null ? "add_photo_alternate" : "category";
	}

	//Both buttons are answered here rather than in OnRightClick . A right click only becomes onrightclick on release, and only when the press and the release
	//were on the same panel with nothing dragged in between - a menu that never opened is what that costs. Mouse down is sent whatever happens after it.
	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton == MouseButtons.Right )
		{
			OpenMenu();
			e.StopPropagation();
			return;
		}

		if ( e.MouseButton != MouseButtons.Left ) return;

		_pressPending = true;
		_dragStarted = false;
		_pressStart = Application.CursorPosition;

		_owner.Select( Entry );
		e.StopPropagation();
	}

	//The press already opened the menu, so the row does not open one of its own.
	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );
		e.StopPropagation();
	}

	void UpdateDrag()
	{
		if ( !_pressPending || _dragStarted ) return;

		if ( ( Application.MouseButtons & MouseButtons.Left ) == 0 )
		{
			_pressPending = false;
			return;
		}

		if ( Application.CursorPosition.Distance( _pressStart ) <= 4 ) return;

		_dragStarted = true;
		_pressPending = false;

		//The index is what is carried: a drop lands the entry where it is let go.
		EditorDrag.Begin( this, new DragData { Object = Index }, null );
	}

	//The drop target is the innermost panel that takes drops, so a tile that said yes to everything would swallow an asset dragged over it - the grid behind it would never hear about it, and never light up.
	/// <summary>A tile only takes another tile.</summary>
	public bool AcceptEditorDrops => EditorDrag.Data?.Object is int;

	public DropAction OnEditorDragHover( Widget.DragEvent ev )
	{
		var accept = ev.Data.Object is int index && index != Index;

		SetClass( "drop-target", accept );

		return accept ? DropAction.Move : DropAction.Ignore;
	}

	public void OnEditorDragLeave() => SetClass( "drop-target", false );

	public void OnEditorDragDrop( Widget.DragEvent ev )
	{
		SetClass( "drop-target", false );

		if ( ev.Data.Object is not int from ) return;

		_owner.Move( from, Index );
		ev.Action = DropAction.Move;
	}

	void OpenMenu()
	{
		var asset = ClutterEntriesGridControl.AssetOf( Entry );
		var menu = new Menu();

		menu.AddOption( "Find in Asset Browser", "search", () => _owner.ShowInAssetBrowser( Entry ) )
			.Enabled = asset is not null;

		var editable = !_owner.IsReadOnly;

		menu.AddSeparator();
		menu.AddOption( "Set Weight...", "balance", OpenWeight ).Enabled = editable;
		menu.AddSeparator();
		menu.AddOption( "Remove Entry", "close", () => _owner.Remove( Entry ) ).Enabled = editable;

		menu.OpenAtCursor( this );
	}

	void OpenWeight()
	{
		var popup = ClutterWeightPopup.Open( this, Entry.Weight, weight =>
		{
			Entry.Weight = weight;
			_owner.NoteWeightChanged();
			Sync();
		} );

		popup?.SyncAnchor();
	}
}
