namespace Editor.Inspectors;

using Rinten.Resources;
using Rinten.UI;

/// <summary>Where a resource comes from - a file, a copy embedded in whatever uses it, or a generator - and the editor for whichever of those it is.</summary>
[StyleSheet( "ResourceWrapperControl.cs.scss" )]
[CustomEditor( typeof( Resource ) )]
[CustomEditor( typeof( Resource ), WithAllAttributes = [typeof( InlineEditorAttribute )] )]
public sealed class ResourceWrapperControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	//The option for a resource that lives in a file, which is what most of them do.
	const string FromDisk = null;

	Panel _body;
	ToolbarButton _button;
	string _option;

	Resource Resource => Property?.GetValue<Resource>( null );

	public ResourceWrapperControl()
	{
		ShowBackground = false;

	}

	public override void Rebuild()
	{
		DeleteChildren();

		_body = null;
		_button = null;

		if ( Property is null ) return;

		_body = AddChild<Panel>( "body" );

		//Nothing to choose between, so nothing to choose with.
		if ( HasSourceOptions() )
		{
			_button = AddChild<ToolbarButton>();
			_button.ToolTip = "Source Type";
			_button.Disabled = IsReadOnly;
			_button.OnPressed = OpenMenu;
		}

		_option = StoredOption();

		BuildBody();
	}

	protected override void OnReadOnlyChanged()
	{
		if ( _button.IsValid() )
		{
			_button.Disabled = IsReadOnly;
			_button.StateHasChanged();
		}

		foreach ( var control in _body?.ChildrenOfType<BaseControl>() ?? [] )
			control.ReadOnly = IsReadOnly;
	}

	//Where the value says it came from, which is where the editor starts.
	string StoredOption()
	{
		if ( Resource?.EmbeddedResource is not { } embedded ) return FromDisk;

		return embedded.ResourceCompiler == EmbeddedResources.Compiler
			? EmbeddedResources.Compiler
			: embedded.ResourceGenerator;
	}

	bool HasSourceOptions()
	{
		return EmbeddedResources.CanEmbed( Property.PropertyType )
			|| Generators().Any();
	}

	//Every generator that can make a resource of this property's type.
	IEnumerable<TypeDescription> Generators()
	{
		return EditorTypeLibrary
			.GetGenericTypes( typeof( ResourceGenerator<> ), [Property.PropertyType] )
			.Where( x => !x.TargetType.IsAbstract );
	}

	void OpenMenu()
	{
		if ( IsReadOnly ) return;

		var menu = new Menu();

		AddOption( menu, "From Disk", "folder", FromDisk );

		if ( EmbeddedResources.CanEmbed( Property.PropertyType ) )
			AddOption( menu, "Embedded", "document_scanner", EmbeddedResources.Compiler );

		foreach ( var generator in Generators().OrderBy( x => x.Order ).ThenBy( x => x.Name ) )
		{
			AddOption( menu, generator.Title, generator.Icon ?? "auto_awesome", generator.ClassName );
		}

		menu.OpenBelowLeft( _button, 2.0f );
	}

	void AddOption( Menu menu, string title, string icon, string option )
	{
		var item = menu.AddOption( title, icon, () => SwitchTo( option ) );

		item.Checkable = true;
		item.Checked = _option == option;
	}

	void SwitchTo( string option )
	{
		if ( option == EmbeddedResources.Compiler )
		{
			SwitchToEmbedded();
			return;
		}

		if ( option == FromDisk )
		{
			SwitchToDisk();
			return;
		}

		_option = option;
		BuildBody();
	}

	//A file is picked, not carried: whatever was embedded or generated goes, so the property
	//is empty and waiting for an asset rather than holding a resource with no file behind it.
	void SwitchToDisk()
	{
		_option = FromDisk;

		//Already a resource that lives in a file - there is nothing to throw away.
		if ( Resource?.EmbeddedResource is not null )
		{
			Property.Parent?.NoteStartEdit( Property );
			Property.SetValue<Resource>( null );
			Property.Parent?.NoteFinishEdit( Property );
		}

		BuildBody();
	}

	//Switching to embedded keeps what was on the file: the embedded resource starts as a copy of it rather than as an empty one.
	void SwitchToEmbedded()
	{
		var existing = Property.GetValue<GameResource>();

		_option = EmbeddedResources.Compiler;

		if ( existing is not null )
		{
			Property.Parent?.NoteStartEdit( Property );
			EmbeddedResources.CreateFromFile( Property, existing );
			Property.Parent?.NoteFinishEdit( Property );
		}

		BuildBody();
	}

	void BuildBody()
	{
		_body.DeleteChildren();

		var generator = Generators().FirstOrDefault( x => x.ClassName == _option );

		if ( generator?.Create<ResourceGenerator>() is { } instance )
		{
			var control = new ResourceGeneratorControl { Generator = instance };

			SetBody( control, DisplayInfo.For( instance ).Icon ?? "auto_awesome", multipleRows: true );
			return;
		}

		if ( _option == EmbeddedResources.Compiler )
		{
			SetBody( new EmbeddedResourceControl(), "document_scanner", multipleRows: false );
			return;
		}

		SetBody( new ResourceControl(), "folder", multipleRows: false );
	}

	void SetBody( BaseControl control, string icon, bool multipleRows )
	{
		control.ReadOnly = IsReadOnly;
		control.Property = Property;

		_body.AddChild( control );

		//A row that only holds a field stays one row high; a generator's settings are a sheet.
		MultipleRows = multipleRows;

		if ( !_button.IsValid() ) return;

		_button.Icon = icon;
		_button.StateHasChanged();
	}
}
