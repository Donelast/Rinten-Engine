using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Editor;

/// <summary>Displays a list of assets, set by <see cref="BaseItemWidget.SetItems"/>. They should be either <see cref="Asset"/> or <see cref="DirectoryInfo"/>.</summary>
public partial class AssetList : ListView, AssetSystem.IEventListener
{
	internal bool SingleColumnMode { get; set; }
	internal bool FullPathMode
	{
		get => _fullPathMode;
		set
		{
			_fullPathMode = value;
			ParentListHeader.SetColumnVisible( "Path", _fullPathMode );
		}
	}

	private bool _fullPathMode = false;
	private ListHeader ParentListHeader;

	bool AllowItemHover( VirtualWidget item = null ) =>
		!Window.BlocksWidgetInput( this )
		&& ( Paint.HasMouseOver || ( item?.Dropping ?? false ) );

	/// <summary>Selection has been changed</summary>
	public Action<IEnumerable<IAssetListEntry>> OnHighlight;

	/// <summary>Called when the viewmode has been modified via user input</summary>
	public Action OnViewModeChanged;

	public AssetList( Widget parent ) : base( parent )
	{
		ItemSelected = ( a ) =>
		{
			if ( a is not IAssetListEntry entry )
				return;

			entry.OnClicked( this );
		};

		OnSelectionChanged = ( object[] a ) =>
		{
			OnHighlight?.Invoke( a.OfType<IAssetListEntry>() );
		};

		ItemActivated = ( a ) =>
		{
			if ( a is not IAssetListEntry entry )
				return;

			entry.OnDoubleClicked( this );
		};

		ItemDrag = ( a ) =>
		{
			if ( a is AssetEntry ae )
			{
				var asset = ae.Asset;
				var drag = new Drag( this );

				if ( asset == null )
				{
					drag.Data.Text = ae.FileInfo.FullName;
					drag.Data.Url = new System.Uri( "file:///" + ae.FileInfo.FullName );
				}
				else
				{
					drag.Data.Object = asset;
					drag.Data.Text = asset.RelativePath;
					drag.Data.Url = new System.Uri( "file:///" + asset.AbsolutePath );
				}

				//Add the other selected assets too..
				foreach ( var item in SelectedItems.OfType<AssetEntry>() )
				{
					if ( ae == item ) continue;
					drag.Data.Text += "\n" + item.FileInfo.FullName;
				}

				drag.Execute();

				return true;
			}

			if ( a is DirectoryEntry de )
			{
				var directory = de.DirectoryInfo;

				var drag = new Drag( this );
				drag.Data.Url = new Uri( "file:///" + directory.FullName );
				drag.Execute();

				return true;
			}

			return false;
		};

		ItemContextMenu = OpenItemContextMenu;

		ItemScrollEnter = OnItemScrollEnter;
		ItemScrollExit = OnItemScrollExit;

		ItemHoverEnter = OnItemHoverEnter;
		ItemHoverLeave = OnItemHoverExit;

		SetListMode();

		MultiSelect = true;
		FocusMode = FocusMode.TabOrClick;
		Margin = new Margin( 4 );

		ParentListHeader = Parent.GetDescendants<ListHeader>().FirstOrDefault();
	}

	protected override void OnShortcutPressed( KeyEvent e )
	{
	}

	/// <summary>Switch the asset list to show icons.</summary>
	public void SetIconMode( int iconWidth, int iconHeight )
	{
		ItemSize = new Vector2( iconWidth, iconHeight );
		ItemPaint = PaintIconMode;
	}

	/// <summary>Switch the asset list to list mode.</summary>
	public void SetListMode()
	{
		ItemSize = new Vector2( 0, Theme.RowHeight );
		ItemPaint = PaintListItem;
		ItemSpacing = 0;
	}

	void SetStatusText( string msg )
	{
		Widget window = GetWindow();
		if ( window is Window win && win.StatusBar.IsValid() )
		{
			win.StatusBar.ShowMessage( msg, 0 );
		}
	}
	private void OnItemHoverEnter( object item )
	{
		if ( item is not IAssetListEntry entry )
			return;

		SetStatusText( entry.GetStatusText() );
	}

