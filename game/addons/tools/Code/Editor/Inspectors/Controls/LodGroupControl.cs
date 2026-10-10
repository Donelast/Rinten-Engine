namespace Editor.Inspectors;

/// <summary>Picks one of the project's detail groups by name - see <see cref="LodGroup"/>.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( LodGroupNameAttribute )] )]
public sealed class LodGroupControl : StringDropdownControl
{
	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option> { new( "None", "" ) };

		foreach ( var name in LodGroup.Names )
		{
			options.Add( new Rinten.UI.Option( name, name ) );
		}

		return options;
	}
}
