namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>Picks a font family by name.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( FontNameAttribute )] )]
public sealed class FontControl : StringDropdownControl
{
	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option> { new( "Default", "" ) };

		foreach ( var family in EditorUtility.FontFamilies.Order() )
			options.Add( new Rinten.UI.Option( family, family ) );

		return options;
	}
}
