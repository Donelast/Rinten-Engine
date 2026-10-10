using System;

namespace Editor;

[CustomEditor( typeof( Enum ) )]
public partial class EnumControlWidget : ControlWidget
{
	/// <summary>If true, then this control is operating in flags mode (FlagsAttribute)</summary>
	public bool IsFlagsMode { get; init; }

	EnumDescription _enumDesc;
	IconButton _valueIcon;
	Label _titleLabel;
	Label _arrowLabel;

	public override bool IsControlActive => base.IsControlActive || _menu.IsValid();

	public override bool IsControlButton => true;
	public override bool IsControlHovered => base.IsControlHovered || _menu.IsValid();

	public override bool SupportsMultiEdit => true;

	protected virtual float? MenuWidthOverride => null;

	public EnumControlWidget( SerializedProperty property ) : base( property )
	{
		var propertyType = property.NullableType ?? property.PropertyType;
		var typeDesc = EditorTypeLibrary.GetType( propertyType );
		if ( typeDesc is null )
		{
			Log.Warning( $"Couldn't create an enum editor for {propertyType} - it's not in EditorTypeLibrary" );
			return;
		}

		_enumDesc = EditorTypeLibrary.GetEnumDescription( propertyType );

		//Button groups live on the panel side now - Editor.Inspectors.EnumControl lays a small enum out as buttons, and
		//PanelControlWidget brings that into a widget sheet for anything marked [EnumButtonGroup]. What is left here is the dropdown.

		IsFlagsMode = property.HasAttribute<FlagsAttribute>() || typeDesc.HasAttribute<FlagsAttribute>();

		Cursor = CursorShape.Finger;
		Layout = Layout.Row();
		Layout.Spacing = 0;
		Layout.Margin = new Rinten.UI.Margin( 8, 0, 4, 0 );
		HorizontalSizeMode = SizeMode.CanGrow;

		_valueIcon = Layout.Add( new IconButton( "" )
		{
			Background = Color.Transparent,
			TransparentForMouseEvents = true,
			IconSize = 12,
			FixedSize = Theme.RowHeight,
			Hidden = true,
		} );

		_titleLabel = Layout.Add( new Label
		{
			Alignment = TextFlag.LeftCenter | TextFlag.SingleLine,
			HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand,
			TransparentForMouseEvents = true,
		} );

		_arrowLabel = Layout.Add( new Label( "\u25BE" )
		{
			FixedWidth = 17,
			Alignment = TextFlag.RightCenter | TextFlag.SingleLine,
			TransparentForMouseEvents = true,
		} );

		RefreshDisplay();
		RefreshMinimum();
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();
		RefreshDisplay();
		RefreshMenuOptions();
	}

	//Cached minimum size until the value changes; layout asks repeatedly and measuring hits the font
	Vector2? _minHint;

	protected override Vector2 MinimumSizeHint() => _minHint ??= MeasureMinimum();

	//Measures again, after whatever the control shows has changed.
	void RefreshMinimum()
	{
		_minHint = null;
		MinimumWidth = MinimumSizeHint().x;
	}

	Vector2 MeasureMinimum()
	{
		var height = Theme.RowHeight;

		if ( _enumDesc is null )
			return new Vector2( 60, height );

		var value = SerializedProperty.GetValue<long>( 0 );
		var entry = _enumDesc.GetEntry( value );
		var title = entry.Title ?? entry.Name ?? "Unset";

		const float arrow = 17.0f;
		const float pad = 16.0f;
		const float iconGap = 4.0f;
		var width = Paint.MeasureText( title ).x + arrow + pad;

		if ( !string.IsNullOrWhiteSpace( entry.Icon ) )
			width += Theme.RowHeight + iconGap;

		return new Vector2( MathF.Max( width, 60 ), height );
	}

	protected override Vector2 SizeHint()
	{
		return MinimumSizeHint();
	}

