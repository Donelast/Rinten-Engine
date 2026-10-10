//Portable-frame instanced draw: runs a surface's Vertex/Shade per copy read via SV_InstanceID

[shader("vertex")]
V2F MainInstancedVs( VIn i, uint copy : SV_InstanceID )
{
	//The copy's numbers before the body runs: the body reads them as it reads the push block on an ordinary draw.
	InstanceNumbers( copy );

	V2F o = Vertex( i );

	//Which copy this is, for the pixel stage to take its numbers from.
	o.depthId = float( copy );

	return o;
}

[shader("fragment")]
Out MainInstancedPs( V2F i, bool front : SV_IsFrontFace )
{
	STEREO_EYE_IN( i );
	InstanceNumbers( uint( i.depthId + 0.5 ) );

	//Which side of the sheet, only where the material draws both - the same nesting cluster_surface.slang
	//needs, because a preprocessor asked about a name nobody defined says so even on the far side of an &&.
	bool facing = true;

#ifdef F_BACKFACES
#if F_BACKFACES
	facing = front;
#endif
#endif

	return Shade( i, facing );
}
