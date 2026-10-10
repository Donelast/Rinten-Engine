using Rinten.Engine;
using Rinten.Internal;
using Rinten.Modals;
using Rinten.Rendering;
using Rinten.UI;

namespace Rinten;

//Holds onto a list of root panels to tick, input and draw
internal partial class UISystem
{
	internal PanelRenderer Renderer = new();

	internal PanelInput Input { get; set; } = new();

	internal readonly CommandList GlobalCommandList = new();

	internal List<RootPanel> RootPanels = new();
	internal List<Panel> DeletionList = new();
	internal InputEventQueue InputEventQueue = new();

	//focus
	internal Panel CurrentFocus { get; set; }
	internal Panel NextFocus { get; set; }
	internal bool FocusPendingChange { get; set; }

	internal void AddRoot( RootPanel rootPanel )
	{
		if ( RootPanels.Contains( rootPanel ) )
			throw new System.Exception( "Adding root panel twice" );

		RootPanels.Add( rootPanel );
	}

	internal void RemoveRoot( RootPanel rootPanel )
	{
		RootPanels.Remove( rootPanel );
	}

	internal void DeleteAllRoots()
	{
		var deleteList = new List<RootPanel>( RootPanels );
		foreach ( var rootPanel in deleteList )
		{
			rootPanel.Delete( true );

			//User can override Delete/OnDelete, so let's make sure we always remove from lists
			rootPanel.RemoveFromLists();
			RootPanels.Remove( rootPanel );
		}

		RunDeferredDeletion( true );
		DeletionList.Clear();
	}

	internal void Render( float opacity = 1.0f )
	{
		using ( Performance.Scope( "Execute Command Lists" ) )
		{
			Graphics.Attributes.SetCombo( "D_WORLDPANEL", 0 );
			GlobalCommandList.ExecuteOnRenderThread();
		}
	}

	internal void CombineCommandLists()
	{
		GlobalCommandList.Reset();

		for ( int i = RootPanels.Count - 1; i >= 0; i-- )
		{
			var root = RootPanels[i];
			if ( !root.IsValid ) continue;
			if ( root.RenderedManually || root.IsWorldPanel ) continue;
			if ( root.ExternallyDriven ) continue;

			GlobalCommandList.InsertList( root.PanelCommandList );
		}
	}

	internal void Simulate( bool allowMouseInput )
	{
		using ( Performance.Scope( "Update Screen Size" ) )
		{
			Screen.UpdateFromEngine();
		}

		using ( Performance.Scope( "Tick Panels" ) )
		{
			TickPanels();
		}

		using ( Performance.Scope( "Tick Input" ) )
		{
			TickInput( allowMouseInput );
		}

		using ( Performance.Scope( "Pre Layout" ) )
		{
			PreLayout();
		}

		using ( Performance.Scope( "Deferred Deletion" ) )
		{
			RunDeferredDeletion();
		}

		using ( Performance.Scope( "Layout" ) )
		{
			Layout();
		}

		using ( Performance.Scope( "Post Layout" ) )
		{
			PostLayout();
		}

		using ( Performance.Scope( "Deferred Deletion" ) )
		{
			RunDeferredDeletion();
		}

		//The rest builds legacy-style descriptors and command lists, unused when our renderer draws
	}

	internal void DirtyAllStyles()
	{
		for ( int i = RootPanels.Count() - 1; i >= 0; i-- )
		{
			if ( !RootPanels[i].IsValid ) continue;
			RootPanels[i].DirtyStylesRecursive();
		}
	}

	//The loops below skip ExternallyDriven roots (editor windows), which tick and lay out themselves

	internal void TickPanels()
	{
		RootPanels.RemoveAll( x => x == null );

		for ( int i = 0; i < RootPanels.Count; i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			if ( RootPanels[i].ExternallyDriven ) continue;
			RootPanels[i].TickInternal();
		}
	}

	internal void PreLayout()
	{
		var width = Screen.Width;
		var height = Screen.Height;

		var screenRect = new Rect( 0, 0, width, height );

		for ( int i = 0; i < RootPanels.Count; i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			if ( RootPanels[i].ExternallyDriven ) continue;
			RootPanels[i].PreLayout( screenRect );
		}
	}

	internal void Layout()
	{
		for ( int i = 0; i < RootPanels.Count; i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			if ( RootPanels[i].ExternallyDriven ) continue;
			RootPanels[i].CalculateLayout();
		}
	}

	internal void PostLayout()
	{
		for ( int i = 0; i < RootPanels.Count; i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			if ( RootPanels[i].ExternallyDriven ) continue;
			RootPanels[i].PostLayout();
		}
	}

	internal void BuildDescriptors()
	{
		for ( int i = 0; i < RootPanels.Count; i++ )
		{
			var root = RootPanels[i];
			if ( !root.IsValid ) continue;
			if ( root.ExternallyDriven ) continue;

			root.BuildDescriptors();
		}
	}

