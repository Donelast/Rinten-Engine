using System;

namespace Editor
{
	/// <summary>Check state of a <see cref="Checkbox"/>.</summary>
	public enum CheckState
	{
		/// <summary>The checkbox is not checked.</summary>
		Off,

		/// <summary>Partial. This is useful in cases when representing multiple objects, with a boolean value where some are set to true, and others to false.</summary>
		Partial,

		/// <summary>The checkbox is checked.</summary>
		On
	}

	/// <summary>A generic checkbox widget.</summary>
	public class Checkbox : Widget
	{
		/// <summary>Called when the box is clicked.</summary>
		public Action Clicked;

		/// <summary>Called when it is pressed down.</summary>
		public Action Pressed;

		/// <summary>Called when the press ends.</summary>
		public Action Released;

		/// <summary>Called when the tick changes, with the new state.</summary>
		public Action<CheckState> StateChanged;

		/// <summary>Called when the tick changes.</summary>
		public Action Toggled;

		/// <summary>The words beside the box.</summary>
		public string Text
		{
			get => text;
			set
			{
				if ( text == value ) return;

				text = value;

				AdjustSize();
				Update();
			}
		}

		private string text = string.Empty;


		private CheckState state;
		private bool triState;

		/// <summary>Whether the checkbox is checked or not.</summary>
		public bool Value
		{
			get => state == CheckState.On;
			set
			{
				State = value ? CheckState.On : CheckState.Off;
			}
		}

		/// <summary>Current state of this checkbox.</summary>
		public CheckState State
		{
			get => state;

			set
			{
				if ( state == value ) return;

				state = value;

				OnToggled();
				OnStateChanged( state );
				Update();
			}
		}
		/// <summary>Enable the third state, the half checked half not checked state. Disabled by default</summary>
		public bool TriState
		{
			get => triState;
			set
			{
				triState = value;
			}
		}


		//How big the box itself is, beside the words.
		private const float BoxSize = 14.0f;

		protected override void OnPaint()
		{
			Paint.Antialiasing = true;

			var box = new Rect( 0, (Height - BoxSize) * 0.5f, BoxSize, BoxSize );

			var ticked = State != CheckState.Off;
			var faded = !Enabled || ReadOnly;

			//Ticked is the accent; empty is a well with nothing in it. Only the empty one is outlined, and only enough to be found.
			Paint.ClearPen();

			var fill = ticked ? Theme.TileDown : Theme.Sunken;

			if ( faded ) fill = fill.WithAlphaMultiplied( 0.4f );
			else if ( Paint.HasMouseOver ) fill = ticked ? Color.White.WithAlpha( 0.3f ) : Theme.TileHover;

			Paint.SetBrush( fill );
			Paint.DrawRect( box, Theme.ControlRadius );

			if ( !ticked )
			{
				Paint.ClearBrush();
				Paint.SetPen( Paint.HasMouseOver ? Theme.EdgeLit : Theme.Edge, 1.0f );
				Paint.DrawRect( box, Theme.ControlRadius );
			}

			if ( State == CheckState.On )
			{
				//The tick: two strokes, as everybody draws it.
				Paint.SetPen( Theme.Ink, 2.0f );

				var tick = box.Shrink( 3 );

				Paint.DrawLine( new Vector2( tick.Left, tick.Center.y ), new Vector2( tick.Center.x - 0.5f, tick.Bottom ) );
				Paint.DrawLine( new Vector2( tick.Center.x - 0.5f, tick.Bottom ), new Vector2( tick.Right, tick.Top ) );
			}
			else if ( State == CheckState.Partial )
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.Ink );
				Paint.DrawRect( box.Shrink( 4 ), 1 );
			}

			if ( string.IsNullOrEmpty( Text ) ) return;

			Paint.SetDefaultFont();
			Paint.SetPen( faded ? Theme.InkOff : Paint.HasMouseOver ? Theme.Ink : Theme.InkDim );

			var words = LocalRect.Shrink( BoxSize + 6.0f, 0, 0, 0 );

			Paint.DrawText( words, Text, TextFlag.LeftCenter | TextFlag.SingleLine );
		}

		protected override Vector2 SizeHint()
		{
			var words = EditorText.Measure( Text );

			return new Vector2( BoxSize + 6.0f + words.x, MathF.Max( BoxSize, words.y ) );
		}

		protected override void OnMousePress( MouseEvent e )
		{
			if ( !Enabled || ReadOnly ) return;

			OnPressed();
		}

		protected override void OnMouseReleased( MouseEvent e )
		{
			if ( !Enabled || ReadOnly ) return;

			OnReleased();
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			if ( !Enabled || ReadOnly ) return;

			//Off to on to off, and through the half-checked state in between where the box was asked to have one.
			State = State switch
			{
				CheckState.Off => triState ? CheckState.Partial : CheckState.On,
				CheckState.Partial => CheckState.On,
				_ => CheckState.Off,
			};

			OnClicked();
		}

		public Checkbox( Widget parent = null ) : base()
		{
			MakeSurface( parent );

			TriState = false;
			AdjustSize();
		}

		public Checkbox( string title, Widget parent = null ) : this( parent )
		{
			if ( title != null )
				Text = title;

		}
		public Checkbox( string title, string icon, Widget parent = null ) : this( title, parent )
		{
			if ( icon != null )
				Icon = icon;
		}

		//Called when checkbox was clicked, on release.
		protected virtual void OnClicked()
		{
			Clicked?.Invoke();
		}

		//Called when checkbox was pressed down.
		protected virtual void OnPressed()
		{
			Pressed?.Invoke();
		}

		//Called when checkbox was released.
		protected virtual void OnReleased()
		{
			Released?.Invoke();
		}

		//Called when checkbox gets toggled on or off.
		protected virtual void OnToggled()
		{
			Toggled?.Invoke();
			SignalValuesChanged();
		}

		//Called when the State of the checkbox states.
		protected virtual void OnStateChanged( CheckState state )
		{
			StateChanged?.Invoke( state );
		}

		/// <summary>Name of a material icon to be drawn in front of the checkbox label.</summary>
		public string Icon { get; set; }

		internal void InternalOnPressed() => OnPressed();
		internal void InternalOnReleased() => OnReleased();
		internal void InternalOnClicked() => OnClicked();
		internal void InternalOnToggled() => OnToggled();
		internal void InternalOnStateChanged() => OnStateChanged( State );
	}
}
