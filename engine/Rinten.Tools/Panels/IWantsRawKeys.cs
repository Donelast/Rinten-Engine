namespace Editor;

/// <summary>A panel that wants the keyboard to itself while it has the focus.</summary>
public interface IWantsRawKeys
{
	/// <summary>Whether the keys typed right now are this panel's. True unless there is a reason for it not to be - a field that is not being edited, say.</summary>
	bool WantsRawKeys => true;

	//False for a field: somebody writing a name still expects ctrl+S to save. True only while a key is being caught rather than typed - binding ctrl+D should record ctrl+D, not duplicate whatever is selected.
	/// <summary>Whether combinations with ctrl or alt are this panel's as well.</summary>
	bool WantsModifiedKeys => false;
}
