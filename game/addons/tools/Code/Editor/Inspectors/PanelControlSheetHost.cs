using System.Linq.Expressions;

namespace Editor.Inspectors;

/// <summary>Widget dock slot that hosts razor <see cref="ControlSheet"/> (panel path).</summary>
public class PanelControlSheetHost : PanelWidget
{
	public ControlSheet Sheet => (ControlSheet)Panel;

	/// <summary>Adds the raw property name to each row's tooltip. Set it before filling the sheet.</summary>
	public bool ShowPropertyNameInTooltip
	{
		get => Sheet.ShowPropertyNameInTooltip;
		set => Sheet.ShowPropertyNameInTooltip = value;
	}

	public PanelControlSheetHost( Widget parent = null ) : base( parent )
	{
		HorizontalSizeMode = SizeMode.Flexible;

		//Size to the sheet's rows, or the layout keeps its initial size and rows overlap
		SizeToContent = true;

		SetPanel( new ControlSheet() );
	}

	public void SetObject( SerializedObject so, Func<SerializedProperty, bool> filter = null )
	{
		Sheet.PropertyFilter = filter;
		Sheet.Target = so;
		Sheet.Rebuild();
	}

	//For a window that shows a few properties off more than one object - the video export puts its own beside its config's - where there is no single serialized object to hand over.
	/// <summary>Fills the sheet from properties gathered elsewhere, in the order given.</summary>
	public void SetProperties( List<SerializedProperty> properties )
	{
		Sheet.PropertyFilter = null;
		Sheet.Target = properties;
		Sheet.Rebuild();
	}

	public void SetProperties( SerializedObject so, IEnumerable<string> names )
	{
		Sheet.PropertyFilter = null;

		var list = new List<SerializedProperty>();
		foreach ( var name in names )
		{
			var p = so.GetProperty( name );
			if ( p is not null ) list.Add( p );
		}

		Sheet.Target = list;
		Sheet.Rebuild();
	}

	public void AddRow( SerializedProperty property ) => Sheet.AddPropertyRow( property );

	public void AddGroup( string name, IEnumerable<SerializedProperty> properties ) => Sheet.AddGroup( name, properties );

	public ControlWidget AddRowWithWidget( SerializedProperty property )
	{
		var editor = global::Editor.ControlSheetRow.CreateEditor( property );
		AddRow( property );
		return editor;
	}

	public void AddProperty<T, U>( T target, Expression<Func<T, U>> property ) => Sheet.AddProperty( target, property );

	public ControlWidget AddPropertyWithWidget<T>( Expression<Func<T>> property )
		=> AddRowWithWidget( ReflectionSerializedProperty.For( property ) );

	public void AddProperty<T>( Expression<Func<T>> property ) => Sheet.AddProperty( property );
}
