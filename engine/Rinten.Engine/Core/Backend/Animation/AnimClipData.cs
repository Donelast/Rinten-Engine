using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Rinten.CoreAnimation;

//Reads and samples compiled animation clips (writer is AnimClipBuilder); bones matched by name
internal sealed class AnimClipData
{
	//"ESSA", little-endian.
	private const uint Magic = 0x41535345;

	//The one layout this reader understands, same as the writer's.
	private const uint Version = 3;

	private const uint FlagLooping = 1u << 0;
	private const uint FlagAdditive = 1u << 1;

	public string Name = "";
	public float Duration;
	public float FrameRate;
	public int FrameCount;
	public bool Looping;
	public bool Additive;

	/// <summary>Their loop-start frame, kept but unused: their clips actually loop cleanly at frame zero</summary>
	public int LoopStart;

	public readonly record struct Bone( string Name, int Parent );

	/// <summary>Where one channel's keys live: an index into the channel's own array and a count. One key means the channel never changes; zero means it was not authored and the bound skeleton's rest value stands.</summary>
	public readonly record struct Keys( uint First, uint Count );

	public readonly record struct Track( Keys Translation, Keys Rotation, Keys Scale );

	public readonly record struct Event( string Id, int Kind, float Start, float End, int Data,
		uint CurveFirst, uint CurveCount );

	public Bone[] Bones = Array.Empty<Bone>();
	public Track[] Tracks = Array.Empty<Track>();

	public float[] Translations = Array.Empty<float>();
	public float[] Rotations = Array.Empty<float>();
	public float[] Scales = Array.Empty<float>();

	public Event[] Events = Array.Empty<Event>();
	public float[] Curves = Array.Empty<float>();

	//-- the disk layout, exactly as anim_format.h writes it --------------------

	[StructLayout( LayoutKind.Sequential )]
	internal readonly struct DiskBlock
	{
		public readonly uint Offset;
		public readonly uint Count;

		public DiskBlock( uint offset, uint count ) { Offset = offset; Count = count; }
	}

	[StructLayout( LayoutKind.Sequential )]
	internal readonly struct DiskKeys
	{
		public readonly uint First;
		public readonly uint Count;

		public DiskKeys( uint first, uint count ) { First = first; Count = count; }
	}

	[StructLayout( LayoutKind.Sequential )]
	internal readonly struct DiskBone
	{
		public readonly uint Name;
		public readonly int Parent;

		public DiskBone( uint name, int parent ) { Name = name; Parent = parent; }
	}

	[StructLayout( LayoutKind.Sequential )]
	internal readonly struct DiskTrack
	{
		public readonly DiskKeys Translation;
		public readonly DiskKeys Rotation;
		public readonly DiskKeys Scale;

		public DiskTrack( DiskKeys translation, DiskKeys rotation, DiskKeys scale )
		{
			Translation = translation;
			Rotation = rotation;
			Scale = scale;
		}
	}

	[StructLayout( LayoutKind.Sequential )]
	internal readonly struct DiskEvent
	{
		public readonly uint Id;
		public readonly uint Kind;
		public readonly float Start;
		public readonly float End;
		public readonly int Data;
		public readonly uint Padding;
		public readonly DiskKeys Curve;

		public DiskEvent( uint id, uint kind, float start, float end, int data, uint padding, DiskKeys curve )
		{
			Id = id;
			Kind = kind;
			Start = start;
			End = end;
			Data = data;
			Padding = padding;
			Curve = curve;
		}
	}

	[StructLayout( LayoutKind.Sequential )]
	internal readonly struct DiskHeader
	{
		public readonly uint Magic;
		public readonly uint Version;
		public readonly uint ByteCount;
		public readonly uint Flags;

		public readonly uint Name;
		public readonly uint FrameCount;

		public readonly float FrameRate;
		public readonly float Duration;

		/// <summary>The frame a loop goes back to - see <see cref="AnimClipData.LoopStart"/>.</summary>
		public readonly uint LoopStart;

		public readonly DiskBlock Text;
		public readonly DiskBlock Bones;
		public readonly DiskBlock Tracks;
		public readonly DiskBlock Translations;
		public readonly DiskBlock Rotations;
		public readonly DiskBlock Scales;
		public readonly DiskBlock Events;
		public readonly DiskBlock Curves;

		public DiskHeader( uint magic, uint version, uint byteCount, uint flags,
			uint name, uint frameCount, float frameRate, float duration, uint loopStart,
			DiskBlock text, DiskBlock bones, DiskBlock tracks,
			DiskBlock translations, DiskBlock rotations, DiskBlock scales,
			DiskBlock events, DiskBlock curves )
		{
			Magic = magic;
			Version = version;
			ByteCount = byteCount;
			Flags = flags;
			Name = name;
			FrameCount = frameCount;
			FrameRate = frameRate;
			Duration = duration;
			LoopStart = loopStart;
			Text = text;
			Bones = bones;
			Tracks = tracks;
			Translations = translations;
			Rotations = rotations;
			Scales = scales;
			Events = events;
			Curves = curves;
		}
	}

