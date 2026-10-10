using System;

namespace Editor
{
	public partial class LineEdit
	{
		List<string> historyEntries = new();
		AutoComplete historyMenu { get; set; }

		/// <summary>True if history menu is visible</summary>
		public bool HistoryVisible => historyMenu?.Visible ?? false;

		/// <summary>if set > 1 we will support history items (which you need to add using AddHistory)</summary>
		public int MaxHistoryItems { get; set; }


		/// <summary>Up/down cycle through history in the field like a console, instead of opening a list; off by default</summary>
		public bool HistoryCycles { get; set; }

		//How far back through the history the field is standing, or minus one when it is showing what somebody is typing.
		int historyAt = -1;

		//What was in the field before walking back into the history.
		string historyTyped;

		public void AddHistory( string text )
		{
			if ( MaxHistoryItems <= 0 ) return;

			historyEntries.RemoveAll( x => x == text );
			historyEntries.Add( text );

			while ( historyEntries.Count > MaxHistoryItems )
				historyEntries.RemoveAt( 0 );

			//Back to the near end of it. Running a line is the end of walking through the old ones, and the next up should be what was just run.
			historyAt = -1;
			historyTyped = null;

			SaveHistoryCookie();
		}

		//Steps one entry back (back) or forward through the history, putting it in the field. False where there was nowhere to go.
		internal bool CycleHistory( bool back )
		{
			if ( historyEntries.Count == 0 ) return false;

			if ( back )
			{
				//The first step remembers whatever was half-typed, so coming back down returns it rather than an empty field.
				if ( historyAt < 0 )
				{
					historyTyped = Text;
					historyAt = historyEntries.Count - 1;
				}
				else if ( historyAt > 0 )
				{
					historyAt--;
				}
				else
				{
					//Already at the oldest. Stay there rather than wrapping round to the newest, which loses your place.
					return true;
				}
			}
			else
			{
				if ( historyAt < 0 ) return false;

				historyAt++;

				if ( historyAt >= historyEntries.Count )
				{
					historyAt = -1;

					Text = historyTyped ?? string.Empty;
					historyTyped = null;

					CursorPosition = Text.Length;
					return true;
				}
			}

			Text = historyEntries[historyAt];

			//At the end of it, because the usual reason for calling one back is to run it again with one more word on the end.
			CursorPosition = Text.Length;

			return true;
		}

		void OpenHistory()
		{
			if ( historyMenu == null )
			{
				historyMenu = new AutoComplete( this );
				historyMenu.OnOptionSelected = ( o ) =>
					{
						historyMenu.Visible = false;
						Text = o;
					};
				historyMenu.OnBuildOptions = BuildHistoryOptions;
			}

			historyMenu?.OnAutoComplete( null, ScreenPosition );
		}

		void BuildHistoryOptions( Menu menu, string partial )
		{
			foreach ( var item in historyEntries )
			{
				menu.AddOption( item );
			}
		}
	}
}
