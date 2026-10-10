using Rinten.UI;
using System;
using System.Text;

namespace Editor;

//There is deliberately no Panel.Tick here. A row is written to by the list that owns it, and a row that ticked would count against every panel above it, every frame, for every row on screen.
/// <summary>A row that a <see cref="PooledCellList{TCell}"/> puts over one index and then another, rather than building one per item.</summary>
public class PanelPooledRow : Panel
{
	private int band = -1;

	protected PanelPooledRow()
	{
	}

	/// <summary>Which line of the list this row is over.</summary>
	public int Index { get; set; } = -1;

	/// <summary>Pressed, with the row that was pressed.</summary>
	public Action<PanelPooledRow> Clicked { get; set; }

	/// <summary>Asked to give up what it says. Copies the row's own line when nothing takes this.</summary>
	public Action<PanelPooledRow> Copied { get; set; }

	//Takes precedence over Copied: a row that opens a menu should put "Copy" in the menu rather than have the same press do two things.
	/// <summary>Right-clicked, with the row it happened on - for a list whose rows have something to offer beyond being copied.</summary>
	public Action<PanelPooledRow> ContextMenu { get; set; }

	//Opt-in alternating band (styled in design.scss) to keep long lists readable
	protected void SetBand( int index )
	{
		if ( band == index ) return;

		band = index;

		AddClass( "list-row" );

		//Even is the banded one, the same way round as the hierarchy's rows.
		SetClass( "even", ( index & 1 ) == 0 );
	}

	/// <summary>Writes what this row says into a buffer, for taking away.</summary>
	public virtual void Write( StringBuilder into ) { }

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		Clicked?.Invoke( this );
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );

		if ( ContextMenu is not null )
		{
			ContextMenu( this );
			return;
		}

		if ( Copied is not null )
		{
			Copied( this );
			return;
		}

		var text = new StringBuilder();

		Write( text );

		if ( text.Length > 0 ) EditorUtility.Clipboard.Copy( text.ToString() );
	}
}
