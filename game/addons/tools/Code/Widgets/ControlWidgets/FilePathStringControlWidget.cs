using Editor.AssetPickers;
using Rinten.Diagnostics;

namespace Editor;

/// <summary>File paths stored as strings</summary>
[CustomEditor( typeof( string ), WithAllAttributes = new[] { typeof( FilePathAttribute ) } )]
public class FilePathStringControlWidget : ControlWidget
{
	public override bool IsControlButton => true;
	public override bool SupportsMultiEdit => true;

	public string Extension { get; set; } = string.Empty;

	public FilePathStringControlWidget( SerializedProperty property ) : base( property )
	{
		property.TryGetAttribute<FilePathAttribute>( out var attr );
		Assert.NotNull( attr, "FilePathStringControlWidget property has no FilePathAttribute" );

		Extension = attr.Extension ?? "";

		Cursor = CursorShape.Finger;
		MouseTracking = true;
		AcceptDrops = true;
		IsDraggable = true;

		OnValueChanged();
	}

	protected override void PaintControl()
	{
		var filePath = SerializedProperty.GetValue<string>( null );
		var asset = filePath != null ? AssetSystem.FindByPath( filePath ) : null;
		var isEmpty = string.IsNullOrWhiteSpace( filePath );

		var rect = new Rect( 0, Size );

		var iconRect = rect.Shrink( 2 );
		iconRect.Width = iconRect.Height;

		rect.Left = iconRect.Right + 10;

		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground.WithAlpha( 0.2f ) );
		Paint.DrawRect( iconRect, 2 );

		var pickerName = "Any File";
		if ( !string.IsNullOrEmpty( Extension ) )
		{
			var extensions = Extension.Split( ',' );
			if ( extensions.Length > 1 )
			{
				pickerName = $"(*.{extensions[0]} | *." + string.Join( " | *.", extensions.Skip( 1 ) ) + ")";
			}
			else if ( extensions.Length == 1 )
			{
				pickerName = $"(*.{extensions[0]})";
			}
		}

		Pixmap icon = null;
		var relatedAssetType = AssetType.All.FirstOrDefault( x => x.FileExtension == Extension );
		if ( relatedAssetType is not null )
		{
			icon = relatedAssetType.Icon64;
		}

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			var textRect = rect.Shrink( 0, 3 );
			if ( icon != null ) Paint.Draw( iconRect, icon );

