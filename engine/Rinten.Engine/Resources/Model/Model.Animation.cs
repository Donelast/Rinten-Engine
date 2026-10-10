namespace Rinten;

public partial class Model
{
	//For a model in our own format this is what its definition lists, not something baked into the compiled file: a model and its clips are separate assets, and which of them belong together is the definition's business.
	/// <summary>Number of animations this model has.</summary>
	public int AnimationCount => CoreSkeletons.SequenceNames( this ).Count;

	/// <summary>Returns name of an animation at given animation index.</summary>
	public string GetAnimationName( int animationIndex )
	{
		if ( animationIndex < 0 || animationIndex >= AnimationCount )
			throw new ArgumentOutOfRangeException( "animationIndex", $"Tried to access out of range animation index {animationIndex}, range is 0-{AnimationCount - 1}" );

		return CoreSkeletons.SequenceNames( this )[animationIndex];
	}

	private List<string> _animationNames;
	public IReadOnlyList<string> AnimationNames =>
		_animationNames is { Count: > 0 }
			? _animationNames
			: _animationNames = Enumerable.Range( 0, AnimationCount ).Select( GetAnimationName ).ToList();

	/// <summary>Resource path of the clip played under this sequence name, or null.</summary>
	public string GetAnimationClipPath( string sequenceName ) => CoreSkeletons.ClipPath( this, sequenceName );

	//Lives on the model so every scene using it gets the same graph by default
	/// <summary>The graph in our own format that this model brings with it, or null.</summary>
	public AnimGraph DefaultAnimationGraph => UseModelGraphs ? CoreSkeletons.Graph( this ) : null;

	//Older name for the same graph, kept for code that still uses it
	/// <summary>Alias of <see cref="DefaultAnimationGraph"/>.</summary>
	public AnimGraph AnimGraph => DefaultAnimationGraph;

	//Turn off to compare a character with and without its graph
	/// <summary>Whether a model's own graph is used at all.</summary>
	[ConVar( "anim_graph", Help = "Animate characters with the graph their model names" )]
	public static bool UseModelGraphs { get; set; } = true;
}
