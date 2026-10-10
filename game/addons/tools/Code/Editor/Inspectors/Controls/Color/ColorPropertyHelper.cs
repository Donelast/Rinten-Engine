using System.Runtime.CompilerServices;

namespace Editor.Inspectors;

internal static class ColorPropertyHelper
{
	sealed class PickerState
	{
		public float Hue;
		public float Saturation;
		public float Value;
		public float Alpha = 1f;
		public float Range = 1f;
	}

	static readonly ConditionalWeakTable<SerializedProperty, PickerState> PickerStates = new();

	public static void BeginPicker( SerializedProperty property )
	{
		if ( property is null )
			return;

		PickerStates.Remove( property );
		PickerStates.Add( property, ImportPickerState( property ) );
	}

	public static void ImportPickerColor( SerializedProperty property, Color color )
	{
		if ( property is null )
			return;

		if ( !TryGetPickerState( property, out var state ) )
		{
			SetColor( property, color );
			return;
		}

		var max = MathF.Max( color.r, MathF.Max( color.g, color.b ) );
		state.Range = max > 1f ? max : 1f;
		var div = 1f / state.Range;
		var normalized = color with
		{
			r = color.r * div,
			g = color.g * div,
			b = color.b * div
		};
		var hsv = normalized.ToHsv();
		state.Alpha = color.a;
		state.Saturation = hsv.Saturation;
		state.Value = hsv.Value;
		if ( hsv.Saturation > 0.001f )
			state.Hue = hsv.Hue;

		ApplyPickerState( property, state );
	}

	public static Color GetColor( SerializedProperty property )
	{
		if ( property.PropertyType == typeof( Color32 ) )
			return property.GetValue<Color32>().ToColor();

		if ( property.PropertyType == typeof( ColorHsv ) )
			return property.GetValue<ColorHsv>().ToColor();

		return property.GetValue<Color>();
	}

	/// <summary>Color clamped for swatch display (HDR rgb normalized, alpha kept).</summary>
	public static Color GetDisplayColor( Color color )
	{
		var (normalized, _) = SplitRange( color );
		return normalized with { a = color.a };
	}

	public static void SetColor( SerializedProperty property, Color color )
	{
		if ( property.PropertyType == typeof( Color32 ) )
			property.SetValue( color.ToColor32() );
		else if ( property.PropertyType == typeof( ColorHsv ) )
			property.SetValue( color.ToHsv() );
		else
			property.SetValue( color );
	}

	static bool TryGetPickerState( SerializedProperty property, out PickerState state )
	{
		if ( property is null )
		{
			state = null;
			return false;
		}

		return PickerStates.TryGetValue( property, out state );
	}

	static PickerState ImportPickerState( SerializedProperty property )
	{
		var color = GetColor( property );
		var max = MathF.Max( color.r, MathF.Max( color.g, color.b ) );
		var range = max > 1f ? max : 1f;
		var div = 1f / range;
		var normalized = color with
		{
			r = color.r * div,
			g = color.g * div,
			b = color.b * div
		};
		var hsv = normalized.ToHsv();

		return new PickerState
		{
			Hue = hsv.Hue,
			Saturation = hsv.Saturation,
			Value = hsv.Value,
			Alpha = color.a,
			Range = range
		};
	}

	static void ApplyPickerState( SerializedProperty property, PickerState state )
	{
		var rgb = new ColorHsv( state.Hue, state.Saturation, state.Value, state.Alpha ).ToColor();
		SetColor( property, new Color(
			rgb.r * state.Range,
			rgb.g * state.Range,
			rgb.b * state.Range,
			state.Alpha ) );
	}

	static Color PickerStateToColor( PickerState state, float? hue = null, float? saturation = null, float? value = null )
	{
		var rgb = new ColorHsv(
			hue ?? state.Hue,
			saturation ?? state.Saturation,
			value ?? state.Value,
			state.Alpha ).ToColor();

		return new Color(
			rgb.r * state.Range,
			rgb.g * state.Range,
			rgb.b * state.Range,
			state.Alpha );
	}