	internal void BuildCommandLists()
	{
		Renderer.AdvanceFrame();

		for ( int i = 0; i < RootPanels.Count; i++ )
		{
			var root = RootPanels[i];
			if ( !root.IsValid ) continue;
			if ( root.RenderedManually && !root.IsWorldPanel ) continue;
			if ( root.ExternallyDriven ) continue;

			root.BuildCommandList();
		}
	}

	internal void TickInput( bool allowMouseInput )
	{
		for ( int i = 0; i < RootPanels.Count; i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			if ( RootPanels[i].ExternallyDriven ) continue;
			RootPanels[i].TickInputInternal();
		}

		//Tick various input systems. An editor window's tree is not the game's to point at - it has a pointer of its own,
		//and hover-testing the whole editor every frame was work spent finding panels the game must not touch anyway.
		GameInputOrder();
		Input.Tick( inputOrder, allowMouseInput && DoAnyPanelsWantMouseVisible() );

		TickWorldInput();

		//We tick focus here, after the layout. This way any styles that were set at the same time as
		//changing focus will be applied so that when we judge elibility the logic will be correct
		InputFocus.Tick();

		//Send all key events to the focused panel
		InputEventQueue.TickFocused( CurrentFocus );

		//Pass our global input events ( mouse move, double click ) to 2d panels WorldInputs simulate this themselves in WorldInputInternal.Tick
		InputEventQueue.Tick( Input.Hovered, Input.Active );

		//Set mouse delta to 0 so it doesn't repeat the last frame's delta on the next frame
		Mouse.Frame();

		bool inGame = InputRouter.GameRunning;

		var mouseState = Rinten.Engine.InputContext.InputState.Ignore;
		var buttonState = Rinten.Engine.InputContext.InputState.Ignore;

		if ( Game.IsMenu )
		{
			if ( !inGame )
			{
				mouseState = InputContext.InputState.UI;
				buttonState = Rinten.Engine.InputContext.InputState.Game;

				if ( CurrentFocus is not null )
					buttonState = CurrentFocus.ButtonInput == PanelInputType.Game ? InputContext.InputState.Game : InputContext.InputState.UI;
			}

			//A modal is open
			if ( (IModalSystem.Current?.HasModalsOpen() ?? false) )
			{
				mouseState = Rinten.Engine.InputContext.InputState.UI;
				buttonState = Rinten.Engine.InputContext.InputState.Game;

				if ( CurrentFocus is not null )
					buttonState = CurrentFocus.ButtonInput == PanelInputType.Game ? InputContext.InputState.Game : InputContext.InputState.UI;
			}

			//The loading screen is visible
			if ( Rinten.LoadingScreen.IsVisible )
			{
				mouseState = Rinten.Engine.InputContext.InputState.UI;
				buttonState = Rinten.Engine.InputContext.InputState.UI;
			}

			//The developer console / chat is open
			if ( IMenuSystem.Current?.ForceCursorVisible ?? false )
			{
				mouseState = Rinten.Engine.InputContext.InputState.UI;
				buttonState = Rinten.Engine.InputContext.InputState.UI;
			}
		}

		//If we're a game menu, and there is no client - then treat all input as game input by default.
		if ( !Game.IsMenu )
		{
			mouseState = Rinten.Engine.InputContext.InputState.Game;
			buttonState = Rinten.Engine.InputContext.InputState.Game;

			if ( DoAnyPanelsWantMouseVisible() )
				mouseState = InputContext.InputState.UI;

			if ( CurrentFocus is not null )
				buttonState = CurrentFocus.ButtonInput == PanelInputType.Game ? InputContext.InputState.Game : InputContext.InputState.UI;

			//No input if we're not playing
			if ( !inGame )
			{
				mouseState = InputContext.InputState.UI;
				buttonState = InputContext.InputState.UI;
			}

			if ( Application.IsEditor && !Game.IsPlaying )
			{
				mouseState = InputContext.InputState.Ignore;
				buttonState = InputContext.InputState.Ignore;
			}

			//The player asked an editor for the game - clicked into the view it is being played in. Then it is the game's keyboard and mouse
			//whatever else is loaded: a scene played on its own has no game instance, and would otherwise be a scene played with no input.
			if ( Application.IsEditor && Game.IsPlaying && InputRouter.GameHasInput )
			{
				mouseState = InputContext.InputState.Game;
				buttonState = InputContext.InputState.Game;

				if ( DoAnyPanelsWantMouseVisible() )
					mouseState = InputContext.InputState.UI;

				//Only text-input panels take the keyboard; otherwise keys still go to the game
				if ( CurrentFocus is { AcceptsImeInput: true } )
					buttonState = InputContext.InputState.UI;
				else if ( CurrentFocus is not null && CurrentFocus.ButtonInput != PanelInputType.Game )
					buttonState = InputContext.InputState.Game;
			}
		}

		Game.InputContext.UpdateInputFromUI( mouseState, Input.Hovered, Panel.MouseCapture is not null, buttonState, CurrentFocus );
	}

	//The game's own panels in the order they take the pointer - see GameInputOrder.
	private readonly List<RootPanel> inputOrder = new();

