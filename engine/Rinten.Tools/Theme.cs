using System.Runtime.InteropServices;

namespace Editor;

public static partial class Theme
{



	internal static string GetDefaultFont()
	{
		return DefaultFont;
	}

	public static Color Blue;
	public static Color Green;
	public static Color Red;
	public static Color Yellow;
	public static Color Pink;

	public static Color Prefab;
	public static Color Folder;

	public static Color TabBackground;
	public static Color TabBarBackground;
	public static Color TabInactiveBackground;
	public static Color SurfaceBackground;
	public static Color SurfaceLightBackground;
	public static Color SidebarBackground;
	public static Color WindowBackground;
	public static Color WidgetBackground;
	public static Color ControlBackground;
	public static Color ButtonBackground;
	public static Color SelectedBackground;
	public static Color StatusBarBackground;
	public static Color Text;
	public static Color TextControl;
	public static Color TextLight;
	public static Color TextWidget;
	public static Color TextButton;
	public static Color TextSelected;
	public static Color TextLink;
	public static Color TextHighlight;
	public static Color TextDisabled;
	public static Color Border;
	public static Color BorderLight;
	public static Color BorderButton;
	public static Color PopupBackground;
	public static Color PopupBorderColor;
	public static Color Shadow;
	public static Color Primary;
	public static Color Overlay;
	public static Color MultipleValues;
	public static Color Highlight;
	public static Color TextDark;
	public static Color Base;
	public static Color BaseAlt;

	public static Color ToggleEnabled;
	public static Color ToggleDisabled;

	public static float ControlRadius;
	public static float ControlHeight;
	public static float RowHeight;

	/// <summary>List row height; must match $list-row-height in theme.scss (both read theme.json)</summary>
	public static float ListRowHeight;

	/// <summary>Width of dock gaps and splitter handles; must match $separator-width in theme.scss</summary>
	public static float SeparatorWidth;

	//Kept beside $separator-color and $separator-hover-color in theme.scss. Absent from theme.json they inherit: the header's dark at rest, so a splitter and the gap between two docks are one seam, and the panel under the pointer.
	/// <summary>What a separator is made of, and what it turns into under the pointer.</summary>
	public static Color SeparatorColor;

	public static Color SeparatorHoverColor;

	public static string HeadingFont;
	public static string DefaultFont;
	public static string MonospaceFont;

	static unsafe Theme()
	{
		LoadFromFile();
	}

	//One value out of the theme file, or null where the file said nothing about it - and then whoever asked falls back to what it had.
	private static string Value( Dictionary<string, string> theme, string name )
	{
		return theme is not null && theme.TryGetValue( name, out var value ) ? value : null;
	}

	//Re-reads theme.json off the disk.
	internal static void Reload() => LoadFromFile();

