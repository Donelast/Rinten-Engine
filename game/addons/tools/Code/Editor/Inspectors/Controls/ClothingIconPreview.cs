namespace Editor.Inspectors;

using Rinten.UI;

//The scene is drawn to a texture of the panel's own size every frame and worn by an Image, which is how a panel shows a picture the editor's walk did not paint.
/// <summary>The scene a clothing icon is rendered from, drawn into a panel.</summary>
public sealed class ClothingIconPreview : Panel
{
	readonly ClothingScene _scene = new();
	readonly Rinten.UI.Image _view;

	Texture _target;

	public ClothingIconPreview()
	{
		AddClass( "clothing-icon-preview" );

		_scene.Update();

		_view = AddChild<Rinten.UI.Image>( "view" );
	}

	/// <summary>Dresses the scene in this clothing and looks at it the way its icon asks.</summary>
	public void Show( Clothing clothing )
	{
		if ( clothing is null ) return;

		_scene.InstallClothing( clothing );
	}

	public override void Tick()
	{
		base.Tick();

		if ( !_scene.Scene.IsValid() ) return;

		_scene.Update();

		Render();
	}

	void Render()
	{
		var rect = Box.RectInner;

		var width = rect.Width.CeilToInt();
		var height = rect.Height.CeilToInt();

		if ( width < 8 || height < 8 ) return;

		var camera = _scene.Scene.Camera;
		if ( !camera.IsValid() ) return;

		_target = Texture.CreateRenderTarget( "clothing_icon_preview", ImageFormat.RGBA8888,
			new Vector2( width, height ), _target );

		if ( _target is null ) return;

		if ( !camera.RenderToTexture( _target ) ) return;

		_view.Texture = _target;
	}

	public override void Delete( bool immediate = false )
	{
		_view.Texture = null;

		_target?.Dispose();
		_target = null;

		_scene.Scene?.Destroy();

		base.Delete( immediate );
	}
}
