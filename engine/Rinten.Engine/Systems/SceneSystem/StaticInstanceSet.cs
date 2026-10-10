namespace Rinten;

//Static model instances (grass, rocks, trees) stored only as GPU instance table records, no CPU cost
internal sealed class StaticInstanceSet : IDisposable
{
	private readonly Scene scene;
	private readonly List<int> slots = new();

	public StaticInstanceSet( Scene scene )
	{
		this.scene = scene;
	}

	/// <summary>How many copies it holds.</summary>
	public int Count => slots.Count;

	/// <summary>One more copy. <paramref name="lodGroup"/> names the detail group it keeps its detail by with distance; null or empty is the model's own - see <see cref="Model.LodGroup"/>.</summary>
	public void Add( Model model, in Transform transform, bool castShadows = true, Material over = null, float lodScale = 1.0f, string lodGroup = null )
	{
		if ( Application.IsHeadless || scene is null || model is null ) return;

		var slot = CoreSceneTables.Of( scene ).AddStatic( model, transform, over, castShadows, lodScale, lodGroup );
		if ( slot >= 0 ) slots.Add( slot );
	}

	/// <summary>Takes every copy away.</summary>
	public void Clear()
	{
		if ( slots.Count == 0 ) return;

		var tables = CoreSceneTables.Of( scene );

		foreach ( var slot in slots ) tables.RemoveStatic( slot );

		slots.Clear();
	}

	public void Dispose() => Clear();
}
