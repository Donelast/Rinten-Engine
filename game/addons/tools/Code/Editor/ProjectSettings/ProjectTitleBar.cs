using Editor.ProjectSettingPages;
using Editor.Utility;

namespace Editor;

file class ProjectTitleButton : Widget
{
	Project Project { get; init; }

	private const int HorizontalPadding = 8;
	private const int LogoSize = 32;
	private const int LogoShrink = 4;
	private const int LogoTextSpacing = 8;

	Pixmap _placeholderIcon;

	public ProjectTitleButton( Project project )
	{
		Project = project;
		FixedHeight = 32;

		Layout = Layout.Row();
		Layout.Margin = new( 8, 0 );
		Layout.Spacing = 0;
		Layout.Alignment = TextFlag.LeftCenter;
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();

		if ( Paint.HasMouseOver )
		{
			Paint.SetBrush( Theme.ButtonBackground.Lighten( 0.25f ) );
			Paint.DrawRect( LocalRect );
		}

		var contentRect = LocalRect.Shrink( HorizontalPadding, 0 );

		var logoRect = contentRect;
		logoRect.Width = contentRect.Height;
		logoRect = logoRect.Shrink( LogoShrink );

		_placeholderIcon ??= PlaceholderIcon.Generate( Project.Config.Title, 64 );

		Paint.BilinearFiltering = true;
		ProjectThumb.Draw( logoRect, Project, _placeholderIcon );
		Paint.BilinearFiltering = false;

		var textRect = contentRect;
		textRect.Left = logoRect.Right + LogoTextSpacing;

		Paint.ClearBrush();
		Paint.SetPen( Theme.Text );
		Paint.DrawText( textRect, Project.Config.Title, TextFlag.LeftCenter );
	}

	protected override Vector2 SizeHint() => CalculateSize();

	private Vector2 CalculateSize()
	{
		Paint.SetDefaultFont();
		var textSize = Paint.MeasureText( new Rect( 0, 0, 256, 32 ), Project.Config.Title, TextFlag.LeftCenter );
		var width = HorizontalPadding + LogoSize + LogoTextSpacing + textSize.Width + HorizontalPadding;

		return new Vector2( width, 32 );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		var menu = new Menu( this );
		menu.AddOption( $"Open in {CodeEditor.Title}", "integration_instructions", CodeEditor.OpenSolution );
		menu.AddOption( "Open in Explorer", "folder", () => EditorUtility.OpenFolder( Project.GetRootPath() ) );
		menu.AddSeparator();
		menu.AddOption( "Build Project...", "save_alt", () => ProjectSettingsWindow.OpenForProject( Project.Current, Editor.ProjectSettingPages.Categories.ProjectPage.PageTitle ) );

		menu.OpenNextTo( this, WidgetAnchor.BottomEnd );
	}

	[Event( "editor.titlebar.buttons.build" )]
	public static void OnBuildTitleBarButtons( TitleBarButtons titleBarButtons )
	{
		//The editor is up before any project is: the bar is built again once one is.
		if ( Project.Current is null ) return;

		//Discord and Patreon just before the project's name
		if ( CommunityLinks.Any )
			titleBarButtons.Add( new PanelWidget( null, new CommunityLinks() ) { FixedSize = new Vector2( 62, 32 ) } );

		titleBarButtons.Add( new ProjectTitleButton( Project.Current ) );
		titleBarButtons.AddButton( "settings", OpenProjectSettings );
	}

	private static void OpenProjectSettings()
	{
		ProjectSettingsWindow.OpenForProject( Project.Current );
	}

	int ContentHash() => HashCode.Combine( Project.GetThumbPath(), Project.Config?.Title );

	//ContentHash as the delegate SetContentHash takes, made once - a method group is a new delegate every frame.
	Func<int> contentHash;

	[EditorEvent.Frame]
	public void FrameUpdate()
	{
		if ( SetContentHash( contentHash ??= ContentHash, 0.5f ) )
		{
			Update();
			AdjustSize();
		}
	}
}
