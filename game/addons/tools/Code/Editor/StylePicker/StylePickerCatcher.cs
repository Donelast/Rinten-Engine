using System;
using Rinten.UI;

namespace Editor;

//Eats mouse input so nothing underneath hears it while the picker is active.
internal sealed class StylePickerCatcher
{
	readonly Sheet sheet;
	EditorOverlay layer;

	public StylePickerCatcher( Action picked )
	{
		sheet = new Sheet { Picked = picked };
	}

	public void CoverFor( EditorOverlay onto )
	{
		if ( onto is null || ReferenceEquals( layer, onto ) )
		{
			KeepOnTop();
			return;
		}

		layer = onto;
		layer.AddChild( sheet );
		KeepOnTop();
	}

	public void KeepOnTop()
	{
		if ( layer is null || sheet?.Parent is null ) return;

		layer.SetChildIndex( sheet, layer.ChildrenCount - 1 );
	}

	public void Destroy() => sheet?.Delete( true );

	sealed class Sheet : Panel
	{
		public Action Picked { get; init; }

		public Sheet()
		{
			AddClass( StylePickerTool.FurnitureClass );
			AddClass( "picker-catcher" );
		}

		protected override void OnMouseDown( MousePanelEvent e )
		{
			base.OnMouseDown( e );
			e.StopPropagation();
			Picked?.Invoke();
		}

		protected override void OnMouseUp( MousePanelEvent e )
		{
			base.OnMouseUp( e );
			e.StopPropagation();
		}

		protected override void OnMouseMove( MousePanelEvent e )
		{
			base.OnMouseMove( e );
			e.StopPropagation();
		}

		public override void OnButtonEvent( ButtonEvent e )
		{
			e.StopPropagation = true;
			base.OnButtonEvent( e );
		}

		public override void OnMouseWheel( Vector2 value )
		{
			base.OnMouseWheel( value );
		}
	}
}
