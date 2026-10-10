namespace Rinten;

/// <summary>Watching one of our animation graphs run.</summary>
public sealed partial class SceneModel
{
	private Animation.GraphDebug Watching()
	{
		if ( Animation.RunningGraph is not { } graph ) return null;

		return graph.Watching ?? graph.Watch();
	}

	private Animation.GraphRecording Recording => Animation.RunningGraph?.Recording;

	/// <summary>How long the last graph update took, in milliseconds.</summary>
	public float AnimGraph2UpdateTime => Watching()?.UpdateTime ?? 0.0f;

	/// <summary>What one node of the graph did on the frame just gone.</summary>
	public bool TryGetAnimGraph2NodeDebugInfo( int node, out AnimGraphNodeDebugInfo info )
	{
		info = default;

		return Watching() is { } watching && watching.TryGetInfo( node, out info );
	}

	/// <summary>And what it worked out, for a node that works out a value.</summary>
	public bool TryGetAnimGraph2NodeDebugValue( int node, out AnimGraphNodeDebugValue value )
	{
		value = default;

		return Watching() is { } watching && watching.TryGetValue( node, out value );
	}

	/// <summary>One of the marks on what a node is playing.</summary>
	public bool TryGetAnimGraph2SyncTrackEvent( int node, int index, out AnimGraphSyncTrackEvent marked )
	{
		marked = default;

		return Watching() is { } watching && watching.TryGetSyncEvent( node, index, out marked );
	}

	/// <summary>The word an id was written as, for one the graph has used.</summary>
	public string GetAnimGraph2DebugIDName( StringToken id ) => Watching()?.Name( id );

	/// <summary>Which two poses a blend space is between, and how far.</summary>
	public bool TryGetAnimGraph2ParameterizedBlendDebugInfo( int node, out int first, out int second, out float weight )
	{
		first = -1;
		second = -1;
		weight = 0.0f;

		return Watching() is { } watching && watching.TryGetBlend( node, out first, out second, out weight );
	}

	/// <summary>Where in a two dimensional blend space it is.</summary>
	public bool TryGetAnimGraph2Blend2DParameter( int node, out Vector2 parameter )
	{
		parameter = default;

		return Watching() is { } watching && watching.TryGetBlend2D( node, out parameter );
	}

	/// <summary>What one layer of a layers node ended up weighing.</summary>
	public bool TryGetAnimGraph2LayerWeight( int node, int layer, out float weight )
	{
		weight = 0.0f;

		return Watching() is { } watching && watching.TryGetLayerWeight( node, layer, out weight );
	}

	/// <summary>Which state a machine is in, and what is blending into it.</summary>
	public bool TryGetAnimGraph2StateMachineState( int node,
		out int state, out int from, out int transition, out float progress )
	{
		state = -1;
		from = -1;
		transition = -1;
		progress = 0.0f;

		return Watching() is { } watching && watching.TryGetState( node, out state, out from, out transition, out progress );
	}

	//-- recording -------------------------------------------------------------- Every update the graph makes, kept, so the
	//frame where something went wrong can be found by stepping back to it rather than by trying to make it happen again.

	public bool IsAnimGraph2Recording => Recording?.IsRecording ?? false;

	public bool HasAnimGraph2Recording => ( Recording?.Count ?? 0 ) > 0;

	public bool IsAnimGraph2RecordingPlaybackActive => Recording?.IsPlaying ?? false;

	public int AnimGraph2RecordedUpdateCount => Recording?.Count ?? 0;

	public int AnimGraph2CurrentRecordedUpdate => Recording?.Current ?? 0;

	public void StartAnimGraph2Recording() => Animation.RunningGraph?.Record();

	public void StopAnimGraph2Recording() => Animation.RunningGraph?.StopRecording();

	public void ClearAnimGraph2Recording() => Animation.RunningGraph?.ClearRecording();

	public bool StartAnimGraph2RecordingPlayback() => Recording?.StartPlayback() ?? false;

	public void StopAnimGraph2RecordingPlayback() => Recording?.StopPlayback();

	/// <summary>Put the model back in a recorded update.</summary>
	public bool GoToAnimGraph2RecordedUpdate( int update ) => Animation.RunningGraph?.GoTo( update ) ?? false;

	public bool TryGetAnimGraph2RecordedUpdateInfo( out AnimGraphRecordedUpdateInfo info )
	{
		info = default;

		return Recording is { } recording && recording.TryGetInfo( recording.Current, out info );
	}

	public bool TryGetAnimGraph2RecordedUpdateInfo( int update, out AnimGraphRecordedUpdateInfo info )
	{
		info = default;

		return Recording is { } recording && recording.TryGetInfo( update, out info );
	}

	//Kept with the update because a character that walks does not stand still while it is being stepped through: a recording that put every frame at the origin would be a walk on the spot.
	/// <summary>Where the character was standing on the recorded update being shown.</summary>
	public Transform GetAnimGraph2RecordedCharacterTransform()
		=> Recording is { } recording ? recording.WorldOf( recording.Current ) : Transform.Zero;
}
