using Rinten.UI;

namespace Editor.SpriteEditor;

//Shows a sprite editor card on a ModalSheet; cards handle Escape themselves and call Close
internal static class SpriteEditorModal
{
	public static void Open( Panel card )
	{
		if ( ModalSheet.Open( Rinten.Internal.GlobalToolsNamespace.EditorWindow, card ) is null )
		{
			Log.Warning( "Sprite editor: no window to open the card over." );
			return;
		}

		card.Focus();
	}

	/// <summary>Takes the card away, with the sheet it is on.</summary>
	public static void Close( Panel card )
	{
		if ( card.Ancestors.OfType<ModalSheet>().FirstOrDefault() is { } sheet ) sheet.Close();
		else card.Delete();
	}
}
