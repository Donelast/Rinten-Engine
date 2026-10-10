namespace Editor;

[CustomEditor( typeof( Vector2 ) )]
[CustomEditor( typeof( Vector3 ) )]
[CustomEditor( typeof( Vector4 ) )]
public class VectorControlWidget : ControlWidget
{
	SerializedObject obj;

	SerializedProperty Property;

	FloatControlWidget FirstControl;

	readonly bool canToggleUniform;
	bool uniform;

	private string unit;

	/// <summary>What these numbers are in - <c>m</c> for a position - shown in grey after each of them. Null where they are not in anything: a rotation is degrees and a scale is a ratio.</summary>
	public string Unit
	{
		get => unit;
		set
		{
			if ( unit == value ) return;

			unit = value;

			if ( obj is not null ) BuildFields();
		}
	}

	public override bool SupportsMultiEdit => true;

	public VectorControlWidget( SerializedProperty property ) : base( property )
	{
		PaintBackground = false;
		Property = property;

		property.TryGetAsObject( out obj );

		if ( obj is null )
		{
			Log.Warning( $"Error when trying to get {property} as object" );
			return;
		}

		var dimensions = obj.GetProperty( "w" ) is not null ? 4
			: obj.GetProperty( "z" ) is not null ? 3
			: 2;
		MinimumWidth = dimensions * 72 + (dimensions - 1) * 2;

		canToggleUniform = property.TryGetAttribute<UniformAttribute>( out var uniformAttr );
		uniform = canToggleUniform && EditorCookie.Get( UniformCookieKey, uniformAttr.Default );

		Layout = Layout.Row();
		Layout.Spacing = 2;

		BuildFields();
	}

	string UniformCookieKey => $"vector.uniform.{Property.Parent?.TypeName}.{Property.Name}";

	void BuildFields()
	{
		Layout.Clear( true );

		if ( uniform )
		{
			FirstControl = TryAddField( new UniformVectorProperty( obj ), Theme.Blue, null );
			FirstControl.Icon = "zoom_out_map";
			return;
		}

		FirstControl = TryAddField( obj.GetProperty( "x" ), Theme.Red, "X" );
		TryAddField( obj.GetProperty( "y" ), Theme.Green, "Y" );
		TryAddField( obj.GetProperty( "z" ), Theme.Blue, "Z" );
		TryAddField( obj.GetProperty( "w" ), Theme.Yellow, "W" );
	}

	private FloatControlWidget TryAddField( SerializedProperty prop, Color color, string text )
	{
		if ( prop is null ) return null;

		var control = Layout.Add( new FloatControlWidget( prop ) { HighlightColor = color, Label = text, Unit = unit } );

		control.MinimumWidth = 72;
		control.HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
		control.MakeRanged( Property );

		return control;
	}

	public override void OnLabelContextMenu( ContextMenu menu )
	{
		if ( !canToggleUniform ) return;

		var o = menu.AddOption( "Uniform", "zoom_out_map", () =>
		{
			uniform = !uniform;
			EditorCookie.Set( UniformCookieKey, uniform );
			BuildFields();
		} );

		o.Checkable = true;
		o.Checked = uniform;

		menu.AddSeparator();
	}

	public override void StartEditing()
	{
		FirstControl?.StartEditing();
	}

	protected override void OnPaint()
	{
		//nothing
	}
}

internal sealed class UniformVectorProperty( SerializedObject components ) : SerializedProperty.Proxy
{
	protected override SerializedProperty ProxyTarget => components.GetProperty( "x" );

	public override string DisplayName => components.ParentProperty?.DisplayName ?? ProxyTarget?.DisplayName;
	public override string Description => components.ParentProperty?.Description ?? ProxyTarget?.Description;

	public override bool IsMultipleValues => ProxyTarget?.IsMultipleValues ?? false;
	public override bool IsMultipleDifferentValues => ProxyTarget?.IsMultipleDifferentValues ?? false;

	public override void SetValue<T>( T value )
	{
		components.GetProperty( "x" )?.SetValue( value );
		components.GetProperty( "y" )?.SetValue( value );
		components.GetProperty( "z" )?.SetValue( value );
		components.GetProperty( "w" )?.SetValue( value );
	}

	internal override void NoteStartEdit( SerializedProperty childProperty ) => ProxyTarget?.NoteStartEdit( childProperty );
	internal override void NoteFinishEdit( SerializedProperty childProperty ) => ProxyTarget?.NoteFinishEdit( childProperty );
}
