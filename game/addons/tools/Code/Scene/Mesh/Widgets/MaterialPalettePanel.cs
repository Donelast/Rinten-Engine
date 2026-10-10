using Editor.TreeBrowser;
using Rinten.UI;

namespace Editor.MeshEditor;

//Active material and shelf, over the bottom-left of every scene view regardless of tool
[StyleSheet( "MaterialPalettePanel.cs.scss" )]
internal sealed class MaterialPalettePanel : Panel
{
	readonly ShelfSlot[] kept = new ShelfSlot[MaterialShelf.KeptSlots];
	readonly ShelfSlot[] used = new ShelfSlot[MaterialShelf.RecentSlots];

	public MaterialPalettePanel()
	{
		AddClass( "material-palette" );

		AddChild( new ActiveMaterialSquare() );

		var shelf = AddChild<Panel>( "shelf" );

		var keptRow = shelf.AddChild<Panel>( "shelf-row" );

		for ( var i = 0; i < kept.Length; i++ )
			kept[i] = keptRow.AddChild( new ShelfSlot( this, i, isKept: true ) );

		var usedRow = shelf.AddChild<Panel>( "shelf-row" );

		for ( var i = 0; i < used.Length; i++ )
			used[i] = usedRow.AddChild( new ShelfSlot( this, i, isKept: false ) );

		MaterialShelf.Changed += RefreshShelf;

		RefreshShelf();
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		MaterialShelf.Changed -= RefreshShelf;
	}

	//Told rather than asked every frame: the recent row is written by using a material, and the eyedropper
	//that takes one off a face is as much a use as picking one here is - see MaterialShelf.Changed.
	void RefreshShelf()
	{
		var favourites = MaterialShelf.Favourites;
		var recent = MaterialShelf.Recent;

		for ( var i = 0; i < kept.Length; i++ )
			kept[i].PaintedWith = i < favourites.Count ? favourites[i] : default;

		for ( var i = 0; i < used.Length; i++ )
			used[i].PaintedWith = i < recent.Count ? recent[i] : default;
	}

	//A slot is pressed: it hands over both halves of what it stands for, so the colour comes with the material.
	internal static void Apply( MaterialPaint paint )
	{
		if ( !paint.IsValid ) return;

		//The colour straight onto the selection, the material through the property - that is what
		//carries the undo note and what everything watching the active material is watching.
		MaterialSelection.Current.Tint = paint.Tint;

		SetMaterial( paint.Material );

		MaterialSelection.Current.Chose();
	}

	//The active material, through the property the palette binds to.
	internal static void SetMaterial( Material material )
	{
		var property = MaterialSelection.Property;

		property.Parent?.NoteStartEdit( property );
		property.SetValue( material );
		property.Parent?.NoteFinishEdit( property );
	}
}

//Palette square: a material with tint, accepts material drops and can be dragged off
internal abstract class MaterialSquare : Panel, IEditorDropTarget
{
	readonly Panel picture;
	readonly IconPanel glyph;

	MaterialPaint paintedWith;

	/// <summary>Both halves of what this stands for - see <see cref="MaterialPaint"/>.</summary>
	public MaterialPaint PaintedWith
	{
		get => paintedWith;
		set
		{
			if ( paintedWith == value ) return;

			paintedWith = value;
			Show();
		}
	}

	//The glyph an empty square wears.
	protected abstract string EmptyGlyph { get; }

	//What the pointer is told over an empty square.
	protected abstract string EmptyTip { get; }

	protected MaterialSquare()
	{
		AddClass( "material-square" );

		picture = AddChild<Panel>( "picture" );
		glyph = AddChild<IconPanel>( "glyph" );
	}

	//The asset behind the material, or null where there is none to show.
	protected Asset ShownAsset
	{
		get
		{
			var material = paintedWith.Material;
			if ( !material.IsValid() ) return null;

			var asset = AssetSystem.FindByPath( material.ResourcePath );

			return asset is null || asset.IsDeleted ? null : asset;
		}
	}

	Pixmap shownThumb;
	Color32 shownTint;
	bool downloading;
	RealTimeSince sinceShown;

