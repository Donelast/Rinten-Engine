namespace Rinten.UI;

public partial class Panel
{
	private int layoutStyleHash;

	//Whether layout-relevant style inputs changed since last push; relative units are tracked elsewhere
	private bool LayoutStyleChanged()
	{
		var hash = LayoutStyleHash();
		if ( hash == layoutStyleHash ) return false;

		layoutStyleHash = hash;
		return true;
	}

	private int LayoutStyleHash()
	{
		var style = ComputedStyle;
		var h = new HashCode();

		AddLength( ref h, style.Width );
		AddLength( ref h, style.Height );
		AddLength( ref h, style.MinWidth );
		AddLength( ref h, style.MinHeight );
		AddLength( ref h, style.MaxWidth );
		AddLength( ref h, style.MaxHeight );
		AddLength( ref h, style.Left );
		AddLength( ref h, style.Right );
		AddLength( ref h, style.Top );
		AddLength( ref h, style.Bottom );
		AddLength( ref h, style.MarginLeft );
		AddLength( ref h, style.MarginRight );
		AddLength( ref h, style.MarginTop );
		AddLength( ref h, style.MarginBottom );
		AddLength( ref h, style.PaddingLeft );
		AddLength( ref h, style.PaddingRight );
		AddLength( ref h, style.PaddingTop );
		AddLength( ref h, style.PaddingBottom );
		AddLength( ref h, style.BorderLeftWidth );
		AddLength( ref h, style.BorderRightWidth );
		AddLength( ref h, style.BorderTopWidth );
		AddLength( ref h, style.BorderBottomWidth );
		AddLength( ref h, style.FlexBasis );
		AddLength( ref h, style.RowGap );
		AddLength( ref h, style.ColumnGap );

		h.Add( style.Display );
		h.Add( style.Position );
		h.Add( style.AspectRatio );
		h.Add( style.FlexGrow );
		h.Add( style.FlexShrink );
		h.Add( style.FlexWrap );
		h.Add( style.FlexDirection );
		h.Add( style.JustifyContent );
		h.Add( style.AlignContent );
		h.Add( style.AlignItems );
		h.Add( style.AlignSelf );
		h.Add( style.Overflow );

		//The scrollbar gutter rides on the border
		AddLength( ref h, style.ScrollbarWidth );
		h.Add( style.ScrollbarGutter );

		return h.ToHashCode();
	}

	//This panel's size changed. A child whose lengths were resolved against that size (a percentage in a calc(), a percentage border) holds a stale
	//pixel value and nothing in the cascade would revisit it, so it is marked for the next cascade here, from the one place that knows the size moved.
	private void MarkSizeDependentChildren()
	{
		if ( _children is null ) return;

		var marked = false;
		for ( var i = 0; i < _children.Count; i++ )
		{
			var child = _children[i];
			if ( child.LayoutTree?.DependsOnParentSize != true ) continue;

			child.SetNeedsPreLayout();
			marked = true;
		}

		if ( marked ) RequestTick();
	}

	//Hashes a length by unit and value; a calc() by its text, since its value alone says nothing.
	[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining )]
	private static void AddLength( ref HashCode h, Length? length )
	{
		if ( !length.HasValue )
		{
			h.Add( -1 );
			return;
		}

		var value = length.Value;
		h.Add( (int)value.Unit );
		if ( value.Unit == LengthUnit.Expression ) h.Add( value.ToString() );
		else h.Add( value.Value );
	}
}
