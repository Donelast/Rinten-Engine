using Rinten.Engine;

namespace Rinten.UI;

/// <summary>Handles input focus for <see cref="Panel"/>s.</summary>
public class InputFocus
{
	/// <summary>The panel that currently has input focus.</summary>
	public static Panel Current
	{
		get => GlobalContext.Current.UISystem.CurrentFocus;
		internal set => GlobalContext.Current.UISystem.CurrentFocus = value;
	}

	/// <summary>The panel that will have the input focus next.</summary>
	public static Panel Next
	{
		get => GlobalContext.Current.UISystem.NextFocus;
		internal set => GlobalContext.Current.UISystem.NextFocus = value;
	}

	static bool PendingChange
	{
		get => GlobalContext.Current.UISystem.FocusPendingChange;
		set => GlobalContext.Current.UISystem.FocusPendingChange = value;
	}

	/// <summary>Set the focus to this panel (or its nearest ancestor with AcceptsFocus). Note that <see cref="Current"/> won't change until the next frame.</summary>
	public static bool Set( Panel panel )
	{
		return TrySetOrParent( panel );
	}

	/// <summary>Clear focus away from this panel.</summary>
	public static bool Clear( Panel panel )
	{
		Next = null;
		PendingChange = true;

		Set( panel?.Parent );

		return true;
	}

	/// <summary>Clear keyboard focus</summary>
	public static bool Clear()
	{
		if ( Current == null )
			return false;

		Next = null;
		PendingChange = true;
		return true;
	}

	//Focus for a press: nearest ancestor that takes focus on press; else drop tab-focus back to the game
	internal static bool SetFromPress( Panel panel )
	{
		for ( var p = panel; p != null; p = p.Parent )
		{
			if ( !p.AcceptsFocus || !p.FocusOnPress )
				continue;

			if ( Next == p ) return true;

			Next = p;
			PendingChange = true;
			return true;
		}

		if ( Current is { FocusOnPress: false } )
			return Clear();

		return false;
	}

	static bool TrySetOrParent( Panel panel )
	{
		if ( panel == null ) return false;
		if ( Next == panel ) return true;

		//Note that we're not judging eligibility based on styles here That happens in the Tick because those styles might not have been calculated yet.

		if ( panel.AcceptsFocus )
		{
			Next = panel;
			PendingChange = true;
			return true;
		}

		return TrySetOrParent( panel.Parent );
	}

	internal static void Tick()
	{
		//TODO - maintain - make sure Current/Next can still be the focus move the focus up if they can't

		//TODO - make sure current selected is a child of one of the root panels if the panel was removed then it's no good to us

		//If our focus became uneligable then defocus
		if ( Current != null )
		{
			if ( !IsPanelEligibleForFocus( Current ) )
			{
				if ( !PendingChange || Next == Current )
				{
					//TODO - should we shift focus to a parent or should that logic be in panels?
					Next = null;
					PendingChange = true;
				}
			}
		}

		//Don't swap to an uneligable panel
		if ( PendingChange && Next != null )
		{
			if ( !Next.AcceptsFocus )
			{
				Next = null;
				PendingChange = false;
			}
		}


		if ( PendingChange )
		{
			PendingChange = false;

			if ( Current != Next )
			{
				if ( Current != null )
				{
					Panel.Switch( PseudoClass.Focus, false, Current, Next );
					Current.CreateEvent( new PanelEvent( "onblur", Current ) );
					Current.MarkRenderDirty();
				}

				Current = Next;

				Panel.Switch( PseudoClass.Focus, true, Current, null );
				Current?.CreateEvent( new PanelEvent( "onfocus", Current ) );

				//What a panel draws can depend on having the focus (a TextEntry's caret) without
				//any style changing on :focus, so both sides repaint.
				Current?.MarkRenderDirty();
			}
		}

		//Log.Info( $"InputRouter.NeedsKeyboardInput = {InputRouter.NeedsKeyboardInput} ({Current})" );
		Next = null;
	}

	static bool IsPanelEligibleForFocus( Panel panel )
	{
		if ( !panel.IsVisible ) return false;
		if ( !panel.AcceptsFocus ) return false;

		return true;
	}
}
