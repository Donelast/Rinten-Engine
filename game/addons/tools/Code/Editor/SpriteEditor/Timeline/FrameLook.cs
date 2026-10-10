namespace Editor.SpriteEditor;

/// <summary>How one frame stands against the timeline's state: picked, outside the loop, at either end of it.</summary>
public readonly record struct FrameLook( bool Selected, bool OutsideLoop, bool LoopStart, bool LoopEnd )
{
	/// <summary>The look of a frame of an animation, given which frames are picked.</summary>
	public static FrameLook Of( Sprite.Animation animation, int index, FrameSelection selection )
	{
		var selected = selection.Contains( index );

		if ( animation is null || animation.LoopMode == Sprite.LoopMode.None )
			return new FrameLook( selected, false, false, false );

		var start = animation.EffectiveLoopStart;
		var end = animation.EffectiveLoopEnd;
		var handles = animation.IsAnimated;

		return new FrameLook( selected, index < start || index > end, handles && index == start, handles && index == end );
	}
}
