using Rinten;
using Rinten.Utility;
using System;

namespace Editor
{
	public partial class Window : Widget
	{
		public static List<Window> All = new();

		Widget _canvas;

		public Widget Canvas
		{
			get => _canvas;
			set
			{
				_canvas = value;

				_canvas?.SetPanelParent( this );
				ArrangeParts();
			}
		}

		public virtual MenuBar MenuBar
		{
			get
			{
				if ( MenuWidget is TitleBar tb ) return tb.MenuBar;
				if ( MenuWidget is MenuBar mb ) return mb;

				//One strip: the name, the menus and the window buttons.
				if ( OwnCaption )
				{
					MenuWidget = new TitleBar( this, this is EditorMainWindow );
					return ( MenuWidget as TitleBar ).MenuBar;
				}

				MenuWidget = new MenuBar( this );
				return MenuWidget as MenuBar;
			}

			set
			{
				MenuWidget = value;
			}
		}

		Widget _menu;
		public Widget MenuWidget
		{
			get => _menu;
			set
			{
				if ( _menu == value ) return;

				if ( _menu is MenuBar mb )
				{
					MenuBar = mb;
				}

				//The bar being replaced goes with it. Every window is given a title bar when it is made, so
				//a window that swaps in a bar of its own would otherwise keep both, one drawn over the other.
				var previous = _menu;

				_menu = value;

				_menu?.SetPanelParent( this );

				if ( previous.IsValid() ) previous.Destroy();

				ArrangeParts();
			}
		}

		StatusBar _status;

		public StatusBar StatusBar
		{
			get
			{
				return _status ??= new StatusBar( this );
			}
			set
			{
				if ( _status == value ) return;

				//destroy old one?

				_status = value;

				_status?.SetPanelParent( this );
				ArrangeParts();
			}
		}

		/// <summary>Initialises the window at the centre of the screen (or main editor window if one is present) by default.</summary>
		public bool StartCentered { get; set; } = true;

		public Window( Widget parent = null ) : base()
		{
			MakeSurface( parent );

			All.Add( this );
			Size = new Vector2( 1024, 720 );

			Init();
		}

		//Builds the title bar from the constructor, so every window gets a caption
		private void Init()
		{
			//All three buttons, because the bar now draws the ones the flags name and a window that never mentions them means the usual
			//three. One that does mention them - by setting WindowFlags outright - replaces this and gets exactly what it asked for.
			SetWindowFlag( WindowFlags.CloseButton | WindowFlags.MinMaxButtons, true );

			MenuWidget = new TitleBar( this, this is EditorMainWindow );

			//Not DeleteOnClose: windows that want it say so, and turning it on for every one of them would destroy the ones meant to be reopened.

			try
			{
				SetWindowIcon( "logo_rounded.png" );
			}
			catch ( System.Exception )
			{
				//A window with no icon is a window; one that threw on the way up is not.
			}
		}

		protected override void OnResize()
		{
			base.OnResize();

			//The three parts of the window are arranged here rather than by a toolkit, so the window changing shape is where that happens.
			ArrangeParts();

			//Force redraw the titlebar so that we can change the maximize button
			if ( MenuWidget is TitleBar tb )
			{
				tb.Update();
			}
		}

		private const WindowFlags DialogWindowFlags = WindowFlags.Customized | WindowFlags.Dialog;

		/// <summary>Whether this is a dialog: a window that sits over the one that opened it. Ours are all ordinary windows so far, so this is only remembered.</summary>
		public bool IsDialog
		{
			get => HasWindowFlag( DialogWindowFlags );
			set => SetWindowFlag( DialogWindowFlags, value );
		}

		public bool CloseButtonVisible
		{
			get => HasWindowFlag( WindowFlags.CloseButton );
			set => SetWindowFlag( WindowFlags.CloseButton, value );
		}

		/// <summary>What the window is called. The same as <see cref="Widget.WindowTitle"/>, which is what a widget that is not a window calls it.</summary>
		public string Title
		{
			get => WindowTitle;
			set => WindowTitle = value;
		}

		public override string WindowTitle
		{
			get => base.WindowTitle;
			set
			{
				base.WindowTitle = value;
				title = value;

				if ( shell is not null ) shell.Title = value;

				//The bar paints the name, so it has to be told the name changed.
				if ( MenuWidget is TitleBar bar ) bar.Update();
			}
		}

		public override void SetWindowIcon( string name )
		{
			base.SetWindowIcon( name );
			if ( MenuWidget is TitleBar tb ) tb.IconPixmap = Pixmap.FromFile( name );
		}

		public override void SetWindowIcon( Pixmap icon )
		{
			base.SetWindowIcon( icon );
			if ( MenuWidget is TitleBar tb ) tb.IconPixmap = icon;
		}

		public override void Show()
		{
			if ( !OpenShell() ) return;

			//A window drawn inside another one has no window of its own to name or to put on the screen: it is already in the one it belongs to.
			if ( shell is not null )
			{
				shell.Title = title;

				//Put where it opens before it is put up, not after: a window moved once it is on the screen is a window somebody watches jump.
				PlaceCentred();

				shell.Shown = true;
			}

			Visible = true;
			ArrangeParts();

			//Counted from being shown, as the dump always was.
			if ( !dumped && Rinten.Utility.CommandLine.HasSwitch( "-dumplayout" ) ) _ = DumpLayoutOnceSettled();
		}

		public override void Hide()
		{
			if ( shell is not null )
				shell.Shown = false;

			base.Hide();
		}

		public override void Close()
		{
			SaveToStateCookie();

			CloseInside();

			shell?.Dispose();
			shell = null;

			All.Remove( this );

			base.Close();
		}

		/// <summary>TODO this was a test, get rid of it</summary>
		public void Clear()
		{
			if ( !IsValid )
				return;

			if ( MenuBar is MenuBar mb )
			{
				mb.Clear();
			}

			Canvas?.Destroy();
			Canvas = null;
		}

		protected override void OnClosed()
		{
			SaveToStateCookie();
			base.OnClosed();
		}

		/// <summary>Where the toolbars are. main window kept its own arrangement in here; ours has nothing beyond what the dock manager already saves.</summary>
		public string SaveState( int version = 0 ) => string.Empty;

		public void RestoreState( string state )
		{
			return;
		}

		protected override void OnBlur( FocusChangeReason reason )
		{
			base.OnBlur( reason );

			SaveToStateCookie();
		}

		/// <summary>Position the window at the centre of the screen, or main editor window if one is present.</summary>
		public void Center() => CenterWindow();
	}
}
