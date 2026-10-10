namespace Editor.Gallery;

//Closed over the value's type at run time, so a control for any type has a real property of that type to read and write. Public because reflection makes it.
/// <summary>A property to edit, with nothing around it: the object behind an automatic card.</summary>
public sealed class SampleHolder<T>
{
	[Title( "Value" )]
	public T Value { get; set; }
}
