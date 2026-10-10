using Rinten.Utility;

namespace Rinten;

//Per-frame scene counters for the perf readout, one slot per thread to avoid cache-line contention
internal static class SceneMetrics
{
	private static readonly SlotCounter particlesCreated = new();
	private static readonly SlotCounter particlesDestroyed = new();
	private static readonly SlotCounter gameObjectsCreated = new();
	private static readonly SlotCounter gameObjectsDestroyed = new();
	private static readonly SlotCounter componentsCreated = new();
	private static readonly SlotCounter componentsDestroyed = new();
	private static readonly SlotCounter rayTrace = new();
	private static readonly SlotCounter rayTraceAll = new();

	public static void Flip()
	{
		particlesCreated.Clear();
		particlesDestroyed.Clear();
		gameObjectsCreated.Clear();
		gameObjectsDestroyed.Clear();
		componentsCreated.Clear();
		componentsDestroyed.Clear();
		rayTrace.Clear();
		rayTraceAll.Clear();
	}

	public static void CountParticleCreated() => particlesCreated.Add();
	public static void CountParticleDestroyed() => particlesDestroyed.Add();
	public static void CountGameObjectCreated() => gameObjectsCreated.Add();
	public static void CountGameObjectDestroyed() => gameObjectsDestroyed.Add();
	public static void CountComponentCreated() => componentsCreated.Add();
	public static void CountComponentDestroyed() => componentsDestroyed.Add();
	public static void CountRayTrace() => rayTrace.Add();
	public static void CountRayTraceAll() => rayTraceAll.Add();

	public static double ParticlesCreated => particlesCreated.Total;
	public static double ParticlesDestroyed => particlesDestroyed.Total;
	public static double GameObjectsCreated => gameObjectsCreated.Total;
	public static double GameObjectsDestroyed => gameObjectsDestroyed.Total;
	public static double ComponentsCreated => componentsCreated.Total;
	public static double ComponentsDestroyed => componentsDestroyed.Total;
	public static double RayTrace => rayTrace.Total;
	public static double RayTraceAll => rayTraceAll.Total;
}
