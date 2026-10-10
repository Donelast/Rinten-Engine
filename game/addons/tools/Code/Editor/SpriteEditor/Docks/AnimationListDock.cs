namespace Editor.SpriteEditor;

/// <summary>The sprite's animations, one row each, with the selected one lit. The rows are <see cref="AnimationListPanel"/>.</summary>
public sealed class AnimationListDock : SpriteDock
{
	private readonly ISpriteDocument _document;
	private readonly IAnimationSelection _selection;
	private readonly AnimationCommands _commands;

	public AnimationListDock( ISpriteDocument document, IAnimationSelection selection, AnimationCommands commands )
		: base( "Animations", "directions_walk" )
	{
		_document = document;
		_selection = selection;
		_commands = commands;

		Rebuild();
	}

	protected override Rinten.UI.Panel CreatePanel()
		=> new AnimationListPanel { Document = _document, Selection = _selection, Commands = _commands };
}

//What a dragged animation row carries.
internal sealed record AnimationDragData( Sprite.Animation Animation );
