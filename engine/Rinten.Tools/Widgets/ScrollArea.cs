using System;

namespace Editor
{
	/// <summary>A widget that can scroll its <see cref="Canvas"/>.</summary>
	public partial class ScrollArea : Frame
	{

		public ScrollBar VerticalScrollbar { get; init; }
		public ScrollBar HorizontalScrollbar { get; init; }

		public ScrollArea( Widget parent ) : base( parent )
		{
			SetSizeMode( SizeMode.Flexible, SizeMode.Flexible );

			//Whatever hangs outside the area is not drawn - which is the whole difference between an area that scrolls and one that simply has a big widget in it.
			Surface.Style.Overflow = Rinten.UI.OverflowMode.Hidden;
		}

		Widget _canvas;

		/// <summary>The content widget to scroll.</summary>
		public Widget Canvas
		{
			get => _canvas;
			set
			{
				if ( value is not null )
				{
					Assert.False( value == this, "Tried to set scrollarea canvas to itself!" );
					Assert.False( value.IsAncestorOf( this ), "Tried to set scrollarea canvas to a parent of itself!" );
				}

				//The one being replaced goes out. It was only ever parented in, never out, so a panel that swapped its canvas kept both - the old sheet
				//stayed where it was and the new one was drawn over the top of it, which is a properties panel showing two sets of properties at once.
				if ( _canvas.IsValid() && _canvas != value )
					_canvas.SetPanelParent( null );

				_canvas = value;

				_canvas?.SetPanelParent( this );

				scroll = 0;
				PlaceCanvas();
				Update();
			}
		}

		public ScrollbarMode HorizontalScrollbarMode
		{
			get => horizontalMode;
			set
			{
				horizontalMode = value;
			}
		}

		public ScrollbarMode VerticalScrollbarMode
		{
			get => verticalMode;
			set
			{
				verticalMode = value;
			}
		}

		private ScrollbarMode horizontalMode;
		private ScrollbarMode verticalMode;

		public void MakeVisible( Widget widget )
		{
			if ( widget is null ) return;

			MakeVisible( new Vector3( widget.Position.x, widget.Position.y + widget.Size.y, 0 ) );
		}

		public void MakeVisible( Vector3 pos )
		{
			//Enough of a nudge to bring the point inside, and no more: a list that jumps to the middle every time is unusable.
			var wanted = ScrollOffset;

			if ( pos.y - ScrollOffset.y > Size.y ) wanted.y = pos.y - Size.y;
			if ( pos.y - ScrollOffset.y < 0 ) wanted.y = pos.y;

			ScrollOffset = wanted;
		}
	}
}
