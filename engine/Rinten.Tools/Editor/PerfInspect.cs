using Rinten;
using System.Linq;

namespace Editor;

//-perfinspect/-perfswitch: reselect objects or switch workspaces repeatedly to measure UI building
internal static class PerfInspect
{
	//Seconds between one object being opened and the next.
	private static readonly float every =
		Rinten.Utility.CommandLine.HasSwitch( "-perfinspect" )
			? MathX.Clamp( Rinten.Utility.CommandLine.GetSwitchInt( "-perfinspect", 500 ) / 1000.0f, 0.05f, 30.0f )
			: 0.0f;

	//Seconds between one workspace and the next.
	private static readonly float switching =
		Rinten.Utility.CommandLine.HasSwitch( "-perfswitch" )
			? MathX.Clamp( Rinten.Utility.CommandLine.GetSwitchInt( "-perfswitch", 2000 ) / 1000.0f, 0.2f, 60.0f )
			: 0.0f;

	//-perfprefab <path>: open a prefab and keep reselecting its root, the heavy inspector case
	private static readonly string prefab =
		Rinten.Utility.CommandLine.GetSwitch( "-perfprefab", "" ).TrimQuoted();

	//An object to put in the inspector once and leave there, by name - -perfselect Camera - for an inspector at rest.
	private static readonly string select =
		Rinten.Utility.CommandLine.GetSwitch( "-perfselect", "" ).TrimQuoted();

	//Whether to write the memory down after each round of -perfinspect - -perfinspectmemory .
	private static readonly bool remember = Rinten.Utility.CommandLine.HasSwitch( "-perfinspectmemory" );

	//Whether those notes leave the collector alone - -perfinspectnocollect - so they read
	//what the memory panel reads rather than what is alive. See PerfScenario.NoteMemory.
	private static readonly bool collect = !Rinten.Utility.CommandLine.HasSwitch( "-perfinspectnocollect" );

	//Whether -perfinspect walks the project's assets rather than the scene - -perfinspectassets .
	private static readonly bool assets = Rinten.Utility.CommandLine.HasSwitch( "-perfinspectassets" );

	private static bool opened;
	private static bool selected;
	private static GameObject heaviest;

	private static RealTimeSince sinceInspected;
	private static RealTimeSince sinceSwitched;

	private static int nextObject;
	private static int nextWorkspace;
	private static long inspected;

	[EditorEvent.Frame]
	public static void Frame()
	{
		//Not before there is a session: the first frames of a run happen while the project is still coming up, and a prefab asked for then opens into nothing.
		if ( !opened && !string.IsNullOrWhiteSpace( prefab ) && SceneEditorSession.Active is not null )
		{
			opened = true;
			Open();
		}

		if ( !selected && !string.IsNullOrWhiteSpace( select ) && SceneEditorSession.Active?.Scene is { } scene )
		{
			selected = Select( scene );
		}

		if ( every > 0.0f && sinceInspected > every )
		{
			sinceInspected = 0;
			Inspect();
		}

		if ( switching > 0.0f && sinceSwitched > switching )
		{
			sinceSwitched = 0;
			Switch();
		}
	}

	//Opens the prefab the run asked for, once.
	private static void Open()
	{
		foreach ( var path in new[] { prefab, $"{prefab}.prefab" } )
		{
			if ( ResourceLibrary.Get<PrefabFile>( path ) is not { } file ) continue;

			EditorScene.OpenPrefab( file );

			Log.Info( $"-perfprefab: opened {path}" );
			return;
		}

		Log.Warning( $"-perfprefab {prefab}: no such prefab - the run is measuring whatever was open" );
	}

	//Puts the named object in the inspector, once the scene has any objects to look through.
	private static bool Select( Scene scene )
	{
		var objects = scene.Children.SelectMany( x => x.GetAllObjects( false ) ).ToArray();
		if ( objects.Length == 0 ) return false;

		var chosen = objects.FirstOrDefault( x => string.Equals( x.Name, select, System.StringComparison.OrdinalIgnoreCase ) );

		if ( chosen is null )
		{
			Log.Warning( $"-perfselect {select}: no such object - the run is measuring an empty inspector" );
			return true;
		}

		//Selected the way a click in the hierarchy selects it, so the inspector, the tree and the gizmos all follow.
		SceneEditorSession.Active.Selection.Set( chosen );

		Log.Info( $"-perfselect: {chosen.Name} ({chosen.Components.Count} components) selected" );
		return true;
	}

