namespace Editor.Inspectors;

/// <summary>Custom per-component inspector body (panel path).</summary>
public abstract class ComponentPanel : Panel
{
	public SerializedObject TargetObject { get; protected set; }

	/// <summary>Returns true when this panel fills the header ⋮ menu itself.</summary>
	public virtual bool FillHeaderContextMenu( Menu menu ) => false;

	public static ComponentPanel Create( SerializedObject obj )
	{
		if ( obj is null ) return null;

		var componentType = EditorTypeLibrary.GetType( obj.TypeName )?.TargetType;
		if ( componentType is null ) return null;

		var editors = EditorTypeLibrary.GetTypesWithAttribute<CustomEditorAttribute>( false )
			.Where( x => x.Type.TargetType.IsAssignableTo( typeof( ComponentPanel ) ) )
			.Select( x => ( score: GetEditorScore( x.Attribute.TargetType, componentType ), type: x.Type ) )
			.Where( x => x.score > 0 )
			.OrderByDescending( x => x.score );

		foreach ( var entry in editors )
		{
			var panel = entry.type.Create<ComponentPanel>( new object[] { obj } );
			if ( panel is not null )
				return panel;
		}

		return null;
	}

	static int GetEditorScore( Type targetType, Type componentType )
	{
		int score = 0;
		var baseType = componentType;
		while ( targetType.IsAssignableFrom( baseType ) )
		{
			score++;
			baseType = baseType.BaseType;
			if ( baseType is null ) break;
		}
		return score;
	}
}
