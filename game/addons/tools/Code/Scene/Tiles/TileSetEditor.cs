using Rinten.Tiles;

namespace Editor.Tiles;

//A tile set, opened: the sheet and how it is cut, what each tile is, and the terrains - see TileSetEditorPanel.razor, which is all of it.
class TileSetEditor : BaseResourceEditor<TileSet>
{
	protected override void Initialize( Asset asset, TileSet resource )
	{
		Layout = Layout.Column();
		//As tall as what it shows: in the inspector it is stacked in a column and only the panel knows how tall that is.
		Layout.Add( new PanelWidget( this, new TileSetEditorPanel( resource, () => NoteChanged() ) ) { SizeToContent = true } );
	}
}
