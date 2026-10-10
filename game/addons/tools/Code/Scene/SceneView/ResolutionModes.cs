using System;

namespace Editor;

//The shapes a game view can be forced into: a free size, a screen's aspect, or an exact resolution.
internal static class ResolutionModes
{
	//One entry on the menu, and what picking it does to a scene view.
	internal sealed record Mode( string Title, string Icon, Action<SceneViewPanel> Apply );

	public static IReadOnlyList<Mode> All { get; } = new Mode[]
	{
		new( "Free Size", "fit_screen", view => view.SetFreeSize() ),
		new( "16:9 Aspect", "aspect_ratio", view => view.SetForceAspect( 16.0f / 9.0f ) ),
		new( "21:9 Aspect", "aspect_ratio", view => view.SetForceAspect( 21.0f / 9.0f ) ),
		new( "32:9 Aspect", "aspect_ratio", view => view.SetForceAspect( 32.0f / 9.0f ) ),
		new( "4:3 Aspect", "aspect_ratio", view => view.SetForceAspect( 4.0f / 3.0f ) ),
		new( "9:16 Aspect", "aspect_ratio", view => view.SetForceAspect( 9.0f / 16.0f ) ),
		new( "1280×800 handheld", "stay_current_landscape", view => view.SetForceResolution( new( 1280, 800 ) ) ),
		new( "720p", "laptop_windows", view => view.SetForceResolution( new( 1280, 720 ) ) ),
		new( "1080p", "desktop_windows", view => view.SetForceResolution( new( 1920, 1080 ) ) ),
	};

	/// <summary>The one that was picked last, remembered with the project.</summary>
	public static string CurrentTitle
	{
		get => ProjectCookie.Get( "EditorDisplayMode", All[0].Title );
		set => ProjectCookie.Set( "EditorDisplayMode", value );
	}

	public static Mode Current => All.FirstOrDefault( x => x.Title == CurrentTitle ) ?? All[0];

	/// <summary>Puts the game view back into the shape it was left in.</summary>
	public static void ApplyCookie( SceneViewPanel view )
	{
		if ( view is null ) return;

		Current.Apply( view );
	}

	public static void Pick( SceneViewPanel view, Mode mode )
	{
		CurrentTitle = mode.Title;

		mode.Apply( view );
	}
}
