using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Rinten.CoreAnimation;

using Quat = System.Numerics.Quaternion;

//Writes compiled animation clips (reader is AnimClipData, sharing its disk structs); bones by name
internal sealed class AnimClipBuilder : IDisposable
{
	/// <summary>Ten floats a frame: position xyz, rotation xyzw, scale xyz.</summary>
	public const int FloatsPerFrame = 10;

	private const uint Magic = 0x41535345;
	private const uint Version = 3;

	private const uint FlagLooping = 1u << 0;
	private const uint FlagAdditive = 1u << 1;

	/// <summary>Matches anim_format.h's EventKind and the graph's own.</summary>
	public enum EventKind
	{
		Generic = 0,
		Foot = 1,
		Sync = 2,
		Curve = 3,
		Transition = 4,
	}

	private sealed class BoneEntry
	{
		public string Name = "";
		public int Parent = -1;

		public float[] Frames = Array.Empty<float>();
		public int FrameCount;
	}

	private sealed class EventEntry
	{
		public string Id = "";
		public uint Kind = (uint)EventKind.Generic;

		public float Start;
		public float End;

		public int Data;

		/// <summary>Curve events only.</summary>
		public float[] Curve;
	}

	private readonly List<BoneEntry> bones = new();
	private readonly List<EventEntry> events = new();

	public string Name { get; set; } = "";

	public float FrameRate { get; private set; } = 30.0f;

	public bool Looping { get; set; }
	public bool Additive { get; set; }

	//An animation with a run-up that plays once and a cycle that plays for ever after - their citizen's breathing settles over thirty frames and loops what follows.
	/// <summary>The frame a loop goes back to, which is not always the first.</summary>
	public int LoopStart { get; set; }

	/// <summary>How many frames the clip has. Set by the first bone given more than one, and every later bone has to agree.</summary>
	public int FrameCount { get; private set; }

	public int BoneCount => bones.Count;

	/// <summary>Lets go of the frames the builder was gathering - what sbc_anim_builder_destroy was. Nothing has to call it.</summary>
	public void Dispose()
	{
		bones.Clear();
		events.Clear();
	}

	public void SetFrameRate( float rate )
	{
		if ( rate <= 0.0f ) return;
		FrameRate = rate;
	}

	/// <summary>Blends both ends of a clip toward each other over ramps so it loops without a jolt at the seam</summary>
	public bool SmoothLoop( int fromStart, int fromEnd )
	{
		if ( FrameCount < 3 ) return false;

		var head = Math.Clamp( fromStart, 0, FrameCount / 2 );
		var tail = Math.Clamp( fromEnd, 0, FrameCount / 2 );

		if ( head <= 0 && tail <= 0 ) return false;

		foreach ( var bone in bones )
		{
			if ( bone.FrameCount < 3 ) continue;

			var last = bone.FrameCount - 1;

			var start = Frame( bone, 0 );
			var end = Frame( bone, last );

			//Where the two ends agree to meet.
			var middle = Lerp( start, end, 0.5f );

			for ( var k = 0; k <= head && k <= last; ++k )
			{
				var amount = head <= 0 ? 0.0f : 1.0f - ( (float)k / head );
				if ( amount <= 0.0f ) continue;

				//Toward the meeting point by as much as the frame at the very edge is moving, falling off as the clip goes on.
				Write( bone, k, Lerp( Frame( bone, k ), Add( Frame( bone, k ), Delta( start, middle ) ), amount ) );
			}

			for ( var j = 0; j <= tail && j <= last; ++j )
			{
				var amount = tail <= 0 ? 0.0f : 1.0f - ( (float)j / tail );
				if ( amount <= 0.0f ) continue;

				var index = last - j;

				Write( bone, index, Lerp( Frame( bone, index ), Add( Frame( bone, index ), Delta( end, middle ) ), amount ) );
			}
		}

		return true;
	}

	private static AnimTrs Frame( BoneEntry bone, int frame )
	{
		var at = Math.Clamp( frame, 0, bone.FrameCount - 1 ) * FloatsPerFrame;

		return new AnimTrs
		{
			Position = new Vector3( bone.Frames[at], bone.Frames[at + 1], bone.Frames[at + 2] ),
			Rotation = new Quat( bone.Frames[at + 3], bone.Frames[at + 4], bone.Frames[at + 5], bone.Frames[at + 6] ),
			Scale = new Vector3( bone.Frames[at + 7], bone.Frames[at + 8], bone.Frames[at + 9] ),
		};
	}

