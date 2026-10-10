#pragma warning disable CS0618 //this control exists to edit AnyOfType<T>, which is obsolete

namespace Editor.Inspectors;

using Rinten.UI;

//Obsolete along with the thing it edits. It is here so the one property still using AnyOfType keeps an editor while it waits for a replacement, and it goes when that property does.
/// <summary>Picks the concrete type an <see cref="AnyOfType{T}"/> holds, and edits the instance under it.</summary>
[Obsolete( "Editor for AnyOfType<T>, which is obsolete. Remove both together." )]
[StyleSheet( "AnyOfTypeControl.cs.scss" )]
[CustomEditor( typeof( AnyOfType<> ) )]
public sealed class AnyOfTypeControl : BaseControl
{
	//The concrete types a base type has, worked out once. Filtering and sorting
	//the type library is not free, and the answer only changes when the code does.
	static readonly Dictionary<Type, TypeDescription[]> Concrete = new();

	readonly ControlDropDown _type;
	readonly ControlSheet _sheet;

	//The instance the sheet was built for, to notice when it is swapped out from elsewhere.
	object _shown;

	//The wrapper's Value , looked up once rather than per tick.
	System.Reflection.PropertyInfo _valueProperty;

	/// <summary>The wrapper holds one instance, so there is nothing to agree on across a selection.</summary>
	public override bool SupportsMultiEdit => false;

	public AnyOfTypeControl()
	{
		ShowBackground = false;
		MultipleRows = true;

		_type = AddChild<ControlDropDown>();
		_type.ValueChanged = OnPicked;

		//Anything abstract enough to be worth wrapping tends to have a long list of implementations behind it, and a long list has to be searchable.
		_type.Searchable = true;

		//BuildOptions is deliberately left unwired: the dropdown calls it back on every write of its value, and it walks the
		//type library. The list is set on Rebuild instead, and again only if it turns out not to hold what the property says.

		_sheet = AddChild<ControlSheet>();
	}

	public override void Rebuild()
	{
		_valueProperty = Property?.PropertyType?.GetProperty( nameof( AnyOfType<object>.Value ) );
		_type.Options = BuildOptions();

		var inner = Inner();

		SyncDisplay( inner );
		SyncSheet( inner, true );
		ApplyDisabled();
	}

	public override void Tick()
	{
		base.Tick();

		var inner = Inner();

		SyncDisplay( inner );
		SyncSheet( inner, false );
	}

	protected override void OnReadOnlyChanged() => ApplyDisabled();

	public override void OnPropertyContextMenu( Menu menu )
	{
		if ( Property is null ) return;

		menu.AddOption( "Clear", "backspace", () => Write( null ) )
			.Enabled = !IsReadOnly && Inner() is not null;
	}

	//Every concrete type the wrapper will take, plus what an empty one reads as.
	List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option> { new( "None", "cancel", "" ) };

		foreach ( var type in SupportedTypes() )
		{
			options.Add( new Rinten.UI.Option( type.Title, type.Icon ?? "category", Key( type.TargetType ) ) );
		}

		//A type that has gone away, or one that has since been made abstract, still has to read as what it is rather than as an empty wrapper.
		var current = Inner()?.GetType();

		if ( current is not null && !options.Any( x => string.Equals( x.Value as string, Key( current ), StringComparison.Ordinal ) ) )
		{
			options.Insert( 1, new Rinten.UI.Option( DisplayInfo.ForType( current ).Name, "category", Key( current ) ) );
		}

		return options;
	}

	//The concrete types on offer. The whole point of the wrapper is that the property's own type
	//is abstract, so what can go in it is whatever the type library has built on top of it.
	TypeDescription[] SupportedTypes()
	{
		var baseType = BaseType();

		if ( baseType is null ) return [];

		if ( Concrete.TryGetValue( baseType, out var cached ) ) return cached;

		var types = EditorTypeLibrary.GetTypes( baseType )
			.Where( x => !x.IsAbstract && x.TargetType.IsAssignableTo( baseType ) )
			.OrderBy( x => x.Title )
			.ToArray();

		Concrete[baseType] = types;

		return types;
	}

	[EditorEvent.Hotload]
	static void FlushCache() => Concrete.Clear();

	void OnPicked( string value )
	{
		if ( IsReadOnly || Property is null ) return;

		var picked = SupportedTypes().FirstOrDefault( x => Key( x.TargetType ) == value );

		if ( picked is null )
		{
			//The empty option, or the placeholder standing in for a type that has gone: neither is something to build, and only the first is a change.
			if ( string.IsNullOrEmpty( value ) && Inner() is not null ) Write( null );
			return;
		}

		if ( Inner()?.GetType() == picked.TargetType ) return;

		Write( picked.Create<object>() );
	}

	//Puts a new wrapper on the property. The wrapper is a readonly struct closed over a type only
	//known at runtime, so it has to be built through the type library rather than written directly.
	void Write( object instance )
	{
		var baseType = BaseType();

		if ( baseType is null ) return;

		var wrapper = EditorTypeLibrary.GetType( typeof( AnyOfType<> ) )
			?.CreateGeneric<object>( [baseType], instance is null ? null : [instance] );

		if ( wrapper is null ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue( wrapper );
		Property.Parent?.NoteFinishEdit( Property );

		var written = Inner();

		SyncDisplay( written );
		SyncSheet( written, true );
	}

	void SyncDisplay( object inner )
	{
		if ( Property is null ) return;

		var value = Key( inner?.GetType() );

		if ( string.Equals( _type.Value as string, value, StringComparison.Ordinal ) ) return;

		//A type written from somewhere else - undo, a script, another inspector - may not be in the list the control was built with.
		if ( !Listed( value ) )
			_type.Options = BuildOptions();

		_type.Value = value;
	}

	bool Listed( string value )
	{
		foreach ( var option in _type.Options )
		{
			if ( string.Equals( option.Value as string, value, StringComparison.Ordinal ) ) return true;
		}

		return false;
	}

	//Points the sheet at the wrapped instance; edits land there and report as this property's
	void SyncSheet( object inner, bool force )
	{
		if ( !force && ReferenceEquals( inner, _shown ) ) return;

		_shown = inner;

		if ( inner is null )
		{
			_sheet.ClearBody();
			return;
		}

		var serialized = inner.GetSerialized();
		serialized.ParentProperty = Property;

		_sheet.Target = serialized;
		_sheet.Rebuild();
	}

	void ApplyDisabled()
	{
		_type.Disabled = IsReadOnly;
		_type.AcceptsFocus = !IsReadOnly;
	}

	//What the wrapper is holding, reached by name because T is not known here.
	object Inner()
	{
		if ( _valueProperty is null ) return null;

		var wrapper = Property?.GetValue<object>();

		return wrapper is null ? null : _valueProperty.GetValue( wrapper );
	}

	//The type the wrapper is closed over, which is what may go in it.
	Type BaseType()
	{
		var type = Property?.PropertyType;

		return type is { IsGenericType: true } && type.GenericTypeArguments.Length == 1
			? type.GenericTypeArguments[0]
			: null;
	}

	static string Key( Type type ) => type?.FullName ?? "";
}
