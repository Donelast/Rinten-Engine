using Rinten.Helpers;
using System.Text.Json.Nodes;

namespace Editor.SpriteEditor;

/// <summary>The sprite, its undo stack, and the file it lives in.</summary>
public sealed class SpriteDocument : ISpriteDocument
{
	private readonly UndoSystem _undo = new();

	//The sprite as it was after the last commit, serialized.
	private string _committed;

	//The sprite as it is on disk, for going back to without saving.
	private JsonObject _saved;

	private bool _restoring;

	public Sprite Sprite { get; private set; } = new();
	public Asset Asset { get; private set; }
	public bool IsModified { get; private set; }

	public event Action Loaded;
	public event Action Restored;
	public event Action<SpriteChange> Changed;

	public SpriteDocument()
	{
		_undo.Initialize();
		_committed = Snapshot();
	}

	//Undo

	public bool CanUndo => _undo.Back.Count > 0;
	public bool CanRedo => _undo.Forward.Count > 0;

	/// <summary>What undo would take back, or null.</summary>
	public string UndoName => _undo.Back.TryPeek( out var entry ) ? entry.Name : null;

	/// <summary>What redo would do again, or null.</summary>
	public string RedoName => _undo.Forward.TryPeek( out var entry ) ? entry.Name : null;

	public bool Undo() => _undo.Undo();
	public bool Redo() => _undo.Redo();

	public void Edit( string name, Action edit, object source = null )
	{
		edit?.Invoke();
		Commit( name, coalesce: false, source );
	}

	public void Commit( string name, bool coalesce = false, object source = null )
	{
		if ( _restoring ) return;

		var pre = _committed;
		var post = Snapshot();

		if ( post is null || post == pre ) return;

		if ( coalesce && _undo.Back.TryPeek( out var last ) && last.Name == name )
		{
			//The step already there keeps its own way back; only where it goes forward moves on.
			_undo.Back.Pop();
			_undo.Insert( name, last.Undo, () => Restore( post ) );
		}
		else
		{
			_undo.Insert( name, () => Restore( pre ), () => Restore( post ) );
		}

		_committed = post;
		IsModified = true;

		Changed?.Invoke( new SpriteChange( name, source ) );
	}

	private string Snapshot()
	{
		try
		{
			return Sprite?.Serialize()?.ToJsonString();
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Sprite editor: could not snapshot the sprite" );
			return null;
		}
	}

	private void Restore( string state )
	{
		if ( string.IsNullOrEmpty( state ) ) return;

		var json = Json.ParseToJsonObject( state );
		if ( json is null ) return;

		_restoring = true;

		try
		{
			Sprite.Deserialize( json );
			_committed = state;
			IsModified = state != _saved?.ToJsonString();
		}
		finally
		{
			_restoring = false;
		}

		Restored?.Invoke();
	}

	private void ResetHistory()
	{
		_undo.Initialize();
		_committed = Snapshot();
		_saved = Sprite.Serialize();
	}

	//The file

	/// <summary>Opens a sprite asset here. False if the asset does not hold a sprite.</summary>
	public bool Open( Asset asset )
	{
		if ( asset is null ) return false;

		var sprite = asset.LoadResource<Sprite>();
		if ( sprite is null )
		{
			Log.Warning( $"Failed to load sprite resource from asset {asset.Name}" );
			return false;
		}

		Take( asset, sprite );
		return true;
	}

	/// <summary>Makes a new sprite at a path and opens it.</summary>
	public bool CreateNew( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return false;

		var asset = AssetSystem.CreateResource( "sprite", path );
		var sprite = asset?.LoadResource<Sprite>();
		if ( sprite is null ) return false;

		Take( asset, sprite );
		return true;
	}

	private void Take( Asset asset, Sprite sprite )
	{
		Asset = asset;
		Sprite = sprite;
		IsModified = false;

		ResetHistory();

		Loaded?.Invoke();
	}

	/// <summary>Writes the sprite to its asset. Hand over a new asset to write it somewhere else from now on.</summary>
	public bool Save( Asset target = null )
	{
		if ( target is not null ) Asset = target;
		if ( Asset is null ) return false;

		Asset.SaveToDisk( Sprite );

		_saved = Sprite.Serialize();
		IsModified = false;

		return true;
	}

	/// <summary>Puts the sprite back to what is on disk. The resource is shared with whoever else has it loaded, so a discarded edit has to be undone in memory and not merely left unsaved.</summary>
	public void RevertToSaved()
	{
		if ( _saved is null )
		{
			IsModified = false;
			return;
		}

		Sprite.Deserialize( _saved );
		Asset?.SaveToDisk( Sprite );

		_committed = Snapshot();
		IsModified = false;

		Restored?.Invoke();
	}
}
