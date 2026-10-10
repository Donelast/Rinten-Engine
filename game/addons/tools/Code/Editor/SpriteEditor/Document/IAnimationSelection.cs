namespace Editor.SpriteEditor;

/// <summary>Which of the sprite's animations the editor is looking at.</summary>
public interface IAnimationSelection
{
	/// <summary>The animation the docks show, or null where the sprite has none.</summary>
	Sprite.Animation Selected { get; set; }

	/// <summary>A different animation is selected - or the same one by name, after undo replaced the object behind it.</summary>
	event Action Changed;
}
