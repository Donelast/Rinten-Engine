using System;

namespace Editor
{
	/// <summary>A simple button widget.</summary>
	public class Button : Widget
	{

		/// <summary>Called when the button is clicked.</summary>
		public Action Clicked;

		/// <summary>Called when the button is pressed down.</summary>
		public Action Pressed;

		/// <summary>Called when the press ends.</summary>
		public Action Released;

		/// <summary>Called when a toggle button changes state.</summary>
		public Action Toggled;

		/// <summary>Text on the button.</summary>
		public string Text
		{
			get => text;
			set
			{
				if ( text == value ) return;

				text = value;

				//The words decide how wide it is, unless somebody said.
				AdjustSize();
				Update();
			}
		}

		private string text = string.Empty;
		private bool isChecked;
		private bool isToggle;

		Color _tint = Theme.Quiet;

		/// <summary>The tint of the button color</summary>
		public Color Tint
		{
			get => _tint;
			set
			{
				if ( _tint == value ) return;
				_tint = value;
				Update();
			}
		}

		/// <summary>Whether this button is checked. See <see cref="IsToggle"/>.</summary>
		public bool IsChecked
		{
			get => isChecked;
			set
			{
				if ( isChecked == value ) return;

				isChecked = value;

				OnToggled();
				Update();
			}
		}

		/// <summary>Whether this button can be toggled on or off. See <see cref="IsChecked"/>.</summary>
		public bool IsToggle
		{
			get => isToggle;
			set
			{
				isToggle = value;
			}
		}


		//Also raises the widget's own press event, which dialogs wire their buttons through
		protected override void OnMousePress( MouseEvent e )
		{
			if ( !Enabled || ReadOnly ) return;

			base.OnMousePress( e );

			OnPressed();
		}

		protected override void OnMouseReleased( MouseEvent e )
		{
			if ( !Enabled || ReadOnly ) return;

			base.OnMouseReleased( e );

			OnReleased();
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			if ( !Enabled || ReadOnly ) return;

			base.OnMouseClick( e );

			if ( IsToggle ) IsChecked = !IsChecked;

			OnClicked();
		}

		protected override Vector2 SizeHint()
		{
			var measured = EditorText.Measure( Text );

			//The words, room either side of them, and the icon where there is one - which is what the paint below lays out. Roomier than
			//the text strictly needs: a button is a thing to hit, and one sized to its label is a target the width of the word "OK".
			var width = measured.x + 22.0f;

			if ( !string.IsNullOrWhiteSpace( Icon ) ) width += 24.0f;

			return new Vector2( width, MathF.Max( measured.y + 10.0f, Theme.ControlHeight ) );
		}

		public Button( Widget parent = null ) : base()
		{
			MakeSurface( parent );
			Size = new Vector2( 80, Theme.ControlHeight );
		}

		public Button( string title, Widget parent = null ) : this( parent )
		{
			if ( title != null )
				Text = title;
		}
		public Button( string title, string icon, Widget parent = null ) : this( title, parent )
		{
			if ( icon != null )
				Icon = icon;
		}

		protected virtual void OnClicked()
		{
			Clicked?.Invoke();
		}

		protected virtual void OnPressed()
		{
			Pressed?.Invoke();
		}

		protected virtual void OnReleased()
		{
			Released?.Invoke();
		}

		protected virtual void OnToggled()
		{
			Toggled?.Invoke();
		}

		string _icon;

		/// <summary>Sets an icon for the button via a filepath.</summary>
		public string Icon
		{
			set
			{
				if ( _icon == value )
					return;

				_icon = value;

				//The button's own paint draws it, from the icon font or from a file, so there is nothing to hand over.
				AdjustSize();
				Update();
			}

			get => _icon;
		}

		/// <summary>Sets an icon for the button via a raw image.</summary>
		public void SetIcon( Pixmap pixmap )
		{
			iconPixmap = pixmap;
			Update();
		}

