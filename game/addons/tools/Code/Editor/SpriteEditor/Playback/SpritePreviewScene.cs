using Rinten.Rendering;

namespace Editor.SpriteEditor;

//Owned by the window rather than by the preview dock, so the dock can be rebuilt on hotload without the renderer - which is what keeps playback time - starting over.
/// <summary>The scene the sprite is drawn in: a flat camera, the sprite in front of it, and a plate behind that.</summary>
public sealed class SpritePreviewScene : IDisposable
{
	public Scene Scene { get; }
	public CameraComponent Camera { get; }

	/// <summary>The renderer the sprite plays on. Playback is read off it.</summary>
	public SpriteRenderer Renderer { get; }

	/// <summary>How the sprite is sampled - an index into <see cref="TextureFilterLevels.All"/>, the preview's toggle.</summary>
	public int FilterLevel
	{
		get => _filterLevel;
		set => _filterLevel = value;
	}

	private int _filterLevel = TextureFilterLevels.Default;

	/// <summary>How the sprite is sampled, as <see cref="FilterLevel"/> says.</summary>
	public FilterMode Filter => TextureFilterLevels.ToFilter( _filterLevel );

	public SpritePreviewScene()
	{
		Scene = Scene.CreateEditorScene();

		using ( Scene.Push() )
		{
			Camera = FlatSceneParts.AddOrthographicCamera();

			var forward = Camera.WorldTransform.Forward;

			Renderer = FlatSceneParts.AddSprite( "sprite", forward * FlatSceneParts.ViewHeight );
			FlatSceneParts.AddBackdrop( forward * FlatSceneParts.ViewHeight * 2f );
		}
	}

	public void Dispose() => Scene?.Destroy();

	/// <summary>Once a frame, before the scene is drawn: the sprite is put where its origin says, sampled how the menu says, and the scene ticks.</summary>
	public void Frame( Vector2 origin )
	{
		if ( !Scene.IsValid() || !Renderer.IsValid() ) return;

		PlaceAtOrigin( origin );

		Renderer.TextureFilter = Filter;

		Scene.EditorTick( RealTime.Now, RealTime.Delta );

		DrawOriginMark();
	}

	//Shifts the sprite so its origin sits at the middle of the view, whatever shape the frame is.
	private void PlaceAtOrigin( Vector2 origin )
	{
		//The frame's own shape - a cell's, where the picture is a sheet.
		if ( Renderer.CurrentFrame is not { } frame || !frame.Texture.IsValid() ) return;

		var pixels = frame.PixelSize;
		if ( pixels.x <= 0 || pixels.y <= 0 ) return;

		var ratio = pixels.x / pixels.y;
		var offset = new Vector2( 0.5f, 0.5f ) - origin;
		var size = FlatSceneParts.ViewHeight;

		var position = ratio > 1f
			? Renderer.WorldPosition.WithX( -offset.x * size ).WithY( offset.y * size / ratio )
			: Renderer.WorldPosition.WithX( -offset.x * size * ratio ).WithY( offset.y * size );

		Renderer.WorldPosition = position;
	}

	//A cross where the origin is, drawn over the picture.
	private void DrawOriginMark()
	{
		Gizmo.Draw.Text( Renderer.WorldPosition.WithX( FlatSceneParts.ViewHeight * 0.5f ),
			new TextRendering.Scope( "add", Color.White, 28, "Material Icons" ) { FilterMode = FilterMode.Point } );
	}
}
