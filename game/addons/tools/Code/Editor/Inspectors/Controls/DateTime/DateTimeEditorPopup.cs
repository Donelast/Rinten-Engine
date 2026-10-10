namespace Editor.Inspectors;

[StyleSheet( "DateTimeEditorPopup.cs.scss" )]
public sealed class DateTimeEditorPopup : Popup
{
	DateTimeEditorPanel editor;

	public static DateTimeEditorPopup Open( Panel anchor, DateTime value, Action<DateTime> onChange, bool includeTime = true )
	{
		if ( anchor is null )
			return null;

		var popup = new DateTimeEditorPopup();
		popup.SetPositioning( anchor, PositionMode.BelowLeft, 2 );
		popup.CloseWhenParentIsHidden = true;
		popup.AddClass( "editor-popup" );
		popup.AddClass( "datetime-editor-popup" );

		popup.editor = popup.AddChild( new DateTimeEditorPanel( includeTime ) );
		popup.editor.Value = value;
		popup.editor.ValueChanged = onChange;

		popup.SyncAnchor();
		return popup;
	}
}
