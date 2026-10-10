namespace Editor;

public static class WidgetExtensions
{
	/// <summary>Place a widget in a certain position relative to the screen position of another</summary>
	public static void Place( this Widget self, Widget parent, WidgetAnchor placement )
	{
		placement.Apply( self, parent );
	}

	/// <summary>Place a widget in a certain position relative to the screen position of another</summary>
	public static void OpenNextTo( this Menu self, Widget parent, WidgetAnchor placement )
	{
		self.OpenAt( 0, false );
		placement.Apply( self, parent );
	}

	/// <summary>Show a popup dialog warning about unsaved changes. The user can either: <list type="bullet"> <item> <term>Save</term> <description>Call <paramref name="onSave"/>, then destroy the <paramref name="widget"/>.</description> </item> <item> <term>Don't Save</term> <description>Destroy the <paramref name="widget"/> without saving.</description> </item> <item> <term>Cancel</term> <description>Don't do anything, leaving the <paramref name="widget"/> open.</description> </item> </list></summary>
	public static void ShowUnsavedChangesDialog( this Widget widget, string assetName, string assetType, Action onSave )
	{
		EditorModal.Choose( EditorOverlay.For( widget ), $"Closing {assetName}",
			$"Do you want to save the changes you made to this {assetType}?", "Cancel",
			new[]
			{
				new EditorModal.Choice( "Don't Save", () => widget.Destroy(), Destructive: true ),
				new EditorModal.Choice( "Save", () =>
				{
					onSave();
					widget.Destroy();
				} ),
			} );
	}
}
