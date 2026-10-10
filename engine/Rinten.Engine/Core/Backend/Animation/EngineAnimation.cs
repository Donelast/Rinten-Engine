namespace Rinten;

//What the animation backend is doing, said out loud. The counterpart to physics_status , and for the same reason: which engine is
//posing a model is not visible in the picture until it goes wrong, and by then it looks like a rig problem. This answers it directly.
internal static class EngineAnimation
{
	//Logs nodes, clips and files a graph uses on its next update, plus bone length changes from the pose
	/// <summary>Write out what a running graph goes through on its next update.</summary>
	[ConCmd( "anim_trace", ConVarFlags.Protected,
		Help = "Log what each animation graph plays on its next update(s). anim_trace [count] [model name]" )]
	public static void TraceGraphs( int count = 1, string only = "" )
	{
		Rinten.Animation.AnimGraphInstance.TraceUpdates = Math.Max( count, 1 );
		Rinten.Animation.AnimGraphInstance.TraceOnly = only ?? "";

		Log.Info( $"anim_trace: writing out the next {Math.Max( count, 1 )} graph update(s)" +
			( string.IsNullOrEmpty( only ) ? "" : $" of models named like '{only}'" ) );

		var scene = Game.ActiveScene;

		if ( !scene.IsValid() ) return;

		foreach ( var renderer in scene.GetAllComponents<SkinnedModelRenderer>() )
		{
			if ( renderer.Model is null ) continue;

			//What the game asked for, beside what the graph is answering with: a hold type
			//set on a renderer that was rebuilt since is a hold type nothing is holding.
			var held = renderer.GetInt( "holdtype" );
			var hands = renderer.GetInt( "holdtype_handedness" );

			Log.Info( $"  {renderer.GameObject?.Name}: {renderer.Model.ResourcePath}" +
				$"   holdtype={held} handedness={hands}" +
				$" set-on-component={renderer.ContainsParameter( "holdtype" )}" +
				$" graph={( renderer.SceneModel.IsValid() ? "live" : "none" )}" );
		}
	}

	[ConCmd( "anim_status", ConVarFlags.Protected,
		Help = "Report which backend animates each skinned model in the scene" )]
	public static void PrintStatus()
	{
		Log.Info( "Animation backend: our core" );
		Log.Info( $"rintencore         : {CoreNative.BuildInfo}" );

		var scene = Game.ActiveScene;

		if ( !scene.IsValid() )
		{
			Log.Info( "no scene is active" );
			return;
		}

		var renderers = scene.GetAllComponents<SkinnedModelRenderer>().ToList();

		Log.Info( "" );
		Log.Info( $"{renderers.Count} skinned model(s):" );

		foreach ( var renderer in renderers )
		{
			var model = renderer.Model;
			var sceneModel = renderer.SceneModel;

			if ( !sceneModel.IsValid() )
			{
				Log.Info( $"  {renderer.GameObject?.Name}: no scene object" );
				continue;
			}

			var backend = sceneModel.Animation is CoreAnimatableObject ? "our core" : "s&box";
			var sequence = sceneModel.Animation.Sequence;

			Log.Info( $"  {renderer.GameObject?.Name,-24} {backend,-9} {model?.Name}" );

			//The two numbers that say whether a pose is happening at all: how many bones there are to move, and what is moving them.
			var names = sceneModel.Animation.SequenceNames;

			Log.Info( $"      bones {model?.BoneCount ?? 0}, sequences {names.Count}" +
				( names.Count > 0 ? $" ({string.Join( ", ", names.Take( 6 ) )}{( names.Count > 6 ? ", ..." : "" )})" : "" ) );

			if ( string.IsNullOrEmpty( sequence ) )
			{
				Log.Info( renderer.UseAnimGraph
					? "      no sequence - driven by a graph, which our core does not have yet"
					: "      no sequence chosen" );

				continue;
			}

			Log.Info( $"      playing '{sequence}' at {sceneModel.Animation.SequenceCycle:0.00} of " +
				$"{sceneModel.Animation.SequenceDuration:0.00}s, rate {sceneModel.Animation.PlaybackRate:0.00}" +
				( sceneModel.Animation.SequenceFinished ? ", finished" : "" ) );
		}
	}
}
