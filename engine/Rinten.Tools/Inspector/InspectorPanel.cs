using System;
using Rinten.UI;

namespace Editor;

/// <summary>Inspector content as a Panel tree (razor + SCSS), not a Widget subtree.</summary>
[Expose]
public abstract class InspectorPanel : Panel
{
	public SerializedObject SerializedObject { get; init; }

	protected InspectorPanel()
	{
		AddClass( "inspector-panel" );
	}

	protected InspectorPanel( SerializedObject so )
	{
		ArgumentNullException.ThrowIfNull( so, nameof( so ) );
		SerializedObject = so;
		AddClass( "inspector-panel" );
	}

	public bool CloseInspector( object newObj = null ) => OnInspectorClose( newObj );

	protected virtual bool OnInspectorClose( object newObj = null ) => true;

	public static InspectorPanel Create( SerializedObject obj, Type ignore = null )
	{
		var t = obj.Targets.FirstOrDefault()?.GetType();

		while ( t is not null )
		{
			var ed = EditorTypeLibrary.GetTypesWithAttribute<InspectorAttribute>( false )
				.Where( x => x.Type.TargetType.IsAssignableTo( typeof( InspectorPanel ) ) )
				.Where( x => x.Type.TargetType != ignore )
				.Where( x => x.Attribute.Type.Name == t.Name )
				.FirstOrDefault();

			if ( ed.Type is not null )
				return ed.Type.Create<InspectorPanel>( [obj] );

			t = t.BaseType;
		}

		var editor = EditorTypeLibrary.GetTypesWithAttribute<InspectorAttribute>( false )
			.Where( x => x.Type.TargetType.IsAssignableTo( typeof( InspectorPanel ) ) )
			.Where( x => x.Type.TargetType != ignore )
			.Where( x => x.Attribute.Type.Name == obj.TypeName )
			.FirstOrDefault();

		if ( editor.Type is null ) return null;
		return editor.Type.Create<InspectorPanel>( [obj] );
	}
}
