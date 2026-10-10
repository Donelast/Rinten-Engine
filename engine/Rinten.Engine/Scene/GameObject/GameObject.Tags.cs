namespace Rinten;

public partial class GameObject
{
	[ActionGraphInclude]
	public GameTags Tags { get; init; }

	//Tags are inherited, and components cache them, so notify the whole subtree on change
	internal void OnTagsUpdatedInternal()
	{
		foreach ( var c in Components.GetAll( FindMode.EnabledInSelfAndDescendants ) )
		{
			c.OnTagsUpdatedInternal();
		}
	}
}
