using System.Collections.Generic;

namespace Rinten.Clutter;

//Clutter copies of one model as scene instance table records; the renderer culls and draws them
internal sealed class ClutterBatch
{
	private readonly StaticInstanceSet set;
	private readonly Model model;
	private readonly bool castShadows;

	public ClutterBatch( Scene scene, Model model, bool castShadows = true )
	{
		set = new StaticInstanceSet( scene );
		this.model = model;
		this.castShadows = castShadows;
	}

	public void SetInstances( List<Transform> transforms )
	{
		set.Clear();

		foreach ( var transform in transforms )
			set.Add( model, transform, castShadows );
	}

	public void Delete() => set.Dispose();
}
