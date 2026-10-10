using Rinten.UI;

namespace Editor;

//What a primitive tool builds and what shape a block is are the same question asked twice, so it is one control asked for by name: "Shape", "Primitive".
/// <summary>A button that says which type is picked, and opens the list of the others.</summary>
public sealed class TypePickerPanel : EditorPanel
{
	/// <summary>Told when a different type is picked.</summary>
	public Action<TypeDescription> TypeSelected { get; set; }

	private readonly string what;
	private readonly Rinten.UI.Label icon;
	private readonly Rinten.UI.Label text;

	private IReadOnlyList<TypeDescription> items = [];

	public TypePickerPanel( string what )
	{
		this.what = what;

		AddClass( "btn" );
		AddClass( "row" );
		AddClass( "gap-2" );
		AddClass( "type-picker" );

		icon = AddChild<Rinten.UI.Label>( "icon" );
		icon.AddClass( "material-icon" );

		text = AddChild<Rinten.UI.Label>( "text" );

		var chevron = AddChild<IconPanel>( "chevron" );
		chevron.Text = "arrow_drop_down";
	}

	/// <summary>The types this can be set to, shown in the order given.</summary>
	public void SetItems( IEnumerable<TypeDescription> value )
	{
		items = value.OrderBy( x => x.Title ).ToArray();
	}

	/// <summary>Says which one is picked, without telling anyone it changed.</summary>
	public void SelectType( Type type )
	{
		var selected = items.FirstOrDefault( x => x.TargetType == type );

		text.Text = selected is null ? $"Select {what}..." : selected.Title;
		icon.Text = selected?.Icon ?? "category";
		Tooltip = selected?.Description;
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		var menu = new ContextMenu( HostWidget );

		foreach ( var type in items )
		{
			menu.AddOption( type.Title, type.Icon, () =>
			{
				SelectType( type.TargetType );
				TypeSelected?.Invoke( type );
			} );
		}

		menu.OpenAt( ScreenRect.BottomLeft, false );

		e.StopPropagation();
	}
}
