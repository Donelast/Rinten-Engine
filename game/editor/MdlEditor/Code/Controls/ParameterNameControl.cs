namespace Editor.MdlEditor;

using Editor.Inspectors;
using Editor.MdlEditor.Nodes;

/// <summary>The parameter a node reads, picked out of the ones the graph declares.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( AnimGraphParameterNameAttribute )] )]
public sealed class ParameterNameControl : StringDropdownControl
{
	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option>();
		var target = Target();
		var current = Read() ?? "";
		var found = string.IsNullOrWhiteSpace( current );

		//The selectors are the ones that can be pointed at nothing - a parameter node without a parameter has no type to offer the graph.
		if ( target is IDBasedSelectorNode )
			options.Add( new Rinten.UI.Option( "Select parameter...", "arrow_drop_down", "" ) );

		foreach ( var parameter in Parameters( target ) )
		{
			var name = parameter.Name;

			if ( string.IsNullOrWhiteSpace( name ) ) continue;

			found |= string.Equals( current, name, StringComparison.OrdinalIgnoreCase );
			options.Add( new Rinten.UI.Option( name, AnimParameterUI.Icon( parameter.Type ), name ) );
		}

		//A parameter that has been deleted or renamed still shows, so the row says what it is set to rather than looking empty.
		if ( !found && !string.IsNullOrEmpty( current ) )
			options.Add( new Rinten.UI.Option( $"{current} (missing)", "error", current ) );

		return options;
	}

	protected override void Assign( string value )
	{
		var target = Target();
		var parameter = Parameters( target )
			.FirstOrDefault( candidate => string.Equals( candidate.Name, value, StringComparison.OrdinalIgnoreCase ) );

		switch ( target )
		{
			case ParameterNode node when parameter is not null:
				node.ParameterIdentifier = parameter.Identifier;
				Property.SetValue( parameter.Name );
				break;

			case IDBasedSelectorNode selector when parameter is Parameter control:
				selector.SelectParameter( control );
				Notify();
				break;

			case IDBasedSelectorNode selector:
				selector.ClearParameter();
				Notify();
				break;

			default:
				base.Assign( value );
				break;
		}
	}

	void Notify()
	{
		Property.Parent?.NoteChanged( Property );
		Property.Parent?.NoteFinishEdit( Property );
	}

	object Target() => Property?.Parent?.Targets?.FirstOrDefault();

	static IEnumerable<IParameter> Parameters( object target ) => target switch
	{
		ParameterNode node => node.Root?.AllParameters.Where( parameter => parameter.Type == node.ParameterType ) ?? [],
		IDBasedSelectorNode selector => selector.GetAvailableParameters(),
		_ => []
	};
}