	private static void Write( BoneEntry bone, int frame, in AnimTrs trs )
	{
		if ( frame < 0 || frame >= bone.FrameCount ) return;

		var at = frame * FloatsPerFrame;

		bone.Frames[at + 0] = trs.Position.x;
		bone.Frames[at + 1] = trs.Position.y;
		bone.Frames[at + 2] = trs.Position.z;

		bone.Frames[at + 3] = trs.Rotation.X;
		bone.Frames[at + 4] = trs.Rotation.Y;
		bone.Frames[at + 5] = trs.Rotation.Z;
		bone.Frames[at + 6] = trs.Rotation.W;

		bone.Frames[at + 7] = trs.Scale.x;
		bone.Frames[at + 8] = trs.Scale.y;
		bone.Frames[at + 9] = trs.Scale.z;
	}

	//What has to be added to from to arrive at to.
	private static AnimTrs Delta( in AnimTrs from, in AnimTrs to )
	{
		return new AnimTrs
		{
			Position = to.Position - from.Position,
			Rotation = NormalisedQuat( to.Rotation ) * Quat.Conjugate( NormalisedQuat( from.Rotation ) ),
			Scale = new Vector3(
				Divided( to.Scale.x, from.Scale.x ),
				Divided( to.Scale.y, from.Scale.y ),
				Divided( to.Scale.z, from.Scale.z ) ),
		};
	}

	private static AnimTrs Add( in AnimTrs trs, in AnimTrs by )
	{
		return new AnimTrs
		{
			Position = trs.Position + by.Position,
			Rotation = NormalisedQuat( NormalisedQuat( by.Rotation ) * NormalisedQuat( trs.Rotation ) ),
			Scale = new Vector3( trs.Scale.x * by.Scale.x, trs.Scale.y * by.Scale.y, trs.Scale.z * by.Scale.z ),
		};
	}

	private static AnimTrs Lerp( in AnimTrs from, in AnimTrs to, float amount )
		=> AnimTrs.Blend( from, to, amount );

	//The translation of one bone on one frame, or nothing where the clip has no such bone.
	private bool TranslationOf( string bone, int frame, out Vector3 where )
	{
		where = default;

		var at = Named( bone );
		if ( at < 0 ) return false;

		var entry = bones[at];
		if ( entry.FrameCount <= 0 ) return false;

		var index = Math.Clamp( frame, 0, entry.FrameCount - 1 ) * FloatsPerFrame;

		where = new Vector3( entry.Frames[index], entry.Frames[index + 1], entry.Frames[index + 2] );

		return true;
	}

	//A bone by name, or the root where no name was given.
	private int Named( string bone )
	{
		if ( string.IsNullOrWhiteSpace( bone ) )
		{
			for ( var index = 0; index < bones.Count; ++index )
			{
				if ( bones[index].Parent < 0 ) return index;
			}

			return -1;
		}

		for ( var index = 0; index < bones.Count; ++index )
		{
			if ( string.Equals( bones[index].Name, bone, StringComparison.OrdinalIgnoreCase ) ) return index;
		}

		return -1;
	}

	/// <summary>Slides the whole clip so one of its frames stands where another clip stands, on the axes asked for.</summary>
	public bool LineUpWith( AnimClipBuilder other, string bone, int frame, int theirFrame, bool across, bool up )
	{
		if ( other is null || ( !across && !up ) ) return false;

		if ( !TranslationOf( bone, frame, out var mine ) ) return false;
		if ( !other.TranslationOf( bone, theirFrame, out var theirs ) ) return false;

		var at = Named( bone );
		if ( at < 0 ) return false;

		var upward = Vector3.Up;

		Span<bool> moved = stackalloc bool[3];

		moved[0] = upward.x != 0.0f ? up : across;
		moved[1] = upward.y != 0.0f ? up : across;
		moved[2] = upward.z != 0.0f ? up : across;

		Span<float> shift = stackalloc float[3];

		shift[0] = theirs.x - mine.x;
		shift[1] = theirs.y - mine.y;
		shift[2] = theirs.z - mine.z;

		var entry = bones[at];

		for ( var component = 0; component < 3; ++component )
		{
			if ( !moved[component] || shift[component] == 0.0f ) continue;

			for ( var index = 0; index < entry.FrameCount; ++index )
				entry.Frames[index * FloatsPerFrame + component] += shift[component];
		}

		return true;
	}

