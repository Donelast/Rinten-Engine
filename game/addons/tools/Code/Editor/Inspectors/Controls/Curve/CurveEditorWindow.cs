namespace Editor.Inspectors;

//The panel the window is made of, hung where the window system can see it.
sealed class CurveEditorBodyHost : PanelWidget
{
	public CurveEditorBodyHost( Widget parent, CurveEditorPanel panel ) : base( parent )
	{
		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.Flexible;
		TransparentForMouseEvents = false;

		SetPanel( panel );
	}
}

/// <summary>The curve editor, in a window of its own.</summary>
public sealed class CurveEditorWindow : BaseWindow
{
	static CurveEditorWindow instance;

	/// <summary>Opens the editor on a single curve.</summary>
	public static CurveEditorWindow Open( SerializedProperty property )
	{
		if ( property is null ) return null;

		return Build( "Curve", panel =>
		{
			panel.Property = property;
			panel.AddCurve( property );
			panel.AddPresets( property );
		} );
	}

	/// <summary>Opens the editor on the two curves of a range.</summary>
	public static CurveEditorWindow OpenRange( SerializedProperty property, SerializedProperty a, SerializedProperty b )
	{
		if ( a is null || b is null ) return null;

		return Build( "Curve Range", panel =>
		{
			panel.Property = property;
			panel.AddCurve( a );
			panel.AddCurve( b );
			panel.SetIsRange();
		} );
	}

	static CurveEditorWindow Build( string title, Action<CurveEditorPanel> fill )
	{
		if ( instance.IsValid() ) instance.Close();

		instance = new CurveEditorWindow( title, fill );

		return instance;
	}

	CurveEditorWindow( string title, Action<CurveEditorPanel> fill )
	{
		WindowTitle = title;
		SetWindowIcon( "show_chart" );

		Size = new Vector2( 900, 620 );
		MinimumSize = new Vector2( 700, 480 );

		DeleteOnClose = true;
		IsDialog = true;

		var panel = new CurveEditorPanel();

		fill( panel );

		Canvas = new CurveEditorBodyHost( this, panel );

		Show();
	}

	protected override void OnClosed()
	{
		base.OnClosed();

		if ( ReferenceEquals( instance, this ) ) instance = null;
	}
}
