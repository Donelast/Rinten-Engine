using Rinten.UI;
using System;

namespace Editor;

public class SegmentedControl : Widget
{
	private List<string> options = new();
	private int selectedIndex = 0;

	public int SelectedIndex
	{
		get => selectedIndex;
		set
		{
			selectedIndex = value;
			SelectedChanged();
		}
	}

	public string Selected
	{
		get => options[selectedIndex];
		set
		{
			var index = options.IndexOf( value );
			if ( index < 0 )
				return;

			selectedIndex = index;
			RepaintOptions();
		}
	}

	public Action<string> OnSelectedChanged;

	bool _showtext = true;

	public bool ShowText
	{
		get => _showtext;

		set
		{
			if ( ShowText == value ) return;

			_showtext = value;

			foreach ( var option in Children.OfType<Option>() )
			{
				option.ShowText = _showtext;
			}
		}
	}

	public SegmentedControl( Widget parent = null ) : base( parent )
	{
		FixedHeight = 24;
		Layout = Layout.Row();
		Layout.Spacing = 1;
		Layout.Margin = 1;
	}

	protected override void DoLayout()
	{
		var first = Children.OfType<Option>().FirstOrDefault();
		foreach ( var option in Children.OfType<Option>() )
		{
			//Only show text if we can show text for all options and we have enough room
			option.ShowText = first.Width > 72f;
		}
	}

	public void AddOption( string name, string icon = null, int? count = null )
	{
		int index = options.Count;

		Layout.Add( new Option( name, icon, () =>
		{
			selectedIndex = index;
			SelectedChanged();
		} )
		{ ShowText = ShowText, Index = index, Count = count, IsSelected = () => selectedIndex == index }, 1 );

		options.Add( name );
	}

	public bool HasOption( string name )
	{
		return options.Contains( name );
	}

	/// <summary>Turns one segment on or off without removing it.</summary>
	public void SetOptionEnabled( string name, bool enabled, string disabledTip = null )
	{
		var option = Children.OfType<Option>().FirstOrDefault( x => x.Name == name );
		if ( option is null )
			return;

		option.Enabled = enabled;
		option.Cursor = enabled ? CursorShape.Finger : CursorShape.Forbidden;
		option.ToolTip = enabled || string.IsNullOrWhiteSpace( disabledTip ) ? name : disabledTip;
		Update();
	}

	public bool IsOptionEnabled( string name )
	{
		var option = Children.OfType<Option>().FirstOrDefault( x => x.Name == name );
		return option?.Enabled ?? false;
	}

	private void SelectedChanged()
	{
		OnSelectedChanged?.Invoke( Selected );
		RepaintOptions();
	}

	//The whole control, segments and all. Which segment is the chosen one is worked out as each is painted, so the one that has just stopped
	//being it has to be told to paint again - updating only the row around them left the old name lit until the pointer happened to cross it.
	private void RepaintOptions()
	{
		Update();

		foreach ( var option in Children.OfType<Option>() )
			option.Update();
	}

	//Paints nothing itself; the selected segment draws its own plate

	class Option : Widget
	{
		public string Icon { get; set; }
		public Action OnClick { get; set; }
		public bool ShowText { get; set; }
		public int Index { get; set; }
		public int? Count { get; set; }

		/// <summary>Whether this is the one that is open.</summary>
		public Func<bool> IsSelected { get; set; }

		public Option( string name, string icon = null, Action onClick = null, Widget parent = null ) : base( parent )
		{
			Name = name;
			Icon = icon;
			OnClick = onClick;
			Cursor = CursorShape.Finger;
			ToolTip = name;
		}

		protected override Vector2 SizeHint()
		{
			if ( !ShowText )
			{
				return 24;
			}

			var textSize = Paint.MeasureText( Name );
			textSize.x += 16;
			textSize.y += 8;

			if ( !string.IsNullOrWhiteSpace( Icon ) )
			{
				textSize.x += 22 + 8;
			}

			return textSize;
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			if ( !Enabled )
				return;

			base.OnMouseClick( e );
			OnClick?.Invoke();
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.ClearBrush();

			Paint.Antialiasing = true;

			var r = LocalRect;
			Paint.ClearPen();

			var selected = IsSelected?.Invoke() ?? false;

			//The one that is open wears the plate, like every other tab in the editor; the pointer over any of them lifts it the same
			//amount. White at a tenth was fine on a dark track; with the track gone it is a pale smear on the blur, so it is the tab colour.
			var plate = Enabled && Paint.HasMouseOver
				? Theme.Slab
				: selected ? Theme.TabBackground : Color.Transparent;

			Paint.SetBrush( plate );
			Paint.DrawRect( r, Theme.ControlRadius );

			var ink = !Enabled
				? Theme.TextDisabled
				: selected || Paint.HasMouseOver ? Theme.Ink : Theme.InkDim;

			if ( !ShowText )
			{
				Paint.ClearBrush();
				Paint.SetPen( ink );
				Paint.DrawIcon( r, Icon, 12.0f );
				return;
			}

			Paint.ClearBrush();
			Paint.SetPen( ink );
			Paint.SetDefaultFont( 8, selected ? 600 : 400 );
			r.Left += 12.0f;

			var nameRect = Paint.DrawText( r, Name );
			r = nameRect;

			if ( Count.HasValue )
			{
				var rect = LocalRect;
				rect.Left = nameRect.Right + 10;
				rect.Top += 1.0f;

				Paint.SetBrush( Theme.Text.WithAlpha( 0.2f ) );
				Paint.ClearPen();
				Paint.DrawTextBox( rect, Count.Value.ToString(), Theme.Text, new Margin( 5, 0 ), 3.0f, TextFlag.LeftCenter );
			}

			if ( !string.IsNullOrEmpty( Icon ) )
			{
				r.Left -= nameRect.Width + 24.0f;
				r.Top += 2.0f;

				Paint.ClearBrush();
				Paint.SetPen( ink );
				Paint.DrawIcon( r, Icon, 12.0f );
			}
		}
	}
}
