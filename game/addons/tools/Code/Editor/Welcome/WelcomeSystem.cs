namespace Editor;

//The interactive guide a first-time user is shown when a project first opens - see WelcomeGuide
internal static class WelcomeSystem
{
	static readonly List<WelcomeStep> steps = new();
	static WelcomeGuide guide;
	static Widget mainWindow;

	static WelcomeSystem()
	{
		InitializeDefaultSteps();
	}

	static void InitializeDefaultSteps()
	{
		steps.Clear();

		steps.Add( new WelcomeStep(
			"Press <strong>Next</strong> to follow a brief interactive guide to get more familiar with the editor.<br /><br />It will run through what each part of the interface does, and is highly recommended for beginners!"
		) );

		steps.Add( new WelcomeStep(
			"This is the <strong>Main Viewport</strong>, your view into the current scene.<br /><br />Here you can manipulate the <strong>GameObjects</strong> in your scene to build whatever you want!<br /><br />To look around, hold RMB and use WASD while holding RMB to move around.",
			() => SceneDock.Active
		) );

		steps.Add( new WelcomeStep(
			"This is the <strong>Hierarchy</strong>, where all the GameObjects in your scene live.<br /><br />Here you can search through and organize all the objects in the scene.<br /><br />You can also <strong>create new GameObjects</strong> via the + button.",
			() => SceneTreeWidget.Current
		) );

		steps.Add( new WelcomeStep(
			"This is the <strong>Inspector</strong>, which will show you the properties of the selected GameObject.<br /><br />You can also <strong>add and remove components</strong> from here, and edit their properties as well. Components are what tell a GameObject what to do (like render a model with the Model Renderer, or play sounds with a Sound Point).<br /><br />You can make custom Components with C# to control GameObjects in whatever ways you want!",
			() => FindInspector(),
			onEnter: () => InspectMainCamera()
		) );

		steps.Add( new WelcomeStep(
			"This is the <strong>Asset Browser</strong>, where you can browse all the assets in your project.<br /><br />This is essentially the file explorer for this specific project, allowing you to manage your assets as well as the assets from any libraries you may have added.<br /><br />You can <strong>create new assets</strong> in the current folder using the \"New\" button!",
			() => EditorWindow.DockManager.FindDockWidget( "Asset Browser" )?.Widget,
			onEnter: () => RaiseAssetBrowser()
		) );


		steps.Add( new WelcomeStep(
			"When you're ready to test things out in-game, click the <strong>Play button</strong> (or <strong>F5</strong>) to instantly enter Play Mode!<br /><br />While playing, you can press <strong>ESC</strong> to free the mouse, and use the <strong>Pause and Stop buttons</strong> to control execution, or simply press <strong>F5</strong> to instantly exit Play Mode.<br /><br />Press the <strong>Eject button</strong> while the game is playing to view the game's current scene in the editor view.",
			() => FindPlayButton()
		) );

		steps.Add( new WelcomeStep(
			"Then come here to configure your <strong>project settings</strong>.<br /><br /><strong>Build Project</strong> from here to make a standalone build you can run and hand out.",
			() => FindTitleBarButtons()
		) );

		steps.Add( new WelcomeStep(
			"That's it! You're ready to start creating in Rinten.<br /><br />Remember, you can always access this tutorial again from Help → Show Welcome Tutorial if you ever need a reminder."
		) );
	}

	//Shown on its own when a project first opens, for first-time users - not when the window first opens: that is the
	//editor with no project and the projects over it. Started from a frame, the project is opened between frames.
	static RealTimeUntil startIn;
	static bool startPending;

	[Event( "project.opened" )]
	static void OnProjectOpened( EditorMainWindow editorWindow )
	{
		if ( EditorCookie.Get( "editor.welcome.neverShowAgain", false ) )
			return;

		//Half a second for the editor layout to restore first
		startPending = true;
		startIn = 0.5f;
	}

	[EditorEvent.Frame]
	static void StartWhenReady()
	{
		if ( !startPending || !startIn || SceneDock.Active is null ) return;

		startPending = false;
		Start();
	}

	/// <summary>Start the welcome tutorial</summary>
	[Menu( "Editor", "Help/Show Welcome Tutorial", "waving_hand" )]
	public static void Start()
	{
		if ( guide.IsValid() ) return;

		mainWindow = SceneDock.Active?.GetWindow() ?? SceneTreeWidget.Current?.GetWindow();
		if ( mainWindow == null || !mainWindow.IsValid )
			return;

		if ( EditorOverlay.For( mainWindow ) is not { } layer )
			return;

		guide = layer.AddChild( new WelcomeGuide { Steps = steps } );
	}

	//The widgets the steps point at

	static Widget FindInspector()
	{
		var window = mainWindow.GetWindow();
		if ( window is DockWindow dockWindow )
		{
			return dockWindow.DockManager.FindDockWidget( "Inspector" )?.Widget;
		}

		return null;
	}

	static void InspectMainCamera()
	{
		//Find the Main Camera in the current scene and inspect it so there are values to play with in the Inspector step
		var sceneView = SceneViewPanel.Current;
		if ( sceneView == null || !sceneView.IsValid )
			return;

		var scene = sceneView.Session?.Scene;
		if ( scene == null || !scene.IsValid )
			return;

		var mainCameraObj = scene.Children.FirstOrDefault( x => x.Name == "Main Camera" );
		if ( mainCameraObj != null && mainCameraObj.IsValid )
		{
			EditorUtility.InspectorObject = mainCameraObj;
		}
	}

	static void RaiseAssetBrowser()
	{
		//Bring the browser to the front. There is one browser and it opens on its folders, so there is no tab to switch to any more.
		if ( mainWindow.GetWindow() is DockWindow dockWindow )
		{
			dockWindow.DockManager.RaiseDock( "Asset Browser" );
		}
	}

	//The tab the play button is on. The bar itself is markup, and what this highlights is a widget's rectangle.
	static Widget FindPlayButton() => SceneDock.Active;

	static Widget FindTitleBarButtons()
	{
		var window = mainWindow.GetWindow();
		if ( window == null || !window.IsValid )
			return null;

		return window.GetDescendants<Widget>().FirstOrDefault( w => w.GetType().Name == "TitleBarButtons" );
	}
}
