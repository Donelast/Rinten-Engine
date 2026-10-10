using Rinten.UI;

namespace Editor;

//Container-width classes for inspector controls, in logical UI pixels. The UI has no container queries. Apply after
//layout so styles can hide optional parts or stack rows. An eight-pixel release margin prevents threshold flicker.
internal static class WidthTiers
{
	static readonly (int Width, string Class)[] Tiers =
	[
		(240, "under-240"), (180, "under-180"), (140, "under-140"),
		(60, "under-60"), (40, "under-40")
	];

	const float Hysteresis = 8.0f;

	/// <summary>State holds one bit per threshold between layout passes.</summary>
	public static void Apply( Panel panel, ref int state )
	{
		if ( panel is null || !panel.IsValid() ) return;
		var width = panel.Box.Rect.Width * panel.ScaleFromScreen;
		if ( width <= 0.0f ) return;

		for ( var i = 0; i < Tiers.Length; i++ )
		{
			var bit = 1 << i;
			var limit = Tiers[i].Width + ((state & bit) != 0 ? Hysteresis : 0);
			var under = width < limit;
			if ( under ) state |= bit;
			else state &= ~bit;
			panel.SetClass( Tiers[i].Class, under );
		}
	}
}
