namespace Rinten;

//The passes that draw the world's meshes: what the card is told about the scene, the cull over instances and clusters, the
//raster of the shape of the world into IDs and depth, and the classification of that picture by material. See MeshRender.md.
internal static class CoreMeshShaders
{
	//How many views one cull decides for at once: the camera, a frame's bakes of the sun's pages and the two
	//zones of its map of what moves - see CoreSunPages - and the lamps' faces the frame draws, see CoreLampMaps.
	internal const int MaxViews = 32;

	//How many visible lists those views have between them: the camera's seven and three a view after it - see CoreMeshRender.Lists.
	internal const int MaxLists = CoreMeshRender.CameraLists + ( ( MaxViews - 1 ) * CoreMeshRender.ListsPerView );

	//Vertices one cluster is drawn with: a hundred and twenty-four triangles, three corners each.
	internal const int ClusterVertices = 124 * 3;




	//Instance flags - see CoreSceneTables.
	internal const uint InstanceDrawn = 1;
	internal const uint InstanceNoShadow = 2;
	internal const uint InstanceShadowOnly = 4;
	internal const uint InstanceSquare = 8;
	internal const uint InstancePosed = 16;

	//Never baked into the still world, whether or not it is moving right now - see SceneObjectFlags.NeverStill and CoreCasters.
	internal const uint InstanceNeverStill = 32;

	//Material flags - see CoreSceneTables.Materials.
	internal const uint MaterialTwoSided = 1;
	internal const uint MaterialAlphaTest = 2;
	internal const uint MaterialTranslucent = 4;
	internal const uint MaterialLoose = 8;

	//The surface moves its own vertices, so its clusters are drawn but never pre-passed - see mesh_cull.slang's Append and cluster_surface.slang.
	internal const uint MaterialMoves = 16;

	//First distance slice drawn with far-shading constants; matches ClusterFarSlice in mesh_tables.slang
	internal const int FarSlice = 11;

	//Where the slice of the distance sits in a list's entry, above the cluster's number - ClusterSliceShift in mesh_tables.slang is the same.
	internal const int ClusterSliceShift = 26;

}
