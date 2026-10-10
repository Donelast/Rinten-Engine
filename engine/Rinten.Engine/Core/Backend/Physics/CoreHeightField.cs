namespace Rinten;

//Full height grid kept here so edits can patch it and rebuild Box3D's immutable height field
internal sealed unsafe class CoreHeightField
{
	private readonly CorePhysicsBodyBackend body;

	private readonly ushort[] heights;
	private readonly byte[] materials;

	private readonly int sizeX;
	private readonly int sizeY;
	private readonly int materialCount;

	private float sizeScale;
	private float heightScale;

	public CoreHeightField( CorePhysicsBodyBackend body, ushort* heights, byte* materials,
		int sizeX, int sizeY, float sizeScale, float heightScale, int materialCount )
	{
		this.body = body;
		this.sizeX = sizeX;
		this.sizeY = sizeY;
		this.sizeScale = sizeScale;
		this.heightScale = heightScale;
		this.materialCount = materialCount;

		var count = sizeX * sizeY;

		this.heights = new ushort[count];
		for ( var i = 0; i < count; i++ ) this.heights[i] = heights[i];

		this.materials = new byte[count];
		if ( materials is not null )
		{
			for ( var i = 0; i < count; i++ ) this.materials[i] = materials[i];
		}
	}

	/// <summary>Replace a rectangle of the height samples.</summary>
	public void PatchHeights( ushort* source, float sizeScale, float heightScale )
	{
		this.sizeScale = sizeScale;
		this.heightScale = heightScale;

		//Terrain hands over the whole grid for a height edit, not just the region, so there is nothing to index - take the lot.
		for ( var i = 0; i < heights.Length; i++ ) heights[i] = source[i];
	}

	/// <summary>Replace a rectangle of the per-cell materials. Unlike heights, this arrives as just the rectangle.</summary>
	public void PatchMaterials( byte* source, int x, int y, int width, int height )
	{
		for ( var row = 0; row < height; row++ )
		{
			var targetY = y + row;
			if ( targetY < 0 || targetY >= sizeY ) continue;

			for ( var column = 0; column < width; column++ )
			{
				var targetX = x + column;
				if ( targetX < 0 || targetX >= sizeX ) continue;

				materials[targetX + targetY * sizeX] = source[column + row * width];
			}
		}
	}

	/// <summary>Build a fresh native height field from the current grid. The caller swaps it in for the old one.</summary>
	public ulong Build()
	{
		fixed ( ushort* pHeights = heights )
		fixed ( byte* pMaterials = materials )
		{
			return body.CreateHeightFieldShape( pHeights, pMaterials, sizeX, sizeY,
				sizeScale, heightScale, materialCount );
		}
	}
}