	void RefreshDisplay()
	{
		if ( _enumDesc is null || !_titleLabel.IsValid() )
			return;

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			_titleLabel.Text = "Multiple Values";
			_valueIcon.Hidden = true;
			RefreshMinimum();
			return;
		}

		var value = SerializedProperty.GetValue<long>( 0 );

		if ( IsFlagsMode )
		{
			var e = _enumDesc.GetEntries( value );
			_titleLabel.Text = string.Join( ", ", e.Select( x => x.Title ?? x.Name ) );
			_valueIcon.Hidden = true;
		}
		else
		{
			var e = _enumDesc.GetEntry( value );
			_titleLabel.Text = e.Title ?? e.Name ?? "Unset";

			if ( !string.IsNullOrWhiteSpace( e.Icon ) )
			{
				_valueIcon.Icon = e.Icon;
				_valueIcon.Hidden = false;
			}
			else
			{
				_valueIcon.Hidden = true;
			}
		}

		RefreshMinimum();
	}

	protected override void PaintControl()
	{

		if ( _enumDesc is null || !_titleLabel.IsValid() )
			return;

		var color = IsControlHovered ? Theme.Ink : Theme.InkDim;
		if ( IsControlDisabled ) color = color.WithAlpha( 0.5f );

		_titleLabel.Color = SerializedProperty.IsMultipleDifferentValues ? Theme.MultipleValues : color;
		_arrowLabel.Color = color;

		if ( _valueIcon.IsValid() && !_valueIcon.Hidden )
		{
			_valueIcon.Foreground = color;
			_valueIcon.ForegroundActive = color;
		}
	}

	PopupWidget _menu;

	//How tall the list of options gets before it scrolls.
	const float MaxMenuHeight = 320;

	public override void StartEditing()
	{
		if ( IsControlDisabled ) return;

		if ( !_menu.IsValid() )
		{
			OpenMenu();
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( IsControlDisabled ) return;

		if ( e.LeftMouseButton && !_menu.IsValid() )
		{
			e.Accepted = true;
			OpenMenu();
		}
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( IsControlDisabled ) return;

		if ( e.LeftMouseButton )
			e.Accepted = true;
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		//nothing
	}

	void ToggleValue( EnumDescription.Entry e )
	{
		var value = SerializedProperty.GetValue<long>( 0 );

		if ( IsFlagsMode )
		{
			if ( (value & e.IntegerValue) != 0 )
			{
				value &= ~e.IntegerValue;
			}
			else
			{
				value |= e.IntegerValue;
			}
		}
		else
		{
			value = e.IntegerValue;
		}


		SerializedProperty.SetValue( value );
		RefreshDisplay();
		SignalValuesChanged();
	}

	void OpenMenu()
	{
		PropertyStartEdit();

		if ( _enumDesc is null )
			return;

		_menu = new PopupWidget( this );

		_menu.Layout = Layout.Column();
		var menuWidth = MathF.Max( MenuWidthOverride ?? ScreenRect.Width, 240 );
		_menu.MinimumWidth = menuWidth;
		_menu.MaximumWidth = menuWidth;
		_menu.OnLostFocus += PropertyFinishEdit;
		_menu.VerticalSizeMode = SizeMode.CanGrow | SizeMode.Expand;

		var scroller = _menu.Layout.Add( new ScrollArea( this ), 1 );
		scroller.NoSystemBackground = true;
		scroller.TranslucentBackground = true;
		scroller.Canvas = new Widget( scroller )
		{
			Layout = Layout.Column(),
			VerticalSizeMode = SizeMode.CanGrow | SizeMode.Expand,
			MaximumWidth = menuWidth
		};

		var entries = IsFlagsMode ? _enumDesc.AsEnumerable() : _enumDesc;
		float h = 0;

		foreach ( var o in entries )
		{
			if ( !o.Browsable )
				continue;

			var b = scroller.Canvas.Layout.Add( new MenuOption( o, SerializedProperty, IsFlagsMode ) );
			b.MouseLeftPress = () =>
			{
				ToggleValue( o );
				RefreshMenuOptions();

				if ( !IsFlagsMode )
				{
					_menu.Close();
				}
			};

			h += b.FixedHeight;
		}

		scroller.Canvas.AdjustSize();

		_menu.Position = ScreenRect.BottomLeft;
		_menu.OnPaintOverride = PaintMenuBackground;

		var height = MathF.Min( h, MaxMenuHeight );
		scroller.FixedHeight = height;
		_menu.FixedHeight = height;

		_menu.Show();
		_menu.AdjustSize();
		_menu.ConstrainToScreen();

		var scrollbar = scroller.VerticalScrollbar;
		if ( scrollbar is not null && scrollbar.Minimum != scrollbar.Maximum )
		{
			scroller.Canvas.MaximumWidth -= 8; //leave some space for the scrollbar
		}
	}

	void RefreshMenuOptions()
	{
		if ( !_menu.IsValid() ) return;

		foreach ( var option in _menu.GetDescendants<MenuOption>() )
			option.Update();
	}

	bool PaintMenuBackground()
	{
		Paint.SetBrushAndPen( Theme.ControlBackground, Theme.WidgetBackground, 1 );
		Paint.DrawRect( Paint.LocalRect.Shrink( 1 ), Theme.ControlRadius );
		return true;
	}

}

