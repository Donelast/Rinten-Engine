namespace Editor.Inspectors;

//Builds ResourceDisplayState from a bound property.
static class ResourceDisplay
{
	public static ResourceDisplayState Build( SerializedProperty property, AssetType assetType )
	{
		if ( property is null )
			return new( ResourceFieldMode.Placeholder, "", "", "Resource", assetType, null, null );

		if ( property.IsMultipleDifferentValues )
			return new( ResourceFieldMode.Multiple, "Multiple Values", "", "", assetType, null, null );

		var resource = property.GetValue<Resource>( null );
		var asset = resource is not null ? AssetSystem.FindByPath( resource.ResourcePath ) : null;
		var pickerName = assetType?.FriendlyName ?? DisplayInfo.ForType( property.PropertyType ).Name;

		if ( asset is not null && !asset.IsDeleted )
		{
			return new(
				ResourceFieldMode.Set,
				asset.Name,
				asset.RelativePath,
				"",
				assetType,
				asset,
				resource );
		}

		if ( resource is not null )
		{
			var missing = resource.IsError || !resource.IsValid();
			return new(
				missing ? ResourceFieldMode.Missing : ResourceFieldMode.Set,
				missing ? $"Missing {pickerName}" : resource.ResourceName,
				resource.ResourcePath,
				"",
				assetType,
				asset,
				resource );
		}

		return new( ResourceFieldMode.Placeholder, "", "", pickerName, assetType, null, null );
	}
}
