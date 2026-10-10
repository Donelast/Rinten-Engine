using Rinten.UI;

namespace Editor.UiEditor;

//What a computed style's values mean, read the way the editor needs them.
internal static class StyleValues
{
	/// <summary>Whether a length is set at all. A value a transition ran out to is left <see cref="LengthUnit.Undefined"/> rather than null - a rule that lost its <c>right</c> keeps an undefined one on the panel for as long as it lives - and that is not a right to anchor to.</summary>
	public static bool IsSet( this Length? length ) => length is { } value && value.Unit != LengthUnit.Undefined;
}
