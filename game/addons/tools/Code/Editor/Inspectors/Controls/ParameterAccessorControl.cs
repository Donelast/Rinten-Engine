namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>The animation graph parameters a renderer is driving.</summary>
[StyleSheet( "ParameterAccessorControl.cs.scss" )]
[CustomEditor( typeof( SkinnedModelRenderer.ParameterAccessor ) )]
public sealed class ParameterAccessorControl : BaseControl
{
	/// <summary>The list names every parameter, so the property's own name adds nothing - unless there is no list, when the name is all there is to go on.</summary>
	public override bool IncludeLabel => Accessor?.Graph is null;

	AnimGraph _graph;

	public ParameterAccessorControl()
	{
		ShowBackground = false;
		MultipleRows = true;

	}

	SkinnedModelRenderer.ParameterAccessor Accessor => Property?.GetValue<SkinnedModelRenderer.ParameterAccessor>();

	public override void Rebuild()
	{
		DeleteChildren();

		_graph = null;

		if ( Property is null ) return;

		_graph = Accessor?.Graph;

		//No renderer behind it, or a model with no animation graph - say so rather than leaving the row blank.
		if ( Accessor is not { } accessor || _graph is null )
		{
			var note = AddChild<Rinten.UI.Label>( "note" );

			note.Tokenize = false;
			note.Text = "None";

			return;
		}

		var list = AddChild<AnimationParameterList>();

		list.SetGraph( _graph );
		list.SetAccessor( new ParameterAccessorBridge( Property, accessor ) );
	}

	/// <summary>Which parameters there are comes from the graph, and the graph comes from the model - picked in another row of the same inspector.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( Property is null ) return;

		if ( Accessor?.Graph != _graph )
			Rebuild();
	}

	protected override void OnReadOnlyChanged()
	{
	}
}

//Hands the parameter list what it needs from a renderer's accessor.
sealed class ParameterAccessorBridge : AnimationParameterList.IAccessor
{
	private readonly SkinnedModelRenderer.ParameterAccessor _accessor;

	public SerializedProperty ParentProperty { get; }

	public ParameterAccessorBridge( SerializedProperty parentProperty, SkinnedModelRenderer.ParameterAccessor accessor )
	{
		ParentProperty = parentProperty;

		_accessor = accessor;
	}

	public bool GetBool( string name ) => _accessor.GetBool( name );
	public float GetFloat( string name ) => _accessor.GetFloat( name );
	public int GetInt( string name ) => _accessor.GetInt( name );
	public Rotation GetRotation( string name ) => _accessor.GetRotation( name );
	public Vector3 GetVector( string name ) => _accessor.GetVector( name );

	public void Set( string name, bool value ) => _accessor.Set( name, value );
	public void Set( string name, float value ) => _accessor.Set( name, value );
	public void Set( string name, Vector3 value ) => _accessor.Set( name, value );
	public void Set( string name, int value ) => _accessor.Set( name, value );
	public void Set( string name, Rotation value ) => _accessor.Set( name, value );

	public void Clear() => _accessor.Clear();
	public void Clear( string name ) => _accessor.Clear( name );
	public bool Contains( string name ) => _accessor.Contains( name );
}
