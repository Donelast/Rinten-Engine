namespace Rinten;

//Applies a model definition's compile-time TRS to mesh, bones and physics; shared by both compilers
internal static unsafe class ModelTransformOp
{
	public static void Apply( CoreAssets.ModelBuilder builder, ModelDefinition definition )
	{
		var transform = definition?.Transform;
		if ( transform is not { Enabled: true } || transform.IsIdentity )
			return;

		var rotation = transform.Rotation.ToRotation();

		var pos = stackalloc float[3] { transform.Position.x, transform.Position.y, transform.Position.z };
		var quat = stackalloc float[4] { rotation.x, rotation.y, rotation.z, rotation.w };
		var scale = stackalloc float[3] { transform.Scale.x, transform.Scale.y, transform.Scale.z };

		builder.ApplyTransform( pos, quat, scale );
	}
}
