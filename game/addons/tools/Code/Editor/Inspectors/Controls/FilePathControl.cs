namespace Editor.Inspectors;

using Editor.AssetPickers;
using Rinten.UI;
using System.IO;

/// <summary>A file, stored on the property as its path, filtered by <see cref="FilePathAttribute"/>.</summary>
[StyleSheet( "FilePathControl.cs.scss" )]
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( FilePathAttribute )] )]
public sealed class FilePathControl : BaseControl
{
	readonly ResourceThumbPanel _thumb;
	readonly TextEntry _entry;
	readonly ToolbarButton _browse;

	int _thumbHash;

	string _extensions = "";
	string _placeholder = "";
	bool _allowExternal;
	bool _saving;

	//Whether the button goes to the operating system's box rather than the asset picker.
	//Saving always does: a file about to be written is not one the asset library can offer.
	bool BrowsesDisk => _allowExternal || _saving;

	public override bool SupportsMultiEdit => true;

	public FilePathControl()
	{
		_thumb = AddChild<ResourceThumbPanel>();

		//The box lives in a field of its own, the way the other typed controls build theirs - it fills that
		//rather than sharing the row directly, which is what gives its placeholder somewhere to be drawn.
		var field = AddChild<Rinten.UI.Panel>( "field" );

		_entry = field.AddChild<TextEntry>();
		_entry.OnTextEdited = OnTextEdited;

		_browse = AddChild<ToolbarButton>();
		_browse.Icon = "file_open";
		_browse.ToolTip = "Select File";
		_browse.OnPressed = OpenPicker;
	}

	public override void Rebuild()
	{
		var attribute = Property is not null && Property.TryGetAttribute<FilePathAttribute>( out var found ) ? found : null;

		_extensions = attribute?.Extension ?? "";
		_allowExternal = attribute?.AllowExternal ?? false;
		_saving = attribute?.Saving ?? false;

		_placeholder = PlaceholderText();
		_thumbHash = 0;

		SyncEntry();
		SyncThumb();
	}

	public override void Tick()
	{
		if ( !EditorDrag.Active && HasClass( "drop-target" ) )
			SetClass( "drop-target", false );

		base.Tick();

		SyncEntry();
		SyncThumb();
	}

	protected override int BuildHash() => HashCode.Combine( CurrentPath(), IsReadOnly );

	protected override void OnReadOnlyChanged()
	{
		if ( _entry.IsValid() )
			_entry.Disabled = IsReadOnly;

		if ( _browse.IsValid() )
		{
			_browse.Disabled = IsReadOnly;
			_browse.StateHasChanged();
		}
	}

	string CurrentPath() => Property?.GetValue<string>( null );

	//Puts the property's path in the box, unless it is being typed into.
	void SyncEntry()
	{
		if ( !_entry.IsValid() ) return;

		//Kept in step here rather than set once: the box is built before the property arrives, so a
		//placeholder written at build time is written to a box that does not yet know what it takes.
		if ( _entry.Placeholder != _placeholder )
			_entry.Placeholder = _placeholder;

		if ( _entry.HasFocus ) return;

		var path = Property is { IsMultipleDifferentValues: true } ? "" : CurrentPath() ?? "";

		if ( _entry.Value != path )
			_entry.Value = path;
	}

	void OnTextEdited( string text ) => Assign( string.IsNullOrWhiteSpace( text ) ? null : text.Trim() );

	//The picture on the left: the file's own thumbnail where it has one, the icon for the kind of file the field takes where it does not.
	void SyncThumb()
	{
		if ( !_thumb.IsValid() ) return;

		var path = CurrentPath();
		var hash = HashCode.Combine( path, _extensions );

		if ( hash == _thumbHash ) return;

		_thumbHash = hash;
		_thumb.Update( ThumbState( path ) );
	}

	ResourceDisplayState ThumbState( string path )
	{
		var type = AllowedAssetTypes().FirstOrDefault();

		if ( string.IsNullOrWhiteSpace( path ) )
			return new( ResourceFieldMode.Placeholder, "", "", "", type, null, null );

		var asset = AssetSystem.FindByPath( path );
		if ( asset is { IsDeleted: true } ) asset = null;

		if ( asset is not null )
			return new( ResourceFieldMode.Set, "", path, "", type, asset, null );

		//No asset behind it, so the file itself is the only picture available - and where there is no file either, the field is pointing at nothing.
		var thumb = FileThumb( path );

		return thumb is null
			? new( ResourceFieldMode.Missing, "", path, "", type, null, null )
			: new( ResourceFieldMode.Set, "", path, "", type, null, null, thumb );
	}

