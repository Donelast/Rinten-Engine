using Silk.NET.Vulkan;

namespace Rinten;

//Rebuilds pipelines for a two-eye multiview pass from their kept recipe on first use
internal static unsafe partial class VkPipelines
{
	private sealed record Recipe(
		byte[] Vertex, string VertexEntry, byte[] Pixel, string PixelEntry,
		int VertexLayout, int DepthTest, int Topology, int Blend, int DepthWrite, int CullBack,
		int DepthOnly, int Target, int RasterFlags, int Specialize, int PictureFormat );

	//What each pipeline was made of, by the driver's handle.
	private static readonly Dictionary<ulong, Recipe> recipes = new();

	//Each pipeline made again for a pass of more than one layer: by the driver's handle and the layers.
	private static readonly Dictionary<(ulong Pipeline, uint Mask), (Pipeline Made, ulong Handle)> layered = new();

	//Said once for a pipeline nobody kept the makings of.
	private static bool saidUnkept;

	//Whether what a pipeline is made of is kept: where the device can draw both eyes at once and a headset is wanted. See CoreXr.Wanted.
	internal static bool KeepRecipes => VkDevice.multiview && CoreXr.Wanted;

	private static void Remember( Pipeline pipeline, Recipe recipe )
	{
		if ( KeepRecipes && pipeline.Handle != 0 ) recipes[pipeline.Handle] = recipe;
	}

	//A pipeline is going: so do its makings and whatever was made again from them.
	private static void Forget( Pipeline pipeline )
	{
		recipes.Remove( pipeline.Handle );

		if ( layered.Count == 0 ) return;

		foreach ( var key in layered.Keys.Where( x => x.Pipeline == pipeline.Handle ).ToArray() )
		{
			Destroy( layered[key].Handle );
			layered.Remove( key );
		}
	}

	//The pipeline for a pass drawing the layers in mask at once, made the first time it is asked for.
	//The one asked about where it cannot be made - drawn wrong rather than not at all, and said.
	internal static Pipeline Multiview( Pipeline single, uint mask )
	{
		if ( single.Handle == 0 || mask == 0 ) return single;

		if ( layered.TryGetValue( (single.Handle, mask), out var kept ) ) return kept.Made;

		if ( !recipes.TryGetValue( single.Handle, out var recipe ) )
		{
			if ( !saidUnkept )
			{
				saidUnkept = true;
				VkDevice.SayWarning( "a pipeline was drawn with in a pass of both eyes, and nothing it was made of was kept - it draws one" );
			}

			return single;
		}

		ulong handle;

		fixed ( byte* vertex = recipe.Vertex )
		fixed ( byte* pixel = recipe.Pixel )
		{
			handle = Create( vertex, recipe.Vertex.Length, recipe.VertexEntry,
				pixel, recipe.Pixel.Length, recipe.PixelEntry,
				recipe.VertexLayout, recipe.DepthTest, recipe.Topology, recipe.Blend,
				recipe.DepthWrite, recipe.CullBack, recipe.DepthOnly, recipe.Target, recipe.RasterFlags,
				recipe.Specialize, recipe.PictureFormat, mask );
		}

		var made = handle != 0 ? Get( handle ) : single;

		//Kept whichever way it went: a pipeline that would not be made once will not be made the next draw either.
		layered[(single.Handle, mask)] = (made, handle);

		return made;
	}
}
