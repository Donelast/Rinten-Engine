namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>A clothing icon's setup, shown beside the scene it will be rendered from.</summary>
[StyleSheet( "ClothingIconControl.cs.scss" )]
[CustomEditor( typeof( Clothing.IconSetup ) )]
public sealed class ClothingIconControl : BaseControl
{
	readonly ClothingIconPreview _preview;
	readonly ControlSheet _sheet;
	readonly Rinten.UI.Button _save;

	int _dressed;

	public ClothingIconControl()
	{
		AddClass( "clothing-icon" );

		MultipleRows = true;
		ShowBackground = false;

		_preview = AddChild<ClothingIconPreview>( "preview" );

		var side = AddChild<Panel>( "side" );

		_sheet = side.AddChild<ControlSheet>();

		var footer = side.AddChild<Panel>( "footer" );

		_save = footer.AddChild( new Rinten.UI.Button( "Save Icon To Disk", "save", SaveIcon ) );
		_save.AddClass( "save" );
	}

	public override void Rebuild()
	{
		if ( Property is null ) return;

		if ( Property.TryGetAsObject( out var so ) )
		{
			_sheet.Target = so;
			_sheet.Rebuild();
		}

		_dressed = 0;

		UpdatePreview();
	}

	public override void Tick()
	{
		base.Tick();

		UpdatePreview();
	}

	protected override void OnReadOnlyChanged()
	{
		if ( _save.IsValid() )
			_save.Disabled = IsReadOnly;
	}

	//The scene is dressed again only when the setup it was dressed from changes - installing clothing
	//loads a model and reapplies the whole container, which is not something to do every frame.
	void UpdatePreview()
	{
		var clothing = TargetClothing();
		if ( clothing is null ) return;

		var icon = clothing.Icon;
		var hash = HashCode.Combine( clothing.ResourcePath, icon.Mode, icon.PositionOffset, icon.Path );

		if ( hash == _dressed ) return;

		_dressed = hash;

		_preview.Show( clothing );
	}

	//The resource the setup belongs to.
	Clothing TargetClothing() => Property?.Parent?.Targets?.FirstOrDefault() as Clothing;

	void SaveIcon()
	{
		if ( IsReadOnly ) return;

		var clothing = TargetClothing();
		if ( clothing is null ) return;

		var asset = AssetSystem.FindByPath( clothing.ResourcePath );
		if ( asset is null ) return;

		ClothingIconRenderer.RenderIcon( asset, clothing );
	}
}