	private static void LoadFromFile()
	{
		//The colours below all have a value written beside them, so a theme that is not there - a test host, a process with no editor content
		//mounted - leaves the editor looking like itself rather than throwing out of a static constructor, which nothing can catch usefully.
		Dictionary<string, string> theme = null;

		try
		{
			var themeJson = FileSystem.Root?.ReadAllText( "/addons/tools/assets/styles/theme.json" );

			if ( !string.IsNullOrEmpty( themeJson ) )
			{
				theme = Json.Deserialize<Dictionary<string, string>>( themeJson );
			}
		}
		catch ( System.Exception )
		{
			theme = null;
		}

		//The values written beside each one are the theme file's, not something older: a window opened without
		//it - the launcher, a test host - has to look like the editor rather than like the editor used to look.
		TabBackground = Color.Parse( Value( theme, "TabBackground" ) ) ?? Color.Parse( "#18181C8C" ).Value;
		TabBarBackground = Color.Parse( Value( theme, "TabBarBackground" ) ) ?? Color.Parse( "#00000000" ).Value;
		TabInactiveBackground = Color.Parse( Value( theme, "TabInactiveBackground" ) ) ?? Color.Parse( "#00000000" ).Value;
		SurfaceBackground = Color.Parse( Value( theme, "SurfaceBackground" ) ) ?? Color.Parse( "#FFFFFF0D" ).Value;
		SurfaceLightBackground = Color.Parse( Value( theme, "SurfaceLightBackground" ) ) ?? Color.Parse( "#FFFFFF1A" ).Value;
		SidebarBackground = Color.Parse( Value( theme, "SidebarBackground" ) ) ?? Color.Parse( "#05050ABF" ).Value;
		WindowBackground = Color.Parse( Value( theme, "WindowBackground" ) ) ?? Color.Parse( "#0F0F14" ).Value;
		WidgetBackground = Color.Parse( Value( theme, "WidgetBackground" ) ) ?? Color.Parse( "#05050ABF" ).Value;
		ControlBackground = Color.Parse( Value( theme, "ControlBackground" ) ) ?? Color.Parse( "#18181C8C" ).Value;
		ButtonBackground = Color.Parse( Value( theme, "ButtonBackground" ) ) ?? Color.Parse( "#18181C8C" ).Value;
		SelectedBackground = Color.Parse( Value( theme, "SelectedBackground" ) ) ?? Color.Parse( "#FFFFFF14" ).Value;
		StatusBarBackground = Color.Parse( Value( theme, "StatusBarBackground" ) ) ?? Color.Parse( "#05050ABF" ).Value;
		Text = Color.Parse( Value( theme, "Text" ) ) ?? Color.Parse( "#FFFFFF" ).Value;
		TextControl = Color.Parse( Value( theme, "TextControl" ) ) ?? Color.Parse( "#FFFFFF" ).Value;
		TextLight = Color.Parse( Value( theme, "TextLight" ) ) ?? Color.Parse( "#FFFFFF99" ).Value;
		TextWidget = Color.Parse( Value( theme, "TextWidget" ) ) ?? Color.Parse( "#FFFFFF" ).Value;
		TextButton = Color.Parse( Value( theme, "TextButton" ) ) ?? Color.Parse( "#FFFFFF" ).Value;
		TextSelected = Color.Parse( Value( theme, "TextSelected" ) ) ?? Color.Parse( "#FFFFFF" ).Value;
		TextLink = Color.Parse( Value( theme, "TextLink" ) ) ?? Color.Parse( "#8DB53E" ).Value;
		TextHighlight = Color.Parse( Value( theme, "TextHighlight" ) ) ?? Color.Parse( "#FFFFFF" ).Value;
		TextDisabled = Color.Parse( Value( theme, "TextDisabled" ) ) ?? Color.Parse( "#FFFFFF40" ).Value;
		Border = Color.Parse( Value( theme, "Border" ) ) ?? Color.Parse( "#FFFFFF1A" ).Value;
		BorderLight = Color.Parse( Value( theme, "BorderLight" ) ) ?? Color.Parse( "#FFFFFF33" ).Value;
		BorderButton = Color.Parse( Value( theme, "BorderButton" ) ) ?? Color.Parse( "#FFFFFF1A" ).Value;
		PopupBackground = Color.Parse( Value( theme, "PopupBackground" ) ) ?? Color.Parse( "#2a2a2a" ).Value;
		PopupBorderColor = Color.Parse( Value( theme, "PopupBorderColor" ) )
			?? Color.Parse( Value( theme, "PopupBorder" ) )
			?? Color.Parse( "#484848" ).Value;
		Shadow = Color.Parse( Value( theme, "Shadow" ) ) ?? Color.Parse( "#00000066" ).Value;
		Primary = Color.Parse( Value( theme, "Primary" ) ) ?? Color.Parse( "#8DB53E" ).Value;
		Overlay = Color.Parse( Value( theme, "Overlay" ) ) ?? Color.Parse( "#00000080" ).Value;
		MultipleValues = Color.Parse( Value( theme, "MultipleValues" ) ) ?? Color.Parse( "#FFFFFF66" ).Value;
		Highlight = Color.Parse( Value( theme, "Highlight" ) ) ?? Color.Parse( "#FFFFFF0F" ).Value;
		TextDark = Color.Parse( Value( theme, "TextDark" ) ) ?? Color.Parse( "#0A0A0F" ).Value;
		Base = Color.Parse( Value( theme, "Base" ) ) ?? Color.Parse( "#0F0F14" ).Value;
		BaseAlt = Color.Parse( Value( theme, "BaseAlt" ) ) ?? Color.Parse( "#0B0B10" ).Value;

		ToggleEnabled = Color.Parse( Value( theme, "ToggleEnabled" ) ) ?? Color.Parse( "#50C878" ).Value;
		ToggleDisabled = Color.Parse( Value( theme, "ToggleDisabled" ) ) ?? Color.Parse( "#FFFFFF1A" ).Value;

		Blue = Color.Parse( Value( theme, "Blue" ) ) ?? Color.Parse( "#2f34bd" ).Value;
		Green = Color.Parse( Value( theme, "Green" ) ) ?? Color.Parse( "#50C878" ).Value;
		Red = Color.Parse( Value( theme, "Red" ) ) ?? Color.Parse( "#DC3C3C" ).Value;
		Yellow = Color.Parse( Value( theme, "Yellow" ) ) ?? Color.Parse( "#F0C828" ).Value;
		Pink = Color.Parse( Value( theme, "Pink" ) ) ?? Color.Parse( "#DF9194" ).Value;

		Prefab = Color.Parse( Value( theme, "Prefab" ) ) ?? Color.Parse( "#8DB53E" ).Value;
		Folder = Color.Parse( Value( theme, "Folder" ) ) ?? Color.Parse( "#F0C828" ).Value;

		if ( !float.TryParse( Value( theme, "ControlRadius" ), out ControlRadius ) )
			ControlRadius = 6.0f;

		if ( !float.TryParse( Value( theme, "ControlHeight" ), out ControlHeight ) )
			ControlHeight = 28.0f;

		if ( !float.TryParse( Value( theme, "RowHeight" ), out RowHeight ) )
			RowHeight = 28.0f;

		if ( !float.TryParse( Value( theme, "ListRowHeight" ), out ListRowHeight ) )
			ListRowHeight = 22.0f;

		if ( !float.TryParse( Value( theme, "SeparatorWidth" ), out SeparatorWidth ) )
			SeparatorWidth = 4.0f;

		SeparatorColor = Color.Parse( Value( theme, "SeparatorColor" ) ) ?? WindowBackground;
		SeparatorHoverColor = Color.Parse( Value( theme, "SeparatorHoverColor" ) ) ?? Color.Parse( "#282828" ).Value;

		HeadingFont = Value( theme, "HeadingFont" ) ?? "Inter";
		DefaultFont = Value( theme, "DefaultFont" ) ?? "Inter";
		MonospaceFont = Value( theme, "MonospaceFont" ) ?? "Roboto Mono";
	}

