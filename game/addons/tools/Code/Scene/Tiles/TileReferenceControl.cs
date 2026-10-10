using Rinten.Tiles;
using Rinten.UI;

namespace Editor.Tiles;

/// <summary>A <see cref="TileReference"/> in the inspector: the tile's picture and name, and a click opens its set's sheet to pick another off - or another of the project's tile sets.</summary>
[StyleSheet( "TileReferenceControl.cs.scss" )]
[CustomEditor( typeof( TileReference ) )]
public sealed class TileReferenceControl : Editor.Inspectors.BaseControl
{
	readonly Panel field;
	readonly Panel picture;
	readonly Rinten.UI.Label name;

	TileReferencePopup popup;

	public TileReferenceControl()
	{
		ShowBackground = false;

		field = AddChild<Panel>( "field" );
		picture = field.AddChild<Panel>( "picture" );
		name = field.AddChild<Rinten.UI.Label>( "name" );
		name.Tokenize = false;
	}

	TileReference Value => Property?.GetValue<TileReference>() ?? default;

	public override void Rebuild() => Sync();

	public override void Tick()
	{
		base.Tick();

		if ( popup is not null && !popup.IsValid() ) popup = null;

		SetClass( "open", popup is not null );
		Sync();
	}

	int shown = -1;

	void Sync()
	{
		if ( Property is null ) return;

		var value = Value;
		var hash = HashCode.Combine( value.Set, value.Tile, value.Set?.Revision, Property.IsMultipleDifferentValues );
		if ( hash == shown ) return;
		shown = hash;

		if ( Property.IsMultipleDifferentValues )
		{
			name.Text = "Multiple Values";
			picture.Style.Set( "background-image: none;" );
			return;
		}

		field.SetClass( "empty", !value.IsValid );
		name.Text = value.IsValid ? $"{value.Set.ResourceName} - {value}" : "No tile";
		picture.Style.Set( TilePicture.Style( value.Set, value.Tile, 22 ) );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( Property is null || IsReadOnly || e.MouseButton != MouseButtons.Left ) return;

		if ( popup is { IsValid: true } )
		{
			popup.Delete();
			popup = null;
		}
		else
		{
			popup = TileReferencePopup.Open( this, Value, Assign );
		}

		e.StopPropagation();
	}

	public override void OnPropertyContextMenu( Menu menu )
	{
		if ( Property is null ) return;

		menu.AddOption( "Clear", "clear", () => Assign( default ) ).Enabled = !IsReadOnly && Value.Tile != 0;
	}

	void Assign( TileReference value )
	{
		if ( IsReadOnly || Property is null ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue( value );
		Property.Parent?.NoteFinishEdit( Property );
		shown = -1;
	}
}

//The sheet a TileReferenceControl opens, with the project's other tile sets above it.
[StyleSheet( "TileReferenceControl.cs.scss" )]
sealed class TileReferencePopup : Popup
{
	TileReference current;
	Action<TileReference> picked;
	Panel sets;
	TilePalette palette;

	public static TileReferencePopup Open( Panel source, TileReference current, Action<TileReference> picked )
	{
		CloseAll();

		var popup = new TileReferencePopup();
		popup.AddClass( "tile-reference-popup" );
		popup.SetPositioning( source, PositionMode.BelowLeft, 2 );
		popup.CloseWhenParentIsHidden = true;

		popup.current = current;
		popup.picked = picked;
		popup.Build();

		return popup;
	}

	void Build()
	{
		var all = TileMapTool.ProjectTileSets();
		current.Set ??= all.Select( x => x.Set ).FirstOrDefault();

		sets = AddChild<Panel>( "sets" );

		foreach ( var (asset, set) in all )
		{
			var chip = sets.AddChild<Panel>( "chip" );
			chip.AddChild<Rinten.UI.Label>().Text = asset.Name;
			chip.SetClass( "selected", ReferenceEquals( set, current.Set ) );
			chip.AddEventListener( "onclick", () =>
			{
				current.Set = set;
				foreach ( var other in sets.Children ) other.SetClass( "selected", false );
				chip.SetClass( "selected", true );
				palette.TileSet = set;
				palette.Selected = 0;
				palette.StateHasChanged();
			} );
		}

		palette = AddChild( new TilePalette { TileSet = current.Set, Selected = current.Tile } );
		palette.OnPick = tile =>
		{
			Delete();
			picked?.Invoke( new TileReference( current.Set, tile ) );
		};
	}
}

//One tile of a sheet as a panel background, cropped by where the background sits.
static class TilePicture
{
	public static string Style( TileSet set, int tile, float size )
	{
		if ( set?.Sheet is null || !set.Has( tile ) ) return "background-image: none;";

		var region = set.Region( tile );
		var scale = size / MathF.Max( region.Width, 1 );

		return string.Create( System.Globalization.CultureInfo.InvariantCulture,
			$"background-image: url({set.Sheet.ResourcePath}); " +
			$"background-size: {set.Sheet.Width * scale:0.##}px {set.Sheet.Height * scale:0.##}px; " +
			$"background-position: {-region.Left * scale:0.##}px {-region.Top * scale:0.##}px;" );
	}
}
