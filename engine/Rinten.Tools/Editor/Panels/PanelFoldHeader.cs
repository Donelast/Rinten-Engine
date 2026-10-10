using Rinten.UI;
using System;

namespace Editor;

/// <summary>Shared foldable section header (chevron, title, click to toggle); subclasses add the extras</summary>
public class PanelFoldHeader : Panel
{
	/// <summary>What the section is called.</summary>
	public string Title
	{
		get;
		set
		{
			field = value;

			if ( titleLabel.IsValid() )
				titleLabel.Text = field;
		}
	}

	/// <summary>A material icon shown before the title, where there is one.</summary>
	public string Icon
	{
		get;
		set
		{
			field = value;

			if ( !iconLabel.IsValid() ) return;

			iconLabel.Text = field ?? "";
			iconLabel.SetClass( "hidden", string.IsNullOrWhiteSpace( field ) );
		}
	}

	/// <summary>Whether what is under it is showing.</summary>
	public bool Expanded { get; set; } = true;

	/// <summary>Told when the header is clicked, with the state it has just moved to.</summary>
	public Action<bool> OnExpandedChanged;

	/// <summary>The plate the chevron sits in. Readable from outside because what hangs off a header is drawn against it - a property group runs the line joining it to its rows from the bottom of this.</summary>
	public Panel Chevron { get; private set; }

	private Rinten.UI.Label chevronLabel;
	private Rinten.UI.Label iconLabel;
	private Rinten.UI.Label titleLabel;

	public PanelFoldHeader()
	{
		//Not [StyleSheet]: the sheet ships inside this assembly rather than sitting beside the file, and
		//that attribute looks for a file. Every other panel the toolkit ships loads its own the same way.
		EditorStyles.Apply( this, "PanelFoldHeader.cs.scss" );

		AddClass( "panelfoldheader" );
		AddClass( "groupheader" );

		Chevron = AddChild<Panel>( "chevron" );

		chevronLabel = Chevron.AddChild<Rinten.UI.Label>( "icon" );
		chevronLabel.AddClass( "material-icon" );

		iconLabel = AddChild<Rinten.UI.Label>( "lead-icon" );
		iconLabel.AddClass( "material-icon" );
		iconLabel.AddClass( "hidden" );

		titleLabel = AddChild<Rinten.UI.Label>( "title" );

		SyncContentVisible( Expanded );
	}

	//Taken rather than read off Expanded, because what is under a header is not always shown just because the header says open - a group with nothing left in it after filtering is closed whatever it was.
	/// <summary>Says which way the chevron points, and lets the sheet style the header by whether it is open.</summary>
	public virtual void SyncContentVisible( bool visible )
	{
		SetClass( "expanded", visible );

		if ( chevronLabel.IsValid() )
			chevronLabel.Text = visible ? "remove" : "add";
	}

	//Whether a click landing here should fold the section, or belongs to something a subclass put on the bar. A group
	//whose header carries the checkbox that enables it is the case: ticking the box is not asking for the group to close.
	protected virtual bool IsFoldingClick( Panel target ) => true;

	//What a right-click on the bar opens, where a subclass has one.
	protected virtual void OnContextMenu() { }

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		var target = e.Target as Panel;

		if ( e.MouseButton == MouseButtons.Right )
		{
			if ( !IsFoldingClick( target ) )
				return;

			OnContextMenu();
			e.StopPropagation();
			return;
		}

		if ( e.MouseButton != MouseButtons.Left )
			return;

		if ( !IsFoldingClick( target ) )
			return;

		SetExpanded( !Expanded );
		e.StopPropagation();
	}

	/// <summary>Opens or closes it, and says so - but says nothing when nothing changed.</summary>
	public void SetExpanded( bool expanded )
	{
		if ( Expanded == expanded ) return;

		Expanded = expanded;

		OnExpandedChanged?.Invoke( expanded );
	}
}
