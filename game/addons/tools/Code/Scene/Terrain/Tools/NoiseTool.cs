namespace Editor.TerrainEditor;

//Ridged multifractal plus fractal noise with domain warp, added as height in the brush area
/// <summary>Click and drag to grow mountain relief out of the ground - ridges and the flanks between them - the way the horizonclub terrain generator makes it.<br/> <br/> <b>Ctrl</b> - take it away again</summary>
[Title( "Noise" )]
[Icon( "landscape" )]
[Alias( "tools.terrain.noise" )]
[Group( "1" )]
[Order( 0 )]
public class NoiseTool : BaseBrushTool
{
	//Metres a crest rises by in a second at full opacity.
	const float Rate = 6.0f;

	//Features across the terrain at the largest octave - the generator's SynthFrequency.
	const float Frequency = 4.0f;

	//The generator's SynthOctaves, SynthLacunarity, SynthGain, SynthWarp and SynthRidgeMix.
	const int Octaves = 7;
	const float Lacunarity = 2.05f;
	const float Gain = 0.5f;
	const float Warp = 0.35f;
	const float RidgeMix = 0.8f;

	const long Seed = 1337;

	public NoiseTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
		AllowBrushInvert = true;
	}

	protected override void OnDab( Terrain terrain, TerrainMesh mesh, Vector3 centre, float radius, float amount, Vector3 viewDirection )
	{
		var size = MathF.Max( terrain.Storage?.TerrainSize ?? 500.0f, 1.0f );
		var metres = amount * Rate;

		mesh.Displace( centre, radius, Vector3.Up, at => Relief( at.x / size, at.z / size ) * metres );
	}

	//The relief at a point of the terrain's plane, in terrain widths: nought to one.
	static float Relief( float nx, float ny )
	{
		//Domain warp so ridgelines are curved, not grid-aligned.
		float wx = nx, wy = ny;
		float off = Warp * 0.5f;
		wx += off * Fbm( Seed + 40151L, nx * Frequency * 0.5f, ny * Frequency * 0.5f, 4 );
		wy += off * Fbm( Seed + 71317L, ( nx * Frequency * 0.5f ) + 5.2f, ( ny * Frequency * 0.5f ) + 1.7f, 4 );

		float ridge = RidgedMultifractal( Seed, wx * Frequency, wy * Frequency, Octaves );
		float hills = Fbm( Seed + 90007L, wx * Frequency, wy * Frequency, Octaves );

		return ( ridge * RidgeMix ) + ( hills * ( 1.0f - RidgeMix ) );
	}

	//Ridged multifractal: sharp reinforced ridges, alpine peaks. About nought to one.
	static float RidgedMultifractal( long seed, float x, float y, int octaves )
	{
		float sum = 0f, amp = 0.5f, freq = 1f, weight = 1f, norm = 0f;

		for ( int o = 0; o < octaves; o++ )
		{
			float n = OpenSimplex2S.Noise2( ( seed + ( o * 7919L ) ) & 0x7FFFFFFFFFFFFFFFL, x * freq, y * freq );
			n = Math.Clamp( n, -1f, 1f );

			float r = 1f - MathF.Abs( n );
			r *= r;                                   //sharpen the ridge crest
			r *= weight;                              //multifractal: high ground reinforces detail
			weight = Math.Clamp( r * 2f, 0f, 1f );

			sum += r * amp;
			norm += amp;
			amp *= Gain;
			freq *= Lacunarity;
		}

		return norm > 1e-6f ? sum / norm : 0f;
	}

	//Plain fractional Brownian motion, nought to one.
	static float Fbm( long seed, float x, float y, int octaves )
	{
		float sum = 0f, amp = 1f, freq = 1f, norm = 0f;

		for ( int o = 0; o < octaves; o++ )
		{
			float n = OpenSimplex2S.Noise2( ( seed + ( o * 6151L ) ) & 0x7FFFFFFFFFFFFFFFL, x * freq, y * freq );
			sum += Math.Clamp( n, -1f, 1f ) * amp;
			norm += amp;
			amp *= Gain;
			freq *= Lacunarity;
		}

		float v = norm > 1e-6f ? sum / norm : 0f;
		return ( v * 0.5f ) + 0.5f;
	}
}