	/// <summary>Holds a bone still where it started, so a clip that was authored travelling plays on the spot.</summary>
	public bool HoldStill( string bone, bool across, bool up )
	{
		if ( !across && !up ) return false;
		if ( FrameCount <= 0 ) return false;

		//Their document leaves the name empty for "whatever the root is".
		var at = Named( bone );
		if ( at < 0 ) return false;

		var measured = bones[at];
		if ( measured.FrameCount <= 1 ) return true;

		//Which of the three the ground is, and which is up, read off the engine's own up rather than assumed.
		var upward = Vector3.Up;

		Span<bool> held = stackalloc bool[3];

		held[0] = upward.x != 0.0f ? up : across;
		held[1] = upward.y != 0.0f ? up : across;
		held[2] = upward.z != 0.0f ? up : across;

		//Strip travel from every root bone, not just the named one; IK roots carry the same travel
		foreach ( var root in bones )
		{
			if ( root.Parent >= 0 || root.FrameCount <= 1 ) continue;

			for ( var component = 0; component < 3; ++component )
			{
				if ( !held[component] ) continue;

				var start = measured.Frames[component];

				for ( var frame = 1; frame < root.FrameCount; ++frame )
				{
					//How far the measured bone had gone by this frame, which is what every root loses
					//- so a root that also moves on its own keeps that much and only the travel goes.
					var gone = measured.Frames[( Math.Min( frame, measured.FrameCount - 1 ) * FloatsPerFrame ) + component] - start;

					root.Frames[( frame * FloatsPerFrame ) + component] -= gone;
				}
			}
		}

		return true;
	}

	/// <summary>Keeps the run of frames between <paramref name="first"/> and <paramref name="last"/> and throws the rest away.</summary>
	public void Trim( int first, int last )
	{
		if ( FrameCount <= 0 ) return;

		var from = first < 0 ? 0 : Math.Min( first, FrameCount - 1 );
		var to = last < 0 ? FrameCount - 1 : Math.Min( last, FrameCount - 1 );

		if ( to < from ) to = from;
		if ( from == 0 && to == FrameCount - 1 ) return;

		var kept = to - from + 1;

		foreach ( var bone in bones )
		{
			//A bone with one frame holds still for the whole clip, whichever part of it is being kept.
			if ( bone.FrameCount <= 1 ) continue;

			var start = Math.Min( from, bone.FrameCount - 1 );
			var count = Math.Min( kept, bone.FrameCount - start );

			var frames = new float[count * FloatsPerFrame];
			Array.Copy( bone.Frames, start * FloatsPerFrame, frames, 0, frames.Length );

			bone.Frames = frames;
			bone.FrameCount = count;
		}

		FrameCount = kept;

		//The frame a loop goes back to is counted from the start of the clip, and the start of the clip has just moved.
		if ( LoopStart > 0 ) LoopStart = Math.Clamp( LoopStart - from, 0, kept - 1 );

		//What is marked along the clip moves with it, and anything marked outside the
		//slice was marked on a part of the animation this clip is no longer made of.
		for ( var at = events.Count - 1; at >= 0; --at )
		{
			var mark = events[at];

			if ( mark.Start < from || mark.Start > to ) { events.RemoveAt( at ); continue; }

			mark.Start -= from;
			if ( mark.End >= 0.0f ) mark.End = Math.Max( 0.0f, mark.End - from );
		}
	}

