namespace Rinten;

partial class Model
{
	//What our own models can say about themselves: the meshes, their sizes and their bounds. One level of
	//detail, because that is how many they have, and no draw call breakdown, because their draw calls are meshes.
	private ModelMeshInfo OurMeshInfo()
	{
		var count = core is { IsValid: true } ? core.MeshCount : 0;
		var meshes = new ModelMeshInfo.MeshData[count];

		var totalVertices = 0;
		var totalTriangles = 0;

		for ( var i = 0; i < count; i++ )
		{
			var range = core.MeshRange( i );
			var triangles = range.IndexCount / 3;

			totalVertices += range.VertexCount;
			totalTriangles += triangles;

			meshes[i] = new ModelMeshInfo.MeshData
			{
				Name = core.MeshName( i ),
				Vertices = range.VertexCount,
				Triangles = triangles,
				DrawCalls = System.Array.Empty<ModelMeshInfo.DrawCallData>(),
				BoundsMin = MeshBounds( i ).Mins,
				BoundsMax = MeshBounds( i ).Maxs,
			};
		}

		return new ModelMeshInfo
		{
			TotalVertices = totalVertices,
			TotalTriangles = totalTriangles,
			TotalDrawCalls = count,
			Meshes = meshes,
		};
	}

	/// <summary>Contains all mesh and draw call information for a model.</summary>
	public class ModelMeshInfo
	{
		internal ModelMeshInfo() { }

		public int TotalVertices { get; init; }
		public int TotalTriangles { get; init; }
		public int TotalDrawCalls { get; init; }
		public MeshData[] Meshes { get; init; }

		public class MeshData
		{
			internal MeshData() { }

			public string Name { get; init; }
			public int Vertices { get; init; }
			public int Triangles { get; init; }
			public int TranslucencyType { get; init; }
			public Vector3 BoundsMin { get; init; }
			public Vector3 BoundsMax { get; init; }
			public DrawCallData[] DrawCalls { get; init; }
		}

		public class DrawCallData
		{
			internal DrawCallData() { }

			public int Vertices { get; init; }
			public int Indices { get; init; }
			public int PrimitiveType { get; init; }
			public float UvDensity { get; init; }
			public int InstanceCount { get; init; }
			public bool AlphaBlended { get; init; }
			public int StartIndex { get; init; }
			public int BaseVertex { get; init; }
			public Material Material { get; init; }
		}
	}

	private ModelMeshInfo _meshInfo;

	//Counted from core on first ask, cached until reload; never null (empty model reports zeros)
	/// <summary>All mesh and draw call information for this model.</summary>
	public ModelMeshInfo MeshInfo => _meshInfo ??= OurMeshInfo();
}
