namespace Editor.Inspectors;

using Rinten.Audio;

/// <summary>Picks one of the DSP presets the audio system has registered.</summary>
[CustomEditor( typeof( DspPresetHandle ) )]
public sealed class DspPresetHandleControl : StringDropdownControl
{
	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option>();

		foreach ( var name in Sound.DspNames ?? [] )
		{
			options.Add( new Rinten.UI.Option( name, name ) );
		}

		//A preset that has gone away, or one from a package that isn't loaded yet, still has to read as what it is rather than as nothing.
		var current = Property?.As.String;

		if ( !string.IsNullOrEmpty( current ) && !options.Any( x => Matches( x, current ) ) )
		{
			options.Insert( 0, new Rinten.UI.Option( current, current ) );
		}

		return options;
	}

	//The handle is a name in a struct, so the pick has to be wrapped to go back.
	protected override void Assign( string value )
	{
		if ( Property is null ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue( (DspPresetHandle)(value ?? "") );
		Property.Parent?.NoteFinishEdit( Property );
	}

	static bool Matches( Rinten.UI.Option option, string name )
	{
		return string.Equals( option.Value as string, name, StringComparison.OrdinalIgnoreCase );
	}
}
