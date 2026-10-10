//Engine cluster pass for every surface: runs its Vertex and Shade over a vertex pulled from the pool

[shader("vertex")]
V2F MainClusterVs( uint vid : SV_VertexID )
{
	ClusterVertex v;
	LoadClusterVertex( vid, v );

	//The draw's numbers before the body runs: the body reads them.
	ClusterInstance( v.instance );

	//And where the corner was before anything posed it, for a body that asks.
	g_clusterRest = v.rest;

	VIn i;
	i.position = v.position;
	i.colour = v.colour;
	i.frame = v.frame;
	i.texCoord0 = v.uv0;
	i.texCoord1 = v.uv1;

	V2F o = Vertex( i );

	//Use the pre-pass depth unless the surface moves vertices; then this pass writes its own depth
#if !defined( ENGINE_MOVES_VERTICES ) || ENGINE_MOVES_VERTICES == 0
	o.position = v.clip;
#endif

	//The instance, for the pixel stage to take its numbers from.
	o.depthId = v.instance;
	return o;
}

[shader("fragment")]
Out MainClusterPs( V2F i, bool front : SV_IsFrontFace )
{
	ClusterInstance( i.depthId );

	//Which side of the sheet, only where the material draws both - see F_BACKFACES in complex.shader. A surface that has no such feature is
	//always looked at from the front. Nested, because a preprocessor asked about a name nobody defined says so even on the far side of an &&.
	bool facing = true;

#ifdef F_BACKFACES
#if F_BACKFACES
	facing = front;
#endif
#endif

	return Shade( i, facing );
}