	//A picture of the file itself, for a file no asset was made from.
	static Pixmap FileThumb( string filePath )
	{
		var full = AbsolutePath( filePath );

		if ( string.IsNullOrWhiteSpace( full ) || !File.Exists( full ) )
			return null;

		try
		{
			return Pixmap.FromFile( full );
		}
		catch ( Exception )
		{
			return null;
		}
	}

	//The extensions this field takes, without their dots; none means any file. Written both ways - "fbx"
	//and ".fbx" - so the dot comes off here rather than turning up doubled everywhere one is added back.
	string[] Extensions() =>
		_extensions
			.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
			.Select( x => x.TrimStart( '.' ) )
			.Where( x => !string.IsNullOrEmpty( x ) )
			.ToArray();

	//What an empty box says it is for.
	string PlaceholderText()
	{
		var extensions = Extensions();

		return extensions.Length == 0 ? "Any File" : $"(*.{string.Join( " | *.", extensions )})";
	}

	//The asset types behind this field's extensions, so the picker only offers those.
	List<AssetType> AllowedAssetTypes() =>
		Extensions()
			.Select( AssetType.FromExtension )
			.Where( x => x is not null )
			.Distinct()
			.ToList();

	public override void OnPropertyContextMenu( Menu menu )
	{
		if ( Property is null ) return;

		menu.AddOption( "Clear", "backspace", () => Assign( null ) )
			.Enabled = !IsReadOnly && !string.IsNullOrEmpty( CurrentPath() );
	}

	/// <summary>The path, so it pastes into any other field that takes one.</summary>
	public override string ToClipboardString() => CurrentPath();

	public override void FromClipboardString( string clipboard )
	{
		if ( !CanAssign( clipboard ) ) return;

		AssignObject( clipboard );
	}

	public override bool AcceptEditorDrops => !IsReadOnly;

	public override DropAction OnEditorDragHover( Widget.DragEvent ev )
	{
		var accept = TryGetDrop( ev, out _ );
		SetClass( "drop-target", accept );
		return accept ? DropAction.Link : DropAction.Ignore;
	}

	public override void OnEditorDragLeave() => SetClass( "drop-target", false );

	public override void OnEditorDragDrop( Widget.DragEvent ev )
	{
		if ( IsReadOnly ) return;
		if ( !TryGetDrop( ev, out var dropped ) ) return;

		AssignObject( dropped );
		ev.Action = DropAction.Link;
	}

	bool TryGetDrop( Widget.DragEvent ev, out object value )
	{
		value = ev.Data.Object switch
		{
			Asset asset => asset,
			Asset[] many when many.Length > 0 => many[0],
			_ => null
		};

		value ??= ev.Data.HasFileOrFolder ? ev.Data.FileOrFolder : null;
		value ??= string.IsNullOrWhiteSpace( ev.Data.Text ) ? null : ev.Data.Text;

		return value is not null && CanAssign( value );
	}

	bool CanAssign( object value ) => value switch
	{
		Asset asset => CanAssign( asset.RelativePath ?? asset.AbsolutePath ),
		string path => CanAssign( path ),
		_ => false
	};

	bool CanAssign( string path )
	{
		path = NormalizePath( path );
		if ( string.IsNullOrWhiteSpace( path ) ) return false;

		var extensions = Extensions();
		if ( extensions.Length == 0 ) return true;

		var extension = Path.GetExtension( path ).TrimStart( '.' );
		if ( string.IsNullOrEmpty( extension ) ) return false;

		return extensions.Contains( extension, StringComparer.OrdinalIgnoreCase );
	}

	void OpenPicker()
	{
		if ( Property is null || IsReadOnly ) return;

		if ( BrowsesDisk )
		{
			BrowseDisk();
			return;
		}

		AssetPicker.CloseOpenPickers();

		var picker = new GenericPicker( PickerParent(), AllowedAssetTypes(), new AssetPicker.PickerOptions() );
		picker.Title = $"Select {Property.DisplayName}";
		picker.OnAssetPicked = assets => AssignObject( assets?.FirstOrDefault() );
		picker.SetSelection( CurrentPath() );
		picker.Show();
	}

