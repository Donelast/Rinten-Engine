using Rinten.Diagnostics;

namespace Editor.Inspectors;

/// <summary>The row of the inspector that edits a <see cref="Curve"/>.</summary>
[StyleSheet( "CurveControl.cs.scss" )]
[CustomEditor( typeof( Curve ) )]
public class CurveControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	/// <summary>The colour the preview line is drawn in.</summary>
	public Color HighlightColor { get; set; } = Theme.Yellow;

	CurvePreviewPanel preview;

	public CurveControl()
	{
		ShowBackground = false;
	}

	public override void Rebuild()
	{
		DeleteChildren();

		preview = AddChild<CurvePreviewPanel>( "preview" );
		preview.HighlightColor = HighlightColor;
		preview.AddEventListener( "onmousedown", OnMouseDown );

		RefreshValue();
	}

	public override void Tick()
	{
		base.Tick();

		RefreshValue();
	}

	void RefreshValue()
	{
		if ( preview is null || !preview.IsValid() || Property is null ) return;

		if ( Property.IsMultipleDifferentValues )
		{
			preview.Multiple = true;
			return;
		}

		preview.Multiple = false;
		preview.SetCurve( Property.GetValue<Curve>() );
	}

	void OnMouseDown( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe || Property is null ) return;

		if ( mpe.MouseButton != MouseButtons.Left ) return;
		if ( IsReadOnly ) return;

		CurveEditorWindow.Open( Property );

		e.StopPropagation();
	}

	public override void OnPropertyContextMenu( Menu menu )
	{
		if ( Property is null ) return;

		menu.AddOption( "Open in Editor", "edit", () => CurveEditorWindow.Open( Property ) ).Enabled = !IsReadOnly;
	}
}

/// <summary>The same row, for the two curves of a <see cref="CurveRange"/>.</summary>
[StyleSheet( "CurveControl.cs.scss" )]
[CustomEditor( typeof( CurveRange ) )]
public class CurveRangeControl : BaseControl
{
	/// <summary>The colour the preview band is drawn in.</summary>
	public Color HighlightColor { get; set; } = Theme.Yellow;

	CurvePreviewPanel preview;

	public CurveRangeControl()
	{
		ShowBackground = false;
	}

	public override void Rebuild()
	{
		DeleteChildren();

		preview = AddChild<CurvePreviewPanel>( "preview" );
		preview.HighlightColor = HighlightColor;
		preview.AddEventListener( "onmousedown", OnMouseDown );

		RefreshValue();
	}

	public override void Tick()
	{
		base.Tick();

		RefreshValue();
	}

	void RefreshValue()
	{
		if ( preview is null || !preview.IsValid() || Property is null ) return;

		if ( Property.IsMultipleDifferentValues )
		{
			preview.Multiple = true;
			return;
		}

		preview.Multiple = false;
		preview.SetRange( Property.GetValue<CurveRange>() );
	}

	void OnMouseDown( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe || Property is null ) return;

		if ( mpe.MouseButton != MouseButtons.Left ) return;
		if ( IsReadOnly ) return;

		Property.TryGetAsObject( out var obj );
		Assert.NotNull( obj, "CurveRange object was null" );

		var a = obj.GetProperty( "A" );
		var b = obj.GetProperty( "B" );

		Assert.NotNull( a, "CurveRange.A property was null" );
		Assert.NotNull( b, "CurveRange.B property was null" );

		CurveEditorWindow.OpenRange( Property, a, b );

		e.StopPropagation();
	}
}
