namespace Editor;

/// <summary>Marks a static method that builds one example for the Panel Gallery.</summary>
[AttributeUsage( AttributeTargets.Method )]
public sealed class PanelGalleryAttribute : Attribute
{
	/// <summary>Whether the example can only be built inside a live window - it opens a popup from its constructor, or reads the window it is in. The headless self-test leaves these out.</summary>
	public bool RequiresWindow { get; set; }
}

/// <summary>Marks a static method returning a sample value for the gallery; ForType overrides the type</summary>
[AttributeUsage( AttributeTargets.Method )]
public sealed class PanelGallerySampleAttribute : Attribute
{
	/// <summary>The type this sample stands for, where it is not the method's return type.</summary>
	public Type ForType { get; set; }
}
