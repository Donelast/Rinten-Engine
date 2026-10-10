using System;

namespace Editor
{
	public partial class Widget : EditorObject
	{
		Layout _layout;

		/// <summary>The widget's internal layout, if any.</summary>
		public Layout Layout
		{
			get => _layout;
			set
			{
				if ( _layout == value ) return;

				_layout = value;

				_layout?.SetOwner( this );
				UpdateGeometry();
			}
		}

		/// <summary>Raises this widget above overlapping siblings.</summary>
		public void Raise()
		{
			//The last sibling is the one drawn (and hit-tested) on top - see EditorDrag.Under - so raising means moving to the end, not the start.
			var siblings = Surface?.Parent?.ChildrenCount ?? 0;
			if ( siblings > 0 ) Surface.Parent.SetChildIndex( Surface, siblings - 1 );
		}

		/// <summary>Lowers this widget below overlapping siblings.</summary>
		public void Lower()
		{
			Surface?.Parent?.SetChildIndex( Surface, 0 );
		}

		/// <summary>Sets this widget's explicit stacking order among overlapping siblings.</summary>
		public void SetZIndex( int zIndex )
		{
			if ( Surface is not null )
				Surface.Style.ZIndex = zIndex;
		}

		public bool AttachToEditorOverlay( Widget owner )
		{
			if ( Surface is null || EditorOverlay.For( owner ) is not { } overlay )
				return false;

			if ( Surface.Parent != overlay )
				overlay.AddChild( Surface );

			return true;
		}

	}

}
