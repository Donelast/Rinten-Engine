using System.Collections.Generic;

namespace Rinten;

public partial class Terrain
{
	public struct TerrainMaterialInfo
	{
		/// <summary>The layer painted heaviest here, and the next.</summary>
		public int BaseTextureId { get; set; }
		public int OverlayTextureId { get; set; }

		/// <summary>How much of the second there is against the first, nought to one.</summary>
		public float BlendFactor { get; set; }

		public bool IsHole { get; set; }

		public Material BaseMaterial { get; set; }
		public Material OverlayMaterial { get; set; }

		public Material GetDominantMaterial() => BlendFactor < 0.5f ? BaseMaterial : OverlayMaterial;
		public int GetDominantMaterialIndex() => BlendFactor < 0.5f ? BaseTextureId : OverlayTextureId;
	}

	private readonly List<int> queried = new();

	/// <summary>What the ground is painted with nearest a point of the world: the two heaviest layers of the nearest vertex. Null where there is no ground near.</summary>
	public TerrainMaterialInfo? GetMaterialAtWorldPosition( Vector3 worldPosition )
	{
		if ( Storage?.Mesh is not { } mesh ) return null;

		var local = WorldTransform.PointToLocal( worldPosition );
		var reach = MathF.Max( Storage.EdgeLength * 2.0f, 0.5f );

		queried.Clear();
		mesh.VerticesInSphere( local, reach, queried );

		var nearest = -1;
		var best = float.MaxValue;

		foreach ( var v in queried )
		{
			var distance = ( mesh.PositionOf( v ) - local ).LengthSquared;
			if ( distance >= best ) continue;

			best = distance;
			nearest = v;
		}

		if ( nearest < 0 ) return new TerrainMaterialInfo { IsHole = true };

		var vertex = mesh.GetVertex( nearest );

		System.Span<float> weights =
		[
			vertex.Layers.r / 255.0f, vertex.Layers.g / 255.0f, vertex.Layers.b / 255.0f, vertex.Layers.a / 255.0f,
			vertex.Layers2.x, vertex.Layers2.y, vertex.Layers2.z, vertex.Layers2.w,
		];

		var first = 0;
		var second = -1;

		for ( var i = 1; i < weights.Length; i++ )
		{
			if ( weights[i] > weights[first] ) { second = first; first = i; }
			else if ( second < 0 || weights[i] > weights[second] ) second = i;
		}

		if ( second < 0 || weights[second] <= 0.0f ) second = first;

		var total = weights[first] + ( second == first ? 0.0f : weights[second] );

		var result = new TerrainMaterialInfo
		{
			BaseTextureId = first,
			OverlayTextureId = second,
			BlendFactor = second == first || total <= 0.0f ? 0.0f : weights[second] / total,
		};

		if ( first < Storage.Materials.Count ) result.BaseMaterial = Storage.Materials[first];
		if ( second < Storage.Materials.Count ) result.OverlayMaterial = Storage.Materials[second];

		return result;
	}
}
