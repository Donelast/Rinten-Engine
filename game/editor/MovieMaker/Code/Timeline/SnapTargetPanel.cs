using Rinten;
using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>The dashed line that shows what a dragged thing has caught on.</summary>
public sealed class SnapTargetPanel : TimelineItem
{
	private const int DashLength = 2;
	private const int GapLength = 2;

	public SnapTargetPanel( Timeline timeline )
		: base( timeline )
	{
		AddClass( "snap-target" );

		ZIndex = 50_000;

		AddChild( new DashPanel() );
	}

	//One dash and one gap, repeated down the line.
	private sealed class DashPanel : BitmapPanel
	{
		public DashPanel()
		{
			AddClass( "dashes" );
		}

		protected override bool StretchToFit => false;

		protected override Vector2Int BitmapSize => new( 1, DashLength + GapLength );

		protected override void OnDrawBitmap( Bitmap bitmap )
		{
			bitmap.SetFill( Color.White.WithAlpha( 0.75f ) );
			bitmap.DrawRect( new Rect( 0f, 0f, 1f, DashLength ) );
		}
	}
}
