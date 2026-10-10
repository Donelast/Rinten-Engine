using Rinten.Rendering;

namespace Editor.MdlEditor;

sealed class PreviewThrobber : SceneCustomObject
{
	//Whatever the scene is being drawn into - a widget or a panel. The host rather than either of them: this
	//only ever asks how big the picture is, and both answer that in the same units the picture is drawn in.
	private readonly ISceneRenderHost _preview;
	private readonly Texture _texture;
	private bool _enabled;

	//An open ring; it spins, so the gap is what shows it turning
	private const string Ring = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\"><circle cx=\"50\" cy=\"50\" r=\"38\" fill=\"none\" stroke=\"white\" stroke-width=\"10\" stroke-linecap=\"round\" stroke-dasharray=\"170 69\"/></svg>";
	private RealTimeSince _timeSinceDisabled;

	public bool Enabled
	{
		set
		{
			_enabled = value;

			if ( !value )
				_timeSinceDisabled = 0;
		}
	}

	public PreviewThrobber( SceneWorld sceneWorld, ISceneRenderHost preview ) : base( sceneWorld )
	{
		_preview = preview;
		using var ring = Bitmap.CreateFromSvgString( Ring, 100, 100 );
		_texture = ring.ToTexture();
		Bounds = BBox.FromPositionAndSize( Vector3.Zero, float.MaxValue );
	}

	public override void RenderSceneObject()
	{
		base.RenderSceneObject();

		if ( !_enabled && _timeSinceDisabled > 0.5f )
			return;

		const int speed = 300;
		var delta = _enabled ? 0.0f : _timeSinceDisabled * 2.0f;
		var angle = RealTime.Now % (MathF.PI * (2.0f * speed));

		//The picture is rendered at the surface's own size, so what is drawn into it is measured in those same pixels - there is no scale to apply.
		var surface = _preview.SurfaceSize;
		var position = new Vector2( surface.x - 39, 41 );
		var matrix = Matrix.CreateRotation( Rotation.From( 0, angle * speed, 0 ) );
		matrix *= Matrix.CreateTranslation( position );
		Graphics.Attributes.Set( "LayerMat", matrix );

		Graphics.Attributes.Set( "Texture", _texture );
		Graphics.Attributes.SetComboEnum( "D_BLENDMODE", Rinten.BlendMode.Normal );
		Graphics.DrawQuad( new Rect( -50, 100 ), Material.UI.Basic, Color.Black.WithAlpha( 0.5f.LerpTo( 0.0f, delta ) ) );
		Graphics.Attributes.SetComboEnum( "D_BLENDMODE", Rinten.BlendMode.Lighten );

		position = new Vector2( surface.x - 40, 40 );
		matrix = Matrix.CreateRotation( Rotation.From( 0, angle * speed, 0 ) );
		matrix *= Matrix.CreateTranslation( position );
		Graphics.Attributes.Set( "LayerMat", matrix );
		Graphics.DrawQuad( new Rect( -50, 100 ), Material.UI.Basic, _enabled ? Color.White : Color.White.WithAlpha( 1.0f.LerpTo( 0.0f, delta ) ) );
	}
}
