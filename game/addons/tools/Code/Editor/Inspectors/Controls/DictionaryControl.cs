namespace Editor.Inspectors;

[StyleSheet( "DictionaryControl.cs.scss" )]
[CustomEditor( typeof( Dictionary<,> ) )]
[CustomEditor( typeof( NetDictionary<,> ) )]
public class DictionaryControl : BaseControl
{
	Panel body;
	Panel addRow;
	Panel addKeyHost;
	ToolbarButton addButton;
	BaseControl addKeyEditor;
	SerializedProperty pendingKey;
	SerializedCollection collection;
	int buildHash;

	public override bool SupportsMultiEdit => false;

	public DictionaryControl()
	{
		ShowBackground = false;
		MultipleRows = true;

		body = AddChild<Panel>( "body" );

		addRow = AddChild<Panel>( "add-row" );

		addKeyHost = addRow.AddChild<Panel>( "add-key" );

		addButton = addRow.AddChild<ToolbarButton>();
		addButton.AddClass( "dict-action" );
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
			body.AddChild( new DictionaryEntryPanel( this, entry ) );

		addRow.SetClass( "hidden", IsReadOnly );
	}

	void EnsureAddEditor()
	{
		if ( collection is null ) return;

		var prop = collection.NewKeyProperty();
		if ( prop is null ) return;

		if ( pendingKey is not null && pendingKey.PropertyType == prop.PropertyType && addKeyEditor.IsValid() )
			return;

		pendingKey = prop;
		addKeyHost.DeleteChildren();

		addKeyEditor = PanelControlFactory.CreateFor( pendingKey );
		if ( addKeyEditor is null ) return;

		addKeyHost.AddChild( addKeyEditor );
	}

	void AddEntry()
	{
		if ( collection is null || pendingKey is null ) return;

		var key = pendingKey.GetValue<object>() ?? pendingKey.GetDefault();
		if ( key is null && pendingKey.PropertyType == typeof( string ) )
			key = string.Empty;

		if ( key is null ) return;

		//One edit of the property, as ListControl does it - see ListControl.Edit.
		var added = false;

		Property?.Parent?.NoteStartEdit( Property );

		try
		{
			added = collection.Add( key, null );
		}
		finally
		{
			Property?.Parent?.NoteFinishEdit( Property );
		}

		if ( !added ) return;

		if ( pendingKey.PropertyType == typeof( string ) )
			pendingKey.SetValue( string.Empty );
		else
			pendingKey.SetValue( pendingKey.GetDefault() );

		addKeyEditor?.StateHasChanged();
	}

	void RemoveEntry( object key )
	{
		if ( collection is null ) return;

		Property?.Parent?.NoteStartEdit( Property );

		try
		{
			collection.RemoveAt( key );
		}
		finally
		{
			Property?.Parent?.NoteFinishEdit( Property );
		}
	}

	sealed class DictionaryEntryPanel : Panel
	{
		public DictionaryEntryPanel( DictionaryControl parent, SerializedProperty entry )
		{
			AddClass( "dict-entry" );

			var key = entry.GetKey();
			if ( key is not null )
			{
				var keyEditor = PanelControlFactory.CreateFor( key );
				if ( keyEditor is not null )
				{
					keyEditor.AddClass( "dict-key" );
					AddChild( keyEditor );
				}
			}

			var separator = AddChild<Rinten.UI.Label>( "separator" );
			separator.Text = ":";

			var valEditor = PanelControlFactory.CreateFor( entry );
			if ( valEditor is not null )
			{
				valEditor.AddClass( "dict-value" );
				AddChild( valEditor );
			}

			if ( parent.IsReadOnly ) return;

			var remove = AddChild<ToolbarButton>();
			remove.AddClass( "dict-action" );
			remove.Icon = "remove";
			remove.ToolTip = "Remove entry";
			remove.OnPressed = () => parent.RemoveEntry( key?.GetValue<object>() );
		}
	}
}
