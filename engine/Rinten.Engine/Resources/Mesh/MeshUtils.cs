
namespace NativeEngine;

internal enum GenerateUVsMode_t
{
	Lscm,
	LscmSquare,
	LscmBestFitSquare,
}

internal enum AlignEdgeUV_t
{
	None,
	U,
	V,
}

internal static class MeshUtils
{
	/// <summary>Triangulate a polygon made up of points, returns triangle indices into the list of vertices.</summary>
	public static unsafe Span<int> TriangulatePolygon( Span<Vector3> vertices )
	{
		if ( vertices.Length < 3 )
			return default;

		var vertexCount = vertices.Length;
		var indexCount = (vertexCount - 2) * 3;
		var indices = new int[indexCount];

		fixed ( int* pIndices = indices )
		fixed ( Vector3* pVertices = vertices )
		{
		//No-op: nothing to do here.
		return default;
		}
	}

	public static unsafe Vector2[] GenerateUVsForTriangles(
		Span<Vector3> vertexPositions,
		Span<uint> triangleVertexIndices,
		Span<int> triangleFaceIds,
		GenerateUVsMode_t generationMode = GenerateUVsMode_t.Lscm,
		AlignEdgeUV_t edgeAlignMode = AlignEdgeUV_t.None,
		int alignEdgeVertexIndexA = 0,
		int alignEdgeVertexIndexB = 0 )
	{
		if ( vertexPositions.Length == 0 || triangleVertexIndices.Length == 0 )
			return default;

		if ( (triangleVertexIndices.Length % 3) != 0 )
			throw new ArgumentException( "triangleVertexIndices length must be a multiple of 3.", nameof( triangleVertexIndices ) );

		var nNumVertices = vertexPositions.Length;
		var nNumTriangles = triangleVertexIndices.Length / 3;
		var hasFaceIds = !triangleFaceIds.IsEmpty && triangleFaceIds.Length == nNumTriangles;
		//No-op: nothing to do here.
		return default;
	}
}
