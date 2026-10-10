using System;

namespace Editor;

/// <summary>Untyped view of a resource document: open a file, write edits each frame, close on last release</summary>
public interface IResourceDocument
{
	/// <summary>The file open here, or null while there is none.</summary>
	Asset Asset { get; }

	/// <summary>Whether there is a resource here at all.</summary>
	bool IsOpen { get; }

	/// <summary>Open a file here, closing whatever was open.</summary>
	void Open( Asset asset );

	/// <summary>Write what is unsaved and let the file go.</summary>
	void Close();

	/// <summary>Write out whatever was edited this frame.</summary>
	void WriteEdits();
}

/// <summary>One document per open resource file; edits are batched and written once at end of frame</summary>
public abstract class ResourceDocument<TResource> : IResourceDocument
	where TResource : GameResource
{
	Asset _asset;
	TResource _resource;
	SerializedObject _serialized;

	//Whether edits go to the disk: a file in the project rather than a cloud one or a read-only one.
	bool _writable;

	//Whether an edit is waiting to be written.
	bool _saveWanted;

	//Edits in progress (drags, scrubs); writes wait until they finish so a drag is one write
	int _editing;

	//Since the last change came in, for the guard in Write.
	RealTimeSince _sinceChange;

	public Asset Asset => _asset;

	/// <summary>The resource being edited, or null while there is none.</summary>
	public TResource Resource => _resource;

	/// <summary>The resource as the inspector's controls edit it.</summary>
	public SerializedObject Serialized => _serialized;

	public bool IsOpen => _resource is not null;

	/// <summary>Whether the file can be compiled again from here.</summary>
	public bool CanRecompile => _asset is { CanRecompile: true };

	/// <summary>Whether there is a version to go back to.</summary>
	public bool CanStepBack => _writable && ResourceHistory.Count( SourceFile ) > 0;

	/// <summary>A file was opened, or the one open was put back to an earlier version: whatever is showing it starts again from the resource.</summary>
	public event Action Opened;

	//What the history calls one write of this document.
	protected virtual string HistoryLabel => "Edit";

	public void Open( Asset asset )
	{
		Close();

		if ( asset is not null )
		{
			_asset = asset;
			_resource = asset.TryLoadResource<TResource>( out var resource ) ? resource : null;

			if ( _resource is not null )
			{
				_serialized = _resource.GetSerialized();
				_serialized.OnPropertyChanged += OnSerializedPropertyChanged;
				_serialized.OnPropertyStartEdit += OnStartEdit;
				_serialized.OnPropertyFinishEdit += OnFinishEdit;
			}

			_writable = _resource is not null && asset.HasSourceFile && !AssetSystem.IsCloudInstalled( asset.Package );
		}

		Opened?.Invoke();
	}

	public void Close()
	{
		//Whatever this frame changed goes down before the file is let go of - an edit still in progress included.
		Write( force: true );

		if ( _serialized is not null )
		{
			_serialized.OnPropertyChanged -= OnSerializedPropertyChanged;
			_serialized.OnPropertyStartEdit -= OnStartEdit;
			_serialized.OnPropertyFinishEdit -= OnFinishEdit;
		}

		_asset = null;
		_resource = null;
		_serialized = null;
		_writable = false;
		_saveWanted = false;
		_editing = 0;
	}

	void OnSerializedPropertyChanged( SerializedProperty property )
	{
		_saveWanted = true;
		_sinceChange = 0;
		OnPropertyChanged( property );
	}

	void OnStartEdit( SerializedProperty property ) => _editing++;

	//Never below nought: a finish nobody started is a finish, not a debt.
	void OnFinishEdit( SerializedProperty property ) => _editing = Math.Max( 0, _editing - 1 );

	//One of the resource's own properties was edited through the serialized object.
	protected abstract void OnPropertyChanged( SerializedProperty property );

	//The resource was written to directly rather than through one of its properties, and is to be saved.
	protected void MarkEdited()
	{
		if ( _resource is null ) return;

		_resource.StateHasChanged();
		_saveWanted = true;
	}

	string SourceFile => _asset?.GetSourceFile( true );

	/// <summary>Writes out whatever was edited this frame, and remembers the version it replaced.</summary>
	public void WriteEdits() => Write( force: false );

	void Write( bool force )
	{
		if ( !_writable ) return;

		if ( !_saveWanted && _resource is { HasUnsavedChanges: true } )
			_saveWanted = true;

		if ( !_saveWanted ) return;

		if ( _editing > 0 && !force )
		{
			//An edit is still going: it is written when it finishes. Unless it never says so - a control deleted in the middle of a drag - in
			//which case nothing would ever be written again, and a material that stops saving is worse than one that saves once too often.
			if ( _sinceChange < StaleEdit ) return;

			_editing = 0;
		}

		_saveWanted = false;

		var file = SourceFile;
		var before = Read( file );

		_asset.SaveToDisk( _resource );

		ResourceHistory.Record( file, HistoryLabel, before, Read( file ) );
	}

	//How long an edit may go without a change before it is taken to have been abandoned.
	const float StaleEdit = 5.0f;

	//The document as it is on the disk, or nothing where there is none.
	static string Read( string file )
	{
		if ( string.IsNullOrEmpty( file ) ) return null;

		try
		{
			return System.IO.File.Exists( file ) ? System.IO.File.ReadAllText( file ) : null;
		}
		catch ( Exception )
		{
			return null;
		}
	}

	/// <summary>Compile the file again from scratch.</summary>
	public void Recompile()
	{
		if ( !CanRecompile ) return;

		_asset.Compile( true );
	}

	/// <summary>Show the file in the operating system's file browser.</summary>
	public void Reveal()
	{
		if ( string.IsNullOrWhiteSpace( _asset?.AbsolutePath ) ) return;

		EditorUtility.OpenFileFolder( _asset.AbsolutePath );
	}

	/// <summary>Puts the resource back the way it was before the last change.</summary>
	public void StepBack()
	{
		if ( !_writable ) return;

		var file = SourceFile;

		//Whatever this frame changed goes down first, or the step back would be taken from a version the history has not been told about.
		Write( force: true );

		if ( !ResourceHistory.TryStepBack( file, out var document, out var name ) ) return;

		_resource.LoadFromJson( document );
		_asset.SaveToDisk( _resource );
		_saveWanted = false;

		Log.Info( $"{_asset.Name}: back to before '{name}'" );

		Opened?.Invoke();
	}
}
