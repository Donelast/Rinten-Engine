using System;

namespace Editor;

//What the profiler's columns are, and the thing that picks which one orders the tree.
internal sealed class ProfilerHeaderPanel : PanelListHeader
{
	private static readonly (string Title, ProfilerSort Sort)[] Titles =
	[
		("time", ProfilerSort.Time),
		("max", ProfilerSort.Max),
		("self", ProfilerSort.Self),
		("calls", ProfilerSort.Calls),
		("alloc", ProfilerSort.Alloc),
	];

	private readonly PanelListHeaderCell[] cells = new PanelListHeaderCell[Titles.Length];

	/// <summary>A column was chosen to order by.</summary>
	public Action<ProfilerSort> Picked { get; set; }

	public ProfilerHeaderPanel() : base( "name" )
	{
		AddClass( "profiler-header" );

		Name.Pressed = () => Picked?.Invoke( ProfilerSort.Name );

		for ( var at = 0; at < Titles.Length; ++at )
		{
			var sort = Titles[at].Sort;

			cells[at] = AddColumn( Titles[at].Title );
			cells[at].Pressed = () => Picked?.Invoke( sort );
		}
	}

	/// <summary>Lights the column the tree is ordered by.</summary>
	public void SetSort( ProfilerSort sort )
	{
		Name.SetActive( sort == ProfilerSort.Name );

		for ( var at = 0; at < cells.Length; ++at )
		{
			cells[at].SetActive( Titles[at].Sort == sort );
		}
	}
}
