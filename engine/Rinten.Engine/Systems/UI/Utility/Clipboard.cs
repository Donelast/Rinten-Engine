
namespace Rinten.UI;

public static class Clipboard
{
	/// <summary>Sets the clipboard text</summary>
	public static void SetText( string text )
	{
		EnginePlatform.SetClipboardText( text );
	}
}
