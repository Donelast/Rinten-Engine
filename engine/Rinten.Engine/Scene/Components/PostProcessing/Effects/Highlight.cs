using Rinten.Rendering;

namespace Rinten;

//Outlines are drawn into a mask (hidden and visible pass), then one pass draws edges before tonemap
/// <summary>This should be added to a camera that you want to outline stuff</summary>
[Title( "Highlight" )]
[Category( "Post Processing" )]
[Icon( "lightbulb_outline" )]
public sealed class Highlight : BasePostProcess<Highlight>
{
	private readonly CommandList commands = new( "Highlight" );

	//What marks the shapes out - see highlight_mask.shader.
	private static Material Mask => field ??= Material.FromShader( "shaders/engine/highlight_mask.shader" );

	//And what turns them into a line - see highlight.shader.
	private static Material Edge => field ??= Material.FromShader( "shaders/postprocess/highlight.shader" );

	/// <summary>How much of the colour to lay over the shape itself as well as beside it. Nothing is an outline and nothing else.</summary>
	[Range( 0, 1 )]
	[Property] public float Inside { get; set; } = 0.0f;

	public override void Render()
	{
		if ( !Mask.IsValid() || !Edge.IsValid() ) return;

		var outlines = Scene.GetAll<HighlightOutline>();

		//The widest line the frame asks for. One pass draws them all, so one width has to cover them - a line is found
		//by looking that far for a marked pixel, and looking further for a narrow one only finds the same pixels again.
		var width = 0.0f;
		var any = false;

		foreach ( var glow in outlines )
		{
			if ( !glow.IsValid() || !glow.Active ) continue;

			any = true;
			width = MathF.Max( width, glow.Width );
		}

		if ( !any ) return;

		commands.Reset();

		//Mask cleared each frame, 8 bits per channel: colour plus visibility in alpha
		var mask = commands.GetPicture( "Highlight", PictureFormat.Colour );

		commands.SetRenderTarget( mask );

		//The hidden shape first and the visible one over it, so a thing that is half
		//behind a wall is marked as hidden where it is and as visible where it is not.
		Marks( outlines, hidden: true );
		Marks( outlines, hidden: false );

		commands.ClearRenderTarget();

		//And the line, added to the frame rather than replacing it, reading the mask the draws above made - handed over by its place in the table.
		commands.Attributes.Set( "Width", width * 4.0f );
		commands.Attributes.Set( "Inside", GetWeighted( x => x.Inside, 0.0f, onlyLerpBetweenVolumes: true ) );
		commands.Attributes.Set( "Mask", mask.ColorTexture );

		commands.Blit( Edge );

		InsertCommandList( commands, Stage.AfterTransparent, 1000, "Highlight" );
	}

	//Draws outline meshes into the mask; alpha is 1 where visible and 0.5 where drawn through depth
	private void Marks( IEnumerable<HighlightOutline> outlines, bool hidden )
	{
		foreach ( var glow in outlines )
		{
			if ( !glow.IsValid() || !glow.Active ) continue;

			var colour = hidden ? glow.ObscuredColor : glow.Color;

			if ( colour.a <= 0.0f ) continue;

			commands.Attributes.Set( "MarkColour",
				new Vector4( colour.r, colour.g, colour.b, hidden ? 0.5f : 1.0f ) * colour.a );

			//A material of the project's own is trusted to know how to draw whatever it is put in
			//front of, including a mesh that reads its own vertices; the engine's cannot, and says so.
			var own = glow.Material.IsValid();

			foreach ( var model in glow.GetOutlineTargets() )
			{
				commands.DrawRenderer( model, new RendererSetup { Material = own ? glow.Material : Mask, IgnoreDepth = hidden, ReadsOwnVertices = own } );
			}
		}
	}
}
