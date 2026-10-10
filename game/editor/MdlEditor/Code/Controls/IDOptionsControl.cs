namespace Editor.MdlEditor;

using Editor.Inspectors;

//The options belong to the parameter this property is on, so they are read off the object being edited rather than off the property's own type.
/// <summary>The value of an ID parameter that is backed by a fixed set of options.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( AnimGraphOptionsAttribute )] )]
public sealed class IDOptionsControl : StringDropdownControl
{
	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option>();

		foreach ( var name in Parameter()?.Options ?? [] )
		{
			if ( string.IsNullOrWhiteSpace( name ) ) continue;

			options.Add( new Rinten.UI.Option( name, name ) );
		}

		return options;
	}

	Parameter Parameter() => Property?.Parent?.Targets?.OfType<Parameter>().FirstOrDefault();
}
