namespace Editor;

public partial class EditorUtility
{
	public static partial class Clipboard
	{

		public static void Copy( string text ) => Rinten.EnginePlatform.SetClipboardText( text );

		public static string Paste() => Rinten.EnginePlatform.GetClipboardText();
	}
}
