using System.Text.Json.Serialization;

namespace Rinten;

public partial class Sprite
{
	/// <summary>The different loop modes for sprite animation.</summary>
	public enum LoopMode
	{
		/// <summary>The animation will play from start to finish and then stop.</summary>
		[Icon( "not_interested" )]
		None,

		/// <summary>The animation will play from start to finish and then loop back to the start.</summary>
		[Icon( "loop" )]
		Loop,

		/// <summary>The animation will play from start to finish and then backwards from finish to start before looping.</summary>
		[Icon( "sync_alt" )]
		PingPong
	}

	public enum BroadcastEventType
	{
		/// <summary>A custom string will be broadcast by invoking an Action on the SpriteRenderer</summary>
		[Icon( "message" )]
		CustomMessage,

		/// <summary>Plays a sound from the sprite's position</summary>
		[Icon( "volume_up" )]
		PlaySound,

		/// <summary>Spawns a prefab at the sprite's position</summary>
		[Icon( "library_add" )]
		SpawnPrefab
	}

	/// <summary>Describes a single animation frame</summary>
	public class Frame
	{
		[KeyProperty]
		public Texture Texture { get; set; } = Texture.Transparent;

		/// <summary>The part of <see cref="Texture"/> this frame is, in pixels from its top left - one cell of a sheet or an atlas. Empty is the whole picture.</summary>
		public RectInt Region { get; set; }

		//Where Region lies in the picture as texture coordinates: the top left corner and the size, the whole of it for an empty region or no picture.
		internal Vector4 RegionUv
		{
			get
			{
				var region = Region;
				if ( region.Width <= 0 || region.Height <= 0 || Texture is not { Width: > 0, Height: > 0 } picture )
					return new Vector4( 0, 0, 1, 1 );

				float width = picture.Width, height = picture.Height;
				return new Vector4( region.Left / width, region.Top / height, region.Width / width, region.Height / height );
			}
		}

		/// <summary>The frame as its own texture (copied out of the sheet if a cell), cached; used for thumbnails/UI</summary>
		public Texture ToTexture()
		{
			var picture = Texture;
			var region = Region;

			if ( region.Width <= 0 || region.Height <= 0 || picture is not { Width: > 0, Height: > 0 } ) return picture;
			if ( _cut is not null && _cutFrom == picture && _cutRegion == region ) return _cut;

			var left = Math.Clamp( region.Left, 0, picture.Width );
			var top = Math.Clamp( region.Top, 0, picture.Height );
			var width = Math.Clamp( region.Width, 1, picture.Width - left );
			var height = Math.Clamp( region.Height, 1, picture.Height - top );

			var pixels = new Color32[width * height];
			picture.GetPixels( (left, top, width, height), 0, 0, pixels.AsSpan(), ImageFormat.RGBA8888 );

			_cut = Texture.Create( width, height ).WithFormat( ImageFormat.RGBA8888 ).WithData( (ReadOnlySpan<Color32>)pixels ).Finish();
			_cutFrom = picture;
			_cutRegion = region;
			return _cut;
		}

		private Texture _cut;
		private Texture _cutFrom;
		private RectInt _cutRegion;

		/// <summary>The frame's size in pixels: its region's, or the whole picture's.</summary>
		[JsonIgnore, Hide]
		public Vector2 PixelSize
			=> Region is { Width: > 0, Height: > 0 } region
				? new Vector2( region.Width, region.Height )
				: new Vector2( Texture?.Width ?? 1, Texture?.Height ?? 1 );

		public List<BroadcastEvent> BroadcastMessages { get; set; } = new();
	}

	/// <summary>A message that is broadcast when a frame is displayed.</summary>
	public class BroadcastEvent
	{
		[KeyProperty, EnumDropdown]
		public BroadcastEventType Type { get; set; } = BroadcastEventType.CustomMessage;

		[KeyProperty, ShowIf( nameof( Type ), BroadcastEventType.CustomMessage )]
		public string Message { get; set; }

		[KeyProperty, ShowIf( nameof( Type ), BroadcastEventType.PlaySound )]
		public SoundEvent Sound { get; set; }

		[KeyProperty, ShowIf( nameof( Type ), BroadcastEventType.SpawnPrefab )]
		public GameObject Prefab { get; set; }

		public override string ToString()
		{
			switch ( Type )
			{
				case BroadcastEventType.PlaySound:
					return Sound?.ResourceName ?? "no sound";
				case BroadcastEventType.SpawnPrefab:
					return Prefab?.Name ?? "no prefab";
				default:
					return Message;
			}
		}
	}

	/// <summary>Contains one or multiple frames that can be played in sequence.</summary>
	public class Animation
	{
		/// <summary>The name of the animation. Allows you to play specific animations by name.</summary>
		public string Name { get; set; } = "Default";

		/// <summary>The speed of the animation in frames per second.</summary>
		[ShowIf( nameof( IsAnimated ), true )]
		public float FrameRate { get; set; } = 15.0f;

		/// <summary>The point at which the rendered sprite is anchored from. This means scaling/rotating a sprite will do so around the origin.</summary>
		[Range( 0, 1 )]
		public Vector2 Origin { get; set; } = new Vector2( 0.5f, 0.5f );

		/// <summary>The loop mode of the animation. This determines what should happen when the animation reaches the final frame in playback.</summary>
		[ShowIf( nameof( IsAnimated ), true )]
		public LoopMode LoopMode { get; set; } = LoopMode.Loop;

		/// <summary>The frame index at which looping starts. A value of -1 means the first frame (0).</summary>
		[Hide]
		public int LoopStart { get; set; } = -1;

		/// <summary>The frame index at which looping ends. A value of -1 means the last frame.</summary>
		[Hide]
		public int LoopEnd { get; set; } = -1;

		/// <summary>Returns the effective loop start frame index, resolving -1 to 0.</summary>
		[Hide, JsonIgnore]
		public int EffectiveLoopStart => LoopStart < 0 ? 0 : Math.Clamp( LoopStart, 0, Math.Max( 0, (Frames?.Count ?? 1) - 1 ) );

		/// <summary>Returns the effective loop end frame index, resolving -1 to the last frame.</summary>
		[Hide, JsonIgnore]
		public int EffectiveLoopEnd => LoopEnd < 0 ? Math.Max( 0, (Frames?.Count ?? 1) - 1 ) : Math.Clamp( LoopEnd, 0, Math.Max( 0, (Frames?.Count ?? 1) - 1 ) );

		/// <summary>The frames that make up the animation, shown in sequence. Each is a picture, or one cell of a sheet.</summary>
		[Group( "Frames", StartFolded = true ), WideMode( HasLabel = false )]
		public List<Frame> Frames { get; set; } = new();

		/// <summary>True if we have more than one frame</summary>
		[Hide, JsonIgnore]
		public bool IsAnimated => Frames?.Count > 1;
	}
}