	public static Color GetTint( EditorTint tint )
	{
		return tint switch
		{
			EditorTint.Blue => Blue,
			EditorTint.Green => Green,
			EditorTint.Red => Red,
			EditorTint.Yellow => Yellow,
			EditorTint.Pink => Pink,
			_ => Color.White
		};
	}

	public static void DrawFilename( Rect rect, string filename, TextFlag flags, Color color )
	{
		var dir = System.IO.Path.GetDirectoryName( filename ) + "/";
		var file = System.IO.Path.GetFileNameWithoutExtension( filename );
		var extension = System.IO.Path.GetExtension( filename );

		//if we really cared we could do this better
		var size = Paint.MeasureText( rect, filename, flags );
		var overshoot = (size.Width - rect.Width) + 5;
		if ( overshoot > 0 )
		{
			overshoot += 10;
			var startIndex = (overshoot / 4).CeilToInt();

			if ( startIndex < dir.Length )
				dir = ".." + dir.Substring( startIndex );
			else
				dir = "";
		}

		dir = dir.Replace( '\\', '/' );

		Paint.SetPen( color.Darken( 0.3f ) );
		var r = Paint.DrawText( rect, dir, flags );
		rect.Left += r.Width;
		Paint.SetPen( color );
		r = Paint.DrawText( rect, file, flags );
		rect.Left += r.Width;
		Paint.SetPen( color.Darken( 0.1f ) );
		r = Paint.DrawText( rect, extension, flags );
	}

