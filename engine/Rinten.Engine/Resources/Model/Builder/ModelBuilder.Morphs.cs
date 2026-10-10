namespace Rinten;

public sealed partial class ModelBuilder
{
	//Hands a mesh's blend shapes to the builder, against the run of vertices that mesh just put in. The deltas a mesh holds are numbered from its
	//own first vertex, and the builder counts from the model's - so a shape on the second mesh of a model would otherwise move the first mesh's face.
	private static unsafe void AddMorphs( CoreAssets.ModelBuilder builder, CoreMesh mesh, int firstVertex )
	{
		if ( mesh?.MorphTargets is not { Count: > 0 } targets ) return;

		foreach ( var (name, deltas) in targets )
		{
			if ( deltas is not { Length: > 0 } ) continue;

			var vertices = new int[deltas.Length];
			var positions = new float[deltas.Length * 3];
			var normals = new float[deltas.Length * 3];

			for ( var i = 0; i < deltas.Length; i++ )
			{
				vertices[i] = firstVertex + deltas[i].VertexIndex;

				positions[i * 3] = deltas[i].PositionDelta.x;
				positions[( i * 3 ) + 1] = deltas[i].PositionDelta.y;
				positions[( i * 3 ) + 2] = deltas[i].PositionDelta.z;

				normals[i * 3] = deltas[i].NormalDelta.x;
				normals[( i * 3 ) + 1] = deltas[i].NormalDelta.y;
				normals[( i * 3 ) + 2] = deltas[i].NormalDelta.z;
			}

			fixed ( int* v = vertices )
			fixed ( float* p = positions )
			fixed ( float* n = normals )
			{
				builder.AddMorph( name, v, p, n, deltas.Length );
			}
		}
	}
}