	/// <summary>Rotates the whole clip about Vector3.Up via root bones (e.g. diagonal walks from a straight one)</summary>
	public bool TurnAbove( float degrees )
	{
		if ( MathF.Abs( degrees ) < 1e-4f ) return false;

		var turn = Quat.CreateFromAxisAngle(
			new System.Numerics.Vector3( Vector3.Up.x, Vector3.Up.y, Vector3.Up.z ),
			degrees * MathF.PI / 180.0f );

		foreach ( var bone in bones )
		{
			if ( bone.Parent >= 0 || bone.FrameCount <= 0 ) continue;

			for ( var frameAt = 0; frameAt < bone.FrameCount; ++frameAt )
			{
				var at = frameAt * FloatsPerFrame;

				var where = System.Numerics.Vector3.Transform(
					new System.Numerics.Vector3( bone.Frames[at + 0], bone.Frames[at + 1], bone.Frames[at + 2] ), turn );

				bone.Frames[at + 0] = where.X;
				bone.Frames[at + 1] = where.Y;
				bone.Frames[at + 2] = where.Z;

				var facing = Normalised( turn * Normalised( new Quat(
					bone.Frames[at + 3], bone.Frames[at + 4], bone.Frames[at + 5], bone.Frames[at + 6] ) ) );

				bone.Frames[at + 3] = facing.X;
				bone.Frames[at + 4] = facing.Y;
				bone.Frames[at + 5] = facing.Z;
				bone.Frames[at + 6] = facing.W;
			}
		}

		return true;
	}

	/// <summary>Lays a difference back onto every frame of this clip, which is the exact undoing of <see cref="Subtract"/>.</summary>
	public void AddLayer( AnimClipBuilder reference )
	{
		if ( reference is null || reference.FrameCount <= 0 ) return;

		foreach ( var bone in bones )
		{
			if ( bone.FrameCount <= 0 ) continue;

			var over = reference.Named( bone.Name );
			if ( over < 0 ) continue;

			var laid = reference.bones[over];
			if ( laid.FrameCount <= 0 ) continue;

			for ( var frameAt = 0; frameAt < bone.FrameCount; ++frameAt )
			{
				var at = frameAt * FloatsPerFrame;
				var from = Math.Min( frameAt, laid.FrameCount - 1 ) * FloatsPerFrame;

				bone.Frames[at + 0] += laid.Frames[from + 0];
				bone.Frames[at + 1] += laid.Frames[from + 1];
				bone.Frames[at + 2] += laid.Frames[from + 2];

				var turn = Normalised( new Quat(
					laid.Frames[from + 3], laid.Frames[from + 4], laid.Frames[from + 5], laid.Frames[from + 6] ) )
					* Normalised( new Quat(
						bone.Frames[at + 3], bone.Frames[at + 4], bone.Frames[at + 5], bone.Frames[at + 6] ) );

				turn = Normalised( turn );

				bone.Frames[at + 3] = turn.X;
				bone.Frames[at + 4] = turn.Y;
				bone.Frames[at + 5] = turn.Z;
				bone.Frames[at + 6] = turn.W;

				bone.Frames[at + 7] *= laid.Frames[from + 7];
				bone.Frames[at + 8] *= laid.Frames[from + 8];
				bone.Frames[at + 9] *= laid.Frames[from + 9];
			}
		}
	}

	/// <summary>Subtracts a reference frame per channel, leaving an additive difference; bones matched by name</summary>
	public void Subtract( AnimClipBuilder reference, int frame, bool modelSpace )
	{
		if ( reference is null || frame < 0 ) return;

		var against = reference.FrameAt( frame, modelSpace );

		if ( modelSpace )
		{
			//Compose to model space first; PoseAdditiveModel applies the layer on model-space bones
			for ( var frameAt = 0; frameAt < FrameCount; ++frameAt ) Compose( frameAt );
		}

		foreach ( var bone in bones )
		{
			if ( bone.FrameCount <= 0 ) continue;
			if ( !against.TryGetValue( bone.Name, out var held ) ) continue;

			var undo = Quat.Conjugate( Normalised( new Quat( held[3], held[4], held[5], held[6] ) ) );

			for ( var frameAt = 0; frameAt < bone.FrameCount; ++frameAt )
			{
				var at = frameAt * FloatsPerFrame;

				bone.Frames[at + 0] -= held[0];
				bone.Frames[at + 1] -= held[1];
				bone.Frames[at + 2] -= held[2];

				var turn = undo * Normalised( new Quat(
					bone.Frames[at + 3], bone.Frames[at + 4], bone.Frames[at + 5], bone.Frames[at + 6] ) );

				bone.Frames[at + 3] = turn.X;
				bone.Frames[at + 4] = turn.Y;
				bone.Frames[at + 5] = turn.Z;
				bone.Frames[at + 6] = turn.W;

				bone.Frames[at + 7] = Divided( bone.Frames[at + 7], held[7] );
				bone.Frames[at + 8] = Divided( bone.Frames[at + 8], held[8] );
				bone.Frames[at + 9] = Divided( bone.Frames[at + 9], held[9] );
			}
		}
	}

