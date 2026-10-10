namespace Editor.Inspectors;

using Rinten.Engine;
using Rinten.UI;

/// <summary>A row per material on the thing being rendered, each one overridable.</summary>
[StyleSheet( "MaterialAccessorControl.cs.scss" )]
[CustomEditor( typeof( MaterialAccessor ) )]
public sealed class MaterialAccessorControl : BaseControl
{
	/// <summary>The rows say which slot they are, so the property's own name adds nothing - unless there are no rows, when the name is all there is to go on.</summary>
	public override bool IncludeLabel => Count == 0;

	int _built = -1;

	public MaterialAccessorControl()
	{
		ShowBackground = false;
		MultipleRows = true;

	}

	int Count => Property?.GetValue<MaterialAccessor>()?.Count ?? 0;

	public override void Rebuild()
	{
		DeleteChildren();

		_built = -1;

		if ( Property is null ) return;

		_built = Count;

		//No renderer behind it, or a model with no materials - say so rather than leaving the row blank.
		if ( _built == 0 )
		{
			var note = AddChild<Rinten.UI.Label>( "note" );

			note.Tokenize = false;
			note.Text = "None";

			return;
		}

		for ( var i = 0; i < _built; i++ )
		{
			AddChild( new MaterialIndexPanel( Property, i, IsReadOnly ) );
		}
	}

	/// <summary>How many materials there are follows the model, which is picked in another row of the same inspector - so the count is watched rather than read once.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( Property is null || Count == _built ) return;

		Rebuild();
	}

	protected override void OnReadOnlyChanged()
	{
		foreach ( var row in ChildrenOfType<MaterialIndexPanel>() )
			row.SetReadOnly( IsReadOnly );
	}
}

//One material slot: what it is, and a way back to what it was.
file sealed class MaterialIndexPanel : Panel
{
	readonly SerializedProperty _property;
	readonly int _index;
	readonly ResourceControl _resource;
	readonly ToolbarButton _revert;

	bool _readOnly;

	public MaterialIndexPanel( SerializedProperty property, int index, bool readOnly )
	{
		_property = property;
		_index = index;
		_readOnly = readOnly;

		AddClass( "material-index" );

		//The slot isn't a property of anything - it is one entry of the accessor, so it is given a property of its own that reads and writes that entry.
		var slot = Game.TypeLibrary.CreateProperty<Material>( $"Index {index}", GetMaterial, SetMaterial, null, property.Parent );

		_resource = AddChild<ResourceControl>();
		_resource.ReadOnly = readOnly;
		_resource.Property = slot;

		_revert = AddChild<ToolbarButton>();
		_revert.Icon = "replay";
		_revert.ToolTip = "Revert Override";
		_revert.OnPressed = Revert;

		SyncState();
	}

	public override void Tick()
	{
		base.Tick();

		SyncState();
	}

	public void SetReadOnly( bool readOnly )
	{
		_readOnly = readOnly;
		_resource.ReadOnly = readOnly;

		SyncState();
	}

	MaterialAccessor Accessor => _property.GetValue<MaterialAccessor>();

	bool HasOverride() => Accessor?.HasOverride( _index ) ?? false;

	Material GetMaterial()
	{
		var accessor = Accessor;

		if ( accessor is null ) return null;

		return accessor.HasOverride( _index )
			? accessor.GetOverride( _index )
			: accessor.GetOriginal( _index );
	}

	void SetMaterial( Material value )
	{
		Accessor?.SetOverride( _index, value );
	}

	void Revert() => SetMaterial( default );

	//An overridden slot reads differently from one still showing the model's own.
	void SyncState()
	{
		var hasOverride = HasOverride();

		_resource.SetClass( "override-active", hasOverride );
		_resource.SetClass( "override-default", !hasOverride );

		var disabled = _readOnly || !hasOverride;

		if ( _revert.Disabled == disabled ) return;

		_revert.Disabled = disabled;
		_revert.StateHasChanged();
	}
}
