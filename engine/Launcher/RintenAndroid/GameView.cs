using Android.Content;
using Android.Runtime;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using NativeEngine;
using Rinten;

namespace Rinten.Android;

//Game surface; acts as a text editor for the soft keyboard only while a game field wants text
internal sealed class GameView : SurfaceView
{
	public GameView( Context context ) : base( context )
	{
		Focusable = true;
		FocusableInTouchMode = true;
	}

	//Whether a text field in the game has the keyboard.
	public bool Typing { get; private set; }

	//Shows the on-screen keyboard or puts it away. On the activity's thread.
	public void ShowKeyboard( bool shown )
	{
		Typing = shown;

		if ( Context?.GetSystemService( Context.InputMethodService ) is not InputMethodManager keyboard ) return;

		if ( shown )
		{
			RequestFocus();

			//A new connection for a new field, so nothing of the last one's half-typed word carries over.
			keyboard.RestartInput( this );
			keyboard.ShowSoftInput( this, ShowFlags.Implicit );
		}
		else
		{
			keyboard.HideSoftInputFromWindow( WindowToken, HideSoftInputFlags.None );
		}
	}

	public override bool OnCheckIsTextEditor() => Typing;

	public override IInputConnection OnCreateInputConnection( EditorInfo attributes )
	{
		//Plain text, no word suggestions to compose into, and the keyboard over the game rather than a full-screen editor of its own on a phone held sideways.
		attributes.InputType = InputTypes.ClassText | InputTypes.TextFlagNoSuggestions;
		attributes.ImeOptions = ImeFlags.NoExtractUi | ImeFlags.NoFullscreen | (ImeFlags)ImeAction.Done;

		return new Connection( this );
	}

	//Keyboard input as engine text; composing changes are sent as backspaces plus the new tail
	private sealed class Connection : BaseInputConnection
	{
		//The word being composed, as far as it has been sent.
		private string composing = "";

		public Connection( View view ) : base( view, false ) { }

		public override bool CommitText( Java.Lang.ICharSequence text, int newCursorPosition )
		{
			Replace( text?.ToString() ?? "" );
			composing = "";
			return true;
		}

		public override bool SetComposingText( Java.Lang.ICharSequence text, int newCursorPosition )
		{
			var now = text?.ToString() ?? "";
			Replace( now );
			composing = now;
			return true;
		}

		public override bool FinishComposingText()
		{
			composing = "";
			return true;
		}

		public override bool DeleteSurroundingText( int beforeLength, int afterLength )
		{
			for ( var i = 0; i < beforeLength; i++ ) Press( ButtonCode.KEY_BACKSPACE );
			for ( var i = 0; i < afterLength; i++ ) Press( ButtonCode.KEY_DELETE );

			return true;
		}

		public override bool PerformEditorAction( [GeneratedEnum] ImeAction editorAction )
		{
			Press( ButtonCode.KEY_ENTER );
			return true;
		}

		//The word being composed becomes now: backspaces over where they differ, then the rest.
		private void Replace( string now )
		{
			var same = 0;
			while ( same < composing.Length && same < now.Length && composing[same] == now[same] ) same++;

			for ( var i = same; i < composing.Length; i++ ) Press( ButtonCode.KEY_BACKSPACE );

			if ( same < now.Length ) CoreInput.HostText( now[same..] );
		}

		private static void Press( ButtonCode code )
		{
			CoreInput.HostKey( code, true );
			CoreInput.HostKey( code, false );
		}
	}
}