	private static System.Numerics.Quaternion Normalised( System.Numerics.Quaternion q )
		=> NormalisedQuat( q );

	//One frame of every bone by name, composed down the hierarchy where asked.
	private Dictionary<string, float[]> FrameAt( int frame, bool modelSpace )
	{
		var held = new Dictionary<string, float[]>( System.StringComparer.OrdinalIgnoreCase );

		var model = new float[bones.Count][];

		for ( var i = 0; i < bones.Count; ++i )
		{
			var bone = bones[i];
			if ( bone.FrameCount <= 0 || string.IsNullOrEmpty( bone.Name ) ) continue;

			var at = Math.Min( frame, bone.FrameCount - 1 ) * FloatsPerFrame;

			var one = new float[FloatsPerFrame];
			Array.Copy( bone.Frames, at, one, 0, FloatsPerFrame );

			if ( modelSpace && bone.Parent >= 0 && model[bone.Parent] is { } above )
				one = Composed( one, above );

			model[i] = one;
			held[bone.Name] = one;
		}

		return held;
	}

	//Composes every bone of one frame down the hierarchy, in place.
	private void Compose( int frame )
	{
		var model = new float[bones.Count][];

		for ( var i = 0; i < bones.Count; ++i )
		{
			var bone = bones[i];
			if ( frame >= bone.FrameCount ) continue;

			var at = frame * FloatsPerFrame;

			var one = new float[FloatsPerFrame];
			Array.Copy( bone.Frames, at, one, 0, FloatsPerFrame );

			if ( bone.Parent >= 0 && model[bone.Parent] is { } above )
				one = Composed( one, above );

			model[i] = one;

			Array.Copy( one, 0, bone.Frames, at, FloatsPerFrame );
		}
	}

	//A local transform put through the one above it: AnimTrs.Then, on floats.
	private static float[] Composed( float[] local, float[] parent )
	{
		var turn = Normalised( new Quat( parent[3], parent[4], parent[5], parent[6] ) );

		var scaled = new System.Numerics.Vector3(
			local[0] * parent[7], local[1] * parent[8], local[2] * parent[9] );

		var placed = System.Numerics.Vector3.Transform( scaled, turn );

		var mine = Normalised( new Quat( local[3], local[4], local[5], local[6] ) );
		var composed = turn * mine;

		return new[]
		{
			placed.X + parent[0], placed.Y + parent[1], placed.Z + parent[2],
			composed.X, composed.Y, composed.Z, composed.W,
			local[7] * parent[7], local[8] * parent[8], local[9] * parent[9],
		};
	}

	private static Quat NormalisedQuat( Quat q )
	{
		var length = MathF.Sqrt( ( q.X * q.X ) + ( q.Y * q.Y ) + ( q.Z * q.Z ) + ( q.W * q.W ) );

		return length > 1e-8f
			? new Quat( q.X / length, q.Y / length, q.Z / length, q.W / length )
			: Quat.Identity;
	}

	private static float Divided( float value, float by ) => MathF.Abs( by ) < 1e-8f ? value : value / by;

	public int AddBone( string name, int parent )
	{
		var index = bones.Count;

		//A parent has to already be in the list, so a reader can walk the skeleton in order and never look forward.
		if ( parent >= index ) return -1;

		bones.Add( new BoneEntry { Name = name ?? "", Parent = parent < 0 ? -1 : parent } );
		return index;
	}

	public string BoneName( int bone ) => bone >= 0 && bone < bones.Count ? bones[bone].Name : "";
	public int BoneParent( int bone ) => bone >= 0 && bone < bones.Count ? bones[bone].Parent : -1;

	/// <summary>One bone's frames, ten floats each. Fails when they do not line up with the clip's own length.</summary>
	public bool SetFrames( int bone, ReadOnlySpan<float> trs, int frameCount )
	{
		if ( frameCount <= 0 ) return false;
		if ( bone < 0 || bone >= bones.Count ) return false;
		if ( trs.Length < frameCount * FloatsPerFrame ) return false;

		//One frame is a bone that holds still, which any clip may have. Anything else has to be the clip's own
		//length - frames that do not line up are not a shorter animation, they are two animations in one file.
		if ( frameCount > 1 )
		{
			if ( FrameCount == 0 ) FrameCount = frameCount;
			else if ( FrameCount != frameCount ) return false;
		}

		var entry = bones[bone];

		entry.Frames = trs[..( frameCount * FloatsPerFrame )].ToArray();
		entry.FrameCount = frameCount;

		return true;
	}

