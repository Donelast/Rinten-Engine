namespace Editor.SpriteEditor;

//The source is there so a panel that edits the sprite through its own sheet can tell its own change from everybody else's, and not tear the sheet down under the slider that is being dragged.
/// <summary>One committed edit to the sprite: what it was called, and who made it.</summary>
public sealed record SpriteChange( string Name, object Source );
