namespace Rinten.UI;

/// <summary>Smoothed UI cost of a window's frame: layout, input and draw in ms, plus frame rate</summary>
public readonly record struct UiFrameCost( float LayoutMs, float InputMs, float DrawMs, float FramesPerSecond )
{
	/// <summary>Everything the window did for the frame.</summary>
	public float TotalMs => LayoutMs + InputMs + DrawMs;

	/// <summary>Whether a window has written anything here yet.</summary>
	public bool IsMeasured => FramesPerSecond > 0;

	/// <summary>This reading moved a step toward a new one: an exponential average, so one slow frame shows as a bump rather than a jump.</summary>
	public UiFrameCost Toward( in UiFrameCost next, float weight )
	{
		if ( !IsMeasured ) return next;

		weight = Math.Clamp( weight, 0f, 1f );

		return new UiFrameCost(
			LayoutMs + ( next.LayoutMs - LayoutMs ) * weight,
			InputMs + ( next.InputMs - InputMs ) * weight,
			DrawMs + ( next.DrawMs - DrawMs ) * weight,
			FramesPerSecond + ( next.FramesPerSecond - FramesPerSecond ) * weight );
	}

	public override string ToString()
		=> $"layout {LayoutMs:F2} ms, input {InputMs:F2} ms, draw {DrawMs:F2} ms, {FramesPerSecond:F0} fps";
}