	//The packing lives here because the layout does. Every caller writing ten floats by hand was a copy of this loop and a chance to put the quaternion's w in the wrong place.
	/// <summary>The same, from the runtime's own transforms - which is what everything making a clip actually holds.</summary>
	public bool SetFrames( int bone, ReadOnlySpan<AnimTrs> frames )
	{
		if ( frames.IsEmpty ) return false;

		var flat = new float[frames.Length * FloatsPerFrame];

		for ( var frame = 0; frame < frames.Length; ++frame )
		{
			var at = frame * FloatsPerFrame;
			ref readonly var trs = ref frames[frame];

			flat[at + 0] = trs.Position.x;
			flat[at + 1] = trs.Position.y;
			flat[at + 2] = trs.Position.z;

			flat[at + 3] = trs.Rotation.X;
			flat[at + 4] = trs.Rotation.Y;
			flat[at + 5] = trs.Rotation.Z;
			flat[at + 6] = trs.Rotation.W;

			flat[at + 7] = trs.Scale.x;
			flat[at + 8] = trs.Scale.y;
			flat[at + 9] = trs.Scale.z;
		}

		return SetFrames( bone, flat, frames.Length );
	}

	private static void Rotate( ReadOnlySpan<float> q, Span<float> v )
	{
		var tx = 2.0f * ( ( q[1] * v[2] ) - ( q[2] * v[1] ) );
		var ty = 2.0f * ( ( q[2] * v[0] ) - ( q[0] * v[2] ) );
		var tz = 2.0f * ( ( q[0] * v[1] ) - ( q[1] * v[0] ) );

		var x = v[0] + ( q[3] * tx ) + ( ( q[1] * tz ) - ( q[2] * ty ) );
		var y = v[1] + ( q[3] * ty ) + ( ( q[2] * tx ) - ( q[0] * tz ) );
		var z = v[2] + ( q[3] * tz ) + ( ( q[0] * ty ) - ( q[1] * tx ) );

		v[0] = x; v[1] = y; v[2] = z;
	}

	/// <summary>Moves the whole clip: scale on every bone, and a pose applied to the roots.</summary>
	public void ApplyTransform( Vector3? position, Rotation? rotation, Vector3? scale )
	{
		var turn = rotation ?? Rotation.Identity;
		var o = position ?? Vector3.Zero;
		var s = scale ?? new Vector3( 1, 1, 1 );

		var q = NormalisedQuat( new Quat( turn.x, turn.y, turn.z, turn.w ) );

		Span<float> quat = [q.X, q.Y, q.Z, q.W];

		foreach ( var bone in bones )
		{
			var root = bone.Parent < 0;

			for ( var f = 0; f < bone.FrameCount; ++f )
			{
				var p = bone.Frames.AsSpan( f * FloatsPerFrame, FloatsPerFrame );

				p[0] *= s.x;
				p[1] *= s.y;
				p[2] *= s.z;

				if ( !root ) continue;

				Rotate( quat, p );

				p[0] += o.x;
				p[1] += o.y;
				p[2] += o.z;

				//And which way the root faces, not only where it stands. Everything below it hangs off this turn, so without it a clip put on a model that was
				//turned keeps facing the way the art was drawn: the character walks sideways to itself, and the mesh and the pose disagree about where forward is.
				var facing = NormalisedQuat( q * new Quat( p[3], p[4], p[5], p[6] ) );

				p[3] = facing.X;
				p[4] = facing.Y;
				p[5] = facing.Z;
				p[6] = facing.W;
			}
		}
	}

	private static bool EndsWith( string name, string suffix )
		=> name.EndsWith( suffix, StringComparison.OrdinalIgnoreCase );

