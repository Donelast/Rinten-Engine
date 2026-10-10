using Rinten.UI;

namespace Editor;

/// <summary>The button on the game view's bar that forces a resolution or an aspect, and says what the picture actually came out as.</summary>
[StyleSheet( "ResolutionModePanel.cs.scss" )]
public sealed class ResolutionModePanel : EditorPanel
{
	/// <summary>The view whose picture this sizes.</summary>
	public SceneViewPanel SceneView { get; set; }

	private readonly IconPanel icon;
	private readonly Rinten.UI.Label title;
	private readonly Rinten.UI.Label size;

	public ResolutionModePanel()
	{
		AddClass( "resolution-mode" );

		Tooltip = "Force a specific screen resolution or aspect ratio";

		icon = new IconPanel();
		AddChild( icon );

		title = new Rinten.UI.Label();
		title.AddClass( "title" );
		AddChild( title );

		size = new Rinten.UI.Label();
		size.AddClass( "size" );
		AddChild( size );

		var chevron = new IconPanel { Text = "arrow_drop_down" };
		chevron.AddClass( "chevron" );
		AddChild( chevron );
	}

	public override void Tick()
	{
		base.Tick();

		var mode = ResolutionModes.Current;

		icon.Text = mode.Icon;
		title.Text = mode.Title;
		size.Text = PictureSize();
	}

	private string PictureSize()
	{
		var viewport = SceneView?.GetGameTarget();
		if ( viewport is null || !viewport.Renderer.IsValid() ) return string.Empty;

		var picture = viewport.Renderer.ScreenSize;

		return $"{picture.x:0}x{picture.y:0}";
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		var menu = new ContextMenu( HostWidget );

		foreach ( var mode in ResolutionModes.All )
		{
			menu.AddOption( mode.Title, mode.Icon, () => ResolutionModes.Pick( SceneView, mode ) );
		}

		menu.OpenAt( ScreenRect.BottomLeft, false );

		e.StopPropagation();
	}
}
