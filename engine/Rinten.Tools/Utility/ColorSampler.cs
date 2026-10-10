using System;
using System.Linq;
using Rinten.UI;

namespace Editor;

/// <summary>The eyedropper: pick a colour from the editor UI or a scene view.</summary>
public class ColorSampler
{
	public const string FurnitureClass = "color-sampler-furniture";

	public Action<Color> OnPicked;
	public Action OnCancelled;

	static readonly Func<Panel, bool> PreviousDismissal = Panel.SuppressPopupDismissal;

	static bool active;
	static ColorSampler current;
	static Catcher catcher;

	static ColorSampler()
	{
		Panel.SuppressPopupDismissal = SuppressPopupDismissal;
	}

	static bool SuppressPopupDismissal( Panel panel )
	{
		if ( active ) return true;

		if ( HasFurniture( panel ) ) return true;

		return PreviousDismissal?.Invoke( panel ) == true;
	}

	static bool HasFurniture( Panel panel )
	{
		for ( var p = panel; p is not null; p = p.Parent )
		{
			if ( p.Classes?.Contains( FurnitureClass ) == true )
				return true;
		}

		return false;
	}

	public void Show()
	{
		if ( active )
		{
			if ( current == this ) return;

			current.Stop( picked: false );
		}

		active = true;
		current = this;
		catcher = new Catcher( OnClick, OnCancel );
	}

	public void Hide()
	{
		if ( current == this )
			Stop( picked: false );
	}

	void Stop( bool picked )
	{
		if ( current != this ) return;

		active = false;
		catcher?.Destroy();
		catcher = null;
		current = null;

		if ( !picked )
			OnCancelled?.Invoke();
	}

	void OnClick()
	{
		var pos = Application.CursorPosition;

		//Gone before readback so a UI redraw does not include our overlay.
		catcher?.Destroy();
		catcher = null;

		if ( TryPickColor( pos, out var color ) )
		{
			Stop( picked: true );
			OnPicked?.Invoke( color );
		}
		else
		{
			OnCancel();
		}
	}

	void OnCancel() => Stop( picked: false );

	[EditorEvent.Frame]
	static void Frame()
	{
		if ( !active ) return;

		var window = WindowAt( Application.CursorPosition ) ?? Window.All.LastOrDefault();
		catcher?.CoverFor( window is not null ? EditorOverlay.For( window ) : null );
	}

	[Shortcut( "colorsampler.cancel", "ESCAPE", ShortcutType.Window )]
	static void CancelShortcut()
	{
		if ( active ) current?.OnCancel();
	}

	static Window WindowAt( Vector2 pos )
	{
		foreach ( var window in Window.All )
		{
			if ( window.ScreenRect.IsInside( pos ) )
				return window;
		}

		return null;
	}

	internal static bool TryPickColor( Vector2 screenPos, out Color color )
	{
		if ( SceneRenderCore.TrySampleColor( screenPos, out color ) )
			return true;

		if ( TryReadWindowPixel( screenPos, out color ) )
			return true;

		color = default;
		return false;
	}

	static bool TryReadWindowPixel( Vector2 screenPos, out Color color )
	{
		color = default;

		var window = WindowAt( screenPos ) ?? Window.All.LastOrDefault();

		if ( window?.Shell is not { } shell ) return false;

		var x = screenPos.x.FloorToInt();
		var y = screenPos.y.FloorToInt();

		return shell.TrySamplePixel( x, y, out color );
	}

	sealed class Catcher
	{
		readonly Sheet sheet;
		EditorOverlay layer;

		public Catcher( Action picked, Action cancelled )
		{
			sheet = new Sheet { Picked = picked, Cancelled = cancelled };
		}

		public void CoverFor( EditorOverlay onto )
		{
			if ( onto is null || ReferenceEquals( layer, onto ) ) return;

			layer = onto;
			layer.AddChild( sheet );
		}

		public void Destroy() => sheet?.Delete( true );

		sealed class Sheet : Panel
		{
			public Action Picked { get; init; }
			public Action Cancelled { get; init; }

			public Sheet()
			{
				AddClass( FurnitureClass );
				AddClass( "picker-catcher" );
				AddClass( "sampling" );
			}

			protected override void OnMouseDown( MousePanelEvent e )
			{
				base.OnMouseDown( e );

				e.StopPropagation();

				if ( e.Button == "mouseleft" )
					Picked?.Invoke();
				else
					Cancelled?.Invoke();
			}
		}
	}
}
