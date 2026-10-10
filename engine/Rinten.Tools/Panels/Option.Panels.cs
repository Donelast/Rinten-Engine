using Rinten;
using System;

namespace Editor;

//An option is not a widget - it is what a menu draws a row from - so all it has to do here is remember what it says and what it does.
/// <summary>One line of a menu, without a QAction behind it.</summary>
public partial class Option
{
	private string text = string.Empty;
	private string icon;
	private string statusText;
	private string tooltip;
	private string shortcut;

	private bool checkable;
	private bool isChecked;
	private bool enabled = true;
	private bool isHeading;

	//A section title, not something to choose.
	internal bool IsHeading
	{
		get => isHeading;
		set => isHeading = value;
	}

	//Returned from Menu.AddHeading for callers that style it.
	internal Label HeadingLabel { get; set; }

	internal bool OnPanels => EditorBackend.Panels;

	//An option that is only itself, with nothing native behind it.
	private void MakeManaged( string title, string withIcon, Action action )
	{

		if ( title is not null ) Text = title;
		if ( withIcon is not null ) Icon = withIcon;
		if ( action is not null ) Triggered = action;
	}

	//What the row says on the left.
	internal string RowText => string.IsNullOrEmpty( text ) ? iconText : text;

	//What the row says on the right, or null.
	internal string RowShortcut => shortcut;

	//The icon on the row, or null.
	internal string RowIcon => icon;

	//The row's icon as a picture - an asset type's own tile - or null.
	internal Pixmap RowIconImage => IconImage;

	//Whether this is a line between groups rather than something to choose.
	internal bool IsSeparator => !isHeading && string.IsNullOrEmpty( RowText ) && string.IsNullOrEmpty( icon );

	//Where this row goes, so options and submenus keep the order they were added in.
	internal int Order { get; set; }

	//Somebody chose this option.
	internal void Choose()
	{
		if ( !enabled || isHeading ) return;

		if ( checkable ) Checked = !Checked;

		OnTriggeredInternal();
	}
}
