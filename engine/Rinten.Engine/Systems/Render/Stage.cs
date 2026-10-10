namespace Rinten.Rendering;

/// <summary>Where in a frame a project's own full-screen pass happens.</summary>
public enum Stage
{
	//Shape and nothing else: the depth is filled and there is no colour yet. A pass here runs, but anything it paints is painted over by the world - this is the stage for work that reads the depth rather than the picture.
	/// <summary>After the depth pre-pass, before the world is shaded.</summary>
	AfterDepthPrepass = 1000,

	//The world's pass closes for this and reopens after, keeping its depth.
	/// <summary>After the solid world, before the sky fills in behind it.</summary>
	AfterOpaque = 2000,

	/// <summary>After the sky, before the glass.</summary>
	AfterSkybox = 3000,

	/// <summary>After the glass: the whole world, in the light it was shaded in.</summary>
	AfterTransparent = 4000,

	/// <summary>The same place, for anything that wants to be after that.</summary>
	AfterViewmodel = 5000,

	//Where an outline or an edge filter belongs: the colour, the normals and the depth are all there, and the picture is still in half floats.
	/// <summary>Before the engine's own post-process - the reflections, the blur, the tonemap.</summary>
	BeforePostProcess = 6000,

	/// <summary>The last place before the picture becomes a screen.</summary>
	Tonemapping = 6500,

	//The end of the frame, so a pass here draws on the window rather than into a picture of its own - which means several of them do not chain: each reads the world and draws over what the last one drew. For an overlay that is what is wanted; anything that changes the colour belongs earlier.
	/// <summary>Over the finished, tonemapped frame.</summary>
	AfterPostProcess = 7000,

	/// <summary>The same, with the interface drawn as well.</summary>
	AfterUI = 8000,
}