		public Pixmap GetIcon()
		{
			return iconPixmap;
		}

		private Pixmap iconPixmap;

		internal void InternalOnPressed() => OnPressed();
		internal void InternalOnReleased() => OnReleased();
		internal void InternalOnClicked() => OnClicked();
		internal void InternalOnToggled() => OnToggled();

		//A flat fill and the words on it. Flat on purpose: what says a button is a button is that it answers the pointer, not that it is shaded like a pebble.
		protected override void OnPaint()
		{
			//A tint of its own means it says something - primary, danger - and then the fill is
			//that colour. Otherwise it is the veil of white everything else on a panel is made of.
			var tinted = Tint.a > 0.0f;

			//Untinted buttons still get a plate, so they stay visible over see-through windows
			var bg = tinted ? Tint : Theme.Slab;
			var ink = tinted ? Theme.Ink : Theme.InkDim;

			if ( !Enabled || ReadOnly )
			{
				bg = tinted ? Tint.WithAlphaMultiplied( 0.3f ) : Theme.Slab.WithAlphaMultiplied( 0.5f );
				ink = Theme.InkOff;
			}
			else if ( Paint.HasPressed )
			{
				bg = tinted ? Tint.Darken( 0.15f ) : Theme.SlabDown;
				ink = Theme.Ink;
			}
			else if ( Paint.HasMouseOver )
			{
				bg = tinted ? Tint.Lighten( 0.12f ) : Theme.SlabHover;
				ink = Theme.Ink;
			}

			if ( bg.a > 0.0f )
			{
				Paint.Antialiasing = true;

				Paint.ClearPen();
				Paint.SetBrush( bg );
				Paint.DrawRect( LocalRect, Theme.ControlRadius );
			}

			var c = ink;

			Paint.SetDefaultFont();

			var textSize = Paint.MeasureText( Text );
			float iconSize = 16;

			//Only where there are two things to keep apart. An icon with no words beside it used to be laid out with the gap after it counted in anyway, which is
			//an icon sitting half the gap left of the middle of its own button - the one place it is obvious, because there is nothing else in there to look at.
			var words = !string.IsNullOrWhiteSpace( Text );
			float spacing = words ? 4 : 0;

			float totalWidth = words ? textSize.x : 0.0f;
			if ( !string.IsNullOrWhiteSpace( Icon ) )
			{
				totalWidth += iconSize + spacing;
			}

			float startX = LocalRect.Center.x - (totalWidth / 2);
			float centerY = LocalRect.Center.y;

			Paint.SetPen( c );

			if ( !string.IsNullOrWhiteSpace( Icon ) )
			{
				var iconRect = new Rect( startX, centerY - iconSize / 2, iconSize, iconSize );
				Paint.DrawIcon( iconRect, Icon, iconSize );
				startX += iconSize + spacing;
			}

			var textRect = new Rect( startX, LocalRect.Top, textSize.x, LocalRect.Height );
			Paint.DrawText( textRect, Text, TextFlag.Center );
		}

		/// <summary>A visually distinct button.</summary>
		public class Primary : Button
		{
			public Primary( string title, Widget parent = null ) : this( title, null, parent )
			{

			}

			public Primary( string title, string icon, Widget parent = null ) : base( title, icon, parent )
			{
				Tint = Theme.Good;
			}
		}

		public class Clear : Button
		{
			public Clear( string title, Widget parent = null ) : this( title, null, parent )
			{

			}

			public Clear( string title, string icon, Widget parent = null ) : base( title, icon, parent )
			{
				Tint = Color.Transparent;
			}
		}

		public class Danger : Button
		{
			public Danger( string title, Widget parent = null ) : this( title, null, parent )
			{

			}

			public Danger( string title, string icon, Widget parent = null ) : base( title, icon, parent )
			{
				Tint = Theme.Red;
			}
		}
	}
}