file class MenuOption : Widget
{
	EnumDescription.Entry info;
	SerializedProperty property;
	bool flagMode;

	public MenuOption( EnumDescription.Entry e, SerializedProperty p, bool flags ) : base( null )
	{
		info = e;
		property = p;
		flagMode = flags;

		Layout = Layout.Row();
		Layout.Margin = 0;
		HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
		VerticalSizeMode = SizeMode.Default;
		FixedHeight = Theme.RowHeight;
		Cursor = CursorShape.Finger;

		if ( !string.IsNullOrWhiteSpace( e.Icon ) )
		{
			Layout.AddSpacingCell( 4 );
			Layout.Add( new IconButton( e.Icon ) { Background = Color.Transparent, TransparentForMouseEvents = true, IconSize = 12, FixedSize = Theme.RowHeight } );
		}

		var c = Layout.AddColumn();
		c.Margin = new Rinten.UI.Margin( 8, 4 );
		var title = c.Add( new Label( e.Title ?? e.Name ) );
		title.TransparentForMouseEvents = true;
		title.Color = Theme.Text;

		if ( !string.IsNullOrWhiteSpace( e.Description ) )
		{
			var desc = c.Add( new Label( e.Description.Trim( '\n', '\r', '\t', ' ' ) ) );
			desc.TransparentForMouseEvents = true;
			desc.WordWrap = true;
			desc.MinimumHeight = 1;
			desc.VerticalSizeMode = SizeMode.CanGrow;
			desc.Color = Theme.Text.WithAlpha( 0.5f );
			FixedHeight = Theme.RowHeight * 2;

			c.AddStretchCell();
		}
	}

	bool HasValue()
	{
		var value = property.GetValue<long>( 0 );
		if ( flagMode ) return (value & info.IntegerValue) == info.IntegerValue;
		return value == info.IntegerValue;
	}

	public override void SyncSurfaceContent()
	{

		var selected = HasValue();
		var lit = IsUnderMouse;

		if ( lit || selected )
			Surface.Style.BackgroundColor = selected ? Theme.TileDown : Theme.Veil;
		else
			Surface.Style.BackgroundColor = Color.Transparent;
	}

	protected override void OnPaint()
	{
		if ( !Paint.Snapshot ) return;

		var selected = HasValue();
		var lit = Paint.HasMouseOver;

		if ( lit || selected )
		{
			Paint.SetBrushAndPen( selected ? Theme.TileDown : Theme.Veil );
			Paint.DrawRect( LocalRect );
		}
	}
}