	//Shows thumbnail or empty glyph; re-requested periodically as previews render late
	protected void Show()
	{
		sinceShown = 0;

		var asset = ShownAsset;

		SetClass( "empty", asset is null );
		glyph.Text = downloading ? "download" : EmptyGlyph;

		if ( asset is null )
		{
			shownThumb = null;
			picture.Style.BackgroundImage = null;
			Tooltip = EmptyTip;
			return;
		}

		Tooltip = asset.RelativePath;

		var thumb = asset.GetAssetThumb( true );

		if ( thumb != shownThumb )
		{
			shownThumb = thumb;
			picture.Style.BackgroundImage = Thumbnail( asset.Path, thumb );
		}

		var tint = paintedWith.IsValid ? paintedWith.Tint : Color32.White;

		if ( !tint.Equals( shownTint ) || picture.Style.BackgroundTint is null )
		{
			shownTint = tint;
			picture.Style.BackgroundTint = tint.ToColor();
		}
	}

	//Whether what this stands for has moved since it was shown - see the active square.
	protected virtual bool Follow() => false;

	public override void Tick()
	{
		base.Tick();

		PumpDrag();

		if ( Follow() || sinceShown > 0.5f )
			Show();
	}

	//── pressed ────────────────────────────────────────────────────────────────

	//What a click on the square does.
	protected abstract void OnSquareClicked();

	//What the menu on the square offers.
	protected abstract void BuildMenu( ContextMenu menu );

	//A material dropped onto the square.
	protected abstract void OnMaterialDropped( Material material );

	bool held;
	bool dragged;
	Vector2 heldAt;

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		held = true;
		dragged = false;
		heldAt = Application.CursorPosition;
	}

	//Carried off once the pointer has moved a few pixels with the button down - the same distance the asset browser waits for before a click is a drag.
	void PumpDrag()
	{
		if ( !held ) return;

		if ( (Application.MouseButtons & MouseButtons.Left) == 0 )
		{
			held = false;
			return;
		}

		if ( Application.CursorPosition.Distance( heldAt ) <= 4 ) return;

		held = false;

		if ( ShownAsset is not { } asset ) return;

		dragged = true;

		AssetMover.StartDrag( this, new[] { asset.Path }, asset );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		e.StopPropagation();

		//The button coming up at the end of a drag is not a press.
		if ( dragged )
		{
			dragged = false;
			return;
		}

		OnSquareClicked();
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );

		e.StopPropagation();

		var menu = new ContextMenu();

		BuildMenu( menu );

		menu.OpenAtCursor( this );
	}

	//── dropped on ─────────────────────────────────────────────────────────────

	public bool AcceptEditorDrops => true;

	public DropAction OnEditorDragHover( Widget.DragEvent ev )
	{
		var accept = ev.Data.Url?.Scheme == "https" || MaterialFrom( ev ) is not null;

		SetClass( "drop-target", accept );

		return accept ? DropAction.Link : DropAction.Ignore;
	}

	public void OnEditorDragLeave() => SetClass( "drop-target", false );

	public void OnEditorDragDrop( Widget.DragEvent ev )
	{
		SetClass( "drop-target", false );

		if ( ev.Data.Url?.Scheme == "https" )
		{
			_ = InstallAsync( ev.Data.Text );
			ev.Action = DropAction.Link;
			return;
		}

		if ( MaterialFrom( ev ) is not { } material ) return;

		OnMaterialDropped( material );
		ev.Action = DropAction.Link;
	}

	//The material in what is being carried, from a file, an asset or the material itself.
	static Material MaterialFrom( Widget.DragEvent ev )
	{
		var data = ev?.Data;
		if ( data is null ) return null;

		if ( data.HasFileOrFolder )
		{
			var asset = AssetSystem.FindByPath( data.FileOrFolder );
			if ( asset is not null && asset.AssetType == AssetType.Material )
				return asset.LoadResource( typeof( Material ) ) as Material;
		}

		if ( data.Object is Asset carried && carried.AssetType == AssetType.Material )
			return carried.LoadResource( typeof( Material ) ) as Material;

		return data.Object as Material;
	}

	//A material dropped from the web: installed first, then taken like any other.
	async Task InstallAsync( string identUrl )
	{
		try
		{
			downloading = true;
			Show();

			var asset = await AssetSystem.InstallAsync( identUrl );
			if ( asset is null || asset.AssetType != AssetType.Material ) return;

			if ( asset.LoadResource<Material>() is not Material material ) return;

			OnMaterialDropped( material );
		}
		finally
		{
			downloading = false;
			Show();
		}
	}

	//── pictures ───────────────────────────────────────────────────────────────

	//Thumbnail textures cached by path and pixmap, shared between squares
	static Texture Thumbnail( string path, Pixmap pixmap )
	{
		if ( pixmap?.picture is null || string.IsNullOrEmpty( path ) ) return null;

		if ( thumbnails.TryGetValue( path, out var made ) && made.Source == pixmap )
			return made.Texture;

		if ( thumbnails.Count > 64 ) thumbnails.Clear();

		var texture = pixmap.picture.ToTexture( mips: false );
		thumbnails[path] = (pixmap, texture);

		return texture;
	}

	static readonly Dictionary<string, (Pixmap Source, Texture Texture)> thumbnails = new();
}

