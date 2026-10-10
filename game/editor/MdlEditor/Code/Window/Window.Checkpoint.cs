using System;

namespace Editor.MdlEditor;

/// <summary>Keeping what is open here on the disk without anybody having to ask for it.</summary>
public partial class Window
{
	//Writes what was edited, now. Called from both halves - the graph through MarkDirty , the model through
	//OnModelEdited - because the window writes both and a person editing one of them is not editing the other.
	internal void Edited() => Checkpoint();

	//Writes both halves to disk if they have a path; never prompts and never compiles
	internal void Checkpoint()
	{
		try
		{
			//The graph's own file, or the model it is kept in - the session knows which, and writes to whichever it is.
			if ( _dirty && _session.HasHome )
			{
				_session.Write();

				//And build the compiled graph beside the model. Writing is the document - the nodes as the editor holds them - and nothing outside this
				//window can play that: the runtime reads only the compiled graph, and a model whose graph was never compiled stands in its bind pose.
				_session.WriteCompiled();

				_dirty = false;
				UpdateTitle();
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Model editor: could not write what is open" );
		}
	}

	//The last thing a drag was holding back. Every edit writes, except the ones made while a handle is being dragged - those are one edit as
	//far as anybody watching is concerned, and they arrive one per frame. The write they were refused happens when the drag ends, which is here.
	[EditorEvent.Frame]
	private void WriteAfterDrag()
	{
		if ( !HasUnsavedChanges ) return;

		Checkpoint();
	}
}
