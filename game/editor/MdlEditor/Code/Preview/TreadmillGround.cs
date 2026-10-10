namespace Editor.MdlEditor;

//Tracks the treadmill offset opposite to root motion so the model walks in place; draws nothing
internal sealed class TreadmillGround
{
	//How far apart the lines would be, if this still drew any. Kept only as the wrap distance, so the tracked offset doesn't jump when it resets.
	private const float Spacing = 0.5f;

	private Rotation _rotation = Rotation.Identity;
	private Vector3 _wrapped;

	public void Update( SkinnedModelRenderer model )
	{
		if ( !model.IsValid() )
			return;

		var motion = model.RootMotion;

		//The character doesn't turn - the ground rotates the reverse way under it
		_rotation = motion.Rotation.Inverse * _rotation;

		//Along the ground, which is x and z - it was x and y, which was the ground when z was the height and is a wall now.
		var slide = _rotation.Inverse * -motion.Position;
		_wrapped.x = Wrap( _wrapped.x + slide.x );
		_wrapped.z = Wrap( _wrapped.z + slide.z );
	}

	/// <summary>Place the ground from a clip's total root motion at a point in time, for previews that know exactly where they are. Playback only ever reports how far the root moved since the last frame, and a graph reads any step backwards as having looped, so scrubbing can't be driven by <see cref="Update"/>.</summary>
	public void SetFromRootMotion( Transform rootMotion )
	{
		_rotation = rootMotion.Rotation.Inverse;

		var slide = rootMotion.Rotation * -rootMotion.Position;
		_wrapped.x = Wrap( slide.x );
		_wrapped.z = Wrap( slide.z );
	}

	public void Reset()
	{
		_rotation = Rotation.Identity;
		_wrapped = default;
	}

	private static float Wrap( float v ) => v - MathF.Round( v / Spacing ) * Spacing;
}