//The big square: the material being painted with, and in its corner the colour it is painted at.
internal sealed class ActiveMaterialSquare : MaterialSquare
{
	readonly TintChip tint;

	protected override string EmptyGlyph => "texture";
	protected override string EmptyTip => "Active Material";

	public ActiveMaterialSquare()
	{
		AddClass( "active-material" );

		//In the corner of the square rather than beside it: it is not a third thing beside
		//the material and the shelf, it is the other half of the material - so it sits on it.
		tint = AddChild( new TintChip() );

		Follow();
		Show();
	}

	//The palette's selection, which anything can change - the picker, a slot, the eyedropper.
	protected override bool Follow()
	{
		var selection = MaterialSelection.Current;

		tint.Refresh( selection.Active );

		var now = new MaterialPaint( selection.Active, selection.Tint );

		if ( now == PaintedWith ) return false;

		PaintedWith = now;

		return true;
	}

	//Opens the picker - the one thing a click on the big square does.
	protected override void OnSquareClicked()
	{
		var property = MaterialSelection.Property;

		var resource = property.GetValue<Resource>( null );
		var asset = resource != null ? AssetSystem.FindByPath( resource.ResourcePath ) : null;
		var assetType = AssetType.FromType( resource.IsValid() ? resource.GetType() : property.PropertyType );

		property.Parent?.NoteStartEdit( property );

		var picker = AssetPicker.Create( null, assetType, new AssetPicker.PickerOptions()
		{
			EnableMultiselect = false
		} );

		picker.Title = $"Select {property.DisplayName}";

		//Highlighting shows it in the view without choosing it, so it must not reach the recent row - see MaterialShelf.Recording.
		picker.OnAssetHighlighted = ( o ) =>
		{
			using ( MaterialShelf.NotRecording() ) FromAsset( o.FirstOrDefault() );
		};

		picker.OnAssetPicked = ( o ) =>
		{
			FromAsset( o.FirstOrDefault() );
			property.Parent?.NoteFinishEdit( property );
		};

		picker.Show();

		picker.SetSelection( asset );
	}

	protected override void OnMaterialDropped( Material material )
	{
		if ( material is null ) return;

		MaterialPalettePanel.SetMaterial( material );
	}

	protected override void BuildMenu( ContextMenu m )
	{
		var resource = MaterialSelection.Property.GetValue<Resource>( null );
		var asset = resource != null ? AssetSystem.FindByPath( resource.ResourcePath ) : null;

		m.AddOption( "Open in Editor", "edit", () => asset?.OpenInEditor() ).Enabled = asset != null && !asset.IsProcedural;
		m.AddOption( "Find in Asset Browser", "search", () => TreeAssetBrowser.OpenTo( asset ) ).Enabled = asset is not null;
		m.AddSeparator();
		m.AddOption( "Copy", "file_copy", () => EditorUtility.Clipboard.Copy( asset?.Path ) ).Enabled = asset != null;
		m.AddOption( "Paste", "content_paste", () => FromAsset( AssetSystem.FindByPath( EditorUtility.Clipboard.Paste() ) ) );
		m.AddSeparator();
		m.AddOption( "Select Faces Using Material", "texture", SelectFacesWithMaterial ).Enabled = resource is Material;
		m.AddOption( "Select Objects Using Material", "category", SelectObjectsWithMaterial ).Enabled = resource is Material;
		m.AddSeparator();
		m.AddOption( "Clear", "backspace", () => MaterialPalettePanel.SetMaterial( null ) ).Enabled = resource != null;
		m.AddSeparator();
		PaletteMenu.Add( m );
	}

