namespace Editor.TerrainEditor;

public class BrushSettings
{
	/// <summary>Radius of the brush on the ground, in metres.</summary>
	[Property, Range( 0.25f, 64.0f ), Step( 0.25f ), WideMode] public float Size { get; set; } = 5.0f;
	[Property, Range( 0.0f, 1.0f ), Step( 0.01f ), WideMode] public float Opacity { get; set; } = 0.5f;
	[Property, Range( 0, 360 ), Step( 1 ), WideMode] public float Rotation { get; set; } = 0;
	[Property] public bool RandomRotation { get; set; } = false;
	[Property, Title( "Show Grid" )] public bool ShowGridPreview { get; set; } = true;
}
