namespace Editor.MdlEditor;

/// <summary>Which handle the viewport is offering for whatever is selected.</summary>
public enum PlacementTool
{
	None,
	Move,
	Rotate,
	Scale,
}

//A named transform the viewport can drag, written back through Set.
internal sealed class ScenePlacement
{
	/// <summary>What the handle is called, so its drag state is its own.</summary>
	public string Name { get; init; } = "placement";

	/// <summary>Where the thing is now.</summary>
	public Func<Vector3> GetPosition { get; init; }

	/// <summary>And how it is turned.</summary>
	public Func<Angles> GetRotation { get; init; }

	/// <summary>Called with what the handle was dragged to.</summary>
	public Action<Vector3, Angles> Set { get; init; }

	/// <summary>Optional scale the Scale handle writes. Missing means this thing has no scale.</summary>
	public Func<Vector3> GetScale { get; init; }

	public Action<Vector3> SetScale { get; init; }

	/// <summary>True when scale is one number, not three axes.</summary>
	public bool UniformScale { get; init; }

	//A point has a place and no facing. Offering a turn handle for one is worse than offering none: it moves, nothing happens, and the next thing anybody suspects is the gizmo.
	/// <summary>Whether turning this means anything.</summary>
	public bool CanTurn { get; init; } = true;

	public bool CanScale => GetScale is not null && SetScale is not null;

	public bool IsValid => GetPosition is not null && GetRotation is not null && Set is not null;
}
