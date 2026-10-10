namespace Rinten
{
	/// <summary>Calls a method (default On[Property]Changed) with old and new value when the property changes</summary>
	[AttributeUsage( AttributeTargets.Property )]
	[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.Static | CodeGeneratorFlags.WrapPropertySet, "Rinten.ChangeCallback.OnPropertySet", 10 )]
	public class ChangeAttribute : Attribute
	{
		/// <summary>Name of the method to call on change. If no name is provided, we will try to call On[PropertyName]Changed.</summary>
		public string Name { get; set; }

		public ChangeAttribute( string name = null )
		{
			Name = name;
		}
	}
}
