using Microsoft.Win32;
using System.Collections.Immutable;

namespace Rinten;

public static partial class Gizmo
{
	/// <summary>Using pure primary colors is horrible. Lets make it easier to avoid.</summary>
	public static class Colors
	{
		//By the letter, as everywhere else does it: X red, Y green, Z blue. Which also puts each
		//colour on the axis its Euler angle turns about - pitch about X, yaw about Y, roll about Z.
		public static Color Red { get; } = "#ff7096";
		public static Color Right => Red;
		public static Color Pitch => Red;

		public static Color Green { get; } = "#ccff33";
		public static Color Up => Green;
		public static Color Yaw => Green;

		public static Color Blue { get; } = "#00a8e8";
		public static Color Forward => Blue;
		public static Color Roll => Blue;

		public static Color Selected { get; } = "#fbfbfb";
		public static Color Hovered { get; } = "#90f1ef";
		public static Color Active { get; } = "#ffc600";
	}
}
