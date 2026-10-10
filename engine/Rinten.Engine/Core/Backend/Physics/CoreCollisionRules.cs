namespace Rinten;

//Compiles tag-based CollisionRules into a tag-bit table; the least colliding pair wins
internal sealed unsafe class CoreCollisionRules
{
	//Must match SBC_MAX_COLLISION_TAGS.
	private const int MaxTags = 64;

	private static readonly Logger log = new( "CorePhysics" );

	//Tag token to bit index. Only tags the rules mention get one.
	private readonly Dictionary<uint, int> indices = new();

	private readonly byte[] matrix = new byte[MaxTags * MaxTags];

	/// <summary>Tags that appear in a trigger rule, as a mask.</summary>
	public ulong TriggerTags { get; private set; }

	/// <summary>Compile the rules, or null if there is nothing to filter.</summary>
	public static CoreCollisionRules Compile( CollisionRules rules )
	{
		if ( rules is null ) return null;

		var tags = rules.Tags?.ToArray();
		if ( tags is null || tags.Length == 0 ) return null;

		var compiled = new CoreCollisionRules();

		foreach ( var tag in tags )
		{
			if ( compiled.indices.Count >= MaxTags )
			{
				//Past this the table has nowhere to put them. Say so once rather than filtering by a rule set that quietly lost entries.
				log.Warning( $"collision rules use more than {MaxTags} tags; '{tag}' and any after it will always collide" );
				break;
			}

			compiled.indices[StringToken.FindOrCreate( tag )] = compiled.indices.Count;
		}

		//The core wants a resolved answer for every pair, so the defaults and the unset-means-collide rule are applied here rather than crossing the ABI.
		foreach ( var (leftToken, left) in compiled.indices )
		{
			foreach ( var (rightToken, right) in compiled.indices )
			{
				var result = rules.GetCollisionRule( StringToken.GetValue( leftToken ), StringToken.GetValue( rightToken ) );

				compiled.matrix[left * MaxTags + right] = result switch
				{
					CollisionRules.Result.Ignore => 3,
					CollisionRules.Result.Trigger => 2,
					_ => 1,
				};

				if ( result == CollisionRules.Result.Trigger )
					compiled.TriggerTags |= 1ul << left;
			}
		}

		return compiled;
	}

	/// <summary>The bit for one tag, or none for a tag with no rule about it.</summary>
	public ulong BitFor( uint tag ) => indices.TryGetValue( tag, out var index ) ? 1ul << index : 0;

	/// <summary>The bits for a set of tags. Tags with no rule about them get none.</summary>
	public ulong MaskFor( IReadOnlyCollection<uint> tags )
	{
		if ( tags is null || tags.Count == 0 ) return 0;

		ulong mask = 0;
		foreach ( var tag in tags )
		{
			if ( indices.TryGetValue( tag, out var index ) )
				mask |= 1ul << index;
		}

		return mask;
	}

	/// <summary>What a pair resolves to - <see cref="Resolve(byte*, ulong, ulong)"/>. Larger is less colliding.</summary>
	public const byte Collide = 1;
	public const byte Trigger = 2;
	public const byte Ignore = 3;

	/// <summary>The least colliding rule across both tag masks; Collide if either has no known tags. Unmanaged</summary>
	public static byte Resolve( byte* table, ulong maskA, ulong maskB )
	{
		if ( table == null || maskA == 0 || maskB == 0 ) return Collide;

		byte worst = Collide;

		for ( var bitsA = maskA; bitsA != 0; bitsA &= bitsA - 1 )
		{
			var row = System.Numerics.BitOperations.TrailingZeroCount( bitsA ) * MaxTags;

			for ( var bitsB = maskB; bitsB != 0; bitsB &= bitsB - 1 )
			{
				var rule = table[row + System.Numerics.BitOperations.TrailingZeroCount( bitsB )];
				if ( rule > worst ) worst = rule;
			}
		}

		return worst;
	}

	/// <summary><see cref="Resolve(byte*, ulong, ulong)"/> against this table.</summary>
	public byte Resolve( ulong maskA, ulong maskB )
	{
		fixed ( byte* table = matrix )
		{
			return Resolve( table, maskA, maskB );
		}
	}

	/// <summary>Bytes in the compiled table: one result for every pair of tag indices.</summary>
	public const int TableSize = MaxTags * MaxTags;

	/// <summary>The table, row-major, for a backend that keeps its own copy.</summary>
	public void CopyTo( Span<byte> destination ) => matrix.AsSpan().CopyTo( destination );

	public void Upload( ulong world )
	{
		CorePhysics.WorldSetCollisionRules( world, matrix );
	}
}
