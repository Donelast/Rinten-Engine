using Rinten;
using System;

namespace Editor
{
	public class BaseScrollWidget : Frame
	{
		/// <summary>The vertical scroll bar.</summary>
		public ScrollBar VerticalScrollbar { get; init; }

		/// <summary>The horizontal scroll bar.</summary>
		public ScrollBar HorizontalScrollbar { get; init; }


		public bool SmoothScrolling { get; set; } = true;

		public BaseScrollWidget( Widget parent = null ) : base( parent )
		{
			SetSizeMode( SizeMode.Flexible, SizeMode.Flexible );

			VerticalScrollbar = new ScrollBar();
			HorizontalScrollbar = new ScrollBar();

			MouseTracking = true;
		}

		/// <summary><see cref="HorizontalScrollbar"/> mode.</summary>
		public ScrollbarMode HorizontalScrollbarMode
		{
			get => horizontalMode;
			set
			{
				horizontalMode = value;
			}
		}

		private ScrollbarMode horizontalMode;
		private ScrollbarMode verticalMode;

		/// <summary><see cref="VerticalScrollbar"/> mode.</summary>
		public ScrollbarMode VerticalScrollbarMode
		{
			get => verticalMode;
			set
			{
				verticalMode = value;
			}
		}

		public override void Update()
		{
			if ( !IsValid ) return;

			base.Update();
		}

		//The scrolling settles a little every frame, and nothing else drives it in a window that is only panels.
		private protected override void OnWidgetFrame()
		{
			base.OnWidgetFrame();

			ScrollingFrame();
		}

		//The thumb down the right-hand side, where there is more than fits.
		protected override void OnPaint()
		{
			base.OnPaint();

			var range = VerticalScrollbar.Maximum - VerticalScrollbar.Minimum;
			if ( range <= 0 ) return;

			const float width = 6.0f;

			var seen = Size.y;
			var whole = seen + range;

			var height = MathF.Max( seen * (seen / whole), 20.0f );
			var at = ((VerticalScrollbar.Value - VerticalScrollbar.Minimum) / (float)range) * (seen - height);

			Paint.ClearPen();
			Paint.SetBrush( Theme.Text.WithAlpha( Paint.HasMouseOver ? 0.3f : 0.15f ) );
			Paint.DrawRect( new Rect( Size.x - width - 1, at, width, height ), width * 0.5f );
		}

		internal void InternalOnScrollChanged() => OnScrollChanged();

		//Called when the scroll position has changed.
		protected virtual void OnScrollChanged()
		{

		}

		//smooth scrolling

		/// <summary>The smooth scrolling wants to move by this amount</summary>
		public float SmoothScrollTarget { get; set; }

		//We save off the scroll value to a float during smooth scroll so it can keep hold of fractions, that way we don't get chunky rounding errors.
		float SmoothValue;

		protected override void OnMouseWheel( WheelEvent e )
		{
			if ( !SmoothScrolling )
			{
				base.OnMouseWheel( e );
				return;
			}

			e.Accept();

			if ( e.Delta < 0 ) SmoothScrollTarget += VerticalScrollbar.SingleStep;
			else SmoothScrollTarget -= VerticalScrollbar.SingleStep;
		}

		protected override void OnKeyPress( KeyEvent e )
		{
			if ( e.Key == KeyCode.PageDown && SmoothScrolling )
			{
				SmoothScrollTarget += VerticalScrollbar.PageStep;
				e.Accepted = true;
				return;
			}

			if ( e.Key == KeyCode.PageUp && SmoothScrolling )
			{
				SmoothScrollTarget -= VerticalScrollbar.PageStep;
				e.Accepted = true;
				return;
			}

			if ( e.Key == KeyCode.Home && SmoothScrolling )
			{
				SmoothScrollTarget += (VerticalScrollbar.Minimum - VerticalScrollbar.Value);
				e.Accepted = true;
				return;
			}

			if ( e.Key == KeyCode.End && SmoothScrolling )
			{
				SmoothScrollTarget += (VerticalScrollbar.Maximum - VerticalScrollbar.Value);
				e.Accepted = true;
				return;
			}


			//todo arrows

			base.OnKeyPress( e );
		}

		float smoothScrollVelocity;
		float lastScrollbarValue = 0.0f;

		[EditorEvent.Frame]
		public virtual void ScrollingFrame()
		{
			if ( !IsValid )
				return;

			if ( VerticalScrollbar is not { IsValid: true } )
				return;

			const float scrollTime = 0.2f;

			//Scrollbar value changed, obey the scrollbar
			if ( lastScrollbarValue != VerticalScrollbar.Value )
			{
				lastScrollbarValue = VerticalScrollbar.Value;
				SmoothScrollTarget = 0;
				SmoothValue = lastScrollbarValue;
				OnScrollChanged();
				return;
			}

			if ( SmoothScrollTarget.AlmostEqual( 0.0f, 0.01f ) )
			{
				//Sync this in case the user drags the scrollbar manually
				SmoothValue = VerticalScrollbar.Value;
				return;
			}

			//Don't limit the framerate for the next 10 frames
			var dest = MathX.SmoothDamp( SmoothScrollTarget, 0, ref smoothScrollVelocity, scrollTime, RealTime.Delta );

			var difference = SmoothScrollTarget - dest;
			SmoothScrollTarget = dest;

			SmoothValue += difference;

			//At either end there is nowhere left to go, and whatever is left of the target is spent against a wall. Kept, it has to
			//be unwound before the wheel does anything the other way - which reads as a list that ignores the first few notches back.
			if ( SmoothValue < VerticalScrollbar.Minimum )
			{
				SmoothValue = VerticalScrollbar.Minimum;
				SmoothScrollTarget = 0;
				smoothScrollVelocity = 0;
			}

			if ( SmoothValue > VerticalScrollbar.Maximum )
			{
				SmoothValue = VerticalScrollbar.Maximum;
				SmoothScrollTarget = 0;
				smoothScrollVelocity = 0;
			}

			var was = VerticalScrollbar.Value;

			VerticalScrollbar.Value = (int)SmoothValue;
			lastScrollbarValue = VerticalScrollbar.Value;

			if ( VerticalScrollbar.Value == was ) return;

			OnScrollChanged();

			Update();
		}
	}
}
