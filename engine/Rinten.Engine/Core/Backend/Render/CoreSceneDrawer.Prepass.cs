namespace Rinten;

internal sealed partial class CoreSceneDrawer
{
	//The ways a view can draw its still world's depth and cull by it. Which is quickest is a
	//fact about what is in front of the view, so the frame measures and picks - see DepthTuner.
	internal enum DepthWay
	{
		/// <summary>The clusters nearer than the far cut pre-passed, nothing occluded.</summary>
		NearPrepass,

		/// <summary>No pre-pass: every material writes its own depth, near to far.</summary>
		NoPrepass,

		/// <summary>All of it pre-passed, nothing occluded: best for layered cut-outs seen from above.</summary>
		FullPrepass,

		/// <summary>All of it pre-passed and folded into the pyramid the cull tests against - see r_occlusion.</summary>
		Occlusion,
	}

	//Picks this view's DepthWay by timing each option on the GPU every few seconds, keeping the fastest
	private sealed class DepthTuner
	{
		//How long a choice stands before it is asked again.
		private const double SettleSeconds = 2.5;

		//Frames not counted after a change - the card's reports are behind.
		private const int Discard = 4;

		//Frames counted each way.
		private const int Take = 16;

		//How much quicker another way has to be to be taken.
		private const float Margin = 0.97f;

		private DepthWay way = DepthWay.NearPrepass;
		private bool probing;
		private int step;
		private int frames;
		private int counted;
		private int offered;
		private double settledAt;
		private float lastReading = -1.0f;
		private readonly float[] readings = new float[Take];

		//The ways one probe measures, in order - the others, then this one's own again.
		private readonly DepthWay[] sequence = new DepthWay[4];
		private readonly float[] medians = new float[4];
		private int steps;

		//RINTEN_DEPTH_TRACE=1: a line a frame of what the card took and which way the frame went.
		private static readonly bool trace = System.Environment.GetEnvironmentVariable( "RINTEN_DEPTH_TRACE" ) == "1";

		/// <summary>The way this frame goes, given what the card said the last finished frame of this view took - nought where it said nothing - and how many of the ways the view may take this frame: occlusion needs a pyramid and a perspective camera, and may be left out altogether.</summary>
		public DepthWay Pick( float gpuMs, int available )
		{
			var picked = PickInner( gpuMs, available );

			if ( trace ) Log.Info( $"depth way trace: step {( probing ? step : -1 )} gpu {gpuMs:F3} -> {picked}" );

			return picked;
		}

		private DepthWay PickInner( float gpuMs, int available )
		{
			//The ways on offer changed under us - occlusion lost its pyramid, say: whatever was being measured is not worth finishing.
			if ( available != offered || (int)way >= available )
			{
				offered = available;
				if ( (int)way >= available ) way = DepthWay.NearPrepass;
				Settle();
			}

			if ( !probing )
			{
				if ( RealTime.NowDouble - settledAt < SettleSeconds || available < 2 ) return way;

				//The others, then this one's own way again - measured just after, so a card warming or a scene changing moves both.
				steps = 0;
				for ( var other = 0; other < available; other++ )
					if ( other != (int)way ) sequence[steps++] = (DepthWay)other;
				sequence[steps++] = way;

				probing = true;
				step = 0;
				Restart();
				return sequence[0];
			}

			if ( !Count( gpuMs ) ) return probing ? sequence[step] : way;

			medians[step] = Median();

			if ( ++step < steps )
			{
				Restart();
				return sequence[step];
			}

			//Own way last: the quickest of the rest has to beat it by the margin.
			var own = medians[steps - 1];
			var best = way;
			var bestMs = own * Margin;

			for ( var i = 0; i < steps - 1; i++ )
			{
				if ( medians[i] > 0.0f && medians[i] < bestMs )
				{
					best = sequence[i];
					bestMs = medians[i];
				}
			}

			if ( best != way && own > 0.0f )
			{
				//Said when it turns over, so a report's numbers can be read knowing which way the view was drawing - at trace: two ways a tenth of a
				//millisecond apart turn over every few seconds, and at info that was a line every three seconds for as long as the editor was open.
				Log.Trace( $"the view's depth: {best} ({bestMs:F3} ms of card) instead of {way} ({own:F3})" );
				way = best;
			}

			Settle();
			return way;
		}

		private void Settle()
		{
			probing = false;
			settledAt = RealTime.NowDouble;
		}

		private void Restart()
		{
			frames = 0;
			counted = 0;
		}

		//One more frame of a probe step; true once enough were counted.
		private bool Count( float gpuMs )
		{
			frames++;

			//A view that stopped reporting - hidden, or not drawn by the card - is not measured for ever: the probe is given up and the choice stands.
			if ( frames > Discard + ( Take * 8 ) )
			{
				Settle();
				return false;
			}

			//A reading repeated is the same frame read twice - the card had not finished another yet - and is not counted again.
			if ( frames <= Discard || gpuMs <= 0.0f || gpuMs == lastReading ) return false;

			lastReading = gpuMs;
			readings[counted++] = gpuMs;

			return counted >= Take;
		}

		private float Median()
		{
			Array.Sort( readings );
			return readings[Take / 2];
		}
	}

	private readonly DepthTuner depthTuner = new();
}
