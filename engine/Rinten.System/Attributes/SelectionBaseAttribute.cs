namespace Rinten
{
	/// <summary>Marks a component's GameObject as the one picked when clicking a child in the Scene View</summary>
	[AttributeUsage( AttributeTargets.Class )]
	public sealed class SelectionBaseAttribute : Attribute
	{
		public SelectionBaseAttribute() : base()
		{
		}
	}

}
