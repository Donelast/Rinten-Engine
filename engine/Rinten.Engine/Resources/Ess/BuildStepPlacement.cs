using System;

namespace Rinten;

/// <summary>Placement of a build step piece, driven by the editor's own gizmos; steps must not draw their own</summary>
public sealed class BuildStepPlacement
{
	/// <summary>What the handle is called, so its drag state is its own.</summary>
	public string Name { get; init; } = "step";

	/// <summary>Where the thing is now, in the model's space.</summary>
	public Func<Vector3> GetPosition { get; init; }

	/// <summary>And how it is turned. Nothing for a thing that has no facing.</summary>
	public Func<Angles> GetRotation { get; init; }

	/// <summary>Called with what the handle was dragged to.</summary>
	public Action<Vector3, Angles> Set { get; init; }

	/// <summary>Whether turning it means anything. A point on a path has a place and no facing, and offering a handle that does nothing is worse than offering none.</summary>
	public bool CanTurn { get; init; } = true;

	public bool IsValid => GetPosition is not null && Set is not null;
}
