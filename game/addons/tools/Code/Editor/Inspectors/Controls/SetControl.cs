namespace Editor.Inspectors;

[StyleSheet( "SetControl.cs.scss" )]
[CustomEditor( typeof( HashSet<> ) )]
public class SetControl : BaseControl
{
	Panel body;
	Panel addRow;
	Panel addValueHost;
	ToolbarButton addButton;
	BaseControl addValueEditor;
	SerializedProperty pendingProperty;
	SerializedCollection collection;
	int buildHash;

	public override bool SupportsMultiEdit => false;

	public SetControl()
	{
		ShowBackground = false;
		MultipleRows = true;

		body = AddChild<Panel>( "body" );

		addRow = AddChild<Panel>( "add-row" );

		addValueHost = addRow.AddChild<Panel>( "add-value" );

		addButton = addRow.AddChild<ToolbarButton>();
		addButton.AddClass( "set-action" );
		addButton.Icon = "add";
		addButton.ToolTip = "Add entry";
		addButton.OnPressed = AddEntry;
	}

	protected override void OnReadOnlyChanged()
	{
		base.OnReadOnlyChanged();
		if ( addRow.IsValid() )
			addRow.SetClass( "hidden", IsReadOnly );
	}

	public override void Rebuild()
	{
		if ( Property is null ) return;

		if ( !Property.TryGetAsObject( out var so ) || so is not SerializedCollection sc )
			return;

		collection = sc;
		collection.OnEntryAdded = Rebuild;
		collection.OnEntryRemoved = Rebuild;

		EnsureAddEditor();

		var hash = HashCode.Combine( collection.ToList().Count );
		if ( hash == buildHash ) return;
		buildHash = hash;

		body.DeleteChildren();

		foreach ( var entry in collection )
			body.AddChild( new SetEntryPanel( this, entry ) );

		addRow.SetClass( "hidden", IsReadOnly );
	}

	void EnsureAddEditor()
	{
		if ( collection is null ) return;

		var prop = collection.NewKeyProperty();
		if ( prop is null ) return;

		if ( pendingProperty is not null && pendingProperty.PropertyType == prop.PropertyType && addValueEditor.IsValid() )
			return;

		pendingProperty = prop;
		addValueHost.DeleteChildren();

		addValueEditor = PanelControlFactory.CreateFor( pendingProperty );
		if ( addValueEditor is null ) return;

		addValueHost.AddChild( addValueEditor );
	}

	void AddEntry()
	{
		if ( collection is null || pendingProperty is null ) return;

		var value = pendingProperty.GetValue<object>() ?? pendingProperty.GetDefault();
		if ( value is null && collection.ValueType == typeof( string ) )
			value = string.Empty;

		if ( !collection.Add( value ) ) return;

		if ( collection.ValueType == typeof( string ) )
			pendingProperty.SetValue( string.Empty );
		else
			pendingProperty.SetValue( pendingProperty.GetDefault() );

		addValueEditor?.StateHasChanged();
	}

	void RemoveEntry( object value ) => collection?.RemoveAt( value );

	sealed class SetEntryPanel : Panel
	{
		public SetEntryPanel( SetControl parent, SerializedProperty entry )
		{
			AddClass( "set-entry" );

			var editor = PanelControlFactory.CreateFor( entry );
			if ( editor is not null )
			{
				AddChild( editor );
			}

			if ( parent.IsReadOnly ) return;

			var remove = AddChild<ToolbarButton>();
			remove.AddClass( "set-action" );
			remove.Icon = "remove";
			remove.ToolTip = "Remove entry";
			remove.OnPressed = () => parent.RemoveEntry( entry.GetValue<object>() );
		}
	}
}
