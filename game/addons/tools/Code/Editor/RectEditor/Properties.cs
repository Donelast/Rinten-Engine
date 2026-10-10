
namespace Editor.RectEditor;

using Editor.Inspectors;

public class Properties : Widget
{
	private readonly PanelControlSheetHost _sheetHost;

	public SerializedObject SerializedObject
	{
		set
		{
			_sheetHost.Sheet.ClearBody();

			if ( value is not null )
				_sheetHost.SetObject( value );
		}
	}

	public Properties( Widget parent ) : base( parent )
	{
		Name = "Properties";
		WindowTitle = "Properties";
		SetWindowIcon( "edit" );

		MinimumWidth = 200;

		Layout = Layout.Column();
		Layout.Margin = 5;
		Layout.Spacing = 5;

		_sheetHost = new PanelControlSheetHost( this );
		Layout.Add( _sheetHost );

		Layout.AddStretchCell();
	}
}
