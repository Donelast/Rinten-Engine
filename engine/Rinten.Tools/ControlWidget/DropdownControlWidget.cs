using System;
using System.Collections.Generic;

namespace Editor;

/// <summary>Base class for dropdown control widgets with multi-select support.</summary>
public abstract partial class DropdownControlWidget : ControlWidget
{
	public override bool IsControlActive => base.IsControlActive || _menu.IsValid();
	public override bool IsControlButton => true;
	public override bool IsControlHovered => base.IsControlHovered || _menu.IsValid();

	protected PopupWidget _menu;
	protected bool IsMultiSelect { get; set; }

	public DropdownControlWidget( SerializedProperty property ) : base( property )
	{
		Cursor = CursorShape.Finger;
		Layout = Layout.Row();
		Layout.Spacing = 2;
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		if ( _menu.IsValid() )
		{
			foreach ( var option in _menu.GetDescendants<DropdownMenuOption>() )
				option.Update();
		}
	}

	protected override void PaintControl()
	{

		var color = IsControlHovered ? Theme.Ink : Theme.InkDim;
		if ( IsControlDisabled ) color = color.WithAlpha( 0.5f );

		var rect = LocalRect.Shrink( 8, 0 );

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			Paint.SetPen( Theme.MultipleValues );
			Paint.DrawText( rect, "Multiple Values", TextFlag.LeftCenter );
		}
		else
		{
			PaintDisplayText( rect, color );
		}

		Paint.SetPen( color );
		Paint.DrawIcon( rect, "expand_more", 17, TextFlag.RightCenter );
	}

	//Override to paint the display text in the control
	protected abstract void PaintDisplayText( Rect rect, Color color );

	//Text shown on the panel-backed dropdown label.
	protected virtual string GetDropdownDisplayText()
	{
		if ( SerializedProperty is null ) return string.Empty;
		if ( SerializedProperty.IsMultipleDifferentValues ) return "Multiple Values";
		return SerializedProperty.GetValue<object>()?.ToString() ?? string.Empty;
	}

	public override void StartEditing()
	{
		if ( IsControlDisabled ) return;

		if ( !_menu.IsValid )
		{
			OpenMenu();
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( IsControlDisabled ) return;

		if ( e.LeftMouseButton && !_menu.IsValid() )
		{
			OpenMenu();
		}
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		//nothing
	}

	void OpenMenu()
	{
		PropertyStartEdit();

		_menu = new PopupWidget( this );
		_menu.Layout = Layout.Column();
		_menu.MinimumWidth = ScreenRect.Width;
		_menu.MaximumWidth = ScreenRect.Width;
		_menu.OnLostFocus += PropertyFinishEdit;

		var scroller = _menu.Layout.Add( new ScrollArea( _menu ), 1 );
		scroller.Canvas = new Widget( scroller )
		{
			Layout = Layout.Column(),
			//CanGrow, not Expand: an expanding canvas matches the viewport and leaves nothing to scroll
			VerticalSizeMode = SizeMode.CanGrow,
			MaximumWidth = ScreenRect.Width
		};

		PopulateMenu( scroller.Canvas );

		_menu.OnPaintOverride = PaintMenuBackground;
		_menu.OpenAt( ScreenRect.BottomLeft, false );
		_menu.SetZIndex( 100 );

		var scrollbar = scroller.VerticalScrollbar;
		if ( scrollbar is not null && scrollbar.Minimum != scrollbar.Maximum )
		{
			scroller.Canvas.MaximumWidth -= 8;
		}
	}

	//Override to populate the menu with options
	protected abstract void PopulateMenu( Widget canvas );

	//Helper to add a menu option
	protected Widget AddMenuOption( Widget canvas, string displayName, string icon, Func<bool> isSelected, Action onSelect )
	{
		var option = canvas.Layout.Add( new DropdownMenuOption( displayName, icon, isSelected ) );
		option.MouseLeftPress = () =>
		{
			onSelect();
			_menu.Update();

			if ( !IsMultiSelect )
			{
				_menu.Close();
			}
		};
		return option;
	}

	bool PaintMenuBackground()
	{
		Paint.SetBrushAndPen( Theme.ControlBackground );
		Paint.DrawRect( Paint.LocalRect, 0 );
		return true;
	}
}

file class DropdownMenuOption : Widget
{
	string displayName;
	string icon;
	Func<bool> isSelectedFunc;

	public DropdownMenuOption( string displayName, string icon, Func<bool> isSelectedFunc ) : base( null )
	{
		this.displayName = displayName;
		this.icon = icon;
		this.isSelectedFunc = isSelectedFunc;

		Layout = Layout.Row();
		Layout.Margin = 8;
		VerticalSizeMode = SizeMode.CanGrow;

		if ( !string.IsNullOrWhiteSpace( icon ) )
		{
			Layout.Add( new IconButton( icon ) { Background = Color.Transparent, TransparentForMouseEvents = true, IconSize = 18 } );
			Layout.AddSpacingCell( 8 );
		}

		Layout.Add( new Label( displayName ) { TransparentForMouseEvents = true } );
	}

	protected override void OnPaint()
	{
		var isSelected = isSelectedFunc?.Invoke() ?? false;
		if ( Paint.HasMouseOver || isSelected )
		{
			Paint.SetBrushAndPen( isSelected ? Theme.SelectedFill : Theme.Veil );
			Paint.DrawRect( LocalRect.Shrink( 2 ), Theme.ControlRadius - 2.0f );
		}
	}
}
