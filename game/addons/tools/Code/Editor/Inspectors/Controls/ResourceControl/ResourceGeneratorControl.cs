namespace Editor.Inspectors;

using Rinten.Resources;
using Rinten.UI;

/// <summary>A resource that is made rather than loaded: the generator's settings, with the resource rebuilt whenever one of them changes.</summary>
[StyleSheet( "ResourceGeneratorControl.cs.scss" )]
public sealed class ResourceGeneratorControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	/// <summary>The generator whose settings this edits. Set before <see cref="BaseControl.Property"/>.</summary>
	public ResourceGenerator Generator { get; set; }

	ControlSheet _sheet;
	SerializedObject _settings;
	bool _dirty;
	bool _generating;

	public ResourceGeneratorControl()
	{
		ShowBackground = false;
		MultipleRows = true;

	}

	public override void Rebuild()
	{
		DeleteChildren();

		_sheet = null;
		_settings = null;

		if ( Property is null || Generator is null ) return;

		LoadFromResource();

		_settings = Generator.GetSerialized();
		_settings.OnPropertyChanged = _ => _dirty = true;

		_sheet = AddChild<ControlSheet>();
		_sheet.Target = _settings;

		_ = Regenerate();
	}

	public override void Tick()
	{
		base.Tick();

		if ( !_dirty || _generating ) return;

		_dirty = false;
		_ = Regenerate();
	}

	//Settings that were stored alongside the resource, so it opens the way it was left rather than at the generator's defaults.
	void LoadFromResource()
	{
		if ( Property.GetValue<Resource>()?.EmbeddedResource is not { } embedded ) return;

		//Stored by some other generator - none of its settings are ours to read.
		if ( embedded.ResourceGenerator != EditorTypeLibrary.GetType( Generator.GetType() )?.ClassName ) return;

		Generator.Deserialize( embedded.Data );
	}

	async Task Regenerate()
	{
		if ( Generator is null ) return;

		_generating = true;

		try
		{
			var resource = await Generator.FindOrCreateObjectAsync( ResourceGenerator.Options.Default, default );

			//The inspector may have moved on to something else while that ran.
			if ( !this.IsValid() || Property is null ) return;

			Property.SetValue( resource );
		}
		finally
		{
			_generating = false;
		}
	}
}
