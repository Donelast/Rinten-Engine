//One instance of the world as the card sees it - where it stands, where it stood, what it is drawn with. Read by the cluster stages every surface
//gets (cluster_main.slang) and by the engine's cull, raster and skin passes (engine/mesh_tables.slang); written by CoreSceneTables.InstanceRecord.

#ifndef ENGINE_VIS_INSTANCE
#define ENGINE_VIS_INSTANCE

struct VisInstance
{
	float4 world0;
	float4 world1;
	float4 world2;
	float4 world3;
	float4 prev0;
	float4 prev1;
	float4 prev2;
	float4 prev3;
	float4 sphere;
	uint mesh;
	uint materialFirst;
	uint flags;
	float cullGrow;
	float4 numbers;
	float4 tint;
	float4 said0;
	float4 said1;
	uint posedFirst;
	uint posedSource;
	uint2 posedPad;

	//How many times the instance has been put somewhere else - see CoreSceneTables.MoveCount.
	uint moveCount;

	//One while this instance is in motion, nought once it has stopped - see CoreSceneTables.Moving.
	uint moving;

	//What its clusters' error is multiplied by in every view's cut - see ModelRenderer.LodBias. Not read here; the cull reads it.
	float lodScale;

	//Which of the project's level-of-detail groups scales that with the camera's distance:
	//its row plus one, nought for none - see Rendering.LodGroups and LodScaleOf in the cull.
	uint lodGroup;
};

#endif
