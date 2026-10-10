using Rinten.UI;

namespace Editor;

public partial class SceneViewportPanel
{
	private Vector2? forcedSize;
	private float? forcedAspectRatio;

	//The picture's last size, so the styles are only written when it changes.
	private Rect placedPicture;

	/// <summary>Set the forced size to defaults (free size)</summary>
	public void SetDefaultSize()
	{
		forcedSize = null;
		forcedAspectRatio = null;

		PlacePicture();
	}

	/// <summary>Set the viewport to a specific aspect ratio</summary>
	public void SetAspectRatio( float aspectRatio )
	{
		forcedAspectRatio = aspectRatio;
		forcedSize = null;

		PlacePicture();
	}

	/// <summary>Tries to set the viewport to a specific resolution</summary>
	public void SetResolution( Vector2 resolution )
	{
		forcedSize = resolution;
		forcedAspectRatio = null;

		PlacePicture();
	}

	public override void Tick()
	{
		base.Tick();

		PlacePicture();
	}

	//Places the picture: full viewport, or letterboxed when the game view forces a size
	private void PlacePicture()
	{
		if ( !Renderer.IsValid() ) return;

		var available = ScreenSize;
		if ( available.x <= 0 || available.y <= 0 ) return;

		var wanted = WantedPictureSize( available );

		var placing = new Rect(
			MathF.Round( (available.x - wanted.x) * 0.5f ),
			MathF.Round( (available.y - wanted.y) * 0.5f ),
			wanted.x,
			wanted.y );

		if ( placing == placedPicture ) return;

		placedPicture = placing;

		Renderer.Style.Left = Length.Pixels( placing.Left );
		Renderer.Style.Top = Length.Pixels( placing.Top );
		Renderer.Style.Width = Length.Pixels( placing.Width );
		Renderer.Style.Height = Length.Pixels( placing.Height );
	}

	private Vector2 WantedPictureSize( Vector2 available )
	{
		//Exact size, easy
		if ( forcedSize is { } size )
		{
			return new Vector2( MathF.Min( size.x, available.x ), MathF.Min( size.y, available.y ) );
		}

		//Free
		if ( forcedAspectRatio is not { } aspectRatio || aspectRatio <= 0.0f )
		{
			return available;
		}

		var availableAspect = available.y > 0.0f ? available.x / available.y : 1.0f;

		return aspectRatio > availableAspect
			? new Vector2( available.x, available.x / aspectRatio )
			: new Vector2( available.y * aspectRatio, available.y );
	}
}
