namespace Rinten
{
	/// <summary>When applied to an attribute, which is then applied to a type.. This will make <see cref="TargetType"/> set on the attribute upon load. This provides a convenient way to know which type the attribute was attached to.</summary>
	public interface ITypeAttribute
	{
		/// <summary>The type this attribute was attached to.</summary>
		Type TargetType { get; set; }

		/// <summary>Called when a class with this attribute is registered via the TypeLibrary.</summary>
		void TypeRegister() { }

		/// <summary>Called when a class with this attribute is unregistered via the TypeLibrary.</summary>
		void TypeUnregister() { }
	}
}