	/// <summary>Mirrors the clip through an axis, optionally swapping the left and right bones' names so the reversed pose lands on the right joints.</summary>
	public void Mirror( int axis, bool remapLeftRight )
	{
		var a = Math.Clamp( axis, 0, 2 );

		foreach ( var bone in bones )
		{
			for ( var f = 0; f < bone.FrameCount; ++f )
			{
				var p = bone.Frames.AsSpan( f * FloatsPerFrame, FloatsPerFrame );

				p[a] = -p[a];
				p[3 + a] = -p[3 + a];
			}

			if ( !remapLeftRight ) continue;

			if ( EndsWith( bone.Name, "_L" ) ) bone.Name = bone.Name[..^2] + "_R";
			else if ( EndsWith( bone.Name, "_R" ) ) bone.Name = bone.Name[..^2] + "_L";
			else if ( EndsWith( bone.Name, ".l" ) ) bone.Name = bone.Name[..^2] + ".r";
			else if ( EndsWith( bone.Name, ".r" ) ) bone.Name = bone.Name[..^2] + ".l";
		}
	}

	/// <summary>Marks something on the clip and returns its index.</summary>
	public int AddEvent( string id, int kind, float start, float end, int data )
	{
		events.Add( new EventEntry
		{
			Id = id ?? "",
			Kind = (uint)kind,
			Start = start,

			//An event that ends before it starts is a moment, and a moment a graph can ask "how far
			//through" about is a span - so the caller's start stands and the end is pulled up to it.
			End = end < start ? start : end,
			Data = data,
		} );

		return events.Count - 1;
	}

	/// <summary>The keys of a curve event, which turns it into one.</summary>
	public bool SetEventCurve( int index, ReadOnlySpan<float> values )
	{
		if ( values.IsEmpty ) return false;
		if ( index < 0 || index >= events.Count ) return false;

		events[index].Curve = values.ToArray();
		events[index].Kind = (uint)EventKind.Curve;

		return true;
	}

	//-- writing ------------------------------------------------------------

	//Collects strings into one blob and hands out offsets, sharing repeats.
	private sealed class TextBlock
	{
		private readonly List<byte> bytes = [0];
		private readonly Dictionary<string, uint> seen = new( StringComparer.Ordinal );

		public ReadOnlySpan<byte> Bytes => CollectionsMarshal.AsSpan( bytes );

		public uint Add( string value )
		{
			if ( string.IsNullOrEmpty( value ) ) return 0;
			if ( seen.TryGetValue( value, out var found ) ) return found;

			var at = (uint)bytes.Count;

			bytes.AddRange( Encoding.UTF8.GetBytes( value ) );
			bytes.Add( 0 );

			seen[value] = at;
			return at;
		}
	}

	//Appends a block to the file, aligned, and says where it went.
	private static AnimClipData.DiskBlock Append<T>( List<byte> file, ReadOnlySpan<T> data ) where T : unmanaged
	{
		if ( data.IsEmpty ) return default;

		while ( file.Count % 8 != 0 ) file.Add( 0 );

		var block = new AnimClipData.DiskBlock( (uint)file.Count, (uint)data.Length );

		file.AddRange( MemoryMarshal.AsBytes( data ) );

		return block;
	}

	//Recounts a float block as keys of the given component count; tracks index by key
	private static AnimClipData.DiskBlock Keys( AnimClipData.DiskBlock block, uint components )
		=> new( block.Offset, block.Count / components );

	//Whether every frame of a channel holds the same value, near enough that keeping them all would only be storing the exporter's rounding.
	//A micrometre for a position, and the same number on a quaternion component - which at unit length is about a five-thousandth of a degree.
	private static bool IsConstant( float[] frames, int frameCount, int offsetInFrame, int components )
	{
		const float epsilon = 1e-6f;

		for ( var frame = 1; frame < frameCount; ++frame )
		{
			for ( var c = 0; c < components; ++c )
			{
				var first = frames[offsetInFrame + c];
				var here = frames[( frame * FloatsPerFrame ) + offsetInFrame + c];

				if ( MathF.Abs( here - first ) > epsilon ) return false;
			}
		}

		return true;
	}

