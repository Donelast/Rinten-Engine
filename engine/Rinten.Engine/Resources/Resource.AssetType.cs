namespace Rinten;

public partial class Resource
{
	/// <summary>Get the icon for this type of asset. This is an icon that is shown in the editor.</summary>
	public Bitmap GetAssetTypeIcon( int width, int height )
	{
		//cache opportunity at some point
		return CreateAssetTypeIcon( width, height );
	}

	//Create an icon for this type of asset. This is an icon that is shown in the editor.
	protected virtual Bitmap CreateAssetTypeIcon( int width, int height )
	{
		//Asked of the base attribute: a type of ours wears [AssetType], and asking only for the obsolete [GameResource]
		//answered null for every one of them - so they all drew the same green question mark and their IconColor did nothing.
		var at = Game.TypeLibrary.GetAttribute<AssetTypeAttribute>( GetType() );

#pragma warning disable CS0618
		var gr = at as GameResourceAttribute;

		var icon = at?.Icon;
		Color fg = gr?.IconFgColor ?? "#1a2c17";
		Color bg = gr?.IconBgColor ?? at?.IconColor ?? "#67ac5c";

		if ( string.IsNullOrEmpty( icon ) ) icon = "question_mark";

		//we're not supporting loading from paths anymore. I don't think we ever did?
		if ( icon.Contains( "/" ) || icon.Contains( "\\" ) ) icon = "question_mark";

		return CreateSimpleAssetTypeIcon( icon, width, height, bg, fg );
#pragma warning restore CS0618
	}

	/// <summary>Create a simple icon using an icon. This is used by default for asset types.</summary>
	public static Bitmap CreateSimpleAssetTypeIcon( string icon, int width, int height, Color? background = default, Color? foreground = default )
	{
		Color fg = foreground ?? "#1a2c17";
		Color bg = background ?? "#67ac5c";

		var bitmap = new Bitmap( width, height );
		bitmap.Clear( new Color( 0, 0, 0, 0 ) );

		bitmap.SetRadialGradient( width * 0.1f, height * 2, Gradient.FromColors( bg, bg.Darken( 0.25f ) ) );
		bitmap.DrawRoundRect( bitmap.Rect, 4 );

		bitmap.DrawText( new TextRendering.Scope( icon, fg, height * 0.8f, "Material Icons" ), bitmap.Rect, TextFlag.Center | TextFlag.DontClip );
		return bitmap;
	}
}
