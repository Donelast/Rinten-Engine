namespace Editor.Inspectors;

sealed class GradientEditorBodyHost : PanelWidget
{
	public GradientEditorBodyHost( Widget parent, GradientEditorPanel panel ) : base( parent )
	{
		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.Flexible;
		TransparentForMouseEvents = false;
		SetPanel( panel );
	}
}

public sealed class GradientEditorWindow : BaseWindow
{
	static GradientEditorWindow _instance;

	public static void Open( SerializedProperty property, Gradient input, Action<Gradient> onChange )
	{
		if ( _instance.IsValid() )
			_instance.Close();
		_instance = new GradientEditorWindow( property, input, onChange );
	}

	GradientEditorWindow( SerializedProperty property, Gradient input, Action<Gradient> onChange )
	{
		WindowTitle = "Gradient Editor";
		SetWindowIcon( "gradient" );
		Size = new Vector2( 500, 350 );
		MinimumSize = new Vector2( 500, 350 );
		DeleteOnClose = true;
		IsDialog = true;
		var panel = new GradientEditorPanel
		{
			Property = property,
			Value = input,
			ValueChanged = onChange
		};
		Canvas = new GradientEditorBodyHost( this, panel );
		Show();
	}

	protected override void OnClosed()
	{
		base.OnClosed();
		if ( ReferenceEquals( _instance, this ) )
			_instance = null;
	}
}