	//The operating system's own file box, for a field that takes files from outside the
	//project. People know their own file box, and it knows their drives and their shortcuts.
	void BrowseDisk()
	{
		var full = AbsolutePath( CurrentPath() );
		var extensions = Extensions();

		var dialog = new FileDialog( PickerParent() );
		dialog.Title = $"Select {Property.DisplayName}";
		dialog.SetFindFile();
		dialog.SetNameFilter( NameFilter() );

		if ( _saving )
		{
			dialog.SetModeSave();

			//The box adds this where whoever typed the name left it off.
			if ( extensions.Length > 0 )
				dialog.DefaultSuffix = $".{extensions[0]}";
		}
		else
		{
			dialog.SetModeOpen();
		}

		if ( !string.IsNullOrWhiteSpace( full ) )
		{
			dialog.Directory = Path.GetDirectoryName( full );
			dialog.SelectFile( Path.GetFileName( full ) );
		}
		else if ( _saving )
		{
			//Nothing named yet, so start where the project is rather than wherever the box was last left.
			dialog.Directory = Project.Current?.GetRootPath();
		}

		if ( !dialog.Execute() ) return;

		var picked = dialog.SelectedFile;
		if ( string.IsNullOrWhiteSpace( picked ) ) return;

		Assign( PathFromPick( picked ) );
	}

	//The dialog's file type filter, covering every extension this field takes.
	string NameFilter()
	{
		var extensions = Extensions();

		if ( extensions.Length == 0 )
			return "All files (*)";

		var globs = string.Join( ' ', extensions.Select( x => $"*.{x}" ) );
		return $"{string.Join( ", ", extensions )} ({globs})";
	}

	//Where on disk the property's path points, so the box opens on the file it already holds.
	static string AbsolutePath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;
		if ( Path.IsPathRooted( path ) ) return path;

		return FileSystem.Content?.GetFullPath( path ) ?? path;
	}

	static Widget PickerParent() => Rinten.Internal.GlobalToolsNamespace.EditorWindow;

	void AssignObject( object value )
	{
		switch ( value )
		{
			case Asset asset:
				Assign( asset.RelativePath );
				break;

			case string path:
				Assign( PathFromPick( path ) );
				break;
		}
	}

	void Assign( string path )
	{
		if ( IsReadOnly || Property is null ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue( path );
		Property.Parent?.NoteFinishEdit( Property );
	}

	//One line, no quotes, and a file: URL turned back into a path.
	static string NormalizePath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;

		path = path.Trim().Split( '\n' )[0].Trim().Trim( '"' );

		if ( !path.StartsWith( "file:", StringComparison.OrdinalIgnoreCase ) )
			return path;

		if ( Uri.TryCreate( path, UriKind.Absolute, out var uri ) && uri.IsFile )
			return uri.LocalPath;

		const string prefix = "file:///";
		return path.StartsWith( prefix, StringComparison.OrdinalIgnoreCase )
			? Uri.UnescapeDataString( path[prefix.Length..] )
			: path;
	}

	//A picked or dropped path as the project sees it, relative where it can be.
	string PathFromPick( string path )
	{
		path = NormalizePath( path );
		if ( string.IsNullOrWhiteSpace( path ) ) return path;

		//A file about to be written is handed to whatever writes it as a filesystem path, not looked up in the
		//asset library. Making it relative to the assets folder would point the write somewhere else entirely.
		if ( _saving ) return path;

		if ( AssetSystem.FindByPath( path ) is { } found )
			return found.RelativePath;

		if ( Path.IsPathRooted( path ) && Project.Current is not null )
		{
			var relative = Path.GetRelativePath( Project.Current.GetAssetsPath(), path );

			//A file from outside the project has only its absolute path to go by: a walk of "../.." would break the moment the project moved.
			if ( BrowsesDisk && ( relative.StartsWith( ".." ) || Path.IsPathRooted( relative ) ) )
				return path;

			path = relative;
		}

		return path.Replace( '\\', '/' );
	}
}
