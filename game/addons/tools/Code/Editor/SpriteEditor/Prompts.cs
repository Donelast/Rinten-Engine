namespace Editor.SpriteEditor;

//The three questions the sprite editor asks: a name, a yes or no, and "that name will not do". The name is
//asked for with the widget the asking panel is drawn in, which is how the card finds the window to open over.
internal static class Prompts
{
	/// <summary>Asks for a name, and hands it over once it is typed.</summary>
	public static void AskName( Widget owner, string question, string initial, string confirm, Action<string> onSubmit )
	{
		EditorModal.Prompt( EditorOverlay.For( owner ), question, null,
			text => onSubmit?.Invoke( text?.Trim() ?? string.Empty ),
			okay: confirm, initial: initial ?? string.Empty, icon: "directions_walk" );
	}

	/// <summary>Asks whether to go ahead, and does so only on yes - which takes something away, so it is the red button.</summary>
	public static void Confirm( string title, string question, string yes, Action onYes )
	{
		EditorModal.Confirm( null, title, question, onYes, confirm: yes, cancel: "Cancel", destructive: true );
	}

	/// <summary>Says why a name was refused.</summary>
	public static void ShowNamingError( string problem )
	{
		EditorModal.Notice( null, "Invalid name", problem );
	}
}
