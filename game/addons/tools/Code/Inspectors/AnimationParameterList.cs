using Editor.Assets;
using Rinten.UI;

namespace Editor.Inspectors;

/// <summary>Every parameter an animation graph has, as rows that set and clear it.</summary>
[StyleSheet( "AnimationParameterList.cs.scss" )]
public class AnimationParameterList : Panel
{
	private readonly ControlSheet Sheet;
	private AnimGraph Graph;
	private IAccessor Accessor;

	public interface IAccessor
	{
		/// <summary>Optional property that contains the parameter list when inspecting, for example <see cref="SkinnedModelRenderer.Parameters"/>.</summary>
		SerializedProperty ParentProperty { get; }

		void Set( string name, bool value );
		void Set( string name, float value );
		void Set( string name, Vector3 value );
		void Set( string name, int value );
		void Set( string name, Rotation value );

		Rotation GetRotation( string name );
		Vector3 GetVector( string name );
		bool GetBool( string name );
		float GetFloat( string name );
		int GetInt( string name );

		void Clear();
		void Clear( string name );
		bool Contains( string name );
	}

	public void SetAccessor( IAccessor accessor )
	{
		Accessor = accessor;

		Rebuild();
	}

	public void SetGraph( AnimGraph graph )
	{
		Graph = graph;
	}

	public AnimationParameterList()
	{
		AddClass( "animation-parameter-list" );

		var header = AddChild<Panel>( "header" );

		header.AddChild( new Rinten.UI.Button( "Clear", () =>
		{
			Accessor?.Clear();
			Rebuild();
		} ) );

		Sheet = AddChild<ControlSheet>();
	}

	//Builds a row per parameter of the graph, for whatever is holding them.
	private void Rebuild()
	{
		if ( Graph is null || Accessor is null )
		{
			Sheet.ClearBody();
			return;
		}

		Sheet.Target = new GraphParamsObject( Graph, Accessor );
		Sheet.Rebuild();
	}
}

#region Serialized Object

//SerializedObject with properties for each parameter of an AnimGraph.
file class GraphParamsObject : SerializedObject
{
	public AnimGraph Graph { get; }
	public AnimationParameterList.IAccessor Accessor { get; }

	public override IEnumerable<object> Targets => [Accessor];

	public GraphParamsObject( AnimGraph graph, AnimationParameterList.IAccessor accessor )
	{
		Graph = graph;
		Accessor = accessor;
		ParentProperty = accessor.ParentProperty;

		UpdatePropertyList();
	}

	private void UpdatePropertyList()
	{
		PropertyList = new List<SerializedProperty>();

		//Only the ones the graph offers - a parameter a node asked for and nobody declared is the
		//graph's own business, not something to put a row in front of whoever placed the model.
		foreach ( var parameter in Graph.Parameters.OrderBy( x => x.Name ) )
		{
			if ( ParamProperty.Create( this, parameter ) is { } property )
			{
				PropertyList.Add( property );
			}
		}
	}
}

//Base class for animation parameter properties contained in a GraphParamsObject.
file abstract class ParamProperty( GraphParamsObject parent, string name, Type type ) : SerializedProperty
{
	/// <summary>A row for one parameter, or nothing for a kind there is no sensible control for - a pose, a bone mask, or a place in the world the game gives the graph while it runs.</summary>
	public static ParamProperty Create( GraphParamsObject parent, AnimGraph.GraphParameter param )
	{
		return param.Kind switch
		{
			AnimGraphBuilder.NodeKind.Float => new FloatParamProperty( parent, param ),
			AnimGraphBuilder.NodeKind.Bool => new BoolParamProperty( parent, param ),
			AnimGraphBuilder.NodeKind.Vector => new Vector3ParamProperty( parent, param ),
			AnimGraphBuilder.NodeKind.Id => param.Options is { Count: > 0 }
				? new EnumParamProperty( parent, param )
				: null,
			_ => null,
		};
	}

	private GraphParamsObject _parent = parent;

	public override SerializedObject Parent => _parent;
	public override Type PropertyType { get; } = type.IsValueType ? typeof( Nullable<> ).MakeGenericType( type ) : type;
	public override string DisplayName => Name;
	public override string Name { get; } = name;
	public override string Description => Name;

	protected AnimationParameterList.IAccessor Accessor => _parent.Accessor;

	public bool Enabled
	{
		get => Accessor.Contains( Name );
	}
}

//Typed ParamProperty that implements getting / setting.
file abstract class ParamProperty<TValue> : ParamProperty
{
	private IReadOnlyList<Attribute> _attributes;

	protected AnimGraph.GraphParameter Parameter { get; }

	public abstract TValue Value { get; set; }

	protected ParamProperty( GraphParamsObject parent, AnimGraph.GraphParameter param )
		: base( parent, param.Name, typeof( TValue ) )
	{
		Parameter = param;
	}

	protected void SetAttributes( params Attribute[] attributes )
	{
		_attributes = attributes;
	}

	public override IEnumerable<Attribute> GetAttributes() => _attributes ?? Enumerable.Empty<Attribute>();

	public override T GetValue<T>( T defaultValue = default )
	{
		return ValueToType( Enabled ? Value : null, defaultValue );
	}

	public override void SetValue<T>( T value )
	{
		if ( value is null )
		{
			SetValue( GetDefault() );
			Accessor.Clear( Name );

			return;
		}

		Value = ValueToType( value, Value );
	}

	public override bool TryGetAsObject( out SerializedObject obj )
	{
		var typeDesc = EditorTypeLibrary.GetType<TValue>();
		obj = EditorTypeLibrary.GetSerializedObject( () => Value, typeDesc, this );
		return true;
	}
}

