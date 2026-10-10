using System.Linq;
using System.Text.Json.Serialization;

namespace Editor.Inspectors;

/// <summary>Properties sheet for resources without an editor; real changes are written at end of frame</summary>
[StyleSheet( "GameResourceInspectorPanel.cs.scss" )]
public sealed class GameResourceInspectorPanel : InspectorPanel, IInspectorHead
{
	readonly Asset _asset;
	readonly GameResource _resource;
	readonly bool _readOnly;

	//The page's own first card, emptied into the asset's header.
	readonly Rinten.UI.Panel _head;

	//The properties under no heading, for the asset's header to hold.
	readonly Rinten.UI.Panel _loose;

	//The document as it was last written, to tell an edit from a touch.
	int _cleanHash;

	//An edit is waiting to be written, at the end of a tick rather than mid-edit.
	bool _saveWanted;

	/// <summary>The page for one asset, or nothing where it holds no resource.</summary>
	public static GameResourceInspectorPanel For( SerializedObject so )
	{
		if ( so is null || so.IsMultipleTargets ) return null;
		if ( so.Targets.OfType<Asset>().FirstOrDefault() is not { } asset ) return null;
		if ( !asset.TryLoadResource<GameResource>( out var resource ) ) return null;

		//A scene is a resource only in the sense that it is a file the resource system reads: its properties are
		//its objects, and those are the scene editor's to show. The dock falls through to the asset's own fields.
		if ( resource is SceneFile ) return null;

		return new GameResourceInspectorPanel( so, asset, resource );
	}

	GameResourceInspectorPanel( SerializedObject so, Asset asset, GameResource resource ) : base( so )
	{
		AddClass( "game-resource-inspector column" );

		_asset = asset;
		_resource = resource;
		_readOnly = ResourceEdits.IsReadOnly( asset );
		_cleanHash = Hash( resource );

		var editing = resource.GetSerialized();
		editing.OnPropertyChanged += _ => OnEdited();

		//Laid out the way the inspector lays out a GameObject: what is under no heading in the first
		//card - the asset's header - and a card for each heading the type filed the rest under.
		var scroll = AddChild<Rinten.UI.Panel>( "scroll" );

		_head = scroll.AddChild<Rinten.UI.Panel>( "inspector-head" );
		_loose = _head.AddChild<Rinten.UI.Panel>( "loose column" );

		InspectorCard.Lay( _loose, scroll, editing.Where( IsSerialized ), $"resource.{resource.GetType().Name}" );

		_head.SetClass( "hidden", _loose.ChildrenCount == 0 );
	}

	public void FillHead( AssetHeaderPanel head )
	{
		head.Fields.AddChild( _loose );
		_head.AddClass( "hidden" );
	}

	//Only what reaches the file: a property that is not written is not one to edit here.
	static bool IsSerialized( SerializedProperty prop )
	{
		if ( prop.IsMethod ) return true;
		if ( !prop.IsProperty ) return false;
		if ( prop.HasAttribute<JsonIgnoreAttribute>() ) return false;

		return prop.IsPublic || prop.HasAttribute<JsonIncludeAttribute>();
	}

	void OnEdited()
	{
		if ( _readOnly ) return;

		var hash = Hash( _resource );
		if ( hash == _cleanHash ) return;

		_cleanHash = hash;
		_resource.StateHasChanged();
		_saveWanted = true;
	}

	static int Hash( GameResource resource ) => resource.Serialize().ToJsonString().FastHash();

	public override void Tick()
	{
		base.Tick();

		if ( _readOnly || !_saveWanted ) return;

		_saveWanted = false;
		ResourceEdits.Write( _asset, _resource );
	}

	//Whatever this frame changed goes down before the page does.
	protected override bool OnInspectorClose( object newObj = null )
	{
		if ( !_readOnly && _saveWanted )
		{
			_saveWanted = false;
			ResourceEdits.Write( _asset, _resource );
		}

		return true;
	}
}
