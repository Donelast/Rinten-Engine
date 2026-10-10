namespace Rinten
{
	//Curve: x is metres from the camera, y is detail multiplier; baked into a table the cull reads
	/// <summary>A named detail-over-distance curve picked by name on each renderer (characters, debris)</summary>
	[Expose]
	public class LodGroup
	{
		/// <summary>What a renderer names to wear this curve.</summary>
		public string Name { get; set; } = "";

		/// <summary>Detail by distance: metres from the camera across, detail up - one is the world's own detail, lower is coarser.</summary>
		[Title( "Detail By Distance" )]
		public Curve Detail { get; set; } = new Curve( new Curve.Frame( 0.0f, 1.0f ), new Curve.Frame( 1.0f, 0.25f ) )
		{
			TimeRange = new Vector2( 0.0f, 100.0f ),
			ValueRange = new Vector2( 0.0f, 1.0f ),
		};

		/// <summary>The names the project's groups go by, for a picker - see <see cref="LodGroupNameAttribute"/>.</summary>
		public static IReadOnlyList<string> Names
		{
			get
			{
				RenderingSettings.EnsureApplied();
				return Rendering.LodGroups.Names;
			}
		}
	}

	/// <summary>On a string property: the name of one of the project's <see cref="LodGroup"/>s, picked from a list in the inspector.</summary>
	[AttributeUsage( AttributeTargets.Property )]
	public sealed class LodGroupNameAttribute : Attribute
	{
	}
}

namespace Rinten.Rendering
{
	//LOD group curves baked into fixed sample rows for the cull; uploaded each frame with cull params
	//Instances store row+1 (0 is none); scenes re-resolve names when Generation changes
	internal static class LodGroups
	{
		//How many groups a project may have. mesh_tables.slang says the same number.
		internal const int Most = 16;

		//Samples a curve is baked to. mesh_tables.slang says the same number.
		internal const int Samples = 32;

		//The table's size in words: the samples, then a float4 a group.
		internal const int Words = ( Most * Samples ) + ( Most * 4 );

		//The table as the cull reads it - see the class.
		internal static readonly float[] Table = new float[Words];

		//Moves whenever the groups are set, so the scenes look their instances' groups up again.
		internal static int Generation { get; private set; }

		//The groups' names in row order.
		internal static IReadOnlyList<string> Names => names;

		private static string[] names = [];
		private static readonly Dictionary<string, uint> rows = new( StringComparer.OrdinalIgnoreCase );

		static LodGroups() => Set( null );

		//The group a renderer names, as the word its record carries: its row plus one, nought for none.
		internal static uint Find( string name )
		{
			if ( string.IsNullOrWhiteSpace( name ) ) return 0;

			return rows.TryGetValue( name.Trim(), out var row ) ? row : 0;
		}

		//Bakes the project's groups into the table - see RenderingSettings.Apply.
		internal static void Set( IReadOnlyList<LodGroup> groups )
		{
			rows.Clear();
			var named = new List<string>();

			//Every row at one first: a record left naming a row nobody fills until its scene regroups reads the world's own detail.
			Array.Fill( Table, 1.0f );

			for ( var g = 0; g < Most; g++ )
			{
				var span = ( Most * Samples ) + ( g * 4 );
				Table[span] = 0.0f;
				Table[span + 1] = 1.0f;
				Table[span + 2] = 0.0f;
				Table[span + 3] = 0.0f;
			}

			var count = 0;

			foreach ( var group in groups ?? [] )
			{
				if ( group is null || string.IsNullOrWhiteSpace( group.Name ) ) continue;

				var name = group.Name.Trim();
				if ( rows.ContainsKey( name ) ) continue;

				if ( count == Most )
				{
					Log.Warning( $"Rendering: only {Most} level-of-detail groups are read, \"{name}\" and those after it are ignored" );
					break;
				}

				Bake( count, group.Detail );

				rows[name] = (uint)( count + 1 );
				named.Add( name );
				count++;
			}

			names = named.ToArray();
			Generation++;
		}

		private static void Bake( int row, Curve curve )
		{
			var from = curve.TimeRange.x;
			var length = curve.TimeRange.y - from;
			if ( !( length > 0.001f ) ) length = 0.001f;

			var empty = curve.Frames.IsDefaultOrEmpty;

			for ( var k = 0; k < Samples; k++ )
			{
				var detail = empty ? 1.0f : curve.Evaluate( from + ( length * k / ( Samples - 1 ) ) );
				if ( !float.IsFinite( detail ) ) detail = 1.0f;

				//Detail is how fine; the cut wants how much error it may allow.
				Table[( row * Samples ) + k] = 1.0f / Math.Clamp( detail, 0.01f, 100.0f );
			}

			var span = ( Most * Samples ) + ( row * 4 );
			Table[span] = from;
			Table[span + 1] = 1.0f / length;
		}
	}
}
