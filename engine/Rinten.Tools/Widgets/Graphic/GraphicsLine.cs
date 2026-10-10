using Rinten;
using System;

namespace Editor
{
	public class GraphicsLine : GraphicsItem
	{

		//The path this line follows, in the item's own coordinates.
		private readonly List<Vector2> points = new();

		public void Clear()
		{
			points.Clear();

			Update();
		}

		public void MoveTo( Vector2 point )
		{
			points.Clear();
			points.Add( point );
		}

		public void LineTo( Vector2 point )
		{
			points.Add( point );

			Update();
		}

		/// <summary>A curve, drawn as the short straight lines it is made of - which is what a curve is by the time it reaches the screen anyway.</summary>
		public void CubicLineTo( Vector2 c1, Vector2 c2, Vector2 point )
		{
			var from = points.Count > 0 ? points[^1] : point;

			const int steps = 16;

			for ( var i = 1; i <= steps; i++ )
			{
				var t = i / (float)steps;
				var n = 1.0f - t;

				points.Add(
					(n * n * n * from) +
					(3 * n * n * t * c1) +
					(3 * n * t * t * c2) +
					(t * t * t * point) );
			}

			Update();
		}

		/// <summary>How near the line counts as being on it, in scene units.</summary>
		public float HitWidth { get; set; } = 4.0f;

		//The least the reach may shrink to on screen, in pixels. The reach is in scene units, so zooming out shrinks it
		//with everything else - and a wire you cannot put the pointer on is worse than one that is a little generous.
		private const float MinimumPixelReach = 5.0f;

		/// <summary>On the line itself rather than anywhere in the box it spans.</summary>
		public override bool Contains( Vector2 localPos )
		{
			if ( points.Count == 0 ) return false;

			//The scale the view is drawn at, so the reach is a distance on screen rather than one that vanishes as you zoom out.
			var scale = GraphicsView?.Scale.x ?? 1.0f;
			if ( scale <= 0.0f ) scale = 1.0f;

			var reach = MathF.Max( HitWidth, MinimumPixelReach / scale );
			var reachSquared = reach * reach;

			if ( points.Count == 1 ) return points[0].DistanceSquared( localPos ) <= reachSquared;

			for ( var i = 1; i < points.Count; i++ )
			{
				if ( DistanceSquaredToSegment( localPos, points[i - 1], points[i] ) <= reachSquared ) return true;
			}

			return false;
		}

		//How far a point is off a segment, squared - no root taken because it is only ever compared against another squared length.
		private static float DistanceSquaredToSegment( Vector2 point, Vector2 from, Vector2 to )
		{
			var along = to - from;
			var lengthSquared = along.LengthSquared;

			//The two ends in the same place: the segment is a point.
			if ( lengthSquared <= 0.0001f ) return from.DistanceSquared( point );

			var t = Math.Clamp( Vector2.Dot( point - from, along ) / lengthSquared, 0.0f, 1.0f );

			return ( from + ( along * t ) ).DistanceSquared( point );
		}

		protected override void OnPaint()
		{
			Editor.Paint.SetPen( Color.Red, 5.0f );
			PaintLine();
		}

		protected void PaintLine()
		{
			if ( points.Count < 2 ) return;

			Editor.Paint.DrawLine( points );
		}
	}
}
