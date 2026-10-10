namespace Editor.TreeBrowser;

static class AssetFileIconColors
{
	static readonly Dictionary<string, Color> Colors = new( StringComparer.OrdinalIgnoreCase )
	{
		{ ".cs", new Color( 0.4f, 0.7f, 1.0f ) },
		{ ".razor", new Color( 0.6f, 0.4f, 0.9f ) },
		{ ".shader", new Color( 0.3f, 0.9f, 0.5f ) },
		{ ".prefab", new Color( 0.3f, 0.8f, 0.9f ) },
		{ ".scene", new Color( 0.9f, 0.9f, 0.3f ) }
	};

	public static Color GetColor( AssetTreeItem item )
	{
		if ( item.Asset?.AssetType?.Color is { } typeColor && typeColor != default )
			return typeColor;

		if ( Colors.TryGetValue( item.Extension, out var color ) )
			return color;

		return Theme.Text.WithAlpha( 0.7f );
	}
}
