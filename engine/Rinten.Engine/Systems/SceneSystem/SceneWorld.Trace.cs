using Rinten.Engine.Utility.RayTrace;

namespace Rinten;


public partial class SceneWorld
{
	/// <summary>Trace against all scene objects in this scene world</summary>
	public MeshTraceRequest Trace => new() { targetWorld = this };
}
