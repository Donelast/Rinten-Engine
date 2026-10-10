using Rinten.UI;

namespace Editor.UiEditor;

//Something on its way from one place to another under the pointer: a tag from the palette, or a panel from the tree.
internal sealed class Carry
{
	/// <summary>A tag not yet in the file, to be added where it lands.</summary>
	public string Tag;
	public string Classes;
	public string Text;

	/// <summary>A panel already in the file, to be moved to where it lands.</summary>
	public Panel Panel;

	public bool IsNew => Panel is null;

	public override string ToString() => IsNew ? $"<{Tag}>" : $"{Panel.ElementName}.{Panel.Classes}";
}

//Where a carry lands, relative to a panel.
internal enum DropPlace
{
	Into,
	Before,
	After,
}

/// <summary>Drag and drop: the palette and the tree start a carry, the tree and the canvas take one. What a drop does is what the menu would have done - add a tag, move a tag - by the same commands.</summary>
public sealed partial class UiEditorWindow
{
	Carry _carry;

	//What is being carried, if anything.
	internal Carry Carrying => _carry;

	internal void BeginCarry( Carry carry ) => _carry = carry;

	internal void EndCarry() => _carry = null;

	//Whether a carry may land on a panel: not a panel inside what is carried, and not the thing itself.
	//Armed: a tag chosen in the palette, to be drawn onto the canvas

	Carry _armed;

	//The tag the next press on the canvas draws, if one was chosen in the palette.
	internal Carry Armed => _armed;

	//Choose a tag to draw; choosing it again puts it down.
	internal void Arm( Carry carry )
	{
		_armed = _armed is not null && carry is not null && _armed.Tag == carry.Tag && _armed.Classes == carry.Classes ? null : carry;
	}

	internal void Disarm() => _armed = null;

	internal bool CanDrop( Panel target, DropPlace place )
	{
		if ( _carry is null || !target.IsValid() ) return false;

		if ( _carry.Panel is { } moving )
		{
			if ( ReferenceEquals( moving, target ) ) return false;

			for ( var at = target; at is not null; at = at.Parent )
				if ( ReferenceEquals( at, moving ) ) return false;
		}

		//Beside the root there is nothing.
		if ( place != DropPlace.Into && ReferenceEquals( target, Root ) ) return false;

		return LocateTag( target ) is not null;
	}

	//Land the carry on a panel, and select what landed.
	internal void Drop( Panel target, DropPlace place )
	{
		var carry = _carry;
		_carry = null;

		if ( carry is null || !CanDrop( target, place ) ) return;

		Panel landed;

		if ( carry.IsNew )
		{
			landed = place switch
			{
				DropPlace.Into => AddChild( target, carry.Tag, carry.Classes, carry.Text ),
				DropPlace.Before => AddBefore( target, carry.Tag, carry.Classes, carry.Text ),
				_ => AddSibling( target, carry.Tag, carry.Classes, carry.Text ),
			};
		}
		else
		{
			landed = Reparent( carry.Panel, target, place ) ? carry.Panel : null;
		}

		if ( landed is not null ) Select( landed );
	}

	//Put a tag beside a panel's tag, just before it.
	internal Panel AddBefore( Panel panel, string tag, string classes = null, string text = null )
	{
		if ( TagOf( panel, "add before" ) is not { } element ) return null;
		if ( panel.Parent is not { } parent ) return null;

		var markup = _document.Markup;
		var after = markup.WithSiblingBefore( element, TagMarkup( tag, classes, text ) );

		if ( !CommitMarkup( $"add <{tag}>", markup.Text, after ) ) return null;

		var made = MakePanel( tag, classes, text );
		parent.AddChild( made );
		parent.SetChildIndex( made, IndexOf( parent, panel ) );

		PlaceMirrored( made, parent );

		return made;
	}

	//Move a panel's tag into another panel's, or beside it.
	internal bool Reparent( Panel panel, Panel target, DropPlace place )
	{
		if ( TagOf( panel, "move" ) is not { } element ) return false;
		if ( TagOf( target, "move onto" ) is not { } targetTag ) return false;

		var markup = _document.Markup;

		var after = place switch
		{
			DropPlace.Into => markup.WithElementInto( element, targetTag ),
			DropPlace.Before => markup.WithElementBefore( element, targetTag ),
			_ => markup.WithElementAfter( element, targetTag ),
		};

		if ( !CommitMarkup( $"move <{element.Name}>", markup.Text, after ) ) return false;

		//The same move on the live tree.
		switch ( place )
		{
			case DropPlace.Into:
				target.AddChild( panel );
				break;

			case DropPlace.Before when target.Parent is { } parent:
				parent.AddChild( panel );
				parent.SetChildIndex( panel, IndexOf( parent, target ) );
				break;

			case DropPlace.After when target.Parent is { } parent:
				parent.AddChild( panel );
				parent.SetChildIndex( panel, IndexOf( parent, target ) + 1 );
				break;
		}

		if ( IsSelected( panel ) ) NoteSelectionMoved();

		return true;
	}
}
