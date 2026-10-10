namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>Picks an animation off the sprite the renderer beside this property holds.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( SpriteAnimationNameAttribute )] )]
public sealed class SpriteAnimationNameControl : StringDropdownControl
{
	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option>();
		var sprite = Sprite();

		//Nothing to pick from is worth saying, rather than an empty list that reads as a broken control.
		if ( sprite is null || sprite.Animations.Count == 0 )
		{
			options.Add( new Rinten.UI.Option( "None", "" ) );
			return options;
		}

		foreach ( var animation in sprite.Animations )
		{
			if ( string.IsNullOrWhiteSpace( animation.Name ) ) continue;

			options.Add( new Rinten.UI.Option( animation.Name, animation.Name ) );
		}

		return options;
	}

	//The sprite this animation name belongs to, from the renderer's own property.
	Sprite Sprite()
	{
		var sprite = Property?.Parent?.GetProperty( nameof( SpriteRenderer.Sprite ) );

		return sprite?.GetValue<Sprite>( null );
	}
}
