using Rinten.Internal;

namespace Rinten;

partial class Scene
{
	static WeakHashSet<Scene> _all { get; set; } = [];

	/// <summary>All active non-editor scenes.</summary>
	public static IEnumerable<Scene> All => _all.Where( x => !x.IsEditor && x.Active );

	//Snapshot of every live scene, editor and preview scenes included, e.g. for resource change notices
	internal static Scene[] Loaded => _all.Where( x => x.IsValid ).ToArray();

}
