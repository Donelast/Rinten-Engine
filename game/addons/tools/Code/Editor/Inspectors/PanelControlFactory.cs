namespace Editor.Inspectors;

using System.Reflection;
using System.Runtime.CompilerServices;

/// <summary>Creates panel property editors from game and tools type libraries.</summary>
public static class PanelControlFactory
{
	public static BaseControl CreateFor( SerializedProperty property, bool wrapNullable = true )
	{
		var control = CreateCore( property );
		if ( wrapNullable && property.IsNullable && control is not null && !control.IsFallbackEditor )
		{
			var wrapper = new NullableControl();
			wrapper.Property = property;
			return wrapper;
		}

		return control;
	}

	/// <summary>True if a specific control is registered for the property, not just a fallback</summary>
	public static bool HasEditorFor( SerializedProperty property )
	{
		if ( property is null ) return false;

		//Counted rather than kept: the list is scratch shared with the next caller.
		if ( ControlEditors.ForGame( property ).Count > 0 ) return true;

		return ControlEditors.ForEditor( property ).Count > 0;
	}

	static BaseControl CreateCore( SerializedProperty property )
	{
		var control = BaseControl.CreateFor( property );
		if ( control is not null && !control.IsFallbackEditor )
			return control;

		if ( control is not null )
			control.Delete( true );

		control = TryEditorTypeLibrary( property );
		if ( control is not null )
			return control;

		control = TryGenericControl( property );
		if ( control is not null )
			return control;

		return BaseControl.CreateFor( property );
	}

	static BaseControl TryGenericControl( SerializedProperty property )
	{
		if ( property.IsMethod )
			return null;

		var t = property.PropertyType;
		if ( t is null ) return null;
		if ( t.IsPrimitive || t == typeof( string ) || t.IsEnum ) return null;
		if ( t.IsValueType && t.GetCustomAttribute<IsReadOnlyAttribute>() is not null ) return null;
		if ( t.IsAssignableTo( typeof( Resource ) ) ) return null;

		var control = new GenericControl();
		control.Property = property;
		return control;
	}

	static BaseControl TryEditorTypeLibrary( SerializedProperty property )
	{
		var scored = ControlEditors.ForEditor( property );

		//Indexed rather than enumerated because the list is scratch shared with the next caller.
		for ( var i = 0; i < scored.Count; i++ )
		{
			var control = scored[i].Type.Create<BaseControl>();
			if ( control is null ) continue;

			if ( property.IsMultipleValues && !control.SupportsMultiEdit )
			{
				control.Delete( true );
				continue;
			}

			control.Property = property;
			return control;
		}

		return null;
	}
}
