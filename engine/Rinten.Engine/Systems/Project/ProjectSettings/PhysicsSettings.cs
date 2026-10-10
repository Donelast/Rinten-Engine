namespace Rinten.Physics;

[Expose]
public class PhysicsSettings : ConfigData
{
	/// <summary>If false, then instead of operating physics, and UpdateFixed in a fixed update frequency they will be called the same as Update - every frame, with a variable time delta.</summary>
	public bool UseFixedUpdate { get; set; } = true;

	/// <summary>Physics substeps per tick; raise if objects tunnel. Steps per second = tickrate * substeps.</summary>
	public int SubSteps { get; set; } = 1;

	/// <summary>How many times a second FixedUpdate runs</summary>
	public float FixedUpdateFrequency { get; set; } = 50.0f;

	/// <summary>If the frame took longer than a FixedUpdate step, we need to run multiple steps for that frame, to catch up. How many are allowed? Too few, and the simluation will run slower than the game. If you allow an unlimited amount then the frame time could snowball to infinity and never catch up.</summary>
	public int MaxFixedUpdates { get; set; } = 5;
}
