using Rinten.UI;
using System;

namespace Editor;

/// <summary>The names of the columns over a <see cref="PanelListRow"/> list, laid out to the same shape so they line up with what is under them.</summary>
public class PanelListHeader : Panel
{
	//The left cell, over the names.
	protected PanelListHeaderCell Name { get; }

	public PanelListHeader( string name )
	{
		AddClass( "panel-list-header" );

		EditorStyles.Apply( this, "PanelListHeader.cs.scss" );

		Name = AddChild<PanelListHeaderCell>( "head" );
		Name.SetText( name );

		//Empty room where the bar is, so the columns line up with the rows.
		AddChild<Panel>( "bar" );
	}

	/// <summary>Adds a column heading, over the numbers.</summary>
	public PanelListHeaderCell AddColumn( string text )
	{
		var cell = AddChild<PanelListHeaderCell>( "col" );

		cell.SetText( text );

		return cell;
	}
}

/// <summary>One heading, which is also the button that does whatever it does.</summary>
public class PanelListHeaderCell : Panel
{
	private readonly Rinten.UI.Label label;

	/// <summary>Run when the heading is pressed.</summary>
	public Action Pressed { get; set; }

	public PanelListHeaderCell()
	{
		label = AddChild<Rinten.UI.Label>( "text" );
		label.Tokenize = false;
	}

	public void SetText( string text ) => PanelText.Set( label, text );

	/// <summary>Whether this heading is the one in force.</summary>
	public void SetActive( bool active ) => SetClass( "active", active );

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		Pressed?.Invoke();
	}
}
