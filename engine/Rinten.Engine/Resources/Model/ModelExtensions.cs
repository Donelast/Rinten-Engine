using Rinten;

public static partial class RintenSystemExtensions
{
	/// <summary>Returns false if model has no valid render meshes, or is null.</summary>
	public static bool HasRenderMeshes( this Model model )
	{
		if ( model is null ) return false;

		//Ours answers with what it has: geometry or not. The two questions below are about
		//a legacy compiled model's scene objects, which ours has none of and does not need any of.
		return model.MeshCount > 0;
	}
}
