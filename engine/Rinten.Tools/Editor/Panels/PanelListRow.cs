using Rinten.UI;
using System;
using System.Text;

namespace Editor;

/// <summary>Diagnostic list row: indented name, a bar, then right-aligned fixed-width number columns</summary>
public class PanelListRow : PanelPooledRow
{
	//How far a row is pushed in per level of depth, in layout units.
	private const float Indent = 14.0f;

	private readonly Panel head;
	private readonly Rinten.UI.Label chevron;
	private readonly Rinten.UI.Label name;
	private readonly Panel fill;
	private readonly Rinten.UI.Label[] columns;
	private readonly Panel[] cells;

	private int depth = -1;
	private float portion = -1.0f;

	/// <summary>How many number columns this row has.</summary>
	public int ColumnCount => columns.Length;

	public PanelListRow( int columns )
	{
		AddClass( "panel-list-row" );

		EditorStyles.Apply( this, "PanelListRow.cs.scss" );

		head = AddChild<Panel>( "head" );

		chevron = head.AddChild<Rinten.UI.Label>( "chevron" );
		chevron.Tokenize = false;

		name = head.AddChild<Rinten.UI.Label>( "name" );
		name.Tokenize = false;

		var bar = AddChild<Panel>( "bar" );
		fill = bar.AddChild<Panel>( "fill" );

		this.columns = new Rinten.UI.Label[columns];
		cells = new Panel[columns];

		//A column is a panel with a label in it rather than the label itself: a label is measured by its own text, and a width on one is a suggestion.
		for ( var at = 0; at < columns; ++at )
		{
			cells[at] = AddChild<Panel>( "col" );

			this.columns[at] = cells[at].AddChild<Rinten.UI.Label>( "text" );
			this.columns[at].Tokenize = false;
		}
	}

	//The panel a column's text sits in, for a class the sheet colours.
	protected Panel Cell( int at ) => cells[at];

	//One of the number columns, counted from the left.
	protected Rinten.UI.Label Column( int at ) => columns[at];

	//What the row is called.
	protected void SetName( string text ) => PanelText.Set( name, text );

	//How far in the name sits.
	protected void SetDepth( int value )
	{
		if ( depth == value ) return;

		depth = value;

		head.Style.PaddingLeft = Length.Pixels( value * Indent );
	}

	//The arrow before the name, where there is more under it.
	protected void SetChevron( bool foldable, bool folded )
	{
		SetClass( "foldable", foldable );
		SetClass( "folded", folded );

		PanelText.Set( chevron, foldable ? ( folded ? "chevron_right" : "expand_more" ) : string.Empty );
	}

	//Whether this row is a heading rather than a reading.
	protected void SetSection( bool section ) => SetClass( "section", section );

	//How much of the bar is filled, as a scale rather than a width: a transform
	//keeps the row out of the layout pass, and this moves several times a second.
	protected void SetBar( float value )
	{
		value = Math.Clamp( value, 0.0f, 1.0f );

		if ( MathF.Abs( portion - value ) < 0.002f ) return;

		portion = value;

		var transform = new PanelTransform();
		transform.AddScale( new Vector3( value, 1.0f, 1.0f ) );

		fill.Style.Transform = transform;
	}

	//Empties every number column from a point rightward.
	protected void ClearColumns( int from = 0 )
	{
		for ( var at = from; at < columns.Length; ++at ) PanelText.Set( columns[at], string.Empty );
	}

	public override void Write( StringBuilder into )
	{
		into.Append( ' ', Math.Max( 0, depth ) * 2 ).Append( name.Text );

		for ( var at = 0; at < columns.Length; ++at )
		{
			if ( string.IsNullOrEmpty( columns[at].Text ) ) continue;

			into.Append( '\t' ).Append( columns[at].Text );
		}
	}
}
