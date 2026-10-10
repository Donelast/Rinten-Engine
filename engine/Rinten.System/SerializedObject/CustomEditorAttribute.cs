using Facepunch.ActionGraphs;

namespace Rinten;

[AttributeUsage( AttributeTargets.Class, AllowMultiple = true, Inherited = false )]
public class CustomEmbeddedEditorAttribute : Attribute
{
	public Type TargetType { get; }
	public CustomEmbeddedEditorAttribute( Type targetType = null )
	{
		TargetType = targetType;
	}
}

[AttributeUsage( AttributeTargets.Class, AllowMultiple = true, Inherited = false )]
public class CustomEditorAttribute : Attribute
{
	public Type TargetType { get; }
	public Type[] WithAllAttributes { get; set; }
	public bool ForMethod { get; set; }

	/// <summary>Picks this editor for a property tagged <c>[Editor( "name" )]</c>.</summary>
	[Obsolete( "Give the property an attribute of its own and gate the editor on it with WithAllAttributes. Derive that attribute from EditorAttribute to keep the name - see AnimGraphBoneAttribute." )]
	public string NamedEditor { get; set; }

	public bool ForInterface { get; set; }

	public CustomEditorAttribute( Type targetType = null )
	{
		TargetType = targetType;
	}

	public float GetEditorScore( SerializedProperty property )
	{
		//This is a method, and it doesn't apply to it
		if ( property.IsMethod != ForMethod )
			return 0;

		float score = 0;

		if ( ForMethod )
			score += 100;

		if ( property.PropertyType.IsInterface && ForInterface )
			score += 100;

		var t = property.PropertyType;
		if ( t is null ) return score;

		if ( Either.IsEitherType( t ) )
		{
			t = typeof( Either );
		}

		if ( Nullable.GetUnderlyingType( t ) is { } elemType )
		{
			t = elemType;
		}

		//This attribute has a type set, look at that
		if ( TargetType is not null && !ForMethod )
		{
			//Order by derived classes so we get the most relevant editor
			Type baseType = TargetType;
			while ( baseType.BaseType != null )
			{
				score += 10.0f;
				baseType = baseType.BaseType;
			}

			if ( TargetType == t ) score += 1000.0f; //directly targets this class
			else if ( t.IsGenericType && TargetType == t.GetGenericTypeDefinition() ) score += 500.0f; //generic type target
			else if ( t.IsAssignableTo( TargetType ) ) score += 100.0f;  //directly targets a base type
			else return -100; //definitely not this!
		}

		//Properties can have [Editor( "NamedEditor" )]
#pragma warning disable CS0618 //scoring the obsolete path is what keeps it working
		if ( NamedEditor is not null )
		{
			if ( property.TryGetAttribute<EditorAttribute>( out var editorAttribute ) && string.Equals( editorAttribute.Value, NamedEditor ) )
			{
				score += 1000;
			}
			else
			{
				score -= 1000;
			}
		}
#pragma warning restore CS0618

		//Needs all of these attributes
		if ( WithAllAttributes is not null )
		{
			foreach ( var a in WithAllAttributes )
			{
				if ( !property.HasAttribute( a ) && !TaggedWith( property, EditorNameOf( a ) ) )
					return -100;

				score += 1000;
			}


		}

		return score;
	}

	private static readonly Dictionary<Type, string> editorNames = new();

	//The editor name an attribute carries, where it is one of ours by deriving from EditorAttribute.
	private static string EditorNameOf( Type attributeType )
	{
		if ( attributeType is null || !typeof( EditorAttribute ).IsAssignableFrom( attributeType ) )
			return null;

		lock ( editorNames )
		{
			if ( editorNames.TryGetValue( attributeType, out var name ) )
				return name;

			try
			{
				name = (Activator.CreateInstance( attributeType ) as EditorAttribute)?.Value;
			}
			catch ( Exception )
			{
				name = null;
			}

			editorNames[attributeType] = name;
			return name;
		}
	}

	//Whether a property still names this editor the old way, with [Editor( "name" )] .
	private static bool TaggedWith( SerializedProperty property, string name )
	{
		return name is not null
			&& property.TryGetAttribute<EditorAttribute>( out var tag )
			&& string.Equals( tag.Value, name );
	}
}
