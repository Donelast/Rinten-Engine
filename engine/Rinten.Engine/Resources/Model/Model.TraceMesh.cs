namespace Rinten;

public partial class Model
{
	private ModelTraceMesh traceMesh;
	private bool traceMeshBuilt;

	//The model's triangles with a tree over them, or null where the core has no geometry to hand back. Built the first time something traces
	//against this model and kept after - which is nearly always the editor picking, so most models in a scene never build one at all.
	internal ModelTraceMesh TraceMesh
	{
		get
		{
			if ( traceMeshBuilt ) return traceMesh;

			traceMeshBuilt = true;

			if ( !GetTriangles( out var positions, out var indices ) )
				return null;

			traceMesh = new ModelTraceMesh( positions, indices );

			return traceMesh;
		}
	}

	private ModelBoneBoxes boneBoxes;
	private bool boneBoxesBuilt;

	//A box per bone, for a model whose triangles move - or null where nothing moves them and TraceMesh is the exact answer.
	internal ModelBoneBoxes BoneBoxes
	{
		get
		{
			if ( boneBoxesBuilt ) return boneBoxes;

			boneBoxesBuilt = true;
			boneBoxes = ModelBoneBoxes.Build( this );

			return boneBoxes;
		}
	}
}
