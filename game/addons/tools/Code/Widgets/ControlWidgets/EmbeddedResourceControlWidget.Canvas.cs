namespace Editor;

public partial class EmbeddedResourceControlWidget
{
	public override bool UsesCanvasDraw => IsInlineEditor || base.UsesCanvasDraw;
}
