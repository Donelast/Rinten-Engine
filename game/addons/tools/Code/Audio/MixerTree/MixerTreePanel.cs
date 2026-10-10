using Rinten.Audio;

namespace Editor.Audio;

/// <summary>Mixer tree backed by TreePanel.</summary>
[StyleSheet( "MixerTreePanel.cs.scss" )]
public class MixerTreePanel : TreePanel<Mixer>
{
	public int StructureRevision => Revision;

	public MixerTreePanel()
	{
		if ( Mixer.Master is not null )
			Select( [Mixer.Master], skipEvents: true );
	}

	protected override int Revision => GetStructureHash( Mixer.Master );

	protected override IEnumerable<Mixer> Roots
	{
		get
		{
			if ( Mixer.Master is null )
				yield break;

			yield return Mixer.Master;
		}
	}

	protected override IEnumerable<Mixer> GetChildren( Mixer node ) => node.GetChildren();

	protected override TreeItemInfo GetInfo( Mixer node )
		=> new( node.Name, "settings_input_component", node.ChildCount > 0 );

	protected override TreeRowPanel<Mixer> CreateRow( Mixer node, TreeItemInfo info, int depth )
		=> new MixerTreeRow( this, node, info, depth );

	protected override void OnContextMenu( Mixer mixer )
	{
		var menu = new ContextMenu( null );

		menu.AddOption( "Add Child", "add", () =>
		{
			var child = mixer.AddChild();
			OpenTo( child );
			Select( [child] );
		} );

		var delete = menu.AddOption( "Delete", "close", () => mixer.Destroy() );
		delete.Enabled = !mixer.IsMaster;

		var setDefault = menu.AddOption( "Set as Default Mixer", "grade", () => Mixer.Default = mixer );
		setDefault.Enabled = Mixer.Default != mixer;

		menu.OpenAtCursor();
	}

	[Shortcut( "editor.delete", "DEL" )]
	private void DeleteMixer()
	{
		if ( Selected.FirstOrDefault() is not Mixer mixer || mixer.IsMaster )
			return;

		mixer.Destroy();
	}

	public static int GetStructureHash( Mixer mixer ) => StructureHash( mixer, 0 );

	//One list of children per depth of the walk, kept between calls - see StructureHash.
	static readonly List<List<Mixer>> childrenAt = new();

	//Asked every frame by the dock and by the tree, and it made an array of every mixer's children to walk
	//them. Into a list per depth instead: a mixer's children are still being walked while theirs are asked for.
	static int StructureHash( Mixer mixer, int depth )
	{
		if ( mixer is null ) return 0;

		var hash = HashCode.Combine( mixer, mixer.ChildCount, mixer.ProcessorCount, mixer.Name );

		if ( childrenAt.Count <= depth ) childrenAt.Add( new List<Mixer>() );

		var children = childrenAt[depth];
		mixer.GetChildren( children );

		foreach ( var child in children )
			hash = HashCode.Combine( hash, StructureHash( child, depth + 1 ) );

		children.Clear();

		return hash;
	}
}
