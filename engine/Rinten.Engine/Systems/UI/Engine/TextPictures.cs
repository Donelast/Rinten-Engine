namespace Rinten.UI;

//Laid-out text pictures and sizes shared by content key across labels; released ones go to a bounded
//idle list for reuse. Markup is not cached; everything is dropped when fonts change
internal static class TextPictures
{
	//Everything a laid-out block depends on. Style is the block's font hash, which has the text in it too; the text is here as well so two strings
	//can never share an answer by a hash alone. Mipped is a world panel's picture, which carries its smaller levels and so is not a screen label's.
	internal readonly record struct Key( string Text, int Style, bool NoWrap, int Width, int Height, bool Mipped );

	internal sealed class Picture
	{
		public Key Key;
		public Texture Texture;
		public Vector2 BlockSize;
		public bool IsTruncated;
		public int Holders;

		/// <summary>Where it sits in the idle list while nobody holds it.</summary>
		public LinkedListNode<Picture> Idle;

		/// <summary>No longer in the table - fonts changed while it was held. Freed when let go.</summary>
		public bool Dropped;

		public long Bytes => Texture is null ? 0 : (long)Texture.Width * Texture.Height * 4 * ( Texture.Mips > 1 ? 4 : 3 ) / 3;
	}

	[ConVar( "ui_text_cache", Help = "Share laid-out text sizes and pictures between labels showing the same string in the same style; 0 lays out every label on its own" )]
	public static bool Enabled { get; set; } = true;

	//Idle pictures kept at most, and the bytes they may hold.
	const int IdleCeiling = 1024;
	const long IdleBytesCeiling = 24L * 1024 * 1024;

	//Measured sizes kept at most, oldest dropped first.
	const int SizeCeiling = 4096;

	static readonly object Gate = new();
	static readonly Dictionary<Key, Picture> pictures = new();
	static readonly LinkedList<Picture> idle = new();
	static long idleBytes;

	static readonly Dictionary<Key, LinkedListNode<(Key Key, Vector2 Size)>> sizes = new();
	static readonly LinkedList<(Key Key, Vector2 Size)> sizeOrder = new();

	static int fontGeneration = -1;

	//Drops everything if a font came or went since the last look. Under the gate.
	static void CheckFonts()
	{
		var now = Rinten.FontManager.Generation;
		if ( now == fontGeneration ) return;

		fontGeneration = now;

		sizes.Clear();
		sizeOrder.Clear();

		foreach ( var picture in pictures.Values )
		{
			if ( picture.Holders > 0 ) picture.Dropped = true;
		}

		pictures.Clear();

		foreach ( var picture in idle ) Free( picture );

		idle.Clear();
		idleBytes = 0;
	}

	public static bool TryGetSize( in Key key, out Vector2 size )
	{
		lock ( Gate )
		{
			CheckFonts();

			if ( sizes.TryGetValue( key, out var node ) )
			{
				sizeOrder.Remove( node );
				sizeOrder.AddFirst( node );
				size = node.Value.Size;
				return true;
			}

			size = default;
			return false;
		}
	}

	public static void PutSize( in Key key, Vector2 size )
	{
		lock ( Gate )
		{
			CheckFonts();

			if ( sizes.ContainsKey( key ) ) return;

			sizes[key] = sizeOrder.AddFirst( (key, size) );

			while ( sizeOrder.Count > SizeCeiling )
			{
				var last = sizeOrder.Last;
				sizeOrder.RemoveLast();
				sizes.Remove( last.Value.Key );
			}
		}
	}

	/// <summary>The picture for this key, held for the caller, or null.</summary>
	public static Picture Take( in Key key )
	{
		lock ( Gate )
		{
			CheckFonts();

			if ( !pictures.TryGetValue( key, out var picture ) ) return null;

			if ( picture.Idle is not null )
			{
				idle.Remove( picture.Idle );
				picture.Idle = null;
				idleBytes -= picture.Bytes;
			}

			picture.Holders++;
			return picture;
		}
	}

	/// <summary>Takes the oldest idle picture of this exact size for reuse, or null; mipped ones only for mipped</summary>
	public static Texture TakeSpare( int width, int height, bool mipped )
	{
		lock ( Gate )
		{
			var node = idle.Last;

			//Only the older half: the newer is what a list scrolled back asks for, and painting over it gave
			//the hits away - measured, 510 shared pictures a pass fell to 55 when the whole list was open to it.
			for ( int left = idle.Count / 2; node is not null && left > 0; left--, node = node.Previous )
			{
				var picture = node.Value;
				var texture = picture.Texture;

				if ( texture is null || texture.Width != width || texture.Height != height || ( texture.Mips > 1 ) != mipped ) continue;

				idle.Remove( node );
				picture.Idle = null;
				idleBytes -= picture.Bytes;
				Unlist( picture );
				picture.Texture = null;

				return texture;
			}

			return null;
		}
	}

	/// <summary>Enters a freshly painted picture, held once by the caller.</summary>
	public static Picture Add( in Key key, Texture texture, Vector2 blockSize, bool truncated )
	{
		lock ( Gate )
		{
			CheckFonts();

			var picture = new Picture { Key = key, Texture = texture, BlockSize = blockSize, IsTruncated = truncated, Holders = 1 };

			//Two labels that missed the same key in the same frame: the later one is the one kept, the other stays its own holder's and goes when let go.
			if ( pictures.TryGetValue( key, out var was ) )
			{
				if ( was.Holders > 0 ) was.Dropped = true;
				else
				{
					if ( was.Idle is not null ) { idle.Remove( was.Idle ); was.Idle = null; idleBytes -= was.Bytes; }
					Free( was );
				}
			}

			pictures[key] = picture;
			return picture;
		}
	}

	/// <summary>Lets go of a held picture. The last to let go leaves it idle, or frees it if it was dropped.</summary>
	public static void Release( Picture picture )
	{
		if ( picture is null ) return;

		lock ( Gate )
		{
			if ( --picture.Holders > 0 ) return;

			if ( picture.Dropped || !Enabled )
			{
				if ( !picture.Dropped ) Unlist( picture );
				Free( picture );
				return;
			}

			picture.Idle = idle.AddFirst( picture );
			idleBytes += picture.Bytes;

			while ( idle.Count > IdleCeiling || idleBytes > IdleBytesCeiling )
			{
				var oldest = idle.Last.Value;
				idle.RemoveLast();
				oldest.Idle = null;
				idleBytes -= oldest.Bytes;
				Unlist( oldest );
				Free( oldest );
			}
		}
	}

	//Out of the table, if it is the one the table has under its key.
	static void Unlist( Picture picture )
	{
		if ( pictures.TryGetValue( picture.Key, out var listed ) && listed == picture ) pictures.Remove( picture.Key );
	}

	static void Free( Picture picture )
	{
		if ( picture.Texture is null ) return;

		TextStats.Freed( picture.Bytes );
		picture.Texture.Dispose();
		picture.Texture = null;
	}

	/// <summary>Pictures in the table and idle ones, for the counters.</summary>
	public static (int Pictures, int Idle, long IdleBytes, int Sizes) Counts()
	{
		lock ( Gate ) return (pictures.Count, idle.Count, idleBytes, sizes.Count);
	}
}