	/// <summary>Reads a compiled clip, or null when the file is missing, not a clip, or fails any of the bounds checks a hostile or truncated file would fail.</summary>
	public static AnimClipData Load( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;

		byte[] bytes;

		try
		{
			bytes = SealedFile.ReadAllBytes( path );
		}
		catch ( IOException ) { return null; }
		catch ( UnauthorizedAccessException ) { return null; }

		return Parse( bytes );
	}

	public static unsafe AnimClipData Parse( ReadOnlySpan<byte> bytes )
	{
		if ( bytes.Length < sizeof( DiskHeader ) ) return null;

		var header = MemoryMarshal.Read<DiskHeader>( bytes );

		if ( header.Magic != Magic ) return null;
		if ( header.Version != Version ) return null;
		if ( header.ByteCount != bytes.Length ) return null;

		//Every block has to sit inside the file. A short read otherwise becomes an index past the end that only breaks once something samples it.
		if ( !Fits( bytes, header.Text, 1 ) ) return null;
		if ( !Fits( bytes, header.Bones, sizeof( DiskBone ) ) ) return null;
		if ( !Fits( bytes, header.Tracks, sizeof( DiskTrack ) ) ) return null;
		if ( !Fits( bytes, header.Translations, sizeof( float ) * 3 ) ) return null;
		if ( !Fits( bytes, header.Rotations, sizeof( float ) * 4 ) ) return null;
		if ( !Fits( bytes, header.Scales, sizeof( float ) * 3 ) ) return null;
		if ( !Fits( bytes, header.Events, sizeof( DiskEvent ) ) ) return null;
		if ( !Fits( bytes, header.Curves, sizeof( float ) ) ) return null;

		//One track per bone, or a bone would read another's keys.
		if ( header.Tracks.Count != header.Bones.Count ) return null;

		var text = header.Text.Count > 0
			? bytes.Slice( (int)header.Text.Offset, (int)header.Text.Count )
			: ReadOnlySpan<byte>.Empty;

		if ( header.Name >= header.Text.Count && header.Name != 0 ) return null;

		var diskBones = MemoryMarshal.Cast<byte, DiskBone>(
			bytes.Slice( (int)header.Bones.Offset, (int)header.Bones.Count * sizeof( DiskBone ) ) );

		for ( var bone = 0; bone < diskBones.Length; ++bone )
		{
			if ( diskBones[bone].Name >= header.Text.Count && diskBones[bone].Name != 0 ) return null;

			//Parents point backwards, so a reader can walk the skeleton in order.
			if ( diskBones[bone].Parent >= bone ) return null;
		}

		var diskTracks = MemoryMarshal.Cast<byte, DiskTrack>(
			bytes.Slice( (int)header.Tracks.Offset, (int)header.Tracks.Count * sizeof( DiskTrack ) ) );

		for ( var bone = 0; bone < diskTracks.Length; ++bone )
		{
			if ( !KeysFit( diskTracks[bone].Translation, header.Translations.Count, header.FrameCount ) ) return null;
			if ( !KeysFit( diskTracks[bone].Rotation, header.Rotations.Count, header.FrameCount ) ) return null;
			if ( !KeysFit( diskTracks[bone].Scale, header.Scales.Count, header.FrameCount ) ) return null;
		}

		var clip = new AnimClipData
		{
			Name = ReadText( text, header.Name ),
			Duration = header.Duration,
			FrameRate = header.FrameRate,
			FrameCount = (int)header.FrameCount,
			Looping = ( header.Flags & FlagLooping ) != 0,
			Additive = ( header.Flags & FlagAdditive ) != 0,

			//Held inside the clip rather than worked out from it: which frame a cycle starts over at is the author's answer and nothing about the keys says it.
			LoopStart = (int)Math.Min( header.LoopStart, header.FrameCount ),
		};

		clip.Bones = new Bone[diskBones.Length];

		for ( var bone = 0; bone < diskBones.Length; ++bone )
			clip.Bones[bone] = new Bone( ReadText( text, diskBones[bone].Name ), diskBones[bone].Parent );

		clip.Tracks = new Track[diskTracks.Length];

		for ( var bone = 0; bone < diskTracks.Length; ++bone )
		{
			clip.Tracks[bone] = new Track(
				new Keys( diskTracks[bone].Translation.First, diskTracks[bone].Translation.Count ),
				new Keys( diskTracks[bone].Rotation.First, diskTracks[bone].Rotation.Count ),
				new Keys( diskTracks[bone].Scale.First, diskTracks[bone].Scale.Count ) );
		}

		clip.Translations = ReadFloats( bytes, header.Translations, 3 );
		clip.Rotations = ReadFloats( bytes, header.Rotations, 4 );
		clip.Scales = ReadFloats( bytes, header.Scales, 3 );
		clip.Curves = ReadFloats( bytes, header.Curves, 1 );

		if ( header.Events.Count > 0 )
		{
			var diskEvents = MemoryMarshal.Cast<byte, DiskEvent>(
				bytes.Slice( (int)header.Events.Offset, (int)header.Events.Count * sizeof( DiskEvent ) ) );

			clip.Events = new Event[diskEvents.Length];

			for ( var at = 0; at < diskEvents.Length; ++at )
			{
				ref readonly var e = ref diskEvents[at];

				if ( e.Id >= header.Text.Count && e.Id != 0 ) return null;

				clip.Events[at] = new Event( ReadText( text, e.Id ), (int)e.Kind, e.Start, e.End, e.Data,
					e.Curve.First, e.Curve.Count );
			}
		}

		return clip;
	}

