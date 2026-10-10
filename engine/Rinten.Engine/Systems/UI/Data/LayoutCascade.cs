namespace Rinten.UI;

public struct LayoutCascade
{
	public bool SelectorChanged;
	public bool ParentChanged;
	public bool SkipTransitions;

	public Styles ParentStyles;

	internal RootPanel Root;

	//Cascading Properties
	public float Scale;

	//Some properties cascade from their parent onto children if the children don't set them. Things like font size, color, cursor.
	internal void ApplyCascading( Styles cached )
	{
		if ( ParentStyles == null )
			return;

		cached.ApplyCascading( ParentStyles );

		if ( cached.TextShadow.Count == 0 )
		{
			if ( ParentStyles.TextShadow.Count != 0 )
				cached.TextShadow.AddRange( ParentStyles.TextShadow );
		}

		if ( cached.TextGradient.ColorOffsets.IsDefaultOrEmpty )
		{
			if ( !ParentStyles.TextGradient.ColorOffsets.IsDefaultOrEmpty )
			{
				cached.TextGradient = ParentStyles.TextGradient;
			}
		}
	}
}