	static (Color Normalized, float Range) SplitRange( Color color )
	{
		var max = MathF.Max( color.r, MathF.Max( color.g, color.b ) );
		var range = max > 1f ? max : 1f;
		var div = 1f / range;

		return (color with { r = color.r * div, g = color.g * div, b = color.b * div }, range);
	}

	static Color ApplyRange( Color normalized, float range )
	{
		return normalized with
		{
			r = normalized.r * range,
			g = normalized.g * range,
			b = normalized.b * range
		};
	}

	public static float GetHue( SerializedProperty property )
	{
		if ( property is null )
			return 0;

		if ( TryGetPickerState( property, out var state ) )
			return state.Hue;

		var (normalized, _) = SplitRange( GetColor( property ) );
		return normalized.ToHsv().Hue;
	}

	public static void SetHue( SerializedProperty property, float hue )
	{
		if ( property is null )
			return;

		hue = hue.Clamp( 0, 360f - 0.001f );

		if ( TryGetPickerState( property, out var state ) )
		{
			state.Hue = hue;
			ApplyPickerState( property, state );
			return;
		}

		var (normalized, range) = SplitRange( GetColor( property ) );
		var hsv = normalized.ToHsv() with { Hue = hue };
		SetColor( property, ApplyRange( hsv.ToColor(), range ) );
	}

	public static float GetSaturation( SerializedProperty property )
	{
		if ( property is null )
			return 0;

		if ( TryGetPickerState( property, out var state ) )
			return state.Saturation;

		var (normalized, _) = SplitRange( GetColor( property ) );
		return normalized.ToHsv().Saturation;
	}

	public static float GetValue( SerializedProperty property )
	{
		if ( property is null )
			return 0;

		if ( TryGetPickerState( property, out var state ) )
			return state.Value;

		var (normalized, _) = SplitRange( GetColor( property ) );
		return normalized.ToHsv().Value;
	}

	public static void SetSaturationValue( SerializedProperty property, float saturation, float value )
	{
		if ( property is null )
			return;

		saturation = saturation.Clamp( 0, 1 );
		value = value.Clamp( 0, 1 );

		if ( TryGetPickerState( property, out var state ) )
		{
			state.Saturation = saturation;
			state.Value = value;
			ApplyPickerState( property, state );
			return;
		}

		var (normalized, range) = SplitRange( GetColor( property ) );
		var hsv = normalized.ToHsv() with { Saturation = saturation, Value = value };
		SetColor( property, ApplyRange( hsv.ToColor(), range ) );
	}

	public static Color GetColorAtHue( SerializedProperty property, float hue )
	{
		if ( property is null )
			return Color.White;

		hue = hue.Clamp( 0, 360f - 0.001f );

		if ( TryGetPickerState( property, out var state ) )
			return PickerStateToColor( state, hue: hue );

		var (normalized, range) = SplitRange( GetColor( property ) );
		var hsv = normalized.ToHsv() with { Hue = hue };
		return ApplyRange( hsv.ToColor(), range );
	}

	public static Color GetColorAtSaturationValue( SerializedProperty property, float saturation, float value )
	{
		if ( property is null )
			return Color.White;

		saturation = saturation.Clamp( 0, 1 );
		value = value.Clamp( 0, 1 );

		if ( TryGetPickerState( property, out var state ) )
			return PickerStateToColor( state, saturation: saturation, value: value );

		var (normalized, range) = SplitRange( GetColor( property ) );
		var hsv = normalized.ToHsv() with { Saturation = saturation, Value = value };
		return ApplyRange( hsv.ToColor(), range );
	}

	public static bool PickerColorsMatch( SerializedProperty property, Color a, Color b )
	{
		if ( property?.PropertyType == typeof( Color32 ) )
		{
			a = a.ToColor32().ToColor();
			b = b.ToColor32().ToColor();
		}

		const float eps = 0.5f / 255f;
		return MathF.Abs( a.r - b.r ) <= eps
			&& MathF.Abs( a.g - b.g ) <= eps
			&& MathF.Abs( a.b - b.b ) <= eps;
	}

	public static float GetAlpha( SerializedProperty property ) => property is null ? 0 : GetColor( property ).a;

