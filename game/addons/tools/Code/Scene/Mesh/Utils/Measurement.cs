namespace Editor.MeshEditor;

/// <summary>How the mesh tools write a length on a gizmo.</summary>
public static class Measurement
{
	/// <summary>Metres in, centimetres out, as text for a label.</summary>
	public static string Length( float units ) => $"{units * 100.0f:0.#}";

	/// <summary>The same, for something that is only ever a whole number of them.</summary>
	public static string Round( float units ) => $"{units * 100.0f:0}";
}
