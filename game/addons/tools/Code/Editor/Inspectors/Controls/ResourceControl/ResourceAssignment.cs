namespace Editor.Inspectors;

//Loads assets into resource properties and checks whether a drop is allowed.
static class ResourceAssignment
{
	public static bool CanAssign( SerializedProperty property, AssetType assetType, Asset asset )
	{
		if ( asset is null ) return false;
		if ( assetType is null ) return true;

		if ( property.PropertyType.IsInterface )
		{
			if ( !asset.AssetType.IsGameResource ) return false;
			return asset.AssetType.ResourceType.IsAssignableTo( property.PropertyType );
		}

		if ( assetType != asset.AssetType )
		{
			if ( asset.AssetType?.ProvidedResourceType?.IsAssignableTo( property.PropertyType ) == true )
				return true;

			if ( !assetType.IsGameResource ) return false;
			if ( !asset.AssetType.IsGameResource ) return false;
			if ( !asset.AssetType.ResourceType.IsAssignableTo( property.PropertyType ) ) return false;
		}

		var scene = property.GetContainingGameObject()?.Scene;

		if ( property.PropertyType == typeof( PrefabFile ) && scene is PrefabScene prefabScene )
		{
			var prefab = asset.LoadResource<PrefabFile>();
			var scenePrefab = prefabScene.ToPrefabFile();
			if ( prefab is null ) return false;
			if ( scenePrefab == prefab ) return false;
		}

		return true;
	}

	public static Resource Load( SerializedProperty property, Asset asset )
	{
		if ( property.PropertyType.IsInterface )
			return asset.LoadResource<GameResource>();

		return LoadThroughProvider( property, asset ) ?? asset.LoadResource( property.PropertyType );
	}

	public static Resource LoadThroughProvider( SerializedProperty property, Asset asset )
	{
		if ( asset.AssetType?.ProvidedResourceType?.IsAssignableTo( property.PropertyType ) != true )
			return null;

		if ( property.PropertyType == typeof( Model ) )
			return Model.Load( asset.Path );

		return null;
	}

	public static void Assign( SerializedProperty property, Asset asset )
	{
		if ( asset is null || !CanAssign( property, AssetType.FromType( property.PropertyType ), asset ) )
			return;

		property.SetValue( Load( property, asset ) );
		property.Parent?.NoteChanged( property );
	}

	public static void AssignMany( SerializedProperty property, Asset[] assets )
	{
		if ( assets.Length == 0 ) return;

		var assetType = AssetType.FromType( property.PropertyType );

		if ( CanAssign( property, assetType, assets[0] ) )
		{
			property.SetValue( Load( property, assets[0] ) );
			property.Parent?.NoteChanged( property );
		}

		if ( property.Parent is not SerializedCollection collection ) return;

		for ( int i = 1; i < assets.Length; i++ )
		{
			if ( !CanAssign( property, assetType, assets[i] ) ) continue;
			collection.Add( Load( property, assets[i] ) );
		}
	}

	public static Asset AssetFrom( DragData data )
	{
		var carried = data.OfType<Asset>().FirstOrDefault();
		if ( carried is not null && !carried.IsDeleted ) return carried;

		if ( !string.IsNullOrWhiteSpace( data.Text )
			&& !data.Text.Contains( '\n' )
			&& AssetSystem.FindByPath( data.Text ) is { IsDeleted: false } named )
			return named;

		if ( !data.HasFileOrFolder ) return null;

		return AssetSystem.FindByPath( data.FileOrFolder );
	}
}
