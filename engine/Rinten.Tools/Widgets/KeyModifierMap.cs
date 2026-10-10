using Rinten;

namespace Editor;

internal static class KeyModifierMap
{
	public static KeyboardModifiers Translate( NativeKeyModifiers mods )
	{
		var o = KeyboardModifiers.None;

		if ( mods.Contains( NativeKeyModifiers.ShiftModifier ) ) o |= KeyboardModifiers.Shift;
		if ( mods.Contains( NativeKeyModifiers.AltModifier ) ) o |= KeyboardModifiers.Alt;
		if ( mods.Contains( NativeKeyModifiers.ControlModifier ) ) o |= KeyboardModifiers.Ctrl;

		return o;
	}
}
