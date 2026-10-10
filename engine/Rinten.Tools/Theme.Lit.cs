using Rinten;
using System;

namespace Editor;

public static partial class Theme
{
	//The wash: shared highlight gradient from one edge, used by tabs, scene tree and console

	/// <summary>Which edge of the rectangle the wash is brightest against.</summary>
	public enum LitEdge
	{
		/// <summary>Across it, left to right: a row, a label, a line of text.</summary>
		Left,

		/// <summary>Down it from the top: a tab, or anything carrying a strip.</summary>
		Top,
	}

	/// <summary>How much colour is in the wash where it is brightest. Low on purpose - this lies over a surface that is already saying something, and a wash you read as a fill is one that has taken the surface over.</summary>
	public static float LitStrength => 0.22f;

	/// <summary>The same again for the thing that is picked out rather than pointed at.</summary>
	public static float LitStrengthOn => 0.45f;

	/// <summary>And for a row the pointer is merely crossing.</summary>
	public static float LitStrengthOver => 0.12f;

	/// <summary>The corner the wash is cut with, where the caller has no shape of its own.</summary>
	public static float LitRadius => 3.0f;

	/// <summary>How far the colour survives across a row, in pixels.</summary>
	public static float LitReach => 240.0f;

	/// <summary>Plate under the wash for selected items, a step above the hover plate</summary>
	public static Color LitPlate => VeilHover;

	//Short enough that it is never something to wait through. What it is for is the eye finding the thing that changed: a plate that appears between two frames has to be looked for, and one that arrives is watched.
	/// <summary>How long the wash takes to arrive or leave, for whatever is using <see cref="Lit"/> to fade it.</summary>
	public static float LitSeconds => 0.12f;

	/// <summary>The colour of a thing that is lit for no reason but being the one that is on.</summary>
	public static Color LitColour => Accent;

	/// <summary>How thick the rule at the head of a wash is.</summary>
	public static float LitLine => 1.0f;

	public static float LitLineStrength => 0.5f;

	/// <summary>The rule that sits at the head of a wash, where the thing wearing it wants an edge as well as light.</summary>
	public static void DrawLitRule( in Rect rect, Color colour, float amount = 1.0f )
	{
		if ( amount <= 0.0f ) return;
		if ( rect.Width <= 0.0f || rect.Height <= 0.0f ) return;

		Paint.ClearPen();
		Paint.SetBrush( colour.WithAlpha( LitLineStrength * amount ) );
		Paint.DrawRect( rect );
	}

	/// <summary>The wash, over the rectangle given, in the colour given.</summary>
	public static void DrawLit( in Rect rect, Color colour, float strength, LitEdge edge = LitEdge.Left,
		float reach = 0.0f, float radius = -1.0f )
	{
		if ( radius < 0.0f ) radius = LitRadius;

		DrawLit( rect, colour, strength, edge, reach, radius, radius, radius, radius );
	}

	/// <summary>The same, for something whose corners are not all alike - a tab, which is round along the top and square where it meets the panel it opens.</summary>
	public static void DrawLit( in Rect rect, Color colour, float strength, LitEdge edge, float reach,
		float topLeft, float topRight, float bottomRight, float bottomLeft )
	{
		if ( strength <= 0.0f ) return;
		if ( rect.Width <= 0.0f || rect.Height <= 0.0f ) return;

		//Flat since 09-14, the way the launcher answers the pointer: no gradient from an edge any more, a plate of the colour at half the
		//strength - about what the wash averaged over its reach. The edge and reach are still taken so nothing that calls this has to change.
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( colour.WithAlpha( strength * 0.5f ) );
		Paint.DrawRect( rect, topLeft, topRight, bottomRight, bottomLeft );
	}

	/// <summary>How far a thing has faded into being lit, kept by whatever is being lit.</summary>
	public struct Lit
	{
		private float amount;
		private float wanted;
		private float at;

		/// <summary>Where the fade has got to, 0 to 1.</summary>
		public readonly float Amount => amount;

		/// <summary>Whether it still has somewhere to get to.</summary>
		public readonly bool Moving => MathF.Abs( wanted - amount ) > 0.001f;

		/// <summary>Moves towards lit or unlit by however long it has been since the last paint, and says where it got to.</summary>
		public float Step( bool on, float seconds = -1.0f )
		{
			if ( seconds < 0.0f ) seconds = LitSeconds;

			var now = RealTime.Now;
			var target = on ? 1.0f : 0.0f;

			//Time between paints only while fading, capped at 50 ms; the first step counts as zero
			var elapsed = at <= 0.0f || target != wanted
				? 0.0f
				: ( now - at ).Clamp( 0.0f, 0.05f );

			at = now;
			wanted = target;

			amount = seconds <= 0.0f
				? target
				: amount.Approach( target, elapsed / seconds );

			return amount;
		}
	}
}
