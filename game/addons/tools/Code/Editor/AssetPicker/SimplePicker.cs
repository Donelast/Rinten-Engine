using System.IO;
using System.Threading;

namespace Editor.AssetPickers;

public abstract class SimplePicker : AssetPicker
{
	protected LineEdit _search;
	protected ListView _assetList;

	public SimplePicker( Widget parent, AssetType assetType, PickerOptions options ) : base( parent, assetType, options )
	{
		Window.IsPopup = true;
		Window.StartCentered = false;
		Window.FixedSize = new Vector2( 450, 600 );

		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 8;

		var searchRow = Layout.AddRow();
		searchRow.Spacing = 8;

		_search = searchRow.Add( new LineEdit() );
		_search.Layout = Layout.Row();
		_search.Layout.AddStretchCell( 1 );
		_search.Focus();
		_search.PlaceholderText = $"⌕  Search {AssetType}s";
		_search.Height = 64;
		_search.TextEdited += ( s ) => Refresh();

		var clearButton = _search.Layout.Add( new ToolButton( string.Empty, "clear", this ) );
		clearButton.Bind( "Visible" ).ReadOnly().From( () => _search.Text.Length > 0, null );
		clearButton.MouseLeftPress = () =>
		{
			_search.Text = string.Empty;
			Refresh();
		};

		_assetList = Layout.Add( new ListView( this ) );
		_assetList.ItemPaint = ( item ) => PaintLocalAsset( item.Rect, item.Object as IAssetListEntry );
		_assetList.ItemSpacing = 8;
		_assetList.MultiSelect = false;
		_assetList.ItemSize = new Vector2( 0, 64 );
		_assetList.ItemSelected = OnItemSelected;
		_assetList.ItemScrollEnter = OnItemScrollEnter;
		_assetList.ItemScrollExit = OnItemScrollExit;

		Refresh();
	}

	CancellationTokenSource _ct;
	public void Refresh()
	{
		_ct?.Dispose();
		_ct = new CancellationTokenSource();

		_assetList.Clear();

		var items = AssetSystem.All
			.Where( x => x.AssetType == AssetType && x.RelativePath.Contains( _search.Value ) && !ShouldFilterAsset( x ) )
			.Select( x => new AssetEntry( x ) )
			.Cast<IAssetListEntry>();

		_assetList.SetItems( items.OrderBy( x => x.Name ) );
	}

	public override void Show()
	{
		base.Show();
		Window.Position = Application.CursorPosition;
		Window.AdjustSize();
		Window.ConstrainToScreen();
	}

	private void OnItemScrollEnter( object item )
	{
		if ( item is not IAssetListEntry entry )
			return;

		entry.OnScrollEnter();
	}

	private void OnItemScrollExit( object item )
	{
		if ( item is not IAssetListEntry entry )
			return;

		entry.OnScrollExit();
	}

	protected virtual void PaintLocalAsset( Rect rect, IAssetListEntry entry )
	{
		Paint.SetPen( Theme.Text.WithAlpha( 0.7f ) );

		bool active = Paint.HasPressed;
		bool highlight = !active && (Paint.HasSelected || Paint.HasPressed);
		bool hover = !highlight && Paint.HasMouseOver;

		if ( active )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Primary.Darken( 0.5f ) );
			Paint.DrawRect( rect, 4 );
		}

		if ( highlight )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Primary.Darken( 0.4f ) );
			Paint.DrawRect( rect, 4 );
		}

		if ( hover )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceBackground.WithAlpha( 0.5f ) );
			Paint.DrawRect( rect, 4 );
		}

		var iconRect = new Rect( rect.Position, Vector2.One * 64 );
		Paint.BilinearFiltering = true;
		entry.DrawIcon( iconRect );
		Paint.BilinearFiltering = false;

		var textRect = rect;
		textRect.Left = iconRect.Right + 8;
		textRect.Top += 12;

		Paint.SetPen( Theme.Text );
		var strText = Path.GetFileNameWithoutExtension( entry.Name );
		Paint.DrawText( textRect, strText, TextFlag.LeftTop );

		textRect.Top += 12;

		if ( entry is AssetEntry ae )
		{
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawText( textRect, ae.Asset.AssetType.FriendlyName, TextFlag.LeftTop );

			textRect.Top += 12;

			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawText( textRect, ae.Asset.RelativePath, TextFlag.LeftTop );
		}
	}

	void OnItemSelected( object item )
	{
		if ( item is AssetEntry ae )
		{
			Submit( ae.Asset );
		}
	}

	public override void SetSelection( Asset asset )
	{
		if ( asset is null ) return;

		var entry = _assetList.Items.OfType<AssetEntry>().Where( x => x.Asset == asset ).FirstOrDefault();
		_assetList.SelectItem( entry, skipEvents: true );
		_assetList.ScrollTo( entry );

		_assetList.VerticalScrollbar.Value = (int)_assetList.SmoothScrollTarget;
	}
}
