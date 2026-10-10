namespace Rinten;

/// <summary>The editor, on our own core.</summary>
public class CoreEditorAppSystem : CoreAppSystem
{
	protected override bool IsEditorApp => true;

	public override void Init()
	{
		//Said before anything can ask: every widget this process makes is one of ours. It is what the switch would work out for itself
		//once no other renderer is known to be present, but the answer is kept the first time it is asked for and that can be earlier than this.
		Editor.EditorBackend.Panels = true;

		base.Init();
	}
}
