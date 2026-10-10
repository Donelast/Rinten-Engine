namespace Rinten
{
	internal class TokenBasedTagSet : ITagSet
	{
		private HashSet<uint> Tags { get; set; } = new();

		public override void Add( string tag )
		{
			Tags.Add( StringToken.FindOrCreate( tag ) );
		}

		public override IEnumerable<string> TryGetAll()
		{
			foreach ( var k in Tags )
			{
				if ( StringToken.TryLookup( k, out var tag ) )
					yield return tag;
			}
		}

		/// <summary>Try to get all tags in the set.</summary>
		public override IReadOnlySet<uint> GetTokens()
		{
			return Tags;
		}

		public override bool Has( string tag )
		{
			return Tags.Contains( StringToken.FindOrCreate( tag ) );
		}

		public override void Remove( string tag )
		{
			Tags.Remove( StringToken.FindOrCreate( tag ) );
		}

		public override void RemoveAll()
		{
			Tags.Clear();
		}
	}
}
