using Android.Views;
using Rinten;

namespace Rinten.Android;

//Queues every touch step for the engine, including batched historical moves
internal static class TouchInput
{
	//Where the view is in the window, which is what an activity's events are measured from.
	private static readonly int[] origin = new int[2];

	public static void Forward( MotionEvent e, View view )
	{
		view.GetLocationInWindow( origin );

		float ox = origin[0];
		float oy = origin[1];

		switch ( e.ActionMasked )
		{
			case MotionEventActions.Down:
			case MotionEventActions.PointerDown:
			{
				var i = e.ActionIndex;
				CoreInput.HostTouch( CoreInput.HostKind.TouchDown, e.GetPointerId( i ), e.GetX( i ) - ox, e.GetY( i ) - oy );
				break;
			}

			case MotionEventActions.Move:
			{
				for ( var h = 0; h < e.HistorySize; h++ )
				{
					for ( var p = 0; p < e.PointerCount; p++ )
					{
						CoreInput.HostTouch( CoreInput.HostKind.TouchMove, e.GetPointerId( p ),
							e.GetHistoricalX( p, h ) - ox, e.GetHistoricalY( p, h ) - oy );
					}
				}

				for ( var p = 0; p < e.PointerCount; p++ )
				{
					CoreInput.HostTouch( CoreInput.HostKind.TouchMove, e.GetPointerId( p ), e.GetX( p ) - ox, e.GetY( p ) - oy );
				}

				break;
			}

			case MotionEventActions.Up:
			case MotionEventActions.PointerUp:
			{
				var i = e.ActionIndex;
				CoreInput.HostTouch( CoreInput.HostKind.TouchUp, e.GetPointerId( i ), e.GetX( i ) - ox, e.GetY( i ) - oy );
				break;
			}

			//The system took the gesture for itself - every finger of it, and none of them is a tap.
			case MotionEventActions.Cancel:
			{
				for ( var p = 0; p < e.PointerCount; p++ )
				{
					CoreInput.HostTouch( CoreInput.HostKind.TouchCancel, e.GetPointerId( p ), e.GetX( p ) - ox, e.GetY( p ) - oy );
				}

				break;
			}
		}
	}
}
