using Editor.Inspectors;
using Editor.ProjectSettingsUI;

namespace Editor.ProjectSettingPages.Categories;

internal sealed class SystemSerializedObject : SerializedObject
{
	readonly TypeDescription systemType;
	readonly Scene scene;
	readonly Dictionary<(TypeDescription, string), object> pendingChanges;

	public SystemSerializedObject( TypeDescription systemType, Scene scene = null, Dictionary<(TypeDescription, string), object> pendingChanges = null )
	{
		this.systemType = systemType;
		this.scene = scene;
		this.pendingChanges = pendingChanges;
	}

	public TypeDescription SystemType => systemType;
	public Scene Scene => scene;

	public override IEnumerable<object> Targets
	{
		get { yield return this; }
	}

	public object GetValue( string propertyName )
	{
		var prop = systemType.Properties.FirstOrDefault( p => p.Name == propertyName );
		if ( prop == null ) return null;

		object value;
		if ( scene.IsValid() )
		{
			var key = (systemType, propertyName);
			if ( pendingChanges.TryGetValue( key, out value ) )
				return value;

			var system = EditorUtility.GetGameObjectSystem( scene, systemType );
			value = system != null ? prop.GetValue( system ) : null;
		}
		else
		{
			value = ProjectSettings.Systems.GetPropertyValue( systemType, prop );
		}

		if ( value == null && prop.PropertyType.IsValueType )
			value = Activator.CreateInstance( prop.PropertyType );

		return value;
	}

	public void SetValue( string propertyName, object value )
	{
		var prop = systemType.Properties.FirstOrDefault( p => p.Name == propertyName );
		if ( prop == null ) return;

		if ( scene.IsValid() )
			pendingChanges[(systemType, propertyName)] = value;
		else
			ProjectSettings.Systems.SetPropertyValue( systemType, prop, value );
	}
}

internal sealed class SystemPropertyWrapper : SerializedProperty
{
	readonly SystemSerializedObject parent;
	readonly PropertyDescription property;
	readonly DisplayInfo displayInfo;

	public SystemPropertyWrapper( SystemSerializedObject parent, PropertyDescription property )
	{
		this.parent = parent;
		this.property = property;
		displayInfo = property.GetDisplayInfo();
	}

	public override SerializedObject Parent => parent;
	public override string Name => property.Name;
	public override string DisplayName => displayInfo.Name;
	public override string Description => displayInfo.Description;
	public override string GroupName => displayInfo.Group;
	public override int Order => displayInfo.Order;
	public override Type PropertyType => property.PropertyType;
	public override string SourceFile => property.SourceFile;
	public override int SourceLine => property.SourceLine;

	public override object GetDefault()
	{
		if ( !parent.Scene.IsValid() )
			return base.GetDefault();

		if ( ProjectSettings.Systems.TryGetPropertyValue( parent.SystemType, property, out var value ) )
			return value;

		return base.GetDefault();
	}

	public override bool TryGetAsObject( out SerializedObject obj )
	{
		var value = parent.GetValue( property.Name );

		if ( value == null )
		{
			obj = null;
			return false;
		}

		var serializedValue = value.GetSerialized();
		serializedValue.ParentProperty = this;

		serializedValue.OnPropertyChanged += ( prop ) =>
		{
			var modifiedValue = serializedValue.Targets.FirstOrDefault();
			if ( modifiedValue != null )
			{
				parent.SetValue( property.Name, modifiedValue );
				NoteChanged();
			}
		};

		obj = serializedValue;
		return true;
	}

	public override T GetValue<T>( T defaultValue = default ) => ValueToType<T>( parent.GetValue( property.Name ) );

	public override void SetValue<T>( T value )
	{
		NotePreChange();
		parent.SetValue( property.Name, value );
		NoteChanged();
	}

	public override IEnumerable<Attribute> GetAttributes() => property.Attributes;
}

[StyleSheet( "SystemsPage.razor.scss" )]
sealed class ModeButtonPanel : Panel
{
	SystemsPage owner;
	bool sceneMode;

	public void Setup( SystemsPage owner, string title, string icon, bool sceneMode )
	{
		this.owner = owner;
		this.sceneMode = sceneMode;
		AddClass( "mode-btn" );
		SetClass( "active", owner.wantsEditScene == sceneMode );

		DeleteChildren();

		var iconLabel = AddChild<Rinten.UI.Label>( "icon" );
		iconLabel.AddClass( "material-icon" );
		iconLabel.Text = icon;

		var textLabel = AddChild<Rinten.UI.Label>( "text" );
		textLabel.Text = title;
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );
		owner?.SwitchMode( sceneMode );
	}
}