	static void FromAsset( Asset asset )
	{
		if ( asset is null ) return;

		if ( asset.LoadResource( MaterialSelection.Property.PropertyType ) is not Material material ) return;

		MaterialPalettePanel.SetMaterial( material );
	}

	static void SelectFacesWithMaterial()
	{
		if ( MaterialSelection.Current.Active is not { } material ) return;

		var selection = SceneEditorSession.Active.Selection;
		var scene = SceneEditorSession.Active.Scene;

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
			selection.Clear();

		foreach ( var component in scene.GetAllComponents<MeshComponent>() )
		{
			if ( !component.IsValid() ) continue;

			var mesh = component.Mesh;
			if ( mesh is null ) continue;

			foreach ( var face in mesh.FaceHandles )
			{
				var faceMaterial = mesh.GetFaceMaterial( face );

				if ( faceMaterial != null && faceMaterial.ResourcePath == material.ResourcePath )
					selection.Add( new MeshFace( component, face ) );
			}
		}

		EditorToolManager.SetSubTool( nameof( FaceTool ) );
	}

	static void SelectObjectsWithMaterial()
	{
		if ( MaterialSelection.Current.Active is not { } material ) return;

		var selection = SceneEditorSession.Active.Selection;
		var scene = SceneEditorSession.Active.Scene;

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
			selection.Clear();

		var objectsWithMaterial = new HashSet<GameObject>();

		foreach ( var component in scene.GetAllComponents<MeshComponent>() )
		{
			if ( !component.IsValid() ) continue;

			var mesh = component.Mesh;
			if ( mesh is null ) continue;

			foreach ( var face in mesh.FaceHandles )
			{
				var faceMaterial = mesh.GetFaceMaterial( face );

				if ( faceMaterial != null && faceMaterial.ResourcePath == material.ResourcePath )
				{
					objectsWithMaterial.Add( component.GameObject );
					break;
				}
			}
		}

		foreach ( var go in objectsWithMaterial )
			selection.Add( go );

		EditorToolManager.SetSubTool( nameof( ObjectSelection ) );
	}
}

//Tint chip on the big square; shown only if the active material's shader reads the tint
internal sealed class TintChip : Panel
{
	Color32 shown;

	public TintChip()
	{
		AddClass( "tint" );

		Tooltip = "Paint tint";
	}

	/// <summary>Called by the square it sits on, once a frame.</summary>
	public void Refresh( Material active )
	{
		var supported = active?.TintsPerVertex ?? false;

		SetClass( "hidden", !supported );

		if ( !supported ) return;

		var tint = MaterialSelection.Current.Tint;

		if ( tint.Equals( shown ) && Style.BackgroundColor is not null ) return;

		shown = tint;
		Style.BackgroundColor = tint.ToColor();
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		//Its own click, not the square's under it: the square opens the asset picker, and both used to open at once.
		e.StopPropagation();

		var selection = MaterialSelection.Current;

		//Tint has no alpha or HDR; recorded on the shelf once, when picking finishes
		Inspectors.ColorPickerPopup.Open( this, selection.Tint.ToColor(),
			c => selection.Tint = (Color32)c.WithAlpha( 1.0f ),
			hasAlpha: false, hdr: false, finished: () => selection.Chose() );
	}
}

//One slot on the shelf: a material to press, or somewhere to put one.
internal sealed class ShelfSlot : MaterialSquare
{
	readonly MaterialPalettePanel palette;
	readonly int index;
	readonly bool isKept;

	protected override string EmptyGlyph => isKept ? "star" : "history";
	protected override string EmptyTip => isKept ? "Kept material" : "Recently used";

