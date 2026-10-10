using Rinten;

namespace Editor
{
	public partial class Window
	{
		string _stateCookie;

		/// <summary>A unique identifier for this window, to store the window state across sessions using the <see cref="Cookie">Cookie</see> library.</summary>
		public string StateCookie
		{
			get => _stateCookie;
			set
			{
				if ( _stateCookie == value ) return;
				_stateCookie = value;

				RestoreFromStateCookie();
			}
		}

		//Whether a window with no saved state opens maximized; only the main editor window does
		protected virtual bool OpensMaximized => false;

		/// <summary>Called whenever the window should restore its state via the <see cref="EditorCookie">EditorCookie</see> library, that was previously saved in <see cref="SaveToStateCookie"/>.<br/> You should use <see cref="StateCookie"/> in the cookie name.</summary>
		public virtual void RestoreFromStateCookie()
		{
			if ( string.IsNullOrWhiteSpace( StateCookie ) )
				return;

			var restored = EditorCookie.TryGet( $"Window.{StateCookie}.Rect", out Rect rect ) && RestoreGeometry( rect );

			if ( !restored )
			{
				Center();
			}

			//Nothing saved and this is a window that opens filling the screen: the first run of
			//the editor on a machine, which is the one run where there is nothing to put back.
			if ( EditorCookie.Get( $"Window.{StateCookie}.Maximized", !restored && OpensMaximized ) )
				SetMaximized( true );

			var state = EditorCookie.GetString( $"Window.{StateCookie}.State", null );
			if ( state != null ) RestoreState( state );
		}

		/// <summary>Called whenever the window should save its state via the <see cref="EditorCookie">EditorCookie</see> library, to be later restored in <see cref="RestoreFromStateCookie"/>. This is useful to carry data across game sessions.<br/> You should use <see cref="StateCookie"/> in the cookie name.</summary>
		[Event( "app.exit" )]
		public virtual void SaveToStateCookie()
		{
			if ( string.IsNullOrWhiteSpace( StateCookie ) )
				return;

			if ( !this.IsValid() )
				return;

			var state = SaveState();

			EditorCookie.SetString( $"Window.{StateCookie}.State", state );
			EditorCookie.Set( $"Window.{StateCookie}.Maximized", IsMaximized );

			//Only when plain windowed, so the rect always holds the last normal geometry
			if ( !IsMinimized && !IsMaximized )
			{
				EditorCookie.Set( $"Window.{StateCookie}.Rect", WindowGeometry() );
			}
		}
	

		//Puts the window back where it was, if that is still somewhere it can be. A saved rectangle
		//from a screen that is no longer there is no good, and the window is centred instead.
		private bool RestoreGeometry( Rect rect )
		{
			if ( rect.Width < 64 || rect.Height < 64 ) return false;

			if ( Shell is null ) return false;

			//Widget isn't sized here: units differ when scaled; Window.FollowShell reads the result back
			Shell.Placement = rect;

			return true;
		}

		//Where the window is on the desktop, and how big.
		private Rect WindowGeometry()
		{
			return Shell?.Placement ?? new Rect( Position, Size );
		}
	}
}
