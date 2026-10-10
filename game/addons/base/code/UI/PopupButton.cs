
namespace Rinten.UI
{
	/// <summary>A button that opens a <see cref="Popup"/> panel. Useless on its own - you need to implement Open</summary>
	public abstract class PopupButton : Button
	{
		//The opened UI.Popup.
		protected Popup Popup;

		public PopupButton()
		{
			AddClass( "popupbutton" );
		}

		protected override void OnClick( MousePanelEvent e )
		{
			if ( Popup is { IsValid: true } )
			{
				Popup.Delete();
				return;
			}

			base.OnClick( e );
			Open();
		}

		/// <summary>Open a popup. You should set <see cref="Popup"/> here.</summary>
		public abstract void Open();

		//Whether the popup is stretched to the button width, or sized by its own sheet.
		protected virtual bool StretchPopupToWidth => true;

		public override void Tick()
		{
			base.Tick();

			SetClass( "open", Popup.IsValid() && !Popup.IsDeleting );
			SetClass( "active", Popup.IsValid() && !Popup.IsDeleting );

			if ( StretchPopupToWidth && Popup.IsValid() )
			{
				var onOverlay = Popup.Parent is not null && Popup.Parent.HasClass( "editor-overlay" );
				Popup.Style.Width = onOverlay ? Box.Rect.Width : Box.Rect.Width * ScaleFromScreen;
			}
		}
	}
}
