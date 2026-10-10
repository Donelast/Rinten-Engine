namespace Rinten;

internal struct PostProcessContext
{
	public CameraComponent Camera;
	/// <summary>Every instance of the building effect's kind, with its weight - the system's own list for that kind, read while the effect builds and refilled for the next camera.</summary>
	public List<WeightedEffect> Components;
}
