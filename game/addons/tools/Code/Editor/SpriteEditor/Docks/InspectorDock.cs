namespace Editor.SpriteEditor;

/// <summary>The selected animation's properties. The sheet is <see cref="AnimationInspectorPanel"/>.</summary>
public sealed class InspectorDock : SpriteDock
{
	private readonly ISpriteDocument _document;
	private readonly IAnimationSelection _selection;

	public InspectorDock( ISpriteDocument document, IAnimationSelection selection )
		: base( "Inspector", "edit" )
	{
		_document = document;
		_selection = selection;

		MinimumWidth = 350f;

		Rebuild();
	}

	protected override Rinten.UI.Panel CreatePanel()
		=> new AnimationInspectorPanel { Document = _document, Selection = _selection };
}
