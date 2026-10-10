using Rinten.UI;
using System;

namespace Editor
{
	public abstract partial class Layout : EditorObject
	{

		/// <summary>The amount of space between items</summary>
		public float Spacing
		{
			get => ManagedSpacing;
			set
			{
				ManagedSpacing = value;
				OnGeometryChanged();
			}
		}


		SizeConstraint _sc;

		/// <summary>How the layout should resize the owning widget</summary>
		public SizeConstraint SizeConstraint
		{
			get => _sc;
			set
			{
				_sc = value;

				return;
			}
		}

		/// <summary>An enabled layout adjusts dynamically to changes; a disabled layout acts as if it did not exist.</summary>
		public bool Enabled
		{
			get => managedEnabled;
			set
			{
				managedEnabled = value;
				OnGeometryChanged();
			}
		}

		private bool managedEnabled = true;

		/// <summary>An enabled layout adjusts dynamically to changes; a disabled layout acts as if it did not exist.</summary>
		public TextFlag Alignment
		{
			get => managedAlignment;
			set
			{
				managedAlignment = value;
				OnGeometryChanged();
			}
		}

		private TextFlag managedAlignment;

		/// <summary>The rect of this layout including margins</summary>
		public Rect OuterRect => Area;

		/// <summary>The rect of this layout excluding margins</summary>
		public Rect InnerRect => Area.Shrink( ManagedMargin );

		Margin _contentMargin;

		/// <summary>The amount of space to leave free around the outside of the layout</summary>
		public Margin Margin
		{
			get => _contentMargin;
			set
			{
				_contentMargin = value;

				ManagedMargin = value;
				OnGeometryChanged();
			}
		}

		/// <summary>Remove all widgets from this layout, without deleting them outright.</summary>
		public void Clear( bool deleteWidgets )
		{
			foreach ( var cell in Cells )
			{
				//It is not in a flex container any more, whatever becomes of it
				if ( cell.Widget is not null ) cell.Widget.InFlex = false;

				if ( deleteWidgets ) cell.Widget?.Destroy();
				else cell.Widget?.SetPanelParent( null );

				cell.Layout?.Clear( deleteWidgets );
			}

			Cells.Clear();
			ForgetFlex();
			OnGeometryChanged();
		}

		public abstract Layout Add( Layout layout );

		public virtual Layout Add( Layout layout, int stretch )
		{
			Assert.IsValid( layout );
			return Add( layout );
		}

		public virtual T AddLayout<T>( T layout, int stretch = 0 ) where T : Layout
		{
			Assert.IsValid( layout );
			Add( layout );

			return layout;
		}

		public virtual T Add<T>( T widget ) where T : Widget
		{
			Assert.IsValid( widget );

			AddCell( new Cell { Widget = widget } );
			return widget;
		}

		public virtual T Add<T>( T widget, int stretch ) where T : Widget
		{
			Assert.IsValid( widget );

			AddCell( new Cell { Widget = widget, Stretch = stretch } );
			return widget;
		}

		//Removes a widget from this layout or a nested one and reports its slot, so it can be put back there
		internal bool TakeOut( Widget widget, out Layout from, out int index, out Cell cell )
		{
			for ( var i = 0; i < Cells.Count; i++ )
			{
				var here = Cells[i];

				if ( here.Widget == widget )
				{
					Cells.RemoveAt( i );
					OnGeometryChanged();

					from = this;
					index = i;
					cell = here;

					return true;
				}

				if ( here.Layout is { } nested && nested.TakeOut( widget, out from, out index, out cell ) )
					return true;
			}

			from = null;
			index = -1;
			cell = default;

			return false;
		}

		//Puts a cell back where it was taken from.
		internal void PutBack( int index, Cell cell )
		{
			Cells.Insert( Math.Clamp( index, 0, Cells.Count ), cell );

			cell.Widget?.SetPanelParent( Owner );

			//Whatever size it was given while it was away is not a size it asked for; what it gets now is whatever this layout hands it.
			if ( cell.Widget is not null ) cell.Widget.SizeFromLayout = true;

			OnGeometryChanged();
		}

		//Takes a cell into this layout, and whatever is in it under the widget this layout arranges.
		internal void AddCell( Cell cell )
		{
			Cells.Add( cell );

			cell.Widget?.SetPanelParent( Owner );

			if ( cell.Layout is not null )
			{
				cell.Layout.Parent = this;
				cell.Layout.SetOwner( Owner );
			}

			OnGeometryChanged();
		}


		//Types

		public static Layout Row( bool reversed = false )
		{
			return new BoxLayout( reversed ? BoxLayout.Direction.RightToLeft : BoxLayout.Direction.LeftToRight, default );
		}

		public static Layout Column( bool reversed = false )
		{
			return new BoxLayout( reversed ? BoxLayout.Direction.BottomToTop : BoxLayout.Direction.TopToBottom, default );
		}

		public static GridLayout Grid() => new GridLayout();

		/// <summary>A column that does not stretch what is in it.</summary>
		public static Layout Flow() => Column();

		public Layout AddFlow( int stretch = 0 ) => Add( Flow(), stretch );
		public Layout AddRow( int stretch = 0, bool reversed = false ) => Add( Row( reversed ), stretch );
		public Layout AddColumn( int stretch = 0, bool reversed = false ) => Add( Column( reversed ), stretch );

		/// <summary>Add a spacing item</summary>
		public virtual void AddSpacingCell( float size )
		{
			throw new NotImplementedException();
		}

		/// <summary>Add a stretch item</summary>
		public virtual void AddStretchCell( int stretch = 0 )
		{
			throw new NotImplementedException();
		}

		/// <summary>Adds a 1 pixel line</summary>
		public Separator AddSeparator( bool light = false )
		{
			var separator = Add( new Separator( 1 ) { Color = light ? Color.White.WithAlpha( 0.1f ) : Color.Black.WithAlpha( 0.3f ) } );
			return separator;
		}

		/// <summary>Adds a line</summary>
		public Separator AddSeparator( float size, Color color )
		{
			var separator = Add( new Separator( size ) { Color = color } );
			return separator;
		}

	}

}


