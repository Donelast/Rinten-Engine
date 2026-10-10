using System;
using System.Drawing;
using Facepunch.ActionGraphs;
using Rinten;

namespace Editor.ActionGraphs;

//How a value reads on a node.
internal static class ValueText
{
	public static string FormatValue( Type type, object value, out object rawValue )
	{
		rawValue = value;

		if ( type == typeof( Signal ) )
		{
			rawValue = "Signal";
		}

		switch ( rawValue )
		{
			case null when !type.IsValueType:
				return "null";

			case string str:
				return $"\"{str}\"";

			case Resource resource:
				return resource.ResourcePath;

			case Color32 color32:
				rawValue = (Color)color32;

				return color32.a >= 255
					? color32.Hex
					: $"{(color32 with { a = 255 }).Hex}, {color32.a * 100f / 255f:F0}%";

			case Color color:
				return color.a >= 0.995f
					? color.WithAlpha( 1f ).Hex
					: $"{color.WithAlpha( 1f ).Hex}, {color.a * 100:F0}%";

			case float floatVal:
				return $"{floatVal:F2}";

			case double doubleVal:
				return $"{doubleVal:F2}";

			case Vector2 vec2:
				return $"x: {vec2.x:F2}, y: {vec2.y:F2}";

			case Vector3 vec3:
				return $"x: {vec3.x:F2}, y: {vec3.y:F2}, z: {vec3.z:F2}";

			case Vector4 vec4:
				return $"x: {vec4.x:F2}, y: {vec4.y:F2}, z: {vec4.z:F2}, w: {vec4.w:F2}";

			case Rotation rot:
				rawValue = (Angles)rot;
				return $"p: {rot.Pitch():F2}, y: {rot.Yaw():F2}, r: {rot.Roll():F2}";

			case Angles angles:
				return $"p: {angles.pitch:F2}, y: {angles.yaw:F2}, r: {angles.roll:F2}";

			default:
				return $"{rawValue}";
		}
	}
}