	//Game input panels by layer, highest first, kept between frames; stable insertion sort, no LINQ
	private void GameInputOrder()
	{
		inputOrder.Clear();

		for ( int i = 0; i < RootPanels.Count; i++ )
		{
			var panel = RootPanels[i];
			if ( panel.IsWorldPanel || panel.ExternallyDriven ) continue;

			//A screen panel in a headset is a sheet in front of the head, and the controllers point at it rather than the mouse - see TickWorldInput.
			if ( panel is GameRootPanel { InHeadset: true } ) continue;

			var layer = panel.ComputedStyle?.ZIndex ?? 0;
			var at = inputOrder.Count;

			while ( at > 0 && ( inputOrder[at - 1].ComputedStyle?.ZIndex ?? 0 ) < layer ) at--;

			inputOrder.Insert( at, panel );
		}
	}

	void TickWorldInput()
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		//The world's panels, and in a headset the screen's too: there they are sheets in front of the head, pointed at the same way - see ScreenPanel.
		pointedAt.Clear();

		foreach ( var worldPanel in scene.GetAllComponents<WorldPanel>() )
		{
			if ( worldPanel.GetPanel() is RootPanel root ) pointedAt.Add( root );
		}

		foreach ( var screen in scene.GetAllComponents<ScreenPanel>() )
		{
			if ( screen.GetPanel() is GameRootPanel { InHeadset: true } root && root.IsVisible ) pointedAt.Add( root );
		}

		var gameHasPointers = false;

		foreach ( var worldInput in scene.GetAllComponents<WorldInput>() )
		{
			gameHasPointers = true;
			worldInput.WorldPanelInput.Tick( pointedAt, true );
		}

		//A headset with nothing of the game's pointing: the controllers do it.
		HeadsetPointers.Tick( scene, pointedAt, gameHasPointers );
	}

	//What the pointers in the world are tested against this frame, kept between frames.
	private readonly List<RootPanel> pointedAt = new();

	bool DoAnyPanelsWantMouseVisible()
	{
		if ( Mouse.Visibility == MouseVisibility.Visible ) return true;
		if ( Mouse.Visibility == MouseVisibility.Hidden && !Game.IsMenu ) return false;

		for ( int i = 0; i < RootPanels.Count; i++ )
		{
			if ( !RootPanels[i].IsValid )
				continue;

			if ( !RootPanels[i].IsVisible )
				continue;

			if ( RootPanels[i].IsWorldPanel )
				continue;

			//A sheet in a headset is pointed at with the controllers, not the mouse.
			if ( RootPanels[i] is GameRootPanel { InHeadset: true } )
				continue;

			//An editor window always wants the pointer; that says nothing about whether the game's cursor should be shown
			if ( RootPanels[i].ExternallyDriven )
				continue;

			if ( !RootPanels[i].ChildrenWantMouseInput )
				continue;

			if ( Game.IsMenu && RootPanels[i].RenderedManually && !Game.IsMainMenuVisible ) continue;

			return true;
		}

		return false;
	}

	//This panel should get deleted at some point
	internal void AddDeferredDeletion( Panel panel )
	{
		Assert.NotNull( panel );

		DeletionList.Add( panel );
	}

	//Delete all panels that were deferred and are no longer playing outro transitions
	internal void RunDeferredDeletion( bool force = false )
	{
		for ( int i = 0; i < DeletionList.Count; i++ )
		{
			var p = DeletionList[i];

			//Hotload can clear the reference; an ancestor can finish deletion before this outro.
			if ( !p.IsValid() )
			{
				DeletionList.RemoveAt( i );
				i--;
				continue;
			}

			if ( !force && p.HasActiveTransitions ) continue;

			try
			{
				p.Delete( true );
			}
			catch ( System.Exception ex )
			{
				Log.Warning( ex, $"Exception while deferred-deleting {p}" );
			}

			DeletionList.RemoveAt( i );
			i--;
		}
	}

	internal void OnHotload()
	{
		for ( int i = 0; i < RootPanels.Count(); i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			RootPanels[i].OnHotloaded();
		}
	}

	internal void OnLanguageChanged()
	{
		for ( int i = 0; i < RootPanels.Count(); i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			RootPanels[i].LanguageChanged();
		}
	}

	internal void Clear()
	{
		//Clear any dangling tooltip panel references before destroying the tree.
		TooltipSystem.Clear();

		//Delete immediately on shutdown; deferred deletion needs a next frame that never comes
		foreach ( var rp in RootPanels.ToArray() )
		{
			//Skip editor windows; they belong to their own window, not the game's teardown
			if ( rp.ExternallyDriven ) continue;

			try
			{
				rp.Delete( immediate: true );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}
		}

		RootPanels.Clear();
		DeletionList.Clear();

		//Drop the entire input subsystem — replaces it wholesale so we don't need to chase
		//individual panel references inside PanelInput, MouseButtonState, InputEventQueue, etc.
		Input = new();
		InputEventQueue = new();
		Renderer = new();
		CurrentFocus = null;
		NextFocus = null;
	}
}
