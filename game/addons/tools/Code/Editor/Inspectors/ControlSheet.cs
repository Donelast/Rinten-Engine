using System.Linq.Expressions;
using Rinten.Internal;

namespace Editor.Inspectors;

[StyleSheet( "ControlSheet.cs.scss" )]
public partial class ControlSheet : IControlSheet
{
	/// <summary>A row for a property of a static class - a preference, a console variable.</summary>
	public void AddProperty<T>( Expression<Func<T>> property )
		=> AddPropertyRow( ReflectionSerializedProperty.For( property ) );

	/// <summary>A row for one named property of an object.</summary>
	public void AddProperty<T, U>( T target, Expression<Func<T, U>> property )
	{
		if ( property.Body is not MemberExpression member )
			throw new ArgumentException( "Expression must target a property.", nameof( property ) );

		AddPropertyRow( target.GetSerialized().GetProperty( member.Member.Name ) );
	}

	/// <summary>A group of rows, named, from properties gathered elsewhere.</summary>
	public void AddGroup( string name, IEnumerable<SerializedProperty> properties )
		=> AddGroup( new IControlSheet.Group( properties.ToList() ) { Name = name } );
}