			Paint.SetDefaultFont();
			Paint.SetPen( Theme.MultipleValues );
			Paint.DrawText( textRect, $"Multiple Values", TextFlag.LeftCenter );
		}
		else if ( !isEmpty )
		{
			var thumb = asset is not null && !asset.IsDeleted ? asset.GetAssetThumb( true ) : null;
			if ( thumb is null )
			{
				var full = asset?.GetSourceFile( true ) ?? FileSystem.Content?.GetFullPath( filePath );
				if ( string.IsNullOrWhiteSpace( full ) && System.IO.Path.IsPathRooted( filePath ) )
					full = filePath;

				if ( !string.IsNullOrWhiteSpace( full ) && System.IO.File.Exists( full ) )
					thumb = Pixmap.FromFile( full );
			}

			if ( thumb is not null )
				Paint.Draw( iconRect, thumb );

			var textRect = rect.Shrink( 0, 3 );
			var fileExt = System.IO.Path.GetExtension( filePath );
			var fileName = System.IO.Path.GetFileName( filePath );
			fileName = fileName.Remove( fileName.IndexOf( fileExt ) ).ToTitleCase().ToLower();

			Paint.SetPen( Theme.Text.WithAlpha( 0.9f ) );
			Paint.SetHeadingFont( 8, 450 );
			var t = Paint.DrawText( textRect, $"{fileName}", TextFlag.LeftTop );

			textRect.Left = t.Right + 6;
			Paint.SetDefaultFont( 7 );
			Theme.DrawFilename( textRect, filePath, TextFlag.LeftCenter, Theme.Text.WithAlpha( 0.5f ) );
		}
		else
		{
			var textRect = rect.Shrink( 0, 3 );
			if ( icon != null ) Paint.Draw( iconRect, icon );

			Paint.SetDefaultFont( italic: true );
			Paint.SetPen( Theme.Text.WithAlpha( 0.2f ) );
			Paint.DrawText( textRect, pickerName, TextFlag.LeftCenter );
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var m = new ContextMenu();

		var filePath = SerializedProperty.GetValue<string>( null );
		var isEmpty = string.IsNullOrEmpty( filePath );

		//m.AddOption( "Open in Editor", "edit", () => asset?.OpenInEditor() ).Enabled = asset != null;
		//m.AddOption( "Find in Asset Browser", "search", () => AssetBrowser.OpenTo( asset, true ) ).Enabled = asset is not null;
		//m.AddSeparator();
		m.AddOption( "Copy", "file_copy", action: Copy ).Enabled = !isEmpty;
		m.AddOption( "Paste", "content_paste", action: Paste );
		m.AddSeparator();
		m.AddOption( "Clear", "backspace", action: Clear ).Enabled = !isEmpty;

		m.OpenAtCursor( false );
		e.Accepted = true;
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( ReadOnly ) return;

		var filePath = SerializedProperty.GetValue<string>( null );

		var picker = new GenericPicker( Parent, [], new() );
		var query = "";
		foreach ( var ext in Extension.Split( ',' ) )
		{
			if ( string.IsNullOrEmpty( ext ) ) continue;
			query += $"t:{ext} ";
		}
		picker.Browser.SetSearch( query );

		picker.Title = $"Select {SerializedProperty.DisplayName}";
		picker.Browser.OnAssetHighlighted += ( asset ) =>
		{
			PropertyStartEdit();
			UpdateFromObject( asset );
			PropertyFinishEdit();
		};
		picker.Browser.OnFileSelected += ( o ) =>
		{
			PropertyStartEdit();
			UpdateFromObject( o );
			PropertyFinishEdit();
		};
		picker.Show();

		picker.SetSelection( filePath );
	}

	private void UpdateFromObject( object obj )
	{
		if ( obj is null ) return;

		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		if ( obj is Asset asset )
		{
			SerializedProperty.SetValue( asset.RelativePath );
		}
		else if ( obj is string str )
		{
			SerializedProperty.SetValue( PathFromDrop( str ) );
		}
		else if ( obj is AssetEntry assetEntry )
		{
			SerializedProperty.SetValue( PathFromDrop( assetEntry.AbsolutePath ) );
		}
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
	}

	bool CanAssign( string path )
	{
		path = NormalizePath( path );
		if ( string.IsNullOrWhiteSpace( path ) ) return false;

		var ext = System.IO.Path.GetExtension( path ).TrimStart( '.' );
		if ( string.IsNullOrEmpty( Extension ) ) return true;
		if ( string.IsNullOrEmpty( ext ) ) return false;

		return Extension.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
			.Contains( ext, StringComparer.OrdinalIgnoreCase );
	}

	bool CanAssign( object obj )
	{
		return obj switch
		{
			Asset asset => CanAssign( asset.RelativePath ?? asset.AbsolutePath ),
			AssetEntry entry => CanAssign( entry.AbsolutePath ),
			string path => CanAssign( path ),
			_ => false
		};
	}

	bool TryGetDrop( DragEvent ev, out object value )
	{
		value = null;

		if ( ev.Data.Object is Asset asset )
			value = asset;
		else if ( ev.Data.Object is Asset[] many && many.Length > 0 )
			value = many[0];
		else if ( ev.Data.HasFileOrFolder )
			value = ev.Data.FileOrFolder;
		else if ( !string.IsNullOrWhiteSpace( ev.Data.Text ) )
			value = ev.Data.Text;

		return value is not null && CanAssign( value );
	}

	public override void OnDragHover( DragEvent ev )
	{
		if ( !TryGetDrop( ev, out _ ) )
			return;

		ev.Action = DropAction.Link;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		if ( !TryGetDrop( ev, out var value ) )
			return;

		PropertyStartEdit();
		UpdateFromObject( value );
		PropertyFinishEdit();

		ev.Action = DropAction.Link;
	}

	static string NormalizePath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;

		path = path.Trim().Split( '\n' )[0].Trim().Trim( '"' );

		if ( path.StartsWith( "file:", StringComparison.OrdinalIgnoreCase ) )
		{
			if ( Uri.TryCreate( path, UriKind.Absolute, out var uri ) && uri.IsFile )
				return uri.LocalPath;

			const string prefix = "file:///";
			if ( path.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
				return Uri.UnescapeDataString( path[prefix.Length..] );
		}

		return path;
	}

	string PathFromDrop( string path )
	{
		path = NormalizePath( path );
		if ( string.IsNullOrWhiteSpace( path ) ) return path;

		var found = AssetSystem.FindByPath( path );
		if ( found is not null )
			return found.RelativePath;

		if ( System.IO.Path.IsPathRooted( path ) && Project.Current is not null )
			path = System.IO.Path.GetRelativePath( Project.Current.GetAssetsPath(), path );

		return path.Replace( '\\', '/' );
	}

	protected override void OnDragStart()
	{
		var filePath = SerializedProperty.GetValue<string>( null );

		var drag = new Drag( this );
		drag.Data.Text = filePath;
		drag.Execute();
	}

	void Copy()
	{
		var filePath = SerializedProperty.GetValue<string>( null );
		if ( string.IsNullOrEmpty( filePath ) ) return;

		EditorUtility.Clipboard.Copy( filePath );
	}

	void Paste()
	{
		var path = EditorUtility.Clipboard.Paste();
		if ( !CanAssign( path ) )
			return;

		UpdateFromObject( path );
	}

	void Clear()
	{
		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue( (string)null );
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
	}
}