	private static unsafe bool Fits( ReadOnlySpan<byte> bytes, in DiskBlock block, int stride )
	{
		if ( block.Count == 0 ) return true;
		if ( block.Offset > (uint)bytes.Length ) return false;

		return ( (ulong)block.Count * (ulong)stride ) <= (ulong)( bytes.Length - (int)block.Offset );
	}

	private static bool KeysFit( in DiskKeys keys, uint available, uint frameCount )
	{
		//A channel holds nothing, one key for the whole clip, or a key a frame. Anything else and the
		//sampler would index past its end, since it reaches for the frame it wants rather than searching.
		if ( keys.Count != 0 && keys.Count != 1 && keys.Count != frameCount ) return false;

		return keys.Count == 0 || ( (ulong)keys.First + keys.Count ) <= available;
	}

	private static string ReadText( ReadOnlySpan<byte> text, uint at )
	{
		if ( at == 0 || at >= (uint)text.Length ) return "";

		var slice = text[(int)at..];
		var end = slice.IndexOf( (byte)0 );

		return Encoding.UTF8.GetString( end < 0 ? slice : slice[..end] );
	}

	private static float[] ReadFloats( ReadOnlySpan<byte> bytes, in DiskBlock block, int components )
	{
		if ( block.Count == 0 ) return Array.Empty<float>();

		//Blocks of keys are counted in keys; a key is `components` floats.
		var floats = (int)block.Count * components;

		return MemoryMarshal.Cast<byte, float>(
			bytes.Slice( (int)block.Offset, floats * sizeof( float ) ) ).ToArray();
	}

	//-- sampling ----------------------------------------------------------------

	//Frame pair and blend for a time; loops wrap (last frame repeats first), others hold the last frame
	private void FramesAt( float time, bool loop, out uint first, out uint second, out float alpha )
	{
		first = second = 0;
		alpha = 0.0f;

		if ( FrameCount <= 1 || Duration <= 0.0f ) return;

		var at = time;

		if ( loop )
		{
			at %= Duration;
			if ( at < 0.0f ) at += Duration;
		}
		else
		{
			if ( at < 0.0f ) at = 0.0f;
			if ( at > Duration ) at = Duration;
		}

		var frame = at * FrameRate;

		var whole = MathF.Floor( frame );
		alpha = frame - whole;

		if ( whole < 0.0f ) whole = 0.0f;

		first = (uint)whole;

		if ( first >= (uint)FrameCount - 1 )
		{
			first = (uint)FrameCount - 1;
			second = first;
			alpha = 0.0f;
			return;
		}

		second = first + 1;
	}

