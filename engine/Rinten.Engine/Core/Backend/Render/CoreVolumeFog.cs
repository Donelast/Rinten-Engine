using System;
using Rinten.Rendering;

namespace Rinten;

//Froxel fog: the air in front of the camera integrated once a frame into a sheet of slices
//Read by surfaces in one tap, so fog cost follows the picture, not the fragment overdraw
internal static class CoreVolumeFog
{
	//How many screen pixels across one froxel is - see r_fog_sheet_grain.
	internal static int Grain => Math.Clamp( Rinten.Rendering.DrawSettings.FogSheetGrain, 2, 16 );

	//Slice count; slices span only the fog's near-to-far range (see Nearest and Reach)
	internal const int Slices = 128;

	//The nearest a slice may begin, in metres, whatever the fog says.
	internal const float Near = 0.25f;

	//How many tiles across the sheet lays its slices - the rest go on the rows below. Square-ish by construction, so that neither side of the picture is
	//the one that hits the device's ceiling; the reader works the rows out from this and the slice count, so the two agree without either being told.
	internal static int Columns => (int)MathF.Ceiling( MathF.Sqrt( Slices ) );

	//The name the picture is acquired under, which is how the frame finds its place in the table.
	internal const string PictureName = "FogVolume";

	//How many volumes the frame carries and how far the farthest of them reaches from the eye - written by
	//CoreSceneRenderer.SetFogVolumes, read here to decide whether there is anything to draw and how deep to draw it.
	internal static int Volumes;
	internal static float Reach;
	internal static float Nearest;

	//The two distances the last sheet was laid out between, for whoever writes them into the frame.
	internal static (float Near, float Far) Laid;

	//How many froxels across and down the last sheet was, for the reader's filter.
	internal static (int Wide, int Tall) Grid;

	private static ComputeShader shader;

	private static ComputeShader Shader =>
		shader ??= new ComputeShader( "shaders/engine/fog_volume.shader", "MainCs" );

	//Draws this view's sheet and gives back its place in the picture table, with how deep it was drawn - nought where there was nothing to draw.
	internal static (int Picture, float Reach) Draw( (int Width, int Height) pixels, float far )
	{
		//Nothing to draw: no volumes, no sheet, and the reader marches instead - which is what every
		//view but the camera's does anyway, since this is the camera's sheet and nobody else's.
		if ( Volumes <= 0 || pixels.Width <= 0 || pixels.Height <= 0 ) return ( 0, 0.0f );
		if ( !CoreNative.Available || CoreRenderDevice.PortableFrame ) return ( 0, 0.0f );
		if ( !Rinten.Rendering.DrawSettings.FogSheet ) return ( 0, 0.0f );

		var grain = Grain;
		var wide = Math.Max( 1, ( pixels.Width + grain - 1 ) / grain );
		var tall = Math.Max( 1, ( pixels.Height + grain - 1 ) / grain );

		Grid = ( wide, tall );

		//How deep the slices go: as far as the fog itself reaches, never past the view and never so near that the sheet is all first
		//slice. Past the last slice a reader holds what that slice held, which is right - there is no more fog out there to add.
		var reach = Math.Clamp( Reach, Near * 8.0f, Math.Max( far, Near * 8.0f ) );

		//The slices begin at the near face of the nearest volume: before that there is nothing to integrate, and a slice spent there
		//is a slice not spent in the fog. Never nearer than Near, and never so near the far end that the sheet is one slice thick.
		var near = Math.Clamp( Nearest, Near, reach * 0.5f );

		//Slices tiled in a roughly square grid; one tall strip would exceed the device's max texture size
		var columns = Columns;
		var rows = ( Slices + columns - 1 ) / columns;

		var sheet = CorePasses.Acquire( PictureName, CoreRender.PictureKind.Hdr, wide * columns, tall * rows, 1 );

		if ( sheet is not { Picture: not 0 } ) return ( 0, 0.0f );

		var shader = Shader;

		shader.Attributes.Set( "Grid", new Vector4( wide, tall, Slices, reach ) );
		//And whether the frame has a lamp at all - see the march's lamp light in fog_volume.shader,
		//which otherwise walks the lights at every slice of every column to find none.
		shader.Attributes.Set( "Tiles", new Vector4( columns, rows, near, CoreLights.Lamps > 0 ? 1.0f : 0.0f ) );

		//What the reader has to be told, because it inverts this exact mapping.
		Laid = ( near, reach );

		//One thread per column so the running integral is cheap; run apart from the camera's stages
		shader.RunApart( sheet, wide, tall, 1 );

		return ( sheet.Index, reach );
	}
}
