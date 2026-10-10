namespace Rinten;

/// <summary>When applied to a component, the editor will draw a selectable handle sprite for the gameobject in scene</summary>
public class EditorHandleAttribute : System.Attribute
{
	public string Texture { get; set; }
	public string Icon { get; set; }
	public Color Color { get; set; } = Color.White;

	/// <summary>Whether clicking the handle selects the object. A handle that is only ever a marker turns this off: it still draws, it just stops standing in front of whatever is behind it.</summary>
	public bool Clickable { get; set; } = true;


	public EditorHandleAttribute( string texture )
	{
		Texture = texture;
	}

	public EditorHandleAttribute()
	{

	}
}