	public ShelfSlot( MaterialPalettePanel palette, int index, bool isKept )
	{
		this.palette = palette;
		this.index = index;
		this.isKept = isKept;

		AddClass( "slot" );
		SetClass( "kept", isKept );

		Show();
	}

	protected override void OnSquareClicked()
	{
		if ( PaintedWith.IsValid )
		{
			MaterialPalettePanel.Apply( PaintedWith );
			return;
		}

		//An empty kept slot takes whatever is in the big square, which is the shortest way to keep the thing you are already painting with.
		if ( isKept ) MaterialShelf.SetFavourite( index, MaterialSelection.Current.Paint );
	}

	//Dropping onto the kept row keeps it there. The recent row is written by what is used rather than by hand, so a drop onto it is a use. A material dropped
	//from outside the shelf arrives with no colour of its own, so it lands at white - a drop is the material, and the colour is chosen in the palette.
	protected override void OnMaterialDropped( Material material )
	{
		var dropped = new MaterialPaint( material );

		if ( isKept )
		{
			MaterialShelf.SetFavourite( index, dropped );
			return;
		}

		MaterialShelf.Drop( dropped );

		MaterialPalettePanel.Apply( dropped );
	}

	protected override void BuildMenu( ContextMenu m )
	{
		var paint = PaintedWith;
		var has = paint.IsValid;

		PaletteMenu.Add( m );
		m.AddSeparator();

		if ( isKept )
		{
			m.AddOption( has ? "Change Material" : "Set Material", "format_color_fill", Pick );
			m.AddOption( "Use Active Material", "colorize",
				() => MaterialShelf.SetFavourite( index, MaterialSelection.Current.Paint ) );
		}
		else
		{
			m.AddOption( "Keep", "star", () => MaterialShelf.Keep( paint ) ).Enabled = has;
		}

		if ( ShownAsset is { } asset )
		{
			m.AddSeparator();
			m.AddOption( "Open in Editor", "edit", () => asset.OpenInEditor() ).Enabled = !asset.IsProcedural;
			m.AddOption( "Find in Asset Browser", "search", () => TreeAssetBrowser.OpenTo( asset ) );
		}

		if ( isKept )
		{
			m.AddSeparator();
			m.AddOption( "Clear", "backspace", () => MaterialShelf.SetFavourite( index, default ) ).Enabled = has;
		}
	}

	void Pick()
	{
		var picker = AssetPicker.Create( null, AssetType.Material, new AssetPicker.PickerOptions()
		{
			EnableMultiselect = false
		} );

		picker.Title = "Select Material";

		picker.OnAssetPicked = assets =>
		{
			if ( assets.FirstOrDefault()?.LoadResource( typeof( Material ) ) is Material material )
				MaterialShelf.SetFavourite( index, new MaterialPaint( material, PaintedWith.Tint ) );
		};

		picker.Show();
	}
}

//The palettes, as a submenu of the right click on the active material and on every slot: pick one, make one, rename, copy or delete the one in hand.
internal static class PaletteMenu
{
	public static void Add( ContextMenu m )
	{
		var palettes = m.AddMenu( "Palettes", "palette" );
		var active = MaterialShelf.ActivePalette;

		foreach ( var name in MaterialShelf.Palettes )
		{
			var local = name;
			palettes.AddOption( local, local == active ? "check" : "palette", () => MaterialShelf.SelectPalette( local ) );
		}

		palettes.AddSeparator();

		palettes.AddOption( "New Palette…", "add",
			() => Dialog.AskString( name => MaterialShelf.CreatePalette( name ), "Palette name:", okay: "Create", title: "New Palette", minLength: 1 ) );

		palettes.AddOption( "Rename Palette…", "edit",
			() => Dialog.AskString( name => MaterialShelf.RenamePalette( active, name ), "Palette name:", okay: "Rename", title: "Rename Palette", initialName: active, minLength: 1 ) );

		palettes.AddOption( "Duplicate Palette", "content_copy", () => MaterialShelf.DuplicatePalette( active ) );

		palettes.AddOption( "Delete Palette", "delete", () => MaterialShelf.DeletePalette( active ) ).Enabled = MaterialShelf.Palettes.Count > 1;
	}
}
