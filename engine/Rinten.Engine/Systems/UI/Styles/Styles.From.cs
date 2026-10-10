namespace Rinten.UI
{
	public partial class Styles
	{
		public override void Add( BaseStyles bs )
		{
			base.Add( bs );

			if ( bs is not Styles a )
				return;

			if ( a.HasTransitions )
			{
				if ( Transitions == null )
					Transitions = new TransitionList();

				Transitions.AddTransitions( a.Transitions );
			}

			BoxShadow.AddFrom( a.BoxShadow );
			TextShadow.AddFrom( a.TextShadow );
			FilterDropShadow.AddFrom( a.FilterDropShadow );

			if ( !a.TextGradient.ColorOffsets.IsDefaultOrEmpty )
			{
				TextGradient = a.TextGradient;
			}

			//The gradient is part of the background-image property, so any rule that set background-image - url(), none, or a
			//gradient - carries its gradient state (possibly empty) over ours. A later url() must clear an earlier gradient.
			if ( a._backgroundImage != null || !a.BackgroundGradient.ColorOffsets.IsDefaultOrEmpty )
			{
				BackgroundGradient = a.BackgroundGradient;
			}
		}

		public override void From( BaseStyles bs )
		{
			base.From( bs );

			if ( bs is not Styles a ) return;

			CopyShadows( bs );

			Transitions?.Clear();

			if ( a.HasTransitions )
			{
				Transitions ??= new TransitionList();

				Transitions.AddTransitions( a.Transitions );
			}

			TextGradient = a.TextGradient;
			BackgroundGradient = a.BackgroundGradient;
		}

		internal void CopyShadows( BaseStyles bs )
		{
			if ( bs is not Styles a )
				return;

			CopyShadowList( BoxShadow, a.BoxShadow );
			CopyShadowList( TextShadow, a.TextShadow );
			CopyShadowList( FilterDropShadow, a.FilterDropShadow );
		}

		//One list over another - and nothing at all when both are empty, which nearly every panel's are. A cascade copies all three twice
		//for each panel it rebuilds, and in a browser the clear and the add of an empty list are four calls that leave compiled code.
		private static void CopyShadowList( ShadowList to, ShadowList from )
		{
			to.IsNone = from.IsNone;

			if ( to.Count == 0 && from.Count == 0 ) return;

			to.Clear();
			to.AddRange( from );
		}
	}
}
