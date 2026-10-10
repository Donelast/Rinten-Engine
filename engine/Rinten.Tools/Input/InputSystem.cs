using Rinten;

namespace Editor;

public static partial class InputSystem
{
	public static IReadOnlyList<InputAction> GetCommonInputs() => Rinten.Engine.Input.CommonInputs;
}
