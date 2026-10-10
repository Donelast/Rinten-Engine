
namespace Rinten;

public partial class Model
{
	/// <summary>Total bounds of all the meshes.</summary>
	public BBox Bounds => core.RenderBounds;

	/// <summary>Total bounds of all the physics shapes.</summary>
	public BBox PhysicsBounds => core.PhysicsBounds;

	/// <summary>Render view bounds.</summary>
	public BBox RenderBounds => core.RenderBounds;
}
