using System;

namespace Editor;

public partial class Dialog : Widget
{
	//These now open an EditorModal panel; TextDialog below is kept only for subclasses

	/// <summary>Ask for a string which is intended to be a new folder name.. which means it shouldn't have / or " or : etc.</summary>
	public static void AskStringFolder( Action<string> OnSuccess, string question, string okay = "Okay", string cancel = "Cancel", string initialName = "" )
		=> EditorModal.Prompt( null, "Select a folder", question, OnSuccess, okay, cancel, initialName );

	/// <summary>Ask for a string which is intended to be a new file name.. which means it shouldn't have / or " or : etc.</summary>
	public static void AskStringFile( Action<string> OnSuccess, string question, string okay = "Okay", string cancel = "Cancel", string initialName = "" )
		=> EditorModal.Prompt( null, "Select a file", question, OnSuccess, okay, cancel, initialName );

	/// <summary>Ask for a string</summary>
	public static void AskString( Action<string> OnSuccess, string question, string okay = "Okay", string cancel = "Cancel", string initialName = "", string title = "Input required", int minLength = 0 )
		=> EditorModal.Prompt( null, title, question, OnSuccess, okay, cancel, initialName, minLength );

	/// <summary>Ask for a confirmation</summary>
	public static void AskConfirm( Action OnSuccess, string question, string title = "Confirmation", string okay = "Okay", string cancel = "Cancel" )
		=> EditorModal.Confirm( null, title, question, OnSuccess, okay, cancel );

	/// <summary>Ask for a confirmation</summary>
	public static void AskConfirm( Action OnSuccess, Action OnCancel, string question, string title = "Confirmation", string okay = "Okay", string cancel = "Cancel" )
	{
		var modal = EditorModal.Confirm( null, title, question, OnSuccess, okay, cancel );

		if ( modal is not null ) modal.OnDismissed = OnCancel;
	}

	//A wrapper to more easily create dialog windows
	internal class TextDialog : Dialog
	{
		public Action<string> OnSuccess;
		public Action<string> OnCancel;

		public Button OkayButton { get; private set; }
		public Button CancelButton { get; private set; }
		public LineEdit LineEdit { get; private set; }
		public Label Label { get; private set; }

		public int MinLength { get; set; }

		bool WantsText = true;

		public TextDialog( bool wantsText = true )
		{
			Window.SetModal( true, true );

			Label = new Label( this );
			Label.WordWrap = true;
			Label.SetSizeMode( SizeMode.Default, SizeMode.Expand );

			LineEdit = new LineEdit( this );
			if ( wantsText )
			{
				LineEdit.TextEdited += x => Validate();
				LineEdit.ReturnPressed += Finish;
			}
			else
			{
				LineEdit.Visible = false;
				WantsText = false;
			}

			OkayButton = new Button( "Okay", this );
			OkayButton.SetProperty( "type", "primary" );
			OkayButton.Clicked = Okay;

			CancelButton = new Button( "Cancel", this );
			CancelButton.Clicked = Cancel;

			Layout = Layout.Column();

			var content = Layout.AddColumn();
			content.Margin = 16;
			content.Add( Label );
			if ( wantsText )
			{
				content.AddSpacingCell( 16 );
				content.Add( LineEdit );
			}

			var footer = Layout.AddRow();
			footer.Margin = 16;
			footer.Spacing = 8;
			footer.AddStretchCell();
			footer.Add( OkayButton );
			footer.Add( CancelButton );

			Validate();
		}

		//Called when text changes to revalidate the input, disable Okay button, etc.
		protected virtual void Validate()
		{
			var valid = true;
			if ( string.IsNullOrWhiteSpace( LineEdit.Text ) ) valid = false;
			if ( LineEdit.Text.Trim().Length < MinLength ) valid = false;
			if ( !WantsText ) valid = true;

			OkayButton.Enabled = valid;
		}

		//Called when the user presses the Okay button
		protected virtual void Okay()
		{
			OnSuccess?.Invoke( LineEdit.Text );
			Close();
		}

		//Called when the user presses the Cancel button
		protected virtual void Cancel()
		{
			OnCancel?.Invoke( LineEdit.Text );
			Close();
		}

		//Called when the user presses Enter in the text field
		protected virtual void Finish()
		{
			Validate();

			if ( !OkayButton.Enabled )
				return;

			Okay();
		}
	}

}
