using System;

namespace Editor
{
	public partial class TextCursor
	{

		public int Position
		{
			get => BlockManaged;
			set {}
		}

		public int BlockNumber => BlockManaged;
		public int ColumnNumber => 0;

		public string SelectedText => string.Empty;

		public void InsertHtml( string str ) {}
		public void InsertText( string str ) {}

		public bool HasSelection => false;

		public void RemoveSelectedText()
		{
			RemoveSelectionManaged();
		}

		public void ClearSelection()
		{
			return;
		}

		public int SelectionStart => 0;
		public int SelectionEnd => 0;

		public void SelectBlockUnderCursor()
		{
			SelectBlockManaged();
		}
	}
}

internal enum QTextCursorSelectionType
{
	WordUnderCursor,
	LineUnderCursor,
	BlockUnderCursor,
	Document
}