	private System.Numerics.Vector3 ReadVec3( float[] keys, in Keys where, uint frame )
	{
		var key = where.Count == 1 ? 0u : frame;
		var at = (int)( ( where.First + key ) * 3 );

		return new System.Numerics.Vector3( keys[at], keys[at + 1], keys[at + 2] );
	}

	private System.Numerics.Quaternion ReadQuat( float[] keys, in Keys where, uint frame )
	{
		var key = where.Count == 1 ? 0u : frame;
		var at = (int)( ( where.First + key ) * 4 );

		return new System.Numerics.Quaternion( keys[at], keys[at + 1], keys[at + 2], keys[at + 3] );
	}

	//Nearest-arc normalised lerp, same as the pose blend and for the same reasons.
	private static System.Numerics.Quaternion BlendQuat(
		System.Numerics.Quaternion a, System.Numerics.Quaternion b, float alpha )
	{
		var dot = System.Numerics.Quaternion.Dot( a, b );
		if ( dot < 0.0f ) b = -b;

		var blended = new System.Numerics.Quaternion(
			a.X + ( ( b.X - a.X ) * alpha ),
			a.Y + ( ( b.Y - a.Y ) * alpha ),
			a.Z + ( ( b.Z - a.Z ) * alpha ),
			a.W + ( ( b.W - a.W ) * alpha ) );

		return AnimTrs.NormaliseQuat( blended );
	}

	//A channel the clip does not animate leaves rest values behind - zero position, identity rotation, unit scale - which is what the caller composes over its own bind pose.
	/// <summary>The pose at a time, one transform per clip bone, into the front of <paramref name="into"/>. Returns how many were written.</summary>
	public int Sample( float time, bool loop, Span<AnimTrs> into )
	{
		var bones = Math.Min( Bones.Length, into.Length );

		FramesAt( time, loop, out var first, out var second, out var alpha );

		for ( var bone = 0; bone < bones; ++bone )
		{
			ref readonly var track = ref Tracks[bone];
			ref var @out = ref into[bone];

			if ( track.Translation.Count == 0 )
			{
				@out.Position = System.Numerics.Vector3.Zero;
			}
			else
			{
				var a = ReadVec3( Translations, track.Translation, first );
				var b = ReadVec3( Translations, track.Translation, second );

				@out.Position = System.Numerics.Vector3.Lerp( a, b, alpha );
			}

			if ( track.Rotation.Count == 0 )
			{
				@out.Rotation = System.Numerics.Quaternion.Identity;
			}
			else
			{
				var a = ReadQuat( Rotations, track.Rotation, first );
				var b = ReadQuat( Rotations, track.Rotation, second );

				@out.Rotation = BlendQuat( a, b, alpha );
			}

			if ( track.Scale.Count == 0 )
			{
				@out.Scale = System.Numerics.Vector3.One;
			}
			else
			{
				var a = ReadVec3( Scales, track.Scale, first );
				var b = ReadVec3( Scales, track.Scale, second );

				@out.Scale = System.Numerics.Vector3.Lerp( a, b, alpha );
			}
		}

		return bones;
	}

	/// <summary>A curve event's value part way through itself. The keys are spread evenly across the event, so where in it we are is where in the keys we are.</summary>
	public float EventCurve( int index, float fraction )
	{
		if ( index < 0 || index >= Events.Length ) return 0.0f;

		ref readonly var e = ref Events[index];
		if ( e.CurveCount == 0 || Curves.Length == 0 ) return 0.0f;

		var at = Math.Clamp( fraction, 0.0f, 1.0f );

		if ( e.CurveCount == 1 ) return Curves[e.CurveFirst];

		var position = at * ( e.CurveCount - 1 );

		var first = (uint)position;
		var second = first + 1 < e.CurveCount ? first + 1 : first;

		var alpha = position - first;

		var a = Curves[e.CurveFirst + first];
		var b = Curves[e.CurveFirst + second];

		return a + ( ( b - a ) * alpha );
	}
}
