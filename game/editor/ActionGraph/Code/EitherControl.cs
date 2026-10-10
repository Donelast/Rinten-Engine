using Rinten;
using Facepunch.ActionGraphs;
using System;
using System.Collections.Generic;
using System.Linq;
using Rinten.UI;
using Editor.Inspectors;

namespace Editor.ActionGraphs;

//Edits a value of one of several types; the type is inferred from the current value
[StyleSheet( "EitherControl.cs.scss" )]
[CustomEditor( typeof( Either ) )]
internal sealed class EitherControl : BaseControl
{
	TypeValueWrapper _wrapper;
	Panel _valueHost;

	public EitherControl()
	{
		ShowBackground = false;

	}

	public override void Rebuild()
	{
		DeleteChildren();

		_wrapper = null;
		_valueHost = null;

		if ( Property is null ) return;

		var types = Either.Unwrap( Property.PropertyType ).ToArray();

		if ( types.Length == 0 ) return;

		//A bare Either - the base the generic ones share - names no types, and unwrapping it hands back Either itself. A value editor over that is
		//this control again, one level deeper on every frame, until the stack runs out; the Panel Gallery's page of every editor found it that way.
		if ( !Property.PropertyType.IsConstructedGenericType || types.Contains( Property.PropertyType ) )
		{
			var note = AddChild<Rinten.UI.Label>( "value" );
			note.Text = "An Either with no types to choose between";
			return;
		}

		//An Either of an Either is how a node says "whatever the other input is, or a number" - the first of the pair is the one worth defaulting to.
		var preferred =
			Either.IsEitherType( Property.PropertyType ) &&
			Either.IsEitherType( Property.PropertyType.GetGenericArguments()[1] )
				? types[0]
				: null;

		_wrapper = new TypeValueWrapper( Property, types, preferred );

		//The value first, so the type sits on the right where the widget had it.
		_valueHost = AddChild<Panel>( "value" );

		var typeHost = AddChild<Panel>( "type" );
		var typeControl = PanelControlFactory.CreateFor( _wrapper.GetProperty( "Type" ) );

		if ( typeControl is not null )
			typeHost.AddChild( typeControl );

		BuildValue();

		_wrapper.OnPropertyChanged += changed =>
		{
			if ( changed == _wrapper.GetProperty( "Type" ) )
				BuildValue();
		};
	}

	void BuildValue()
	{
		if ( !_valueHost.IsValid() ) return;

		_valueHost.DeleteChildren();

		var control = PanelControlFactory.CreateFor( _wrapper.GetProperty( "Value" ) );

		if ( control is null ) return;

		_valueHost.AddChild( control );
	}

	/// <summary>The pair of properties the two halves edit, over the one that is really there.</summary>
	public class TypeValueWrapper : SerializedObject
	{
		private static Type[] PreferredTypes { get; } = { typeof( string ), typeof( float ), typeof( Vector3 ), typeof( int ) };

		public SerializedProperty Inner { get; }
		public Type ValueType { get; set; }

		public Type[] Options { get; }

		private readonly SerializedProperty _typeProperty;
		private readonly SerializedProperty _valueProperty;

		public TypeValueWrapper( SerializedProperty inner, Type[] options, Type preferred = null )
		{
			Inner = inner;
			Options = options;

			ValueType = inner.GetValue<object>()?.GetType() ?? preferred ?? PreferredTypes.FirstOrDefault( options.Contains ) ?? options.First();

			_typeProperty = new TypeProperty( this );
			_valueProperty = new ValueProperty( this );

			PropertyList = new List<SerializedProperty> { _typeProperty, _valueProperty };
		}

		public override SerializedProperty GetProperty( string v )
		{
			return v switch
			{
				"Type" => _typeProperty,
				"Value" => _valueProperty,
				_ => null
			};
		}

		private class TypeProperty : SerializedProperty, ITypeConstraintSource
		{
			public override string Name => "Type";
			public override string Description => "Value type for this property.";
			public override Type PropertyType => typeof( Type );

			public TypeValueWrapper Wrapper { get; }

			public override SerializedObject Parent => Wrapper;

			//The types this Either was declared with, and nothing else.
			TypeConstraint ITypeConstraintSource.TypeConstraint => new()
			{
				Options = Wrapper.Options,
				AllowOpenGeneric = false
			};

			public TypeProperty( TypeValueWrapper wrapper )
			{
				Wrapper = wrapper;
			}

			public override void SetValue<T>( T value )
			{
				if ( value is null )
				{
					Wrapper.ValueType = typeof( object );
					Wrapper.Inner.SetValue<object>( null );

					NoteChanged();
				}
				else if ( value is Type type )
				{
					var oldValue = Wrapper.Inner.GetValue<object>();
					var newValue = type.IsInstanceOfType( oldValue )
						? oldValue
						: type.IsValueType
							? Activator.CreateInstance( type )
							: null;

					Wrapper.ValueType = type;
					Wrapper.Inner.SetValue( newValue );

					NoteChanged();
				}
			}

			public override T GetValue<T>( T defaultValue = default( T ) )
			{
				return Wrapper.ValueType is T value ? value : defaultValue;
			}
		}

		private class ValueProperty : SerializedProperty
		{
			public override string Name => Wrapper.Inner.Name;
			public override string Description => Wrapper.Inner.Description;
			public override Type PropertyType => Wrapper.ValueType;

			public TypeValueWrapper Wrapper { get; }

			public ValueProperty( TypeValueWrapper wrapper )
			{
				Wrapper = wrapper;
			}

			public override void SetValue<T>( T value )
			{
				Wrapper.Inner.SetValue( value );
				NoteChanged();
			}

			public override T GetValue<T>( T defaultValue = default( T ) )
			{
				return Wrapper.Inner.GetValue( defaultValue );
			}

			public override bool TryGetAsObject( out SerializedObject obj )
			{
				if ( Wrapper.Inner.TryGetAsObject( out obj ) )
				{
					return true;
				}

				if ( PropertyType.IsValueType )
				{
					obj = EditorTypeLibrary.GetSerializedObject( Activator.CreateInstance( PropertyType ) );
					obj.ParentProperty = this;
					return true;
				}

				return false;
			}
		}
	}
}
