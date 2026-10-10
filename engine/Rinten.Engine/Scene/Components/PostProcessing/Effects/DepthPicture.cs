using Rinten.Rendering;

namespace Rinten;

//Copies depth after the pre-pass so later surfaces like water can read what is behind them
/// <summary>Keeps a picture of the world's depth, taken after the depth pre-pass, for the surfaces drawn later that want to know what is behind them.</summary>
[Expose]
[Title( "Depth Picture" )]
[Category( "Post Processing" )]
[Icon( "layers" )]
public sealed class DepthPicture : BasePostProcess<DepthPicture>
{
	public override int ComponentVersion => 1;

	private static Material Copy => field ??= Material.FromShader( "shaders/postprocess/depth_copy.shader" );

	//The name the picture is found under - see CoreSceneDrawer, which says it in the frame block.
	internal const string PictureName = "SceneDepth";

	public override void Render()
	{
		var cl = NextList( "Depth Picture" );

		var depth = cl.GetPicture( PictureName, PictureFormat.SingleFull );

		cl.SetRenderTarget( depth );
		cl.Blit( Copy );
		cl.ClearRenderTarget();

		InsertCommandList( cl, Stage.AfterDepthPrepass, -10, "Depth Picture" );
	}
}
