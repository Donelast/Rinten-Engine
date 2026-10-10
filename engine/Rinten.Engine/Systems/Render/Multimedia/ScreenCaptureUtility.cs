using NativeEngine;
using System.IO;

namespace Rinten;

//Utility methods for screen recording and screenshot functionality
internal static class ScreenCaptureUtility
{
	//Captures the current scene layer for screenshots and video recording. Call this after rendering any overlays that should appear in the capture.
	internal static void CaptureFrame()
	{
		//The frame was taken off their scene layer's colour target. Ours draws the window itself,
		//so a screenshot comes from the renderer rather than from here - see CoreGameRenderer.
	}

	internal static void DrawRecordingBorder()
	{
		if ( !ScreenRecorder.IsRecording() )
			return;

		var width = Graphics.Viewport.Width;
		var height = Graphics.Viewport.Height;

		const float rectSize = 4f;
		var color = new Color( 255, 0, 0, 10 );

		Graphics.DrawRoundedRectangle( new Rect( 0, 0, width, rectSize ), color );
		Graphics.DrawRoundedRectangle( new Rect( 0, height - rectSize, width, rectSize ), color );
		Graphics.DrawRoundedRectangle( new Rect( 0, rectSize, rectSize, height - (rectSize * 2) ), color );
		Graphics.DrawRoundedRectangle( new Rect( width - rectSize, rectSize, rectSize, height - (rectSize * 2) ), color );
	}

	/// <summary>Generates a suitable screenshot filename with timestamp</summary>
	public static string GenerateScreenshotFilename( string extension, string filePath = "screenshots" )
	{
		var fileName = ConsoleSystem.GetValue( "screenshot_prefix", EngineExecutables.Editor );
		if ( string.IsNullOrEmpty( fileName ) )
		{
			fileName = EngineExecutables.Editor;
		}

		//Format extension with dot if needed
		string extensionSeparator = "";
		if ( !string.IsNullOrEmpty( extension ) && !extension.StartsWith( "." ) )
		{
			extensionSeparator = ".";
		}

		//Get timestamp
		string timestamp = DateTime.Now.ToString( "yyyy.MM.dd.HH.mm.ss" );

		//Generate final filename
		string screenshotFilename;
		if ( !string.IsNullOrEmpty( filePath ) )
		{
			screenshotFilename = Path.Combine( filePath, $"{fileName}.{timestamp}{extensionSeparator}{extension}" );
		}
		else
		{
			screenshotFilename = $"{fileName}.{timestamp}{extensionSeparator}{extension}";
		}

		return screenshotFilename;
	}
}
