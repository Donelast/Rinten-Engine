namespace Editor.SpriteEditor;

//The animation list dock used to do all of this itself, row by row. It is here so a menu, a key and a button can all do the same thing, and so what makes a name acceptable is said once.
/// <summary>Everything that can be done to the sprite's list of animations, each as one undo step.</summary>
public sealed class AnimationCommands
{
	private readonly ISpriteDocument _document;
	private readonly IAnimationSelection _selection;

	public AnimationCommands( ISpriteDocument document, IAnimationSelection selection )
	{
		_document = document;
		_selection = selection;
	}

	private List<Sprite.Animation> Animations => _document.Sprite.Animations;

	/// <summary>Whether an animation may be called this. Says why not where it may not.</summary>
	public bool TryValidateName( string name, Sprite.Animation except, out string problem )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
		{
			problem = "You cannot give an animation an empty name";
			return false;
		}

		var taken = Animations.Any( a => a != except && string.Equals( a.Name, name, StringComparison.OrdinalIgnoreCase ) );
		if ( taken )
		{
			problem = $"You cannot give two animations the same name ('{name}')";
			return false;
		}

		problem = null;
		return true;
	}

	/// <summary>Adds an empty animation at the end and selects it.</summary>
	public Sprite.Animation Create( string name )
	{
		var animation = new Sprite.Animation { Name = name };

		_document.Edit( $"Create Animation \"{name}\"", () => Animations.Add( animation ) );
		_selection.Selected = animation;

		return animation;
	}

	public void Rename( Sprite.Animation animation, string name )
	{
		if ( animation.Name == name ) return;

		_document.Edit( "Rename Animation", () => animation.Name = name );
	}

	/// <summary>A copy of an animation under a new name, right after it, and selected.</summary>
	public Sprite.Animation Duplicate( Sprite.Animation animation, string name )
	{
		var copy = Json.Deserialize<Sprite.Animation>( Json.Serialize( animation ) );
		copy.Name = name;

		_document.Edit( $"Duplicate Animation {animation.Name}", () =>
		{
			var index = Animations.IndexOf( animation );
			Animations.Insert( index < 0 ? Animations.Count : index + 1, copy );
		} );

		_selection.Selected = copy;

		return copy;
	}

	public void Delete( Sprite.Animation animation )
	{
		_document.Edit( $"Delete Animation {animation.Name}", () => Animations.Remove( animation ) );
	}

	/// <summary>Puts an animation where another one is, pushing that one along.</summary>
	public void MoveBefore( Sprite.Animation moving, Sprite.Animation target )
	{
		if ( moving == target ) return;

		var from = Animations.IndexOf( moving );
		var to = Animations.IndexOf( target );
		if ( from < 0 || to < 0 ) return;

		_document.Edit( "Reorder Animations", () =>
		{
			Animations.RemoveAt( from );
			Animations.Insert( to, moving );
		} );
	}
}