	//Copies one channel into its own array; constant channels still write one key, since zero keys
	//means rest pose, which breaks additive playback
	private static AnimClipData.DiskKeys AppendChannel( List<float> into, BoneEntry bone,
		int offsetInFrame, int components )
	{
		if ( bone.FrameCount == 0 ) return default;

		var constant = IsConstant( bone.Frames, bone.FrameCount, offsetInFrame, components );

		var keys = constant ? 1 : bone.FrameCount;

		var where = new AnimClipData.DiskKeys( (uint)( into.Count / components ), (uint)keys );

		for ( var key = 0; key < keys; ++key )
		{
			var at = ( key * FloatsPerFrame ) + offsetInFrame;
			for ( var c = 0; c < components; ++c ) into.Add( bone.Frames[at + c] );
		}

		return where;
	}

	/// <summary>Writes the clip out. False when there is nothing to write - which is the importer having found nothing, and worth failing over rather than writing a file that plays as a freeze.</summary>
	public bool Save( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return false;
		if ( bones.Count == 0 ) return false;

		var frameCount = FrameCount > 0 ? (uint)FrameCount : 1u;

		var text = new TextBlock();

		var diskBones = new AnimClipData.DiskBone[bones.Count];
		var tracks = new AnimClipData.DiskTrack[bones.Count];

		List<float> translations = [], rotations = [], scales = [], curves = [];
		var diskEvents = new AnimClipData.DiskEvent[events.Count];

		for ( var i = 0; i < bones.Count; ++i )
		{
			var bone = bones[i];

			diskBones[i] = new AnimClipData.DiskBone( text.Add( bone.Name ), bone.Parent );

			tracks[i] = new AnimClipData.DiskTrack(
				AppendChannel( translations, bone, 0, 3 ),
				AppendChannel( rotations, bone, 3, 4 ),
				AppendChannel( scales, bone, 7, 3 ) );
		}

		for ( var i = 0; i < events.Count; ++i )
		{
			var entry = events[i];

			var curve = default( AnimClipData.DiskKeys );

			if ( entry.Curve is { Length: > 0 } )
			{
				curve = new AnimClipData.DiskKeys( (uint)curves.Count, (uint)entry.Curve.Length );
				curves.AddRange( entry.Curve );
			}

			diskEvents[i] = new AnimClipData.DiskEvent( text.Add( entry.Id ), entry.Kind,
				entry.Start, entry.End, entry.Data, 0, curve );
		}

		var header = default( AnimClipData.DiskHeader );
		var file = new List<byte>( new byte[Marshal.SizeOf<AnimClipData.DiskHeader>()] );

		//Add the clip name to text before the text block is written, or its offset points past the block
		var name = text.Add( Name );

		var textBlock = Append( file, text.Bytes );
		var bonesBlock = Append<AnimClipData.DiskBone>( file, diskBones );
		var tracksBlock = Append<AnimClipData.DiskTrack>( file, tracks );

		//Channels are written as the floats they are and counted in keys, which is what a track's indices
		//are in - so the count Append worked out from the array's length is replaced rather than trusted.
		var translationBlock = Keys( Append<float>( file, CollectionsMarshal.AsSpan( translations ) ), 3 );
		var rotationBlock = Keys( Append<float>( file, CollectionsMarshal.AsSpan( rotations ) ), 4 );
		var scaleBlock = Keys( Append<float>( file, CollectionsMarshal.AsSpan( scales ) ), 3 );

		var eventsBlock = Append<AnimClipData.DiskEvent>( file, diskEvents );
		var curvesBlock = Append<float>( file, CollectionsMarshal.AsSpan( curves ) );

		header = new AnimClipData.DiskHeader(
			Magic, Version, (uint)file.Count,
			( Looping ? FlagLooping : 0u ) | ( Additive ? FlagAdditive : 0u ),
			name, frameCount, FrameRate,

			//Seconds from the first frame to the last, so a one-frame clip - a pose - lasts no time at all rather than one frame's worth.
			frameCount > 1 ? ( frameCount - 1 ) / FrameRate : 0.0f,

			(uint)Math.Clamp( LoopStart, 0, frameCount > 0 ? (int)frameCount - 1 : 0 ),

			textBlock, bonesBlock, tracksBlock,
			translationBlock, rotationBlock, scaleBlock,
			eventsBlock, curvesBlock );

		var bytes = CollectionsMarshal.AsSpan( file );
		MemoryMarshal.Write( bytes, in header );

		try
		{
			File.WriteAllBytes( path, bytes );
			return true;
		}
		catch ( IOException )
		{
			return false;
		}
		catch ( UnauthorizedAccessException )
		{
			return false;
		}
	}
}