	public static Color GetRgbWithoutRange( SerializedProperty property )
	{
		if ( property is null )
			return Color.White;

		if ( TryGetPickerState( property, out var state ) )
			return new ColorHsv( state.Hue, state.Saturation, state.Value, state.Alpha ).ToColor();

		var (normalized, _) = SplitRange( GetColor( property ) );
		return normalized;
	}

	public static Color GetHueHandleColor( float hue ) =>
		new ColorHsv( hue, 1, 1 ).ToColor();

	public static Color GetAlphaHandleColor( SerializedProperty property, float alpha )
	{
		if ( property is null )
			return Color.Transparent;

		return GetColor( property ).WithAlpha( 1 ).WithAlpha( alpha );
	}

	public static Color GetAlphaTrackColor( SerializedProperty property )
	{
		if ( property is null )
			return Color.White;

		return GetColor( property ).WithAlpha( 1 );
	}

	public static Color GetRangeHandleColor( float fraction )
	{
		var v = fraction.Clamp( 0, 1 );
		return new Color( v, v, v, 1 );
	}

	public static void SetAlpha( SerializedProperty property, float alpha )
	{
		if ( property is null )
			return;

		alpha = alpha.Clamp( 0, 1 );

		if ( TryGetPickerState( property, out var state ) )
		{
			state.Alpha = alpha;
			ApplyPickerState( property, state );
			return;
		}

		SetColor( property, GetColor( property ).WithAlpha( alpha ) );
	}

	public static float GetRange( SerializedProperty property )
	{
		if ( property is null )
			return 1f;

		if ( TryGetPickerState( property, out var state ) )
			return state.Range;

		var color = GetColor( property );
		var max = MathF.Max( color.r, MathF.Max( color.g, color.b ) );
		return max > 1f ? max : 1f;
	}

	public static void SetRange( SerializedProperty property, float range )
	{
		if ( property is null )
			return;

		range = range.Clamp( 0, 1000 );

		if ( TryGetPickerState( property, out var state ) )
		{
			state.Range = range;
			ApplyPickerState( property, state );
			return;
		}

		var (normalized, _) = SplitRange( GetColor( property ) );
		SetColor( property, ApplyRange( normalized, range ) );
	}

	public static bool HasAlpha( SerializedProperty property )
	{
		if ( property.TryGetAttribute<ColorUsageAttribute>( out var usage ) )
			return usage.HasAlpha;

		return true;
	}

	public static bool IsHDR( SerializedProperty property )
	{
		if ( property is null )
			return false;

		if ( property.PropertyType == typeof( Color32 ) || property.PropertyType == typeof( ColorHsv ) )
			return false;

		if ( property.TryGetAttribute<ColorUsageAttribute>( out var usage ) )
			return usage.IsHDR;

		return true;
	}

	public static int ChannelToDisplay( SerializedProperty property, int channel, Color color )
	{
		if ( property.PropertyType == typeof( Color32 ) )
		{
			var c32 = color.ToColor32();
			return channel switch
			{
				0 => c32.r,
				1 => c32.g,
				2 => c32.b,
				3 => c32.a,
				_ => 0
			};
		}

		return (int)Math.Round( channel switch
		{
			0 => color.r,
			1 => color.g,
			2 => color.b,
			3 => color.a,
			_ => 0f
		} * 255f );
	}

	public static Color SetChannelFromDisplay( SerializedProperty property, Color color, int channel, int value )
	{
		value = value.Clamp( 0, 255 );

		if ( property.PropertyType == typeof( Color32 ) )
		{
			var c32 = color.ToColor32();
			switch ( channel )
			{
				case 0: c32.r = (byte)value; break;
				case 1: c32.g = (byte)value; break;
				case 2: c32.b = (byte)value; break;
				case 3: c32.a = (byte)value; break;
			}

			return c32.ToColor();
		}

		var f = value / 255f;
		return color with
		{
			r = channel == 0 ? f : color.r,
			g = channel == 1 ? f : color.g,
			b = channel == 2 ? f : color.b,
			a = channel == 3 ? f : color.a
		};
	}
}
