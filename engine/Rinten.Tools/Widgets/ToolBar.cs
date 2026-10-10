using System;

namespace Editor
{
	public enum ToolButtonStyle
	{
		IconOnly,
		TextOnly,
		TextBesideIcon,
		TextUnderIcon,
		Default
	}

	public partial class ToolBar : Widget
	{

		public ToolBar( Widget parent, string name = null ) : base()
		{
			MakeSurface( parent );

			if ( name is not null )
			{
				Name = name;
				Title = name;
			}
		}

		public string Title
		{
			get => this.WindowTitle;
			set => WindowTitle = value;
		}

		public bool Movable
		{
			get => movable;
			set
			{
				//Dragging a bar to another edge is the dock manager's job, and ours does not move yet.
				movable = value;
			}
		}

		public bool Floatable
		{
			get => floatable;
			set
			{
				floatable = value;
			}
		}

		private bool movable;
		private bool floatable;

		public Option AddOption( string text, string icon = null, Action action = null )
		{
			return AddOption( new Option( this, text, icon, action ) );
		}

		public Option AddOption( Option option )
		{
			option.SetParent( this );

			options.Add( option );
			order.Add( option );

			MarkNeedsArrange();
			Update();
			return option;
		}

		/// <summary>Takes one option off the bar.</summary>
		public void RemoveOption( Option option )
		{
			if ( option is null ) return;
			if ( !options.Remove( option ) ) return;

			order.Remove( option );

			MarkNeedsArrange();
			Update();
		}

		public void Clear()
		{
			options.Clear();
			widgets.Clear();
			order.Clear();

			MarkNeedsArrange();
			Update();
		}

		public Option AddSeparator()
		{
			return new Option( this );
		}

		public T AddWidget<T>( T widget ) where T : Widget
		{
			widget.SetPanelParent( this );
			widgets.Add( widget );
			order.Add( widget );

			MarkNeedsArrange();
			return widget;
		}

		/// <summary>Take up whatever width is going spare, so what comes after is pushed along. One of these puts the rest against the right edge; one either side of something puts it in the middle.</summary>
		public Widget AddStretch()
		{
			return AddWidget( new Widget( this ) { HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand } );
		}

		Vector2 _iconSize;

		public void SetIconSize( Vector2 size )
		{
			if ( _iconSize == size )
				return;

			_iconSize = size;

			iconSize = size;

			AdjustSize();
			MarkNeedsArrange();
			Update();
		}

		public ToolButtonStyle ButtonStyle
		{
			get => buttonStyle;
			set
			{
				buttonStyle = value;
				Update();
			}
		}

		private ToolButtonStyle buttonStyle;

	}
}
