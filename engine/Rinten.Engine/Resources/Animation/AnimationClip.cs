using Rinten.CoreAnimation;

namespace Rinten;

//Shared between users; playback state lives in whatever animates it
/// <summary>One animation: a pose over time, for whatever skeleton shares its bone names.</summary>
public sealed class AnimationClip : Resource
{
	private readonly AnimClipData data;

	private AnimationClip( AnimClipData data, string name )
	{
		this.data = data;
		Name = name ?? "";
	}

	//The clip itself, for the runtime that samples it.
	internal AnimClipData Data => data;

	/// <summary>What the clip is called - its resource path.</summary>
	public string Name { get; }

	public override bool IsValid => data is not null;

	public override bool IsError => data is null;

	/// <summary>Load a clip by resource path, compiling it first if nobody has.</summary>
	public static AnimationClip Load( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;

		var data = CoreClips.Clip( path );

		return data is null ? null : new AnimationClip( data, path );
	}

	/// <summary>Drop a cached clip so the next load reads what was just compiled.</summary>
	public static void Forget( string path ) => CoreClips.Forget( path );

	/// <summary>Seconds from the first frame to the last.</summary>
	public float Duration => data?.Duration ?? 0.0f;

	/// <summary>What it was authored at, not what the game runs at.</summary>
	public float FrameRate => data?.FrameRate ?? 0.0f;

	public int FrameCount => data?.FrameCount ?? 0;

	/// <summary>Whether the end runs back into the beginning.</summary>
	public bool IsLooping => data is { Looping: true };

	/// <summary>Whether this is a difference from a base pose rather than a pose - what gets added on top of whatever is already playing.</summary>
	public bool IsAdditive => data is { Additive: true };

	/// <summary>How many bones the clip moves.</summary>
	public int TrackCount => data?.Bones.Length ?? 0;

	/// <summary>The name of one of them.</summary>
	public string GetTrackName( int track )
		=> data is not null && track >= 0 && track < data.Bones.Length ? data.Bones[track].Name : "";

	//The clip's own bone order, which is the file's and not the model's - a clip does not know what it will be played on. Match by name.
	/// <summary>Where a bone stands at a time, relative to the bone above it.</summary>
	public Transform GetLocalTransform( float time, int track )
	{
		var bones = TrackCount;
		if ( track < 0 || track >= bones ) return Transform.Zero;

		//A clip is sampled whole - there is no reading one bone out of one - so the whole pose is worked out and one bone of it is
		//returned. This is an editor and script path, asked once for a value on screen; the runtime samples into a buffer it keeps.
		Span<AnimTrs> pose = bones <= 128 ? stackalloc AnimTrs[bones] : new AnimTrs[bones];

		if ( data.Sample( time, IsLooping, pose ) <= track ) return Transform.Zero;

		return pose[track].ToTransform();
	}

	//The root bone's own transform, which is what root motion is before anything decides whether to use it. Nothing here removes it from the pose - that is a decision, and it belongs to whatever is playing this.
	/// <summary>Where the clip has carried the character by a given time, if it carries it anywhere.</summary>
	public unsafe Transform GetRootMotion( float time )
	{
		if ( !IsValid ) return Transform.Zero;

		for ( var track = 0; track < TrackCount; track++ )
		{
			if ( data.Bones[track].Parent >= 0 ) continue;

			return GetLocalTransform( time, track );
		}

		return Transform.Zero;
	}

	/// <summary>Whether this clip loaded and has anything in it to play.</summary>
	public bool IsBound => IsValid && TrackCount > 0;

	public override string ToString() => $"AnimationClip {Name}";
}