	//Selects the next object (prefab root or scene walk) and logs the synchronous cost only
	private static void Inspect()
	{
		if ( assets )
		{
			InspectAsset();
			return;
		}

		if ( SceneEditorSession.Active?.Scene is not { } scene ) return;

		GameObject chosen;

		if ( !string.IsNullOrWhiteSpace( prefab ) )
		{
			//Every other time nothing, so the next selection builds the rows again rather than finding the ones it already has.
			if ( nextObject++ % 2 == 1 )
			{
				EditorUtility.InspectorObject = null;
				return;
			}

			//Whichever object in it carries the most components, which is the one a person opens
			//a player prefab to look at. The first root is often a bone or a body with three.
			heaviest ??= scene.Children
				.SelectMany( x => x.GetAllObjects( true ) )
				.OrderByDescending( x => x.Components.Count )
				.FirstOrDefault();

			if ( heaviest is not { } root ) return;

			chosen = root;
		}
		else
		{
			var objects = scene.Children.SelectMany( x => x.GetAllObjects( false ) ).Take( 64 ).ToArray();

			if ( objects.Length == 0 ) return;

			//Once round every object: what is still held after the same objects were opened again is what opening one leaks, and the first round is what it caches.
			if ( remember && nextObject > 0 && nextObject % objects.Length == 0 )
				PerfScenario.NoteMemory( $"after {nextObject / objects.Length} round(s) of {objects.Length} objects", collect );

			chosen = objects[nextObject++ % objects.Length];
		}

		var started = System.Diagnostics.Stopwatch.GetTimestamp();

		EditorUtility.InspectorObject = chosen;

		var ms = ( System.Diagnostics.Stopwatch.GetTimestamp() - started ) * 1000.0 /
			System.Diagnostics.Stopwatch.Frequency;

		inspected++;

		Log.Info( $"-perfinspect: {chosen.Name} ({chosen.Components.Count} components, {inspected}) asked in {ms:F1} ms{Allocated()}" );
	}

	//The allocation counter the last line was written at.
	private static long allocatedAt;

	//Bytes allocated since the previous object was inspected, to rank objects by opening cost
	private static string Allocated()
	{
		if ( !remember ) return "";

		var now = System.GC.GetTotalAllocatedBytes( precise: false );
		var since = allocatedAt == 0 ? 0 : now - allocatedAt;

		allocatedAt = now;

		return $", {since / ( 1024.0 * 1024.0 ):F1} MB allocated since the one before";
	}

	//-perfinspectassets: cycles project assets through the inspector, which loads their resources
	private static void InspectAsset()
	{
		projectAssets ??= AssetSystem.All
			.Where( x => !x.IsCloud && !x.IsProcedural && !x.IsTrivialChild && x.AbsolutePath is { } path &&
				path.StartsWith( Project.Current.GetAssetsPath(), System.StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( x => x.RelativePath, System.StringComparer.OrdinalIgnoreCase )
			.ToArray();

		if ( projectAssets.Length == 0 ) return;

		if ( remember && nextObject > 0 && nextObject % projectAssets.Length == 0 )
			PerfScenario.NoteMemory( $"after {nextObject / projectAssets.Length} round(s) of {projectAssets.Length} assets", collect );

		var chosen = projectAssets[nextObject++ % projectAssets.Length];

		var started = System.Diagnostics.Stopwatch.GetTimestamp();

		EditorUtility.InspectorObject = chosen;

		var ms = ( System.Diagnostics.Stopwatch.GetTimestamp() - started ) * 1000.0 /
			System.Diagnostics.Stopwatch.Frequency;

		inspected++;

		Log.Info( $"-perfinspect: {chosen.RelativePath} ({inspected}) asked in {ms:F1} ms{Allocated()}" );
	}

	private static Asset[] projectAssets;

	//Moves to the next workspace there is.
	private static void Switch()
	{
		if ( WorkspaceArea.Current is not { } area ) return;

		var names = new[] { "General", "Mapping" };
		var wanted = names[nextWorkspace++ % names.Length];

		if ( area.Find( wanted ) is not { } found )
		{
			//Mapping is built rather than found until somebody asks for it once.
			if ( string.Equals( wanted, "Mapping", System.StringComparison.OrdinalIgnoreCase ) )
			{
				Rinten.Internal.GlobalToolsNamespace.EditorWindow?.OpenMapping();
			}

			return;
		}

		area.MakeCurrent( found );
	}
}