file class FloatParamProperty : ParamProperty<float>
{
	public Vector2 Range { get; private init; }

	public FloatParamProperty( GraphParamsObject parent, AnimGraph.GraphParameter param )
		: base( parent, param )
	{
		Range = new Vector2( Parameter.Min, Parameter.Max );

		//A graph that says nothing about a parameter's bounds leaves both at nought, and a slider from nought
		//to nought is a control that cannot be moved. Left as a plain number field, which is what it is.
		if ( Range.y > Range.x )
		{
			SetAttributes( new RangeAttribute( Range.x, Range.y ), new DefaultValueAttribute( Parameter.Default ) );
			return;
		}

		SetAttributes( new DefaultValueAttribute( Parameter.Default ) );
	}

	public override float Value
	{
		get => Accessor.GetFloat( Name );
		set => Accessor.Set( Name, value );
	}
}

file class Vector3ParamProperty : ParamProperty<Vector3>
{
	public override Vector3 Value
	{
		get => Accessor.GetVector( Name );
		set => Accessor.Set( Name, value );
	}

	public Vector3ParamProperty( GraphParamsObject parent, AnimGraph.GraphParameter param )
		: base( parent, param )
	{
		SetAttributes( new DefaultValueAttribute( Vector3.Zero ) );
	}
}

file class BoolParamProperty : ParamProperty<bool>
{
	public override bool Value
	{
		get => Accessor.GetBool( Name );
		set => Accessor.Set( Name, value );
	}

	public BoolParamProperty( GraphParamsObject parent, AnimGraph.GraphParameter param )
		: base( parent, param )
	{
		SetAttributes( new DefaultValueAttribute( Parameter.Default != 0.0f ) );
	}
}

file sealed class EnumParamAttribute : Attribute;

file class EnumParamProperty : ParamProperty<byte>
{
	public string[] Options { get; private init; }

	public override byte Value
	{
		get => (byte)Accessor.GetInt( Name );
		set => Accessor.Set( Name, (int)value );
	}

	public EnumParamProperty( GraphParamsObject parent, AnimGraph.GraphParameter param )
		: base( parent, param )
	{
		Options = Parameter.Options.ToArray();

		//An id is a name to the graph and a number to whoever sets it: the number is where
		//that name sits in this list, which is also what the graph's own default says.
		var fallback = (byte)Math.Clamp( (int)Parameter.Default, 0, Math.Max( 0, Options.Length - 1 ) );

		SetAttributes( new EnumParamAttribute(), new DefaultValueAttribute( fallback ) );
	}
}

#endregion

//One of an animation parameter's named options, picked from a list.
[CustomEditor( typeof( byte ), WithAllAttributes = [typeof( EnumParamAttribute )] )]
file class EnumParamControl : BaseControl
{
	private readonly ControlDropDown _dropdown;

	public override bool SupportsMultiEdit => false;

	private string[] Options => (Property as EnumParamProperty)?.Options ?? [];

	public EnumParamControl()
	{
		ShowBackground = false;

		_dropdown = AddChild<ControlDropDown>();
		_dropdown.BuildOptions = BuildOptions;
		_dropdown.ValueChanged = OnPicked;
	}

	public override void Rebuild()
	{
		_dropdown.Options = BuildOptions();

		SyncDisplay();
		ApplyDisabled();
	}

	public override void Tick()
	{
		base.Tick();

		SyncDisplay();
	}

	protected override void OnReadOnlyChanged() => ApplyDisabled();

	//The options are named, but stored as the index of the one that was picked - so that index is what the dropdown carries as its value.
	private List<Rinten.UI.Option> BuildOptions()
	{
		var names = Options;
		var options = new List<Rinten.UI.Option>();

		for ( var i = 0; i < names.Length; i++ )
		{
			options.Add( new Rinten.UI.Option( names[i] ?? "Unset", $"{i}" ) );
		}

		return options;
	}

	private void OnPicked( string value )
	{
		if ( IsReadOnly || Property is null ) return;
		if ( !byte.TryParse( value, out var index ) ) return;
		if ( Property.GetValue<byte>() == index ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue( index );
		Property.Parent?.NoteFinishEdit( Property );
	}

	private void SyncDisplay()
	{
		if ( Property is null ) return;

		var value = $"{Property.GetValue<byte>()}";

		if ( !string.Equals( _dropdown.Value as string, value, StringComparison.Ordinal ) )
			_dropdown.Value = value;
	}

	private void ApplyDisabled()
	{
		_dropdown.Disabled = IsReadOnly;
		_dropdown.AcceptsFocus = !IsReadOnly;
	}
}
