namespace Rinten;

public sealed partial class ModelBuilder
{
	private readonly List<AnimationBuilder> _animations = [];

	/// <summary>Adds an animation to this model and returns a builder to construct the animation. Duplicate names are given a numeric suffix. Animations without frames are omitted from the finished model.</summary>
	public AnimationBuilder AddAnimation( string name, float frameRate )
	{
		var uniqueName = name;
		var suffix = 1;

		while ( _animations.Any( builder => builder.Name == uniqueName ) )
		{
			uniqueName = $"{name}_{suffix++}";
		}

		var builder = new AnimationBuilder { Name = uniqueName, FrameRate = frameRate };
		_animations.Add( builder );
		return builder;
	}

}