	/// <summary>Draw a button with a background color, text, and an optional icon. We'll use Paint.HasPressed and Paint.HasMouseOver to determine the button's state.</summary>
	public static void DrawButton( Rect LocalRect, string text = null, string Icon = null, bool Enabled = true, Color? Tint = null )
	{
		var c = Tint?.ToHsv() ?? Color.Parse( "#48494c" )?.ToHsv() ?? default;
		var bg = c;

		if ( Enabled )
		{
			if ( Paint.HasPressed )
			{
				bg = c with { Value = (c.Value + 0.1f) };
			}
			else if ( Paint.HasMouseOver )
			{
				bg = c with { Value = (c.Value + 0.2f) };
			}
		}
		else
		{
			bg = c = Theme.SurfaceLightBackground;
		}

		if ( !Enabled )
		{
			c = c.WithSaturation( 0.1f ).WithAlpha( 0.5f );
			bg = c.WithAlpha( 0.2f );
		}

		if ( bg.Alpha > 0 )
		{
			//One flat plate, like every other one: no lighter rim and no gradient across it.
			Paint.Antialiasing = true;

			Paint.ClearPen();
			Paint.SetBrush( bg );
			Paint.DrawRect( LocalRect, Theme.ControlRadius );
		}
		else
		{
			c = Color.White.WithAlpha( 0.5f );
		}

		Paint.SetDefaultFont();

		if ( !string.IsNullOrWhiteSpace( text ) )
		{
			var textSize = Paint.MeasureText( text );
			float iconSize = 16;
			float spacing = 4;

			float startX = 8;
			float centerY = LocalRect.Center.y;

			Paint.SetPen( c with { Value = 0.99f, Saturation = c.Saturation * 0.20f } );

			if ( !string.IsNullOrWhiteSpace( Icon ) )
			{
				var iconRect = new Rect( startX, centerY - iconSize / 2, iconSize, iconSize );
				Paint.DrawIcon( iconRect, Icon, iconSize );
				startX += iconSize + spacing;
			}

			Paint.SetPen( c with { Value = 0.99f, Saturation = c.Saturation * 0.20f, Alpha = 0.75f } );

			var textRect = new Rect( startX, LocalRect.Top, textSize.x, LocalRect.Height );
			Paint.DrawText( textRect, text, TextFlag.Center );
		}
	}

	/// <summary>Draw a dropdown control with a text label and an icon. Has a up/down chevron icon to indicate the dropdown state.</summary>
	public static void DrawDropdown( Rect rect, string text, string icon, bool open, bool disabled = false )
	{
		Paint.ClearPen();

		if ( open ) Paint.SetBrush( Theme.ControlBackground.LerpTo( Theme.Green, 0.05f ) );
		else Paint.SetBrush( Theme.ControlBackground );

		Paint.DrawRect( rect, Theme.ControlRadius );

		//cap off the bottom
		if ( open )
		{
			Paint.DrawRect( rect.Shrink( 0, 4, 0, 0 ) );
		}

		if ( disabled ) Paint.SetPen( Theme.Text.WithAlpha( 0.2f ) );
		else if ( open ) Paint.SetPen( Theme.Text );
		else if ( Paint.HasMouseOver ) Paint.SetPen( Theme.TextButton.WithAlpha( 0.7f ) );
		else Paint.SetPen( Theme.TextButton.WithAlpha( 0.5f ) );

		Paint.DrawIcon( rect.Shrink( 0, 0, 4, 0 ), open ? "expand_less" : "expand_more", rect.Height - 3, TextFlag.RightCenter );
		Paint.DrawIcon( rect.Shrink( 4, 0, 0, 0 ), icon, 16, TextFlag.LeftCenter );

		Paint.SetDefaultFont();
		Paint.DrawText( rect.Shrink( 30, 0, 26, 0 ), text, TextFlag.LeftCenter );
	}

	internal static string ParseVariables( string input )
	{
		var values = new Dictionary<string, string>();
		var fields = typeof( Theme ).GetFields();

		foreach ( var field in fields )
		{
			var value = field.GetValue( null );

			if ( value is Color color )
			{
				values.Add( $"${field.Name}", color.Hex );
			}
			else if ( value is float f )
			{
				values.Add( $"${field.Name}", $"{f}px" );
			}
			else if ( value is string s )
			{
				values.Add( $"${field.Name}", s );
			}
		}

		//Sort by length so we don't stomp stuff, e.g. $Surface and $SurfaceSubtle
		values = values.OrderByDescending( x => x.Key.Length ).ToDictionary();

		foreach ( var color in values )
		{
			input = input.Replace( color.Key, color.Value );
		}

		return input;
	}
}
