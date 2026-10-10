using Rinten.UI;

namespace Editor.NodeEditor;

/// <summary>What stands in for a value on a plug that has nothing wired into it.</summary>
public abstract class ValueEditor : Panel
{
	/// <summary>Whether it is shown at all - a plug with a wire in it has no value to edit.</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>Whether it says what the plug is called, so the plug need not.</summary>
	public virtual bool HideLabel => true;

	protected ValueEditor()
	{
		AddClass( "value-editor" );
	}

	/// <summary>Re-reads whatever this shows.</summary>
	public virtual void Update() => MarkRenderDirty();
}
