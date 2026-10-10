using System.Runtime.CompilerServices;

namespace Rinten;

//Draws a SceneCamera through a hidden stand-in camera component in its world's scene
internal static class CoreSceneCameras
{
	private sealed class Stand
	{
		public GameObject Holder;
		public CameraComponent Camera;
		public CoreScenePreview Preview;
	}

	private static readonly ConditionalWeakTable<SceneCamera, Stand> stands = new();

	private static readonly HashSet<string> said = new();

	//The scene a scene camera looks at and the component standing in for it, set up as the scene camera is now. False where there is no scene or no device.
	private static bool Aim( SceneCamera camera, out Scene scene, out Stand stand )
	{
		stand = null;
		scene = camera?.World?.Scene;

		if ( !scene.IsValid() )
		{
			var name = camera?.Name ?? "a scene camera";

			lock ( said )
			{
				if ( said.Add( name ) )
					Log.Warning( $"{name} looks at a world that belongs to no scene, and a world is drawn here as a scene's - put what it shows in a Scene" );
			}

			return false;
		}

		if ( !CoreScenePreview.Available ) return false;

		stand = stands.GetValue( camera, static _ => new Stand() );

		if ( !stand.Holder.IsValid() || stand.Holder.Scene != scene || !stand.Camera.IsValid() )
		{
			if ( stand.Holder.IsValid() ) stand.Holder.Destroy();

			using ( scene.Push() )
			{
				var holder = scene.CreateObject( enabled: false );
				holder.Name = $"Scene Camera: {camera.Name}";
				holder.Flags |= GameObjectFlags.Hidden | GameObjectFlags.NotSaved;

				stand.Holder = holder;
				stand.Camera = holder.Components.Create<CameraComponent>();

				//A viewport's own: never the scene's camera, never drawn into a texture by the scene's own cameras pass.
				stand.Camera.IsSceneEditorCamera = true;
			}
		}

		var standIn = stand.Camera;

		standIn.WorldPosition = camera.Position;
		standIn.WorldRotation = camera.Rotation;
		standIn.FieldOfView = camera.FieldOfView;
		standIn.ZNear = camera.ZNear;
		standIn.ZFar = camera.ZFar;
		standIn.Orthographic = camera.Ortho;
		standIn.OrthographicHeight = camera.OrthoHeight;
		standIn.BackgroundColor = camera.BackgroundColor;
		standIn.EnablePostProcessing = camera.EnablePostProcessing;
		standIn.RenderTags.SetFrom( camera.RenderTags );
		standIn.RenderExcludeTags.SetFrom( camera.ExcludeTags );

		stand.Preview ??= new CoreScenePreview { GameView = true };

		return true;
	}

	//Draws what a scene camera sees into a texture of the game's, every level of it, at size or the texture's own - see CoreScenePreview.RenderPicture.
	//Sent without waiting: the texture is ready by the time anything after it on the queue samples it. Outside any frame being recorded.
	internal static bool RenderToTexture( SceneCamera camera, Texture texture, Vector2? size )
	{
		if ( texture?.core is not { } core ) return false;
		if ( !Aim( camera, out var scene, out var stand ) ) return false;

		var device = core.Device();
		if ( device == 0 ) return false;

		var (width, height) = size is { } asked ? ( (int)asked.x, (int)asked.y ) : ( core.Width, core.Height );

		if ( width < 1 || height < 1 ) return false;

		return stand.Preview.RenderPicture( scene, stand.Camera, width, height, device ) != 0;
	}

	//Draws what a scene camera sees into a bitmap, waiting for it - see CoreScenePreview.Render.
	internal static bool RenderToBitmap( SceneCamera camera, Bitmap bitmap )
	{
		if ( !Aim( camera, out var scene, out var stand ) ) return false;

		return stand.Preview.Render( scene, stand.Camera, bitmap );
	}

	//Draws the six cube faces from the scene camera into the first mip; waits on the GPU per face
	internal static bool RenderToCube( SceneCamera camera, Texture texture )
	{
		if ( texture?.core is not { } core || core.Depth != 6 ) return false;
		if ( !CoreImage.Handles( core.Format ) ) return false;

		var side = Math.Min( core.Width, core.Height );
		if ( side < 1 ) return false;

		var position = camera.Position;
		var rotation = camera.Rotation;
		var fieldOfView = camera.FieldOfView;

		using var face = new Bitmap( side, side );

		var pixelBytes = CoreImage.PixelBytes( core.Format );
		var converted = core.Format == ImageFormat.RGBA8888 ? null : new byte[side * side * pixelBytes];

		try
		{
			for ( var i = 0; i < 6; i++ )
			{
				camera.Rotation = SceneCamera.CubeRotations[i];
				camera.FieldOfView = 90.0f;

				if ( !RenderToBitmap( camera, face ) ) return false;

				var pixels = System.Runtime.InteropServices.MemoryMarshal.AsBytes( face.GetPixels32().AsSpan() );

				if ( converted is not null )
				{
					for ( var at = 0; at < side * side; at++ )
					{
						CoreImage.Write( converted.AsSpan( at * pixelBytes, pixelBytes ), core.Format,
							CoreImage.Read( pixels.Slice( at * 4, 4 ), ImageFormat.RGBA8888 ) );
					}

					core.WriteRegion( converted, 0, 0, side, side, 0, i );
				}
				else
				{
					core.WriteRegion( pixels, 0, 0, side, side, 0, i );
				}
			}
		}
		finally
		{
			camera.Position = position;
			camera.Rotation = rotation;
			camera.FieldOfView = fieldOfView;
		}

		return true;
	}

	//Lets go of what stands in for a scene camera - when it is disposed.
	internal static void Release( SceneCamera camera )
	{
		if ( camera is null || !stands.TryGetValue( camera, out var stand ) ) return;

		stands.Remove( camera );

		if ( stand.Holder.IsValid() ) stand.Holder.Destroy();

		stand.Preview?.Dispose();
	}
}
