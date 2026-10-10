using System;
using Rinten.Layout;

namespace LayoutTests.Harness;

//A stand-in for a text control's measure function: a monospace face where every glyph is 8 x 12 and lines break at spaces, never inside
//a word. The node's IFixtureNode.Context is the text. Deterministic and cheap, so the trees can be large and the numbers repeatable.
internal static class TextMeasure
{
	public const float GlyphWidth = 8f;
	public const float LineHeight = 12f;

	//How far short of the room a word may fall and still be placed on the line.
	public const float WrapSlack = 0.01f;

	private static readonly string[] Vocabulary =
	{
		"panel", "row", "inspector", "value", "name", "x", "position", "rotation", "scale", "mesh",
		"material", "light", "camera", "on", "off", "a", "the", "component", "hierarchy", "tool",
	};

	//A run of words from a small vocabulary.
	public static string Words( Rng rng, int count )
	{
		var words = new string[count];
		for ( var i = 0; i < count; i++ ) words[i] = rng.Pick( Vocabulary );
		return string.Join( ' ', words );
	}

	public static LayoutSize Measure( IFixtureNode node, float width, MeasureMode widthMode, float height, MeasureMode heightMode )
	{
		var text = node.Context as string ?? "";
		var natural = text.Length * GlyphWidth;

		var measuredWidth = widthMode switch
		{
			MeasureMode.Exactly => width,
			MeasureMode.AtMost => MathF.Min( natural, width ),
			_ => natural,
		};

		var contentHeight = CountLines( text, measuredWidth ) * LineHeight;
		var measuredHeight = heightMode switch
		{
			MeasureMode.Exactly => height,
			MeasureMode.AtMost => MathF.Min( contentHeight, height ),
			_ => contentHeight,
		};

		return new LayoutSize( measuredWidth, measuredHeight );
	}

	//The widest word: what the text cannot be narrower than without breaking a word.
	public static float MinContentWidth( string text )
	{
		var widest = 0;
		var current = 0;
		foreach ( var c in text )
		{
			if ( c == ' ' )
			{
				widest = Math.Max( widest, current );
				current = 0;
			}
			else
			{
				current++;
			}
		}

		return Math.Max( widest, current ) * GlyphWidth;
	}

	//Greedy word wrap. A word wider than the line takes a line of its own and overflows it. Scans the string rather
	//than splitting it, so measuring never allocates and the allocation counters describe the engine, not the harness.
	public static int CountLines( string text, float width )
	{
		if ( text.Length == 0 ) return 1;

		var lines = 1;
		var used = 0f;
		var start = 0;
		for ( var i = 0; i <= text.Length; i++ )
		{
			if ( i < text.Length && text[i] != ' ' ) continue;

			var wordWidth = (i - start) * GlyphWidth;
			start = i + 1;
			var needed = used == 0 ? wordWidth : used + GlyphWidth + wordWidth;

			//A word that misses the room by less than a hundredth of a pixel fits: the room comes out of float arithmetic (a percentage of a
			//percentage), and a wrap that hinges on its last bits would make the answer jump between two questions that are the same question.
			if ( used > 0 && needed > width + WrapSlack )
			{
				lines++;
				used = wordWidth;
			}
			else
			{
				used = needed;
			}
		}

		return lines;
	}
}
