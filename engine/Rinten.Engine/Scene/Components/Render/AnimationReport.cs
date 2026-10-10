using System.Linq;

namespace Rinten;

//Prints what each skinned renderer's animation is doing, to tell apart the causes of a T-pose
static class AnimationReport
{
	/// <inheritdoc cref="AnimationReport"/>
	[ConCmd( "anim_report" )]
	public static void Report()
	{
		var scene = Game.ActiveScene;

		if ( !scene.IsValid() )
		{
			Log.Info( "anim_report: no scene is running" );
			return;
		}

		var renderers = scene.GetAllComponents<SkinnedModelRenderer>().ToList();

		Log.Info( $"anim_report: {renderers.Count} skinned renderer(s)" );

		foreach ( var renderer in renderers )
		{
			ReportOn( renderer );
		}
	}

	static void ReportOn( SkinnedModelRenderer renderer )
	{
		var where = renderer.GameObject.IsValid() ? renderer.GameObject.Name : "?";

		if ( renderer.Model is null )
		{
			Log.Info( $"  {where}: no model - Model property is null" );
			return;
		}

		var bones = renderer.Model.Bones.AllBones;

		//Named rather than counted: the whole question is which model is on screen, and a count of ninety-five is true of several of them.
		Log.Info( $"  {where}: {renderer.Model.ResourcePath ?? renderer.Model.Name}" );
		Log.Info( $"    enabled {renderer.Enabled}, {bones.Count} bone(s), " +
			$"merged onto {(renderer.BoneMergeTarget.IsValid() ? renderer.BoneMergeTarget.GameObject.Name : "nothing")}" );

		//What is playing, and whether it was chosen here or came with the model. Printing only the override
		//said "none" for every character animated by its own model's graph, which is most of them.
		var graph = renderer.EffectiveAnimationGraph;
		var from = renderer.AnimGraph is null ? "from the model" : "overridden here";

		Log.Info( $"    graph: {(graph is null ? "none" : $"{graph.ResourcePath ?? graph.Name} ({from})")}" );

		if ( graph is not null )
		{
			Log.Info( $"    {graph.Parameters.Count} declared parameter(s), " +
				$"{renderer.StoredParameterCount} set on this renderer" );
		}

		if ( !renderer.SceneModel.IsValid() )
		{
			Log.Info( "    no scene model: nothing is drawn and nothing is posed" );
			return;
		}

		var answered = 0;
		var moved = 0;

		//Against the bind pose, in the same space: LocalTransform is where the modeller left the bone relative to the
		//root, and the world transform brought back through the object's is the same thing after the graph has had it.
		var toModel = renderer.WorldTransform;

		foreach ( var bone in bones )
		{
			if ( !renderer.TryGetBoneTransform( bone, out var world ) ) continue;

			++answered;

			var local = toModel.ToLocal( world );
			var rest = bone.LocalTransform;

			//A centimetre or a degree: the numbers are floats through a matrix and back, and exact equality would call rounding motion.
			if ( local.Position.Distance( rest.Position ) > 0.01f ||
				local.Rotation.Distance( rest.Rotation ) > 1.0f )
			{
				++moved;
			}
		}

		Log.Info( $"    {answered} of {bones.Count} bone(s) answered with a transform" );
		Log.Info( $"    {moved} of those have moved off the bind pose" );
	}
}
