using Rinten;

namespace Editor;

//What the editor's widgets are made of.
internal static class EditorBackend
{
	private static bool? forced;

	/// <summary>Whether widgets are panels of ours.</summary>
	public static bool Panels
	{
		get
		{
			if ( forced.HasValue ) return forced.Value;

			forced = true;

			return forced.Value;
		}

		//For a test, which makes widgets without a process around them.
		internal set => forced = value;
	}

}
