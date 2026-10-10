namespace Editor.Inspectors;

using Rinten.Audio;

//The tree is flattened depth first, so the list reads down the branches the way the mixer panel draws them rather than in whatever order the children happen to sit in.
/// <summary>Picks the mixer a sound plays through, out of the mixer tree that is live right now.</summary>
[CustomEditor( typeof( MixerHandle ) )]
public sealed class MixerHandleControl : StringDropdownControl
{
	//What an unset handle reads as: the sound falls back to the default mixer.
	const string Unset = "Default";

	//What a handle nobody has set writes itself as, from MixerHandle.JsonWrite.
	const string Unwritten = "unknown";

	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option> { new( Unset, "" ) };

		foreach ( var mixer in Mixers() )
		{
			options.Add( new Rinten.UI.Option( mixer.Name, mixer.Name ) );
		}

		//A mixer that has been deleted, or one outside the branch this property is pinned to, still has to read as what it is rather than as the default.
		var current = Read();

		if ( !string.IsNullOrEmpty( current ) && !options.Any( x => Matches( x, current ) ) )
		{
			options.Insert( 1, new Rinten.UI.Option( current, current ) );
		}

		return options;
	}

	//Mixer name, treating the "unknown" placeholder an untouched handle saves as no mixer
	protected override string Read()
	{
		var name = base.Read();

		return string.Equals( name, Unwritten, StringComparison.OrdinalIgnoreCase ) ? "" : name;
	}

	//The handle is a name and a guid in a struct, so the pick has to be wrapped to go back.
	protected override void Assign( string value )
	{
		if ( Property is null ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue( Handle( value ) );
		Property.Parent?.NoteFinishEdit( Property );
	}

	//The handle a picked name stands for. Built from the mixer itself where there is one, because
	//the handle carries the guid as well as the name and a mixer found by guid survives a rename.
	MixerHandle Handle( string value )
	{
		if ( string.IsNullOrEmpty( value ) )
			return default;

		var mixer = Mixers().FirstOrDefault( x =>
			string.Equals( x.Name, value, StringComparison.OrdinalIgnoreCase ) );

		return mixer is not null ? (MixerHandle)mixer : (MixerHandle)value;
	}

	//Every mixer that can be picked here, depth first from the root.
	IEnumerable<Mixer> Mixers()
	{
		var root = Root();

		if ( root is null ) return [];

		return Flatten( root );
	}

	//Where the list starts. A property marked Voice.ParentMixerAttribute only works against one branch - voice has to stay under the voice mixer -
	//so the list starts there instead of at the master. A branch that is not there is not worth an empty list, so the whole tree stands in for it.
	Mixer Root()
	{
		if ( Property is not null && Property.TryGetAttribute<Voice.ParentMixerAttribute>( out var parent ) )
			return Mixer.FindMixerByName( parent.MixerName ) ?? Mixer.Master;

		return Mixer.Master;
	}

	static IEnumerable<Mixer> Flatten( Mixer mixer )
	{
		yield return mixer;

		foreach ( var child in mixer.GetChildren() )
		{
			foreach ( var descendant in Flatten( child ) )
			{
				yield return descendant;
			}
		}
	}

	static bool Matches( Rinten.UI.Option option, string name )
	{
		return string.Equals( option.Value as string, name, StringComparison.OrdinalIgnoreCase );
	}
}
