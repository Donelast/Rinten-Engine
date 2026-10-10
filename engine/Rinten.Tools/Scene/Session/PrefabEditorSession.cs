using Rinten;
using System.Diagnostics;

namespace Editor;

//Holds a current open scene and its edit state
class PrefabEditorSession : SceneEditorSession
{
	public new PrefabScene Scene => base.Scene as PrefabScene;

	public PrefabEditorSession( PrefabScene scene ) : base( scene )
	{
		scene.Name = scene.Source.ResourceName;
		Selection.Add( scene );
	}

	protected override void OnEdited()
	{
		if ( Scene is not { } prefabScene ) return;

		var prefab = (PrefabFile)prefabScene.Source;

		var started = Stopwatch.GetTimestamp();

		//write from prefab scene to its jsonobject this doesn't save it to disk
		prefabScene.ToPrefabFile();

		var written = Stopwatch.GetTimestamp();

		EditorScene.UpdatePrefabInstances( prefab );

		var whole = Stopwatch.GetElapsedTime( started ).TotalMilliseconds;

		//Log write and propagation times separately when an edit is slow
		if ( whole > 8.0 )
		{
			var made = Stopwatch.GetElapsedTime( started, written ).TotalMilliseconds;

			Log.Info( $"prefab '{Scene?.Name}' passed on in {whole:0.#}ms " +
				$"(json {made:0.#}ms, instances {whole - made:0.#}ms)" );
		}
	}

	//Reuse the json propagation already made when nothing changed since, as serialising is slow
	protected override GameResource ToResource()
	{
		if ( !WantsPropagating && Scene?.Source is PrefabFile current )
			return current;

		return Scene.ToPrefabFile();
	}
}
