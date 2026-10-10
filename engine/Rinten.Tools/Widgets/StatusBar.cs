using System;

namespace Editor
{
	public partial class StatusBar : Widget
	{

		public StatusBar( Widget parent ) : base()
		{
			MakeSurface( parent );

			FixedHeight = Theme.RowHeight + 6;
		}

		public void AddWidgetLeft( Widget w, int stretch = 0 )
		{
			AddManaged( w, left, stretch );
		}

		public void RemoveWidget( Widget w )
		{
			RemoveManaged( w );
		}

		public void AddWidgetRight( Widget w, int stretch = 0 )
		{
			AddManaged( w, right, stretch );
		}

		public void ShowMessage( string text, float seconds = 5.0f )
		{
			ShowMessageManaged( text, seconds );
		}

		private bool sizeGrip;

		public bool SizeGrip
		{
			//Ours is resized by the window's own edges, so there is no corner to put a grip in.
			get { return sizeGrip; }
			set
			{
				sizeGrip = value;
			}
		}

	}
}