	private void OnItemHoverExit( object item )
	{
		SetStatusText( "" );
	}

	private void OnItemScrollEnter( object item )
	{
		if ( item is not IAssetListEntry entry )
			return;

		if ( ViewMode is AssetListViewMode.List )
			return;

		entry.OnScrollEnter();
	}
	private void OnItemScrollExit( object item )
	{
		if ( item is not IAssetListEntry entry )
			return;

		if ( ViewMode is AssetListViewMode.List )
			return;

		entry.OnScrollExit();
	}

	private void PaintIconMode( VirtualWidget item )
	{
		if ( item.Object is not IAssetListEntry entry )
			return;

		Paint.SetPen( Theme.InkDim );

		bool active = Paint.HasPressed;
		bool highlight = !active && (Paint.HasSelected || Paint.HasPressed);
		bool hover = !highlight && AllowItemHover( item );

		var rect = item.Rect.Shrink( 2 );

		var iconRect = rect.Shrink( 4 );
		iconRect.Height = iconRect.Width;

		var textRect = rect.Shrink( 4, 0 );
		textRect.Top = iconRect.Bottom + 4;

		//Accent wash covers only the thumbnail; the name keeps the same ink whether selected or not
		var wash = new Rect( rect.Left, rect.Top, rect.Width, (textRect.Top - rect.Top) - 2.0f );

		//Held down, picked out, pointed at: the accent twice over and a veil of white, which is what a card answers with everywhere else.
		if ( active )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.TileDown );
			Paint.DrawRect( rect, Theme.ControlRadius );
		}

		if ( highlight )
		{
			//The accent: this is the one list in the editor where what is picked out is the
			//whole point of the panel, and a grey on a grid of thumbnails is not seen at all.
			Paint.ClearPen();
			Paint.SetBrush( Theme.SelectedBackground );
			Paint.DrawRect( wash, Theme.ControlRadius );

			//Round the whole tile, so the selection takes in the name without being painted over it.
			Paint.ClearBrush();
			Paint.SetPen( Theme.SelectedBackground.Lighten( 0.3f ), 1 );
			Paint.DrawRect( rect, Theme.ControlRadius );
		}

		if ( hover )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.VeilHover );
			Paint.DrawRect( rect, Theme.ControlRadius );
		}

		if ( !highlight && !hover )
		{
			entry.DrawBackground( rect );
		}

		entry.DrawText( textRect );

		Paint.BilinearFiltering = true;
		entry.DrawIcon( iconRect );
		Paint.BilinearFiltering = false;

		entry.DrawOverlay( rect );
	}

	public void PaintListItem( VirtualWidget item )
	{
		if ( item.Object is Action action )
		{
			action();
			return;
		}

		DrawItemBackground( item );
		SetPenColor( item );

		Paint.SetDefaultFont( 8 );

		if ( item.Object is IAssetListEntry entry && AllowItemHover( item ) )
			SetStatusText( entry.GetStatusText() );

		switch ( item.Object )
		{
			case DirectoryEntry directory:
				DrawDirectoryEntry( item, directory );
				break;
			case AssetEntry asset:
				DrawAssetEntry( item, asset );
				break;
			case string path:
				Paint.Pen = Theme.Yellow;
				DrawDefaultEntry( item, path );
				break;
			default:
				DrawDefaultEntry( item, "Untitled Entry" );
				break;
		}
	}

	private void DrawItemBackground( VirtualWidget item )
	{
		if ( Paint.HasSelected || Paint.HasPressed )
		{
			DrawSelectedBackground( item );
		}
		else if ( AllowItemHover( item ) )
		{
			DrawHoverBackground( item );
		}
	}

	//A row that is picked out: the accent, the same as a tile says it.
	private void DrawSelectedBackground( VirtualWidget item )
	{
		var rect = item.Rect.Shrink( 1 );

		Paint.ClearPen();
		Paint.SetBrush( Paint.HasPressed ? Theme.SelectedBackground.Darken( 0.1f ) : Theme.SelectedBackground );
		Paint.DrawRect( rect, Theme.ControlRadius );

		Paint.ClearBrush();
		Paint.SetPen( Theme.SelectedBackground.Lighten( 0.3f ), 1 );
		Paint.DrawRect( rect, Theme.ControlRadius );
	}

	private void DrawHoverBackground( VirtualWidget item )
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.VeilHover );
		Paint.DrawRect( item.Rect.Shrink( 1 ), Theme.ControlRadius );
	}

	//Entry names use one colour whether selected or not; the accent fill already shows selection
	private void SetPenColor( VirtualWidget item )
	{
		Paint.SetPen( Theme.Text.WithAlpha( 0.9f ) );
	}

	private void DrawDirectoryEntry( VirtualWidget item, DirectoryEntry directory )
	{
		Paint.DrawText( item.Rect.Shrink( 32, 0, 0, 0 ), directory.Name, TextFlag.LeftCenter | TextFlag.SingleLine );

		var metadata = DirectoryEntry.GetMetadata( directory.DirectoryInfo.FullName );
		Paint.SetPen( metadata.Color );
		Paint.DrawIcon( item.Rect.Shrink( 4, 3 ), "folder", 18, TextFlag.LeftCenter );
	}

	private void DrawAssetEntry( VirtualWidget item, AssetEntry asset )
	{
		var columns = new Dictionary<string, string>
		{
			{ "Name", FullPathMode ? asset.Asset?.RelativePath : asset.Name },
			{ "Date", FormatDateTime( asset.LastModified ) },
			{ "Type", asset.TypeName },
			{ "Size", asset.Size?.SizeFormat() },
			{ "Path", asset.Asset?.RelativePath }
		};

		if ( FullPathMode )
		{
			columns["Name"] = asset.Name;
			columns["Path"] = Path.GetDirectoryName( asset.Asset?.RelativePath );
		}

		DrawColumns( item, columns );
		DrawAssetIcon( item, asset );
	}

	private static string _dateTimeFormat;

	//Get normalised DateTime string that keeps the system date format
	private static string FormatDateTime( DateTime? timestamp )
	{
		if ( timestamp is not DateTime dt )
			return "";

		CultureInfo culture = CultureInfo.CurrentCulture;
		if ( _dateTimeFormat is null )
		{
			string datePattern = culture.DateTimeFormat.ShortDatePattern;
			datePattern = Regex.Replace( datePattern, @"(?<!d)d(?!d)", "dd" );
			datePattern = Regex.Replace( datePattern, @"(?<!M)M(?!M)", "MM" );
			datePattern = Regex.Replace( datePattern, @"(?<!y)y{1,2}(?!y)", "yyyy" );

			string timePattern = culture.DateTimeFormat.ShortTimePattern;
			timePattern = Regex.Replace( timePattern, @"(?<!h)h(?!h)", "hh" );
			timePattern = Regex.Replace( timePattern, @"(?<!H)H(?!H)", "HH" );
			timePattern = Regex.Replace( timePattern, @"(?<!m)m(?!m)", "mm" );

			_dateTimeFormat = $"{datePattern} {timePattern}";
		}

		return dt.ToString( _dateTimeFormat, culture );
	}

	private void DrawColumns( VirtualWidget item, Dictionary<string, string> columns )
	{
		int columnCount = SingleColumnMode ? 1 : columns.Count();

		var defaultColWidth = item.Rect.Width / columnCount;
		var textRect = item.Rect;
		int i = 0;
		foreach ( var col in columns )
		{
			//Hide the column if we have a header and the column isn't visible
			if ( ParentListHeader is not null && !ParentListHeader.IsColumnVisible( col.Key ) )
				continue;

			//Get the width of the column from the header if we have one, otherwise split equally
			string text = col.Value;
			var colWidth = ParentListHeader?.GetColumnWidth( col.Key ) ?? defaultColWidth;
			textRect.Right = textRect.Left + colWidth;
			if ( col.Key == "Name" )
			{
				//Leave space for the icon
				textRect.Left += 32;
				colWidth -= 32;
			}

			Paint.DrawText( textRect, text, TextFlag.LeftCenter | TextFlag.SingleLine );
			textRect.Left += colWidth + 4;
			i++;
		}
	}

	private void DrawAssetIcon( VirtualWidget item, AssetEntry asset )
	{
		var iconRect = item.Rect.Shrink( 4, 4 );
		iconRect.Width = iconRect.Height = 16;
		Paint.Draw( iconRect, asset.IconSmall );
	}

	private void DrawWarningIcon( VirtualWidget item )
	{
		var warningRect = item.Rect.Shrink( 3 );
		warningRect.Left += 13;
		warningRect.Size = 16;
		Paint.DrawIcon( warningRect, "warning", 16 );
	}

	private void DrawDefaultEntry( VirtualWidget item, string name )
	{
		Paint.SetDefaultFont( 8 );
		Paint.DrawText( item.Rect.Shrink( 42, 0 ), name, TextFlag.LeftCenter | TextFlag.SingleLine );
	}

	private AssetListViewMode _viewMode;

	/// <summary>Current view mode.</summary>
	public AssetListViewMode ViewMode
	{
		set
		{
			_viewMode = value;

			if ( ParentListHeader is not null )
			{
				ParentListHeader.Visible = _viewMode == AssetListViewMode.List;
			}

			switch ( _viewMode )
			{
				case AssetListViewMode.List:
					SetListMode();
					return;
				case AssetListViewMode.SmallIcons:
					SetIconMode( 64, 94 );
					return;
				case AssetListViewMode.MediumIcons:
					SetIconMode( 96, 136 );
					return;
				case AssetListViewMode.LargeIcons:
					SetIconMode( 128, 172 );
					return;
			}
		}
		get => _viewMode;
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		if ( e.HasCtrl )
		{
			var d = e.Delta > 0 ? 1 : -1;
			var lastViewMode = ViewMode;

			if ( d == 1 && ViewMode == AssetListViewMode.LargeIcons )
				ViewMode = AssetListViewMode.List;
			else if ( d == -1 && ViewMode == AssetListViewMode.List )
				ViewMode = AssetListViewMode.LargeIcons;
			else
				ViewMode += d;

			e.Accept();
			OnViewModeChanged?.Invoke();
			return;
		}

		base.OnMouseWheel( e );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton && GetItemAt( e.LocalPosition ) is null )
		{
			UnselectAll();
			Update();
		}

		base.OnMousePress( e );
	}

	void BuildAllIcons()
	{
		foreach ( var item in Items.OfType<Asset>() )
		{
			item.GetAssetThumb();
		}
	}

	void RecompileAllAssets()
	{
		foreach ( var item in Items.OfType<Asset>() )
		{
			item.Compile( true );
		}
	}


	[Shortcut( "editor.rename", "F2" )]
	public void RenameAsset()
	{
		if ( SelectedItems.FirstOrDefault() is IAssetListEntry entry )
		{
			OpenRenameFlyout( entry );
		}
	}

	[Shortcut( "editor.delete", "DEL" )]
	void DeleteAsset()
	{
		var items = SelectedItems.ToList();
		if ( items.Count() < 1 )
			return;

		var deleteMessage = "these items";
		if ( items.Count == 1 )
		{
			var firstItem = items.First();
			if ( firstItem is AssetEntry assetEntry )
			{
				//Get only the file name + extension, no folders
				var fileName = assetEntry.AbsolutePath ?? assetEntry.FileInfo.Name;

				deleteMessage = $"\"{fileName.Substring( fileName.LastIndexOf( '/' ) + 1 )}\"";
			}
			else if ( firstItem is DirectoryEntry directoryEntry )
			{
				deleteMessage = $"folder \"{directoryEntry.DirectoryInfo.Name}\"";
			}
		}
		var confirm = new PopupWindow(
			$"Deleting {items.Count()} items...",
			$"Are you sure you wish to delete {deleteMessage}?\nEVERYTHING including recursive directories and files will be moved to your Recycle Bin.", "No",
			new Dictionary<string, Action>() { { "Yes", () => {
				foreach ( var item in items )
				{
					if ( item is DirectoryEntry directoryEntry )
					{
						System.IO.Directory.Delete( directoryEntry.DirectoryInfo.FullName, true );
					}
					else if ( item is AssetEntry entry )
					{
						entry.Delete();
					}
				}

				Refresh();
		} } }
		);

		confirm.Show();
	}

	[Shortcut( "editor.duplicate", "CTRL+D" )]
	void DuplicateAsset()
	{
		foreach ( var item in SelectedItems )
		{
			if ( item is IAssetListEntry entry )
				entry.Duplicate();
		}
	}

	public static void Duplicate( Asset asset )
	{
		Action<string> CreateNew = ( string s ) =>
		{
			//Every file of it, not the source read back as text.
			EditorUtility.CopyAsset( asset, s, true );

			var copy = AssetSystem.RegisterFile( s );
			TreeBrowser.TreeAssetBrowser.RefreshOpen();
			TreeBrowser.TreeAssetBrowser.Reveal( copy );
			EditorUtility.InspectorObject = copy;
		};

		var fd = new FileDialog( null );
		fd.Title = $"Duplicate {asset.Name}";
		fd.Directory = Path.GetDirectoryName( asset.AbsolutePath );
		fd.DefaultSuffix = $".{asset.AssetType.FileExtension}";
		fd.SelectFile( $"untitled.{asset.AssetType.FileExtension}" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( $"{asset.AssetType.FriendlyName} (*.{asset.AssetType.FileExtension})" );

		if ( !fd.Execute() )
			return;

		CreateNew( fd.SelectedFile );
	}

	void AssetSystem.IEventListener.OnAssetThumbGenerated( Asset asset )
	{
		Rebuild();
	}


	[Event( "assetsystem.newfolder" )]
	void Refresh() => Rebuild();

	/// <summary>The list itself takes nothing; only a folder in it can be dropped on.</summary>
	public override void OnDragHover( DragEvent ev )
	{
		base.OnDragHover( ev );

		ev.Action = DropAction.Ignore;
	}

	private void OnDrop( DragEvent ev, string directory )
	{
		ev.Action = ev.HasCtrl ? DropAction.Copy : DropAction.Move;

		foreach ( var file in ev.Data.Files )
		{
			if ( directory.ToLowerInvariant() == file.ToLowerInvariant() ) continue;

			var asset = AssetSystem.FindByPath( file );

			if ( asset == null )
			{
				if ( !System.IO.Path.Exists( file ) ) continue;
				//This isn't an asset so just copy the file in directly
				var destinationFile = System.IO.Path.Combine( directory, System.IO.Path.GetFileName( file ) );

				if ( System.IO.Directory.Exists( file ) )
				{
					//Move Directory
					EditorUtility.RenameDirectory( file, destinationFile );
					DirectoryEntry.RenameMetadata( file, destinationFile );
				}
				else
				{
					//Move File
					if ( System.IO.Path.GetFullPath( file ) == System.IO.Path.GetFullPath( destinationFile ) )
						continue;

					if ( ev.HasCtrl ) System.IO.File.Copy( file, destinationFile );
					else System.IO.File.Move( file, destinationFile );
				}
			}
			else
			{
				if ( asset.IsDeleted ) continue;
				if ( ev.HasCtrl ) EditorUtility.CopyAssetToDirectory( asset, directory );
				else EditorUtility.MoveAssetToDirectory( asset, directory );
			}
		}

		Refresh();
	}

	protected override void OnDropOnItem( DragEvent ev, VirtualWidget item )
	{
		if ( !ev.Data.HasFileOrFolder )
			return;

		if ( item.Object is not DirectoryEntry dirInfo )
			return;

		OnDrop( ev, dirInfo.DirectoryInfo.FullName );
	}

	protected override void OnDragHoverItem( DragEvent ev, VirtualWidget item )
	{
		ev.Action = DropAction.Ignore;

		if ( !ev.Data.HasFileOrFolder )
			return;

		if ( item.Object is not DirectoryEntry )
			return;

		ev.Action = ev.HasCtrl ? DropAction.Copy : DropAction.Move;
	}

	[Shortcut( "editor.select-all", "CTRL+A" )]
	void ShortcutSelectAll()
	{
		SelectAll();
	}

	protected override IEnumerable<object> FindItemsThatStartWith( string text )
	{
		var entries = Items.OfType<IAssetListEntry>();
		return entries.Where( x => x.Name.StartsWith( text, StringComparison.OrdinalIgnoreCase ) );
	}
}
