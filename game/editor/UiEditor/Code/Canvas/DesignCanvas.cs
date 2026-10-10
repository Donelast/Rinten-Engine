using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.UiEditor;

/// <summary>The UI editor canvas dock: the document's panel on a screen-sized stage scaled to fit</summary>
[SkipHotload]
public sealed class DesignCanvas : PanelDockHost
{
	readonly DesignCanvasPanel _panel;

	public DesignCanvas( UiEditorWindow window ) : base( null )
	{
		Name = "Canvas";
		WindowTitle = "Canvas";
		SetWindowIcon( "web" );

		_panel = new DesignCanvasPanel( window );

		SetContent( _panel );
	}

	/// <summary>The instance of the document's type on the stage, if there is one.</summary>
	public Panel Root => _panel?.Root;

	//The panel doing the work, for a drag driven from the console.
	internal DesignCanvasPanel Canvas => _panel;

	/// <summary>Build the document's panel on the stage, in place of whatever was there.</summary>
	public void SetDocument( RazorDocument document ) => _panel?.SetDocument( document );

	/// <summary>The selection changed; the frame around it moves on the next frame.</summary>
	public void NoteSelection() { }

	/// <summary>A stylesheet was written; the stage takes the new text now, rather than waiting on the watcher.</summary>
	public void NoteSheetWritten( string path ) => _panel?.NoteSheetWritten( path );
}

//Toolbar plus viewport: a style-bounded stage at the chosen resolution, and an editor overlay over it
[StyleSheet( "DesignCanvas.cs.scss" )]
//Made again on hotload with the dock it is in - see DesignCanvas.
[SkipHotload]
internal sealed class DesignCanvasPanel : EditorPanel
{
	//A screen to lay the document out for.
	readonly record struct Resolution( string Name, int Width, int Height );

	static readonly Resolution[] Resolutions =
	{
		new( "1080p", 1920, 1080 ),
		new( "720p", 1280, 720 ),
		new( "1440p", 2560, 1440 ),
		new( "Deck", 1280, 800 ),
		new( "21:9", 3440, 1440 ),
		new( "4:3", 1024, 768 ),
	};

	const string ResolutionCookie = "UiEditor.Resolution";
	const string ModeCookie = "UiEditor.Interact";
	const string GridCookie = "UiEditor.Grid";
	const string RulersCookie = "UiEditor.Rulers";

	/// <summary>The grid's step, in the stage's pixels.</summary>
	public const float GridStep = 8.0f;

	bool _grid;
	bool _rulers;
	bool _focusOrder;

	/// <summary>Whether the panels that take the keyboard are numbered on the canvas, in the order they would get it.</summary>
	public bool FocusOrder => _focusOrder;

	/// <summary>Whether a drag snaps to the grid, and the grid is drawn.</summary>
	public bool Grid => _grid;

	/// <summary>Whether the rulers are drawn along the viewport's edges.</summary>
	public bool Rulers => _rulers;

	readonly UiEditorWindow _window;

	readonly Panel _viewport;
	readonly Panel _stage;
	readonly Overlay _overlay;
	readonly UILabel _scaleReadout;
	readonly PanelToolbarButton _interactToggle;

	readonly List<(PanelToolbarButton button, Resolution resolution)> _resolutionButtons = new();

	RazorDocument _document;
	Panel _root;

	readonly Panel _trouble;
	readonly UILabel _troubleText;
	RealTimeSince _sinceTroubleRead;
	string _troublePath;
	int _troubleLine;

	//A scene of its own for a document that is a component: a component has a panel only on a game object.
	Scene _scene;
	GameObject _object;

	Resolution _resolution;
	bool _interact;
	float _fit = 1.0f;

	//Over the fit: 1 is the whole stage in the dock, 2 twice that.
	float _zoom = 1.0f;

	//Where the stage's centre is moved to, from the viewport's, in the viewport's units.
	Vector2 _pan;

	readonly UILabel _zoomReadout;

	/// <summary>The scale the stage is drawn at, over its layout: the fit times the zoom.</summary>
	public float Zoom => _zoom;

	public DesignCanvasPanel( UiEditorWindow window )
	{
		_window = window;

		AddClass( "design-canvas" );

		var name = EditorCookie.GetString( ResolutionCookie, Resolutions[0].Name );
		_resolution = Resolutions.FirstOrDefault( x => x.Name == name, Resolutions[0] );
		_interact = EditorCookie.Get( ModeCookie, false );
		_grid = EditorCookie.Get( GridCookie, false );
		_rulers = EditorCookie.Get( RulersCookie, true );

		var bar = AddChild<PanelToolbar>();

		//Design: the pointer selects. Interact: the pointer reaches the document.
		_interactToggle = bar.AddToggle( "touch_app", "Interact - let the pointer through to the document (Ctrl+I)", _interact, SetInteract );

		bar.AddSeparator();

		_gridToggle = bar.AddToggle( "grid_4x4", $"Grid - {GridStep:0} pixel steps of the stage, and a drag snaps to them (Ctrl+G)", _grid, on => { _grid = on; EditorCookie.Set( GridCookie, on ); } );
		_rulersToggle = bar.AddToggle( "straighten", "Rulers (Ctrl+R)", _rulers, on => { _rulers = on; EditorCookie.Set( RulersCookie, on ); } );
		bar.AddToggle( "keyboard_tab", "Focus order - number the panels that take the keyboard, in tree order, which is the order the engine gives it in", _focusOrder, on => _focusOrder = on );

		bar.AddSeparator();

		foreach ( var resolution in Resolutions )
		{
			var button = bar.AddTextToggle( resolution.Name, $"{resolution.Width} × {resolution.Height}", resolution == _resolution,
				on => { if ( on ) SetResolution( resolution ); } );

			_resolutionButtons.Add( ( button, resolution ) );
		}

		bar.AddSeparator();

		_scaleReadout = bar.AddChild<UILabel>( "readout" );
		_scaleReadout.Tokenize = false;

		bar.AddSeparator();

		bar.AddIcon( "zoom_out", "Zoom out", () => ZoomBy( 1 / 1.25f, null ) );
		_zoomReadout = bar.AddChild<UILabel>( "readout zoom" );
		_zoomReadout.Tokenize = false;
		bar.AddIcon( "zoom_in", "Zoom in", () => ZoomBy( 1.25f, null ) );
		bar.AddIcon( "fit_screen", "Fit the stage in the dock (Ctrl+0)", ResetView );
		bar.AddIcon( "looks_one", "Actual pixels (Ctrl+1)", ActualSize );

		bar.AddStretch();

		//What is wrong with the project's code, when something is: the canvas shows the last panel that compiled, and this says why it is not the current one.
		_trouble = AddChild<Panel>( "trouble" );
		_troubleText = _trouble.AddChild<UILabel>( "text" );
		_troubleText.Tokenize = false;
		_trouble.AddEventListener( "onclick", OpenTrouble );
		_trouble.SetClass( "hidden", true );

		_viewport = AddChild<Panel>( "viewport" );

		_stage = _viewport.AddChild<Panel>( "stage" );
		_stage.StyleBoundary = true;

		//The interface's own defaults, as a game's root has them - a text entry's placeholder sits on them, and without them
		//it lay over the whole screen. The boundary keeps the editor's sheet out, so the stage carries this one itself.
		_stage.StyleSheet.Load( "/styles/rootpanel.scss", failSilently: true );

		//The boundary stops this canvas's own sheet too, so the stage is dressed here: a screen's
		//worth of dark, and the defaults a game's root has, since nothing is inherited past it either.
		_stage.Style.Position = PositionMode.Absolute;
		_stage.Style.Overflow = OverflowMode.Hidden;
		_stage.Style.BackgroundColor = new Color( 0.08f, 0.08f, 0.09f );
		_stage.Style.FontFamily = "Inter";
		_stage.Style.FontSize = Length.Pixels( 16 );
		_stage.Style.FontColor = Color.White;
		_stage.Style.BoxShadow.Add( new Shadow { OffsetY = 8, Blur = 32, Color = Color.Black.WithAlpha( 0.6f ) } );

		_overlay = _viewport.AddChild( new Overlay( this ) );

		ApplyResolution();
		SetInteract( _interact );
	}

	/// <summary>The instance of the document's type on the stage, if there is one.</summary>
	public Panel Root => _root.IsValid() ? _root : null;

	/// <summary>The object the document's members are on: the component, or the panel itself.</summary>
	public object Instance => _object.IsValid() ? _object.Components.Get<PanelComponent>() : Root;

	/// <summary>Have the document draw itself again, for a value it shows having changed.</summary>
	public void Rerender()
	{
		if ( Instance is PanelComponent component ) component.StateHasChanged();
		else Root?.StateHasChanged();
	}

	/// <summary>Whether the pointer goes through to the document.</summary>
	public bool Interact => _interact;

	/// <summary>Guide lines pulled off the rulers, in the stage's pixels: the verticals by x, the horizontals by y.</summary>
	public List<float> GuidesX { get; } = new();
	public List<float> GuidesY { get; } = new();

	//Where guides are kept between openings: under this name in the design data.
	const string GuidesKey = "$guides";

	sealed class GuideSet { public List<float> X { get; set; } = new(); public List<float> Y { get; set; } = new(); }

	internal void LoadGuides()
	{
		GuidesX.Clear();
		GuidesY.Clear();

		if ( _window.Design?.Has( GuidesKey ) != true ) return;

		try
		{
			if ( Json.FromNode<GuideSet>( _window.Design.Get( GuidesKey ) ) is { } set )
			{
				GuidesX.AddRange( set.X ?? new() );
				GuidesY.AddRange( set.Y ?? new() );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"UI editor: guides could not be read: {e.Message}" );
		}
	}

	internal void SaveGuides()
	{
		if ( _window.Design is null ) return;

		if ( GuidesX.Count == 0 && GuidesY.Count == 0 ) _window.Design.Clear( GuidesKey );
		else _window.Design.Set( GuidesKey, new GuideSet { X = GuidesX.ToList(), Y = GuidesY.ToList() }, typeof( GuideSet ) );
	}

	/// <summary>The stage in layout units: what the document thinks the screen is.</summary>
	public Vector2 StageSize => new( _resolution.Width, _resolution.Height );

	/// <summary>How much smaller the stage is drawn than it is laid out.</summary>
	public float Fit => _fit;

	/// <summary>Where the stage's top left is, in the viewport's own layout units.</summary>
	public Vector2 StageOrigin => new(
		( _stage.Box.Rect.Left - _viewport.Box.Rect.Left ) * _viewport.ScaleFromScreen,
		( _stage.Box.Rect.Top - _viewport.Box.Rect.Top ) * _viewport.ScaleFromScreen );

	public void SetDocument( RazorDocument document )
	{
		_document = document;

		LoadGuides();
		Rebuild();
	}

	//The file on disk is the document: push its text into the sheets the stage is wearing, drop a leftover <style> sheet if the tag is gone, and dirty the
	//tree. The mount watcher may not have seen a write that went straight to disk, and without this the inline values a drag set are cleared onto the old rules.
	internal void NoteSheetWritten( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) path = _window.Document?.ScssPath;

		if ( !string.IsNullOrEmpty( path ) && System.IO.File.Exists( path ) )
			Rinten.UI.StyleSheet.ReplaceFile( path, System.IO.File.ReadAllText( path ) );

		if ( _root.IsValid() && _window.Document is { HasStyleTag: false } )
			DropStyleTagSheets( _root );

		if ( _root.IsValid() )
			_root.Restyle();
	}

	//Sheets the engine made from a <style> in the old compile, which still win until the type is built again.
	static void DropStyleTagSheets( Panel panel )
	{
		if ( !panel.IsValid() ) return;

		panel.StyleSheet.Remove( StyleCascade.StyleTagSheet + "*" );

		foreach ( var child in panel.Children )
			DropStyleTagSheets( child );
	}

	//Build the document's panel again, from its current type.
	void Rebuild()
	{
		if ( _root.IsValid() ) _root.Delete( true );
		_root = null;

		if ( _object.IsValid() ) _object.Destroy();
		_object = null;

		if ( _document is null ) return;

		_root = _document.IsComponent ? InstantiateComponent() : _document.Instantiate();

		if ( _root is null ) return;

		_stage.AddChild( _root );

		//What the designer last set it to show.
		_window.Design?.Apply( Instance );
	}

	//A component's panel: the component is put on a game object in a scene of this canvas's own, which makes the panel,
	//and the panel is then taken onto the stage. The scene is never played; it is only what a component has to stand on.
	Panel InstantiateComponent()
	{
		_scene ??= new Scene { Name = "UI Editor" };

		try
		{
			using ( _scene.Push() )
			{
				_object = new GameObject( true, _document.Name );

				if ( _object.Components.Create( _document.Type ) is not PanelComponent component )
				{
					Log.Warning( $"UI editor: {_document.Type.FullName} did not make a component" );
					return null;
				}

				return component.Panel;
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"UI editor: could not create {_document.Type.FullName}: {e.Message}" );
			return null;
		}
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		if ( _object.IsValid() ) _object.Destroy();
		_object = null;

		_scene?.Destroy();
		_scene = null;
	}

	void SetResolution( Resolution resolution )
	{
		_resolution = resolution;
		EditorCookie.SetString( ResolutionCookie, resolution.Name );

		foreach ( var (button, each) in _resolutionButtons )
			button.IsActive = each == resolution;

		ApplyResolution();
	}

	void ApplyResolution()
	{
		_stage.Style.Width = Length.Pixels( _resolution.Width );
		_stage.Style.Height = Length.Pixels( _resolution.Height );
		_stage.Style.TransformOriginX = Length.Pixels( 0 );
		_stage.Style.TransformOriginY = Length.Pixels( 0 );

		_fit = 0;
	}

	void SetInteract( bool interact )
	{
		_interact = interact;
		EditorCookie.Set( ModeCookie, interact );

		if ( _interactToggle.IsValid() ) _interactToggle.IsActive = interact;

		_overlay.SetClass( "interact", interact );
		SetClass( "interact", interact );
	}

	/// <summary>Fit the stage to the viewport: the largest scale that shows all of it with a margin, centred. Every frame, because a dock resizes without telling the panels in it.</summary>
	public override void Tick()
	{
		base.Tick();

		//A component's own lifecycle puts its panel where a scene would have it - under nothing, here - so it is put back on the stage.
		if ( _root.IsValid() && _root.Parent != _stage )
			_root.Parent = _stage;

		ClearInlineDue();
		ReadTrouble();

		var viewport = _viewport.Box.Rect.Size * _viewport.ScaleFromScreen;

		if ( viewport.x <= 0 || viewport.y <= 0 ) return;

		const float margin = 24.0f;

		var fit = MathF.Min(
			( viewport.x - margin * 2 ) / _resolution.Width,
			( viewport.y - margin * 2 ) / _resolution.Height );

		fit = MathF.Max( 0.02f, fit ) * _zoom;

		if ( MathF.Abs( fit - _fit ) > 0.0005f )
		{
			_fit = fit;

			var transform = new PanelTransform();
			transform.AddScale( fit );

			_stage.Style.Transform = transform;
			_scaleReadout.Text = $"{fit * 100:0}%";
			_zoomReadout.Text = $"{_zoom * 100:0}%";
		}

		_stage.Style.Left = Length.Pixels( ( viewport.x - _resolution.Width * fit ) * 0.5f + _pan.x );
		_stage.Style.Top = Length.Pixels( ( viewport.y - _resolution.Height * fit ) * 0.5f + _pan.y );
	}

	//Zoom and pan

	//Multiply the zoom, keeping the point under the pointer - in the viewport's units - where it is.
	internal void ZoomBy( float factor, Vector2? around )
	{
		ZoomTo( _zoom * factor, around );
	}

	internal void ZoomTo( float zoom, Vector2? around )
	{
		zoom = Math.Clamp( zoom, 0.1f, 16.0f );
		if ( MathF.Abs( zoom - _zoom ) < 0.0001f ) return;

		var viewport = _viewport.Box.Rect.Size * _viewport.ScaleFromScreen;
		var centre = viewport * 0.5f + _pan;
		var at = around ?? viewport * 0.5f;

		//The stage point under the pointer stays under it: the centre moves away from the pointer by the same ratio the scale grew.
		var ratio = zoom / _zoom;
		_pan = at + ( centre - at ) * ratio - viewport * 0.5f;
		_zoom = zoom;
	}

	internal void PanBy( Vector2 delta ) => _pan += delta;

	//The whole stage in the dock again, centred.
	internal void ResetView()
	{
		_zoom = 1.0f;
		_pan = Vector2.Zero;
	}

	readonly PanelToolbarButton _gridToggle, _rulersToggle;

	//Grid on or off, as the toolbar's own toggle does it.
	internal void ToggleGrid() { _grid = !_grid; EditorCookie.Set( GridCookie, _grid ); if ( _gridToggle.IsValid() ) _gridToggle.IsActive = _grid; }

	internal void ToggleRulers() { _rulers = !_rulers; EditorCookie.Set( RulersCookie, _rulers ); if ( _rulersToggle.IsValid() ) _rulersToggle.IsActive = _rulers; }

	internal void ToggleInteract() => SetInteract( !_interact );

	//One stage pixel per screen pixel.
	internal void ActualSize() => ZoomTo( 1.0f / MathF.Max( 0.01f, _fit / _zoom ), null );

	//The first error in the project the document belongs to, a few times a second: a tag that names nothing,
	//a handler that does not parse, and the canvas would otherwise sit on the old panel saying nothing.
	void ReadTrouble()
	{
		if ( _sinceTroubleRead < 0.5f ) return;
		_sinceTroubleRead = 0;

		string text = null;
		_troublePath = null;

		if ( Project.CompileGroup is { } group )
		{
			foreach ( var compiler in group.Compilers )
			{
				if ( compiler.IsBuilding || compiler.Output?.Successful != false ) continue;

				var error = compiler.Diagnostics.FirstOrDefault( d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error );
				if ( error is null ) continue;

				var span = error.Location.GetLineSpan();
				var file = string.IsNullOrEmpty( span.Path ) ? compiler.Name : System.IO.Path.GetFileName( span.Path );

				_troublePath = span.Path;
				_troubleLine = span.StartLinePosition.Line + 1;
				text = $"{compiler.Name} does not compile - {file}:{_troubleLine}: {error.GetMessage()}";
				break;
			}
		}

		_trouble.SetClass( "hidden", text is null );
		if ( text is not null && _troubleText.Text != text ) _troubleText.Text = text;
	}

	void OpenTrouble()
	{
		if ( string.IsNullOrEmpty( _troublePath ) ) return;

		InspectorPanel.OpenFile( _troublePath, _troubleLine );
	}

	//What the overlay asks

	readonly List<(Panel panel, string[] names, RealTimeSince since)> _inlineToClear = new();

	//Let go of a panel's inline values once the sheet has had a moment to be read.
	internal void ClearInlineLater( Panel panel, string[] names ) => _inlineToClear.Add( ( panel, names, 0 ) );

	void ClearInlineDue()
	{
		for ( var i = _inlineToClear.Count - 1; i >= 0; i-- )
		{
			var (panel, names, since) = _inlineToClear[i];
			if ( since < 0.4f ) continue;

			foreach ( var name in names )
				CanvasDrag.ClearInline( panel, name );

			_inlineToClear.RemoveAt( i );
		}
	}

	/// <summary>Open a field over a panel on the canvas to edit its tag's words where they are.</summary>
	public void BeginTextEdit( Panel panel ) => _overlay.BeginTextEdit( panel );

	public bool IsEditingText => _overlay.IsEditingText;

	/// <summary>What Enter in the field does, with these words - for driving the edit from the console.</summary>
	public void SubmitTextEdit( string words ) => _overlay.SubmitTextEdit( words );

	/// <summary>Select the outermost panels lying whole inside a rect of the stage, in stage pixels - what a marquee there takes.</summary>
	public void SelectInside( Rect stageRect )
	{
		var fit = MathF.Max( 0.01f, Fit );
		_overlay.SelectInside( new Rect( StageOrigin + stageRect.Position * fit, stageRect.Size * fit ) );
	}

	//The document's panel under a point, in the space the pointer is read in.
	internal Panel PanelUnder( Vector2 rootPoint )
	{
		if ( !_root.IsValid() ) return null;

		//Hit-test from the stage so its fit scale applies; panels with pointer-events off still count
		var found = _stage.PanelUnder( rootPoint, selectable: p => !ReferenceEquals( p, _stage ) );

		return found is not null && !ReferenceEquals( found, _stage ) ? found : null;
	}

	internal void Select( Panel panel ) => _window.Select( panel );

	internal Panel Selection => _window.Selection;

	internal UiEditorWindow Window => _window;

	//The editor's furniture over the stage: what the pointer hits in Design mode, and where the frames are drawn in both.
	internal sealed class Overlay : Panel
	{
		readonly DesignCanvasPanel _canvas;

		readonly Panel _hoverBox;
		readonly Panel _selectionBox;
		readonly Panel _marginBox;
		readonly Panel _paddingBox;
		readonly UILabel _readout;

		Panel _hovered;

		public Overlay( DesignCanvasPanel canvas )
		{
			_canvas = canvas;

			AddClass( "overlay" );

			_marginBox = AddChild<Panel>( "box margin" );
			_paddingBox = AddChild<Panel>( "box padding" );
			_hoverBox = AddChild<Panel>( "box hover" );
			_selectionBox = AddChild<Panel>( "box selection" );

			_readout = _selectionBox.AddChild<UILabel>( "readout" );
			_readout.Tokenize = false;

			//Eight grips on the selection, by the compass point each pulls.
			foreach ( var grip in Grips )
				_selectionBox.AddChild<Panel>( $"grip {grip}" );
		}

		static readonly string[] Grips = { "n", "s", "e", "w", "ne", "nw", "se", "sw", "rotate" };

		//Where the pointer is, in the space the panels' rects are in.
		Vector2 PointerInRootSpace => MousePosition + Box.Rect.Position;

		//The guide lines a snapping drag is holding to, in the stage's pixels; x lines then y lines.
		internal List<float> GuideX { get; } = new();
		internal List<float> GuideY { get; } = new();

		const float RulerSize = 18.0f;

		/// <summary>The grid over the stage, the rulers along the viewport, and the guides a drag is snapped to.</summary>
		public override void OnDraw()
		{
			var fit = _canvas.Fit;
			if ( fit <= 0 ) return;

			var box = Box.Rect;
			var scale = ScaleToScreen;
			var origin = box.Position + _canvas.StageOrigin * scale;
			var stage = _canvas.StageSize * fit * scale;

			if ( _canvas.Grid )
			{
				var step = DesignCanvasPanel.GridStep * fit * scale;

				if ( step >= 5 )
				{
					var colour = Color.White.WithAlpha( 0.05f );

					for ( var x = origin.x; x <= origin.x + stage.x; x += step )
						Draw.Rect( new Rect( x, origin.y, 1, stage.y ), colour );

					for ( var y = origin.y; y <= origin.y + stage.y; y += step )
						Draw.Rect( new Rect( origin.x, y, stage.x, 1 ), colour );
				}
			}

			//The rulers' guides: cyan lines with their pixel on them.
			var pulled = new Color( 0.3f, 0.85f, 1.0f, 0.8f );
			var guideText = new Color( 0.3f, 0.85f, 1.0f );

			foreach ( var x in _canvas.GuidesX )
			{
				var sx = origin.x + x * fit * scale;
				Draw.Rect( new Rect( sx, box.Top, 1, box.Height ), pulled );
				Draw.Text( $"{x:0}", new Rect( sx + 3 * scale, box.Top + RulerSize * scale + 2 * scale, 60 * scale, 14 * scale ), 9 * scale, guideText );
			}

			foreach ( var y in _canvas.GuidesY )
			{
				var sy = origin.y + y * fit * scale;
				Draw.Rect( new Rect( box.Left, sy, box.Width, 1 ), pulled );
				Draw.Text( $"{y:0}", new Rect( box.Left + RulerSize * scale + 3 * scale, sy + 2 * scale, 60 * scale, 14 * scale ), 9 * scale, guideText );
			}

			var guide = new Color( 1.0f, 0.3f, 0.6f );

			foreach ( var x in GuideX )
				Draw.Rect( new Rect( origin.x + x * fit * scale, box.Top, 1, box.Height ), guide );

			foreach ( var y in GuideY )
				Draw.Rect( new Rect( box.Left, origin.y + y * fit * scale, box.Width, 1 ), guide );

			if ( _drawFrom is not null )
			{
				var d = new Rect( box.Position + _draw.Position * scale, _draw.Size * scale );
				var line = new Color( 0.55f, 0.71f, 0.24f, 1.0f );
				Draw.Rect( d, new Color( 0.55f, 0.71f, 0.24f, 0.18f ) );
				Draw.Rect( new Rect( d.Left, d.Top, d.Width, 1 ), line );
				Draw.Rect( new Rect( d.Left, d.Bottom - 1, d.Width, 1 ), line );
				Draw.Rect( new Rect( d.Left, d.Top, 1, d.Height ), line );
				Draw.Rect( new Rect( d.Right - 1, d.Top, 1, d.Height ), line );
			}

			if ( _marqueeFrom is not null )
			{
				var m = new Rect( box.Position + _marquee.Position * scale, _marquee.Size * scale );
				var line = new Color( 0.55f, 0.71f, 0.24f, 0.9f );
				Draw.Rect( m, new Color( 0.55f, 0.71f, 0.24f, 0.12f ) );
				Draw.Rect( new Rect( m.Left, m.Top, m.Width, 1 ), line );
				Draw.Rect( new Rect( m.Left, m.Bottom - 1, m.Width, 1 ), line );
				Draw.Rect( new Rect( m.Left, m.Top, 1, m.Height ), line );
				Draw.Rect( new Rect( m.Right - 1, m.Top, 1, m.Height ), line );
			}

			if ( _canvas.FocusOrder ) DrawFocusOrder( scale );

			if ( _canvas.Rulers ) DrawRulers( box, origin, fit * scale, scale );
		}

		//A number on each panel that takes the keyboard, in the order the tree gives them.
		void DrawFocusOrder( float scale )
		{
			var root = _canvas.Root;
			if ( root is null ) return;

			var n = 0;
			var overlayOrigin = Box.Rect.Position;

			foreach ( var panel in root.Descendants.Prepend( root ) )
			{
				if ( !panel.AcceptsFocus || !panel.IsVisible || panel.Box is null ) continue;

				n++;

				var rect = panel.GetScreenPixelRect();
				var badge = new Rect( rect.Left, rect.Top, 20 * scale, 16 * scale );

				Draw.Rect( badge, new Color( 0.2f, 0.5f, 1.0f, 0.9f ), 3 * scale );
				Draw.Text( n.ToString(), new Rect( badge.Left + 4 * scale, badge.Top + 1 * scale, badge.Width, badge.Height ), 10 * scale, Color.White );
			}

			//Nothing takes the keyboard: said once, at the corner.
			if ( n == 0 )
				Draw.Text( "nothing here takes focus", new Rect( overlayOrigin.x + 24 * scale, overlayOrigin.y + 24 * scale, 300 * scale, 16 * scale ), 11 * scale, Color.White.WithAlpha( 0.5f ) );
		}

		//A strip along the top and the left, marked in the stage's pixels.
		void DrawRulers( Rect box, Vector2 origin, float unit, float scale )
		{
			var size = RulerSize * scale;
			var ground = new Color( 0.09f, 0.09f, 0.1f, 0.92f );
			var ink = Color.White.WithAlpha( 0.35f );
			var text = Color.White.WithAlpha( 0.5f );

			Draw.Rect( new Rect( box.Left, box.Top, box.Width, size ), ground );
			Draw.Rect( new Rect( box.Left, box.Top, size, box.Height ), ground );

			//A mark every 50, a number every 100 - or sparser while the stage is small.
			var every = unit >= 0.6f ? 50 : unit >= 0.3f ? 100 : 200;

			for ( var v = 0; v <= _canvas.StageSize.x; v += every )
			{
				var x = origin.x + v * unit;
				if ( x < box.Left + size || x > box.Right ) continue;

				var major = v % ( every * 2 ) == 0;
				Draw.Rect( new Rect( x, box.Top + size - ( major ? 8 : 4 ) * scale, 1, ( major ? 8 : 4 ) * scale ), ink );

				if ( major ) Draw.Text( v.ToString(), new Rect( x + 3 * scale, box.Top + 1 * scale, 60, size ), 9 * scale, text );
			}

			for ( var v = 0; v <= _canvas.StageSize.y; v += every )
			{
				var y = origin.y + v * unit;
				if ( y < box.Top + size || y > box.Bottom ) continue;

				var major = v % ( every * 2 ) == 0;
				Draw.Rect( new Rect( box.Left + size - ( major ? 8 : 4 ) * scale, y, ( major ? 8 : 4 ) * scale, 1 ), ink );

				if ( major ) Draw.Text( v.ToString(), new Rect( box.Left + 2 * scale, y + 1 * scale, size, 14 * scale ), 9 * scale, text );
			}
		}

		protected override void OnMouseMove( MousePanelEvent e )
		{
			base.OnMouseMove( e );

			if ( _panFrom is { } from )
			{
				_canvas.PanBy( MousePosition - from );
				_panFrom = MousePosition;
				return;
			}

			if ( _guideDrag is not null )
			{
				MoveGuide( MousePosition );
				return;
			}

			if ( _canvas.Interact ) return;

			if ( _drag is not null )
			{
				_drag.MoveTo( MousePosition );

				foreach ( var companion in _companions )
					companion.MoveTo( MousePosition );

				return;
			}

			if ( _marqueeFrom is { } start )
			{
				var a = Vector2.Min( start, MousePosition );
				var b = Vector2.Max( start, MousePosition );
				_marquee = new Rect( a, b - a );
				return;
			}

			if ( _drawFrom is { } drawStart )
			{
				var a = Vector2.Min( drawStart, MousePosition );
				var b = Vector2.Max( drawStart, MousePosition );

				//Held to the stage grid when it is on, like a drag.
				if ( _canvas.Grid )
				{
					var step = DesignCanvasPanel.GridStep * MathF.Max( 0.01f, _canvas.Fit );
					var origin = _canvas.StageOrigin;
					a = origin + new Vector2( MathF.Round( ( a.x - origin.x ) / step ), MathF.Round( ( a.y - origin.y ) / step ) ) * step;
					b = origin + new Vector2( MathF.Round( ( b.x - origin.x ) / step ), MathF.Round( ( b.y - origin.y ) / step ) ) * step;
				}

				_draw = new Rect( a, b - a );
				return;
			}

			_hovered = _canvas.PanelUnder( PointerInRootSpace );
		}

		protected override void OnDoubleClick( MousePanelEvent e )
		{
			base.OnDoubleClick( e );

			if ( _canvas.Interact || e.Button != "mouseleft" ) return;

			//The words of a tag, edited where they are.
			var hit = _canvas.PanelUnder( PointerInRootSpace );

			if ( hit is not null && _canvas.Window.HasWords( hit ) )
			{
				e.StopPropagation();
				_canvas.Window.Select( hit );
				BeginTextEdit( hit );
			}
		}

		//A tag drawn on

		//A drawing in progress: where it started in this overlay's units, and the panel it goes into.
		Vector2? _drawFrom;
		Rect _draw;
		Panel _drawInto;

		//The armed tag drawn: a press and a drag give it a rule placing it where the rectangle was, in
		//the parent's own pixels; a bare click adds it inside the panel under the pointer with no rule.
		void LandDrawing()
		{
			var rect = _draw;
			var into = _drawInto;
			_drawFrom = null;
			_drawInto = null;

			var window = _canvas.Window;
			var armed = window.Armed;
			window.Disarm();

			if ( armed is null || !into.IsValid() || window.LocateTag( into ) is null ) return;

			if ( rect.Width < 4 && rect.Height < 4 )
			{
				if ( window.AddChild( into, armed.Tag, armed.Classes, armed.Text ) is { } added ) window.Select( added );
				return;
			}

			//The rectangle in the parent's style pixels, from its padding edge, its scroll put back.
			var scale = into.ScaleFromScreen;
			var a = into.ScreenPositionToPanelPosition( PanelPositionToScreenPosition( rect.Position * ScaleToScreen ) );
			var b = into.ScreenPositionToPanelPosition( PanelPositionToScreenPosition( ( rect.Position + rect.Size ) * ScaleToScreen ) );
			var border = new Vector2( into.Box.Border.Left, into.Box.Border.Top );

			var from = ( a - border ) * scale + into.ScrollOffset;
			var to = ( b - border ) * scale + into.ScrollOffset;

			var made = window.AddDrawn( into, armed.Tag, armed.Classes, armed.Text, new Rect( from, to - from ) );
			if ( made is not null ) window.Select( made );
		}

		//Words edited in place

		TextField _textEdit;
		Panel _textTarget;

		/// <summary>A field over the panel, at its box and the fit's scale, holding the tag's words as written; Enter writes them back, Escape leaves.</summary>
		public void BeginTextEdit( Panel panel )
		{
			EndTextEdit();

			if ( !panel.IsValid() || panel.Box is null ) return;
			if ( !_canvas.Window.HasWords( panel ) || _canvas.Window.LocateTag( panel ) is not { } element ) return;

			_textTarget = panel;

			var words = _canvas.Window.Document?.Markup.Text is { } text ? element.Inner( text ).Trim() : "";

			_textEdit = AddChild<TextField>( "text-edit" );
			_textEdit.Value = words;

			//The panel's own size of type, at the fit, so the words are seen the size they are.
			var size = ( panel.ComputedStyle?.FontSize?.GetPixels( 16 ) ?? 16 ) * _canvas.Fit;
			_textEdit.Style.FontSize = Length.Pixels( MathF.Max( 9, size ) );

			PlaceTextEdit();

			_textEdit.Submitted = value =>
			{
				var target = _textTarget;
				EndTextEdit();
				if ( target.IsValid() ) _canvas.Window.SetText( target, value?.Trim() ?? "" );
			};

			_textEdit.ButtonTyped = e =>
			{
				if ( e.Button != "escape" ) return;

				e.StopPropagation = true;
				if ( e.Pressed ) EndTextEdit();
			};

			_textEdit.AddEventListener( "onblur", () => EndTextEdit() );

			_textEdit.Focus();
			_textEdit.SelectAll();
		}

		void PlaceTextEdit()
		{
			if ( _textEdit is null || !_textTarget.IsValid() || _textTarget.Box is null ) return;

			var rect = RectOf( _textTarget.Box.Rect, _textTarget );

			_textEdit.Style.Left = Length.Pixels( rect.Left );
			_textEdit.Style.Top = Length.Pixels( rect.Top );
			_textEdit.Style.Width = Length.Pixels( MathF.Max( rect.Width, 60 ) );
			_textEdit.Style.Height = Length.Pixels( MathF.Max( rect.Height, 18 ) );
		}

		void EndTextEdit()
		{
			var field = _textEdit;
			_textEdit = null;
			_textTarget = null;

			if ( field.IsValid() ) field.Delete();
		}

		public bool IsEditingText => _textEdit.IsValid();

		public void SubmitTextEdit( string words )
		{
			if ( !_textEdit.IsValid() ) return;

			_textEdit.Value = words;
			_textEdit.Submitted?.Invoke( words );
		}

		protected override void OnMouseDown( MousePanelEvent e )
		{
			base.OnMouseDown( e );

			if ( _canvas.Interact ) return;

			//A press outside the field ends the edit; the field itself takes its own presses.
			if ( _textEdit.IsValid() ) EndTextEdit();

			e.StopPropagation();

			//A tag armed in the palette: this press draws it. The right button puts it down.
			if ( _canvas.Window.Armed is not null )
			{
				if ( e.Button == "mouseright" ) { _canvas.Window.Disarm(); return; }

				if ( e.Button == "mouseleft" && !Application.IsKeyDown( KeyCode.Space ) )
				{
					_drawInto = _canvas.PanelUnder( PointerInRootSpace ) ?? _canvas.Root;
					_drawFrom = MousePosition;
					_draw = new Rect( MousePosition, 0 );
					return;
				}
			}

			//The middle button, or Space held, drags the view.
			if ( e.Button == "mousemiddle" || ( e.Button == "mouseleft" && Application.IsKeyDown( KeyCode.Space ) ) )
			{
				_panFrom = MousePosition;
				return;
			}

			if ( e.Button == "mouseleft" )
			{
				//A guide under the pointer is taken hold of; a press on a ruler pulls a new one off it.
				if ( GuideAt( MousePosition ) is { } held )
				{
					_guideDrag = held;
					return;
				}

				if ( OnRuler( MousePosition, out var top ) )
				{
					var lines = top ? _canvas.GuidesY : _canvas.GuidesX;
					lines.Add( 0 );
					_guideDrag = ( lines, lines.Count - 1 );
					MoveGuide( MousePosition );
					return;
				}

				var window = _canvas.Window;
				var modifiers = Application.KeyboardModifiers;
				var adding = modifiers.HasFlag( KeyboardModifiers.Ctrl ) || modifiers.HasFlag( KeyboardModifiers.Shift );

				//A grip of the selection, or the selection itself, starts a drag; anything else is a click that selects.
				if ( _canvas.Selection is { } selected && window.LocateTag( selected ) is not null )
				{
					if ( GripAt( MousePosition ) is { } grip )
					{
						//The rotate grip turns; a corner with Ctrl held turns too.
						var turn = grip == "rotate" || ( grip.Length == 2 && modifiers.HasFlag( KeyboardModifiers.Ctrl ) );

						_drag = turn
							? CanvasDrag.Rotate( _canvas, selected, MousePosition, SelectionCentre(), this )
							: CanvasDrag.Resize( _canvas, selected, grip, MousePosition, this );
						return;
					}

					var under = _canvas.PanelUnder( PointerInRootSpace );

					//A press on one of the selected moves the whole selection together.
					if ( under is not null && !adding && window.IsInSelection( under ) && CanvasDrag.CanMove( under ) )
					{
						if ( !ReferenceEquals( under, selected ) ) window.SetPrimary( under );

						_drag = CanvasDrag.Move( _canvas, under, MousePosition, this );

						_companions.Clear();
						foreach ( var other in window.Selected )
						{
							if ( ReferenceEquals( other, under ) || window.LocateTag( other ) is null || !CanvasDrag.CanMove( other ) ) continue;
							_companions.Add( CanvasDrag.Move( _canvas, other, MousePosition ) );
						}

						return;
					}
				}

				var hit = _canvas.PanelUnder( PointerInRootSpace );

				//Ctrl or Shift adds to the selection and takes from it.
				if ( adding )
				{
					if ( hit is not null && !ReferenceEquals( hit, _canvas.Root ) ) window.ToggleSelect( hit );
					return;
				}

				//A press on the empty stage, or on the root itself, draws a marquee; what it takes in on release is the selection.
				if ( hit is null || ReferenceEquals( hit, _canvas.Root ) )
				{
					_marqueeFrom = MousePosition;
					_marquee = new Rect( MousePosition, 0 );
				}

				_canvas.Select( hit );
				return;
			}

			if ( e.Button == "mouseright" )
			{
				var hit = _canvas.PanelUnder( PointerInRootSpace );

				if ( hit is not null ) _canvas.Select( hit );
				OpenMenu( hit );
			}
		}

		protected override void OnMouseUp( MousePanelEvent e )
		{
			base.OnMouseUp( e );

			_panFrom = null;

			if ( _guideDrag is not null )
			{
				DropGuide( MousePosition );
				return;
			}

			if ( _marqueeFrom is not null )
			{
				LandMarquee();
				return;
			}

			if ( _drawFrom is not null )
			{
				LandDrawing();
				return;
			}

			if ( _drag is null ) return;

			using ( _canvas.Window.BeginGesture( "drag" ) )
			{
				_drag.Finish();
				_drag = null;

				foreach ( var companion in _companions ) companion.Finish();
				_companions.Clear();
			}

			GuideX.Clear();
			GuideY.Clear();
		}

		//What the marquee took in: every panel whose box lies inside it whole, but not
		//those inside another taken panel - the outermost stands for what is under it.
		void LandMarquee()
		{
			var rect = _marquee;
			_marqueeFrom = null;

			if ( rect.Width < 3 && rect.Height < 3 ) return;

			SelectInside( rect );
		}

		//Select the outermost panels whose boxes lie whole inside a rect of this overlay.
		internal void SelectInside( Rect rect )
		{
			if ( _canvas.Root is not { } root ) return;

			var taken = new List<Panel>();

			void Walk( Panel panel )
			{
				foreach ( var child in panel.Children )
				{
					if ( !child.IsVisible || child.Box is null ) continue;

					var box = RectOf( child.Box.Rect, child );
					var inside = box.Left >= rect.Left && box.Top >= rect.Top && box.Right <= rect.Right && box.Bottom <= rect.Bottom;

					if ( inside && _canvas.Window.LocateTag( child ) is not null ) taken.Add( child );
					else Walk( child );
				}
			}

			Walk( root );

			if ( taken.Count > 0 ) _canvas.Window.SelectMany( taken );
		}

		CanvasDrag _drag;

		//The other selected panels' drags, moving along with the primary's.
		readonly List<CanvasDrag> _companions = new();

		//A marquee being drawn: where it started, in this overlay's units.
		Vector2? _marqueeFrom;
		Rect _marquee;

		//Frames on the selected panels other than the primary, one each, kept between frames.
		readonly List<Panel> _otherBoxes = new();

		//Something carried from the palette or the tree, over the stage: it lands in the panel under the pointer.
		protected override void OnDragEnter( PanelEvent e )
		{
			if ( _canvas.Window.Carrying is not null ) _hovered = _canvas.PanelUnder( PointerInRootSpace );
		}

		protected override void OnDrop( PanelEvent e )
		{
			if ( _canvas.Window.Carrying is null ) return;

			e.StopPropagation();

			var under = _canvas.PanelUnder( PointerInRootSpace ) ?? _canvas.Root;
			if ( under is not null ) _canvas.Window.Drop( under, DropPlace.Into );
		}

		//A pan in progress: where the pointer was last, in this overlay's units.
		Vector2? _panFrom;

		//A guide being placed: which list, and which line in it.
		(List<float> lines, int index)? _guideDrag;

		//The stage pixel under a point of this overlay, on each axis.
		Vector2 StagePoint( Vector2 point )
		{
			var fit = MathF.Max( 0.01f, _canvas.Fit );
			return ( point - _canvas.StageOrigin ) / fit;
		}

		//Whether a point of this overlay is on the top or the left ruler.
		bool OnRuler( Vector2 point, out bool top )
		{
			top = point.y < RulerSize;
			return _canvas.Rulers && ( top || point.x < RulerSize );
		}

		//A guide within a few pixels of a point, if any.
		(List<float> lines, int index)? GuideAt( Vector2 point )
		{
			var fit = MathF.Max( 0.01f, _canvas.Fit );
			var reach = 4.0f / fit;
			var at = StagePoint( point );

			for ( var i = 0; i < _canvas.GuidesX.Count; i++ )
				if ( MathF.Abs( _canvas.GuidesX[i] - at.x ) <= reach ) return ( _canvas.GuidesX, i );

			for ( var i = 0; i < _canvas.GuidesY.Count; i++ )
				if ( MathF.Abs( _canvas.GuidesY[i] - at.y ) <= reach ) return ( _canvas.GuidesY, i );

			return null;
		}

		void MoveGuide( Vector2 point )
		{
			if ( _guideDrag is not { } drag ) return;

			var at = StagePoint( point );
			var value = ReferenceEquals( drag.lines, _canvas.GuidesX ) ? at.x : at.y;

			drag.lines[drag.index] = MathF.Round( value );
		}

		//Let go of a guide: back on a ruler, or off the stage, it is gone.
		void DropGuide( Vector2 point )
		{
			if ( _guideDrag is not { } drag ) return;
			_guideDrag = null;

			var at = StagePoint( point );
			var vertical = ReferenceEquals( drag.lines, _canvas.GuidesX );
			var value = vertical ? at.x : at.y;
			var extent = vertical ? _canvas.StageSize.x : _canvas.StageSize.y;

			if ( OnRuler( point, out _ ) || value < 0 || value > extent )
				drag.lines.RemoveAt( drag.index );

			_canvas.SaveGuides();
		}

		/// <summary>The wheel zooms, at the pointer.</summary>
		public override void OnMouseWheel( Vector2 value )
		{
			var factor = MathF.Pow( 1.15f, -value.y );

			_canvas.ZoomBy( factor, MousePosition );
		}

		//The middle of the selection box, in this overlay's units.
		Vector2 SelectionCentre()
		{
			var rect = _selectionBox.Box.Rect;
			return ( rect.Position + rect.Size * 0.5f - Box.Rect.Position ) * ScaleFromScreen;
		}

		//The grip under a point on this overlay, by name, or null.
		string GripAt( Vector2 point )
		{
			if ( _selectionBox.HasClass( "hidden" ) ) return null;

			foreach ( var child in _selectionBox.Children )
			{
				if ( !child.HasClass( "grip" ) || child.Box is null ) continue;

				//The grip's rect in this overlay's space, with a little slack.
				var rect = new Rect( child.Box.Rect.Position - Box.Rect.Position, child.Box.Rect.Size ) ;
				rect = new Rect( rect.Left * ScaleFromScreen - 3, rect.Top * ScaleFromScreen - 3, rect.Width * ScaleFromScreen + 6, rect.Height * ScaleFromScreen + 6 );

				if ( rect.IsInside( point ) )
					return Grips.FirstOrDefault( child.HasClass );
			}

			return null;
		}

		void OpenMenu( Panel panel )
		{
			if ( panel is null ) return;

			var menu = new Menu( _canvas.HostWidget );
			_canvas.Window.FillMarkupMenu( menu, panel );

			if ( menu.HasOptions || menu.HasMenus ) menu.OpenAtCursor();
		}

		public override void Tick()
		{
			base.Tick();

			if ( _canvas.Window.Carrying is not null )
				_hovered = _canvas.PanelUnder( PointerInRootSpace );
			else if ( !HasHovered || _canvas.Interact || _drag is not null )
				_hovered = null;

			//A press that ended outside the window never sends its release.
			if ( _drag is not null && !Application.MouseButtons.HasFlag( MouseButtons.Left ) )
			{
				_drag.Finish();
				_drag = null;

				GuideX.Clear();
				GuideY.Clear();
			}

			Place( _hoverBox, _hovered, ReferenceEquals( _hovered, _canvas.Selection ) ? null : _hovered );

			if ( _textEdit is not null )
			{
				if ( _textTarget.IsValid() ) PlaceTextEdit();
				else EndTextEdit();
			}

			SetClass( "armed", _canvas.Window.Armed is not null );

			var selection = _canvas.Selection;

			Place( _selectionBox, selection, selection );
			PlaceBoxes( selection );

			//A thin frame on each of the others; the boxes are kept and reused.
			var others = _canvas.Window.Selected.Where( x => !ReferenceEquals( x, selection ) ).ToList();

			while ( _otherBoxes.Count < others.Count ) _otherBoxes.Add( AddChild<Panel>( "box other" ) );

			for ( var i = 0; i < _otherBoxes.Count; i++ )
			{
				var other = i < others.Count ? others[i] : null;
				Place( _otherBoxes[i], other, other );
			}

			if ( selection.IsValid() )
			{
				var size = selection.Box.Rect.Size * selection.ScaleFromScreen;
				var control = _canvas.Window.LocateTag( selection )?.Control;

				_readout.Text = control is null ? $"{size.x:0} × {size.y:0}" : $"{size.x:0} × {size.y:0}  @{control}";
			}
		}

		//Put a frame around a panel, or hide it.
		void Place( Panel box, Panel target, Panel show )
		{
			if ( show is null || !target.IsValid() || target.Box is null )
			{
				box.SetClass( "hidden", true );
				return;
			}

			var rect = RectOf( target.Box.Rect, target );

			box.Style.Left = Length.Pixels( rect.Left );
			box.Style.Top = Length.Pixels( rect.Top );
			box.Style.Width = Length.Pixels( rect.Width );
			box.Style.Height = Length.Pixels( rect.Height );
			box.SetClass( "hidden", false );
		}

		//The margin and padding of the selection, shaded the way a browser shades them.
		void PlaceBoxes( Panel target )
		{
			if ( !target.IsValid() || target.Box is null )
			{
				_marginBox.SetClass( "hidden", true );
				_paddingBox.SetClass( "hidden", true );
				return;
			}

			var outer = RectOf( target.Box.RectOuter, target );
			var inner = RectOf( target.Box.RectInner, target );

			Fill( _marginBox, outer );
			Fill( _paddingBox, inner );
		}

		static void Fill( Panel box, Rect rect )
		{
			box.Style.Left = Length.Pixels( rect.Left );
			box.Style.Top = Length.Pixels( rect.Top );
			box.Style.Width = Length.Pixels( rect.Width );
			box.Style.Height = Length.Pixels( rect.Height );
			box.SetClass( "hidden", false );
		}

		//A rect the panel was laid out with, taken through the stage's scale into this overlay's own layout space.
		Rect RectOf( Rect laidOut, Panel target )
		{
			//All four corners through the transforms, and the box around them: a turned panel's frame is the box it turns inside, not two of its corners joined.
			var local = laidOut.Position - target.Box.Rect.Position;
			var a = target.PanelPositionToScreenPosition( local );
			var b = target.PanelPositionToScreenPosition( local + new Vector2( laidOut.Width, 0 ) );
			var c = target.PanelPositionToScreenPosition( local + laidOut.Size );
			var d = target.PanelPositionToScreenPosition( local + new Vector2( 0, laidOut.Height ) );

			var left = MathF.Min( MathF.Min( a.x, b.x ), MathF.Min( c.x, d.x ) );
			var top = MathF.Min( MathF.Min( a.y, b.y ), MathF.Min( c.y, d.y ) );
			var right = MathF.Max( MathF.Max( a.x, b.x ), MathF.Max( c.x, d.x ) );
			var bottom = MathF.Max( MathF.Max( a.y, b.y ), MathF.Max( c.y, d.y ) );

			var origin = Box.Rect.Position;
			var scale = ScaleFromScreen;

			return new Rect(
				( left - origin.x ) * scale,
				( top - origin.y ) * scale,
				( right - left ) * scale,
				( bottom - top ) * scale );
		}
	}
}

//A canvas move/resize drag: previews via inline style, then writes left/top/width/height to the sheet
internal sealed class CanvasDrag
{
	readonly DesignCanvasPanel _canvas;
	readonly Panel _panel;
	readonly string _grip;
	readonly Vector2 _from;

	//The panel's box in its parent's padding box, in style pixels, when the drag began.
	readonly float _left, _top, _width, _height, _right, _bottom;
	readonly Margin _margin;

	//Which side the horizontal and vertical positions are written from - or both, for a panel stretched between its parent's edges.
	readonly bool _fromRight, _fromBottom;
	readonly bool _stretchedX, _stretchedY;
	readonly bool _absolute;

	//What has been given, and will be written.
	readonly Dictionary<string, float> _values = new();

	//The edges a drag can snap to, in the containing block's pixels: the parent's own and every sibling's, with the middles.
	readonly List<float> _snapX = new();
	readonly List<float> _snapY = new();

	//Where the containing block is in the stage, for the guides.
	readonly Vector2 _blockInStage;
	readonly DesignCanvasPanel.Overlay _overlay;

	//How near an edge has to be, in the stage's pixels at the current fit, to be taken.
	const float SnapReach = 6.0f;

	//A turn: around this point of the overlay, from this angle, by the pointer's angle since.
	readonly bool _turning;
	readonly Vector2 _pivot;
	readonly float _angleFrom;
	readonly float _rotationFrom;

	CanvasDrag( DesignCanvasPanel canvas, Panel panel, string grip, Vector2 from, DesignCanvasPanel.Overlay overlay, Vector2? pivot = null )
	{
		_canvas = canvas;
		_panel = panel;
		_grip = grip;
		_from = from;
		_overlay = overlay;

		if ( pivot is { } centre )
		{
			_turning = true;
			_pivot = centre;
			_angleFrom = MathF.Atan2( from.y - centre.y, from.x - centre.x );
			_rotationFrom = RotationOf( panel );
		}

		var style = panel.ComputedStyle;
		var scale = panel.ScaleFromScreen;

		_absolute = style?.Position is PositionMode.Absolute;

		var box = panel.Box.Rect;
		var parent = panel.Parent?.Box;

		//The containing block: the parent's padding box, where its content starts - which is scrolled up by
		//however far the parent has scrolled, since a child's box already carries that scroll and left does not.
		var scroll = ( panel.Parent?.ScrollOffset ?? Vector2.Zero ) * panel.ScaleToScreen;
		var originX = parent is null ? 0 : parent.Rect.Left + parent.Border.Left - scroll.x;
		var originY = parent is null ? 0 : parent.Rect.Top + parent.Border.Top - scroll.y;
		var extentX = parent is null ? box.Right : parent.Rect.Right - parent.Border.Right - scroll.x;
		var extentY = parent is null ? box.Bottom : parent.Rect.Bottom - parent.Border.Bottom - scroll.y;

		_left = ( box.Left - originX ) * scale;
		_top = ( box.Top - originY ) * scale;
		_right = ( extentX - box.Right ) * scale;
		_bottom = ( extentY - box.Bottom ) * scale;
		_width = box.Width * scale;
		_height = box.Height * scale;

		//The layout puts left between the parent's padding edge and the panel's margin, then
		//the margin: what is written is the border box's edge less the margin on that side.
		var outer = panel.Box.RectOuter;
		_margin = new Margin(
			( box.Left - outer.Left ) * scale,
			( box.Top - outer.Top ) * scale,
			( outer.Right - box.Right ) * scale,
			( outer.Bottom - box.Bottom ) * scale );

		//Anchored from the far side when the rule says so and not the near one; from both when it says both, which is a stretch.
		_fromRight = style is not null && style.Right.IsSet() && !style.Left.IsSet();
		_fromBottom = style is not null && style.Bottom.IsSet() && !style.Top.IsSet();
		_stretchedX = _absolute && style.Left.IsSet() && style.Right.IsSet();
		_stretchedY = _absolute && style.Top.IsSet() && style.Bottom.IsSet();

		//What there is to line up with: the block's own edges and middle, and each sibling's, all in the block's pixels.
		var blockWidth = ( extentX - originX ) * scale;
		var blockHeight = ( extentY - originY ) * scale;

		_snapX.AddRange( new[] { 0, blockWidth * 0.5f, blockWidth } );
		_snapY.AddRange( new[] { 0, blockHeight * 0.5f, blockHeight } );

		if ( panel.Parent is { } p )
		{
			foreach ( var sibling in p.Children )
			{
				if ( ReferenceEquals( sibling, panel ) || !sibling.IsVisible || sibling.Box is null ) continue;

				var r = sibling.Box.Rect;
				_snapX.AddRange( new[] { ( r.Left - originX ) * scale, ( r.Left + r.Width * 0.5f - originX ) * scale, ( r.Right - originX ) * scale } );
				_snapY.AddRange( new[] { ( r.Top - originY ) * scale, ( r.Top + r.Height * 0.5f - originY ) * scale, ( r.Bottom - originY ) * scale } );
			}
		}

		//The block's place in the stage, for drawing a guide where it is.
		var stage = canvas.Root?.Parent?.Box?.Rect ?? default;
		_blockInStage = new Vector2( ( originX - stage.Left ) * scale, ( originY - stage.Top ) * scale );

		//The rulers' guides are lines to hold to as well, brought into the block's pixels.
		foreach ( var x in canvas.GuidesX ) _snapX.Add( x - _blockInStage.x );
		foreach ( var y in canvas.GuidesY ) _snapY.Add( y - _blockInStage.y );
	}

	public static bool CanMove( Panel panel ) => panel.IsValid();

	public static CanvasDrag Move( DesignCanvasPanel canvas, Panel panel, Vector2 from, DesignCanvasPanel.Overlay overlay = null ) => new( canvas, panel, null, from, overlay );

	/// <summary>A turn of the panel around the middle of its box, written as <c>transform: rotate</c>.</summary>
	public static CanvasDrag Rotate( DesignCanvasPanel canvas, Panel panel, Vector2 from, Vector2 pivot, DesignCanvasPanel.Overlay overlay = null ) => new( canvas, panel, "rotate", from, overlay, pivot );

	//The turn a panel's rule gives it, in degrees, from its computed transform.
	static float RotationOf( Panel panel )
	{
		if ( panel.ComputedStyle?.Transform is not { } transform || transform.List is null ) return 0;

		foreach ( var entry in transform.List )
		{
			if ( entry.Type == PanelTransform.EntryType.Rotation ) return entry.Data.z;
		}

		return 0;
	}

	public static CanvasDrag Resize( DesignCanvasPanel canvas, Panel panel, string grip, Vector2 from, DesignCanvasPanel.Overlay overlay = null ) => new( canvas, panel, grip, from, overlay );

	/// <summary>The pointer moved: the same distance in the stage's pixels, snapped, given to the panel.</summary>
	public void MoveTo( Vector2 pointer )
	{
		if ( _turning )
		{
			var angle = MathF.Atan2( pointer.y - _pivot.y, pointer.x - _pivot.x );
			var degrees = _rotationFrom + ( angle - _angleFrom ) * 180.0f / MathF.PI;

			//Fifteens with Shift; whole degrees otherwise.
			var by = Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) ? 15.0f : 1.0f;
			degrees = MathF.Round( degrees / by ) * by;
			degrees = ( ( degrees % 360 ) + 360 ) % 360;

			_values.Clear();
			_values["rotate"] = degrees;

			var transform = new PanelTransform();
			transform.AddRotation( 0, 0, degrees );
			_panel.Style.Transform = transform;
			return;
		}

		var delta = ( pointer - _from ) / MathF.Max( 0.01f, _canvas.Fit );

		//Whole pixels; the grid's step when it is on, or eights with Shift. The grid is the stage's,
		//so a value is snapped where it lands on the stage, not where it lands in its parent.
		var grid = _canvas.Grid || Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift );
		var step = grid ? DesignCanvasPanel.GridStep : 1.0f;

		float SnapX( float v ) => MathF.Round( ( v + _blockInStage.x ) / step ) * step - _blockInStage.x;
		float SnapY( float v ) => MathF.Round( ( v + _blockInStage.y ) / step ) * step - _blockInStage.y;
		float Snap( float v ) => MathF.Round( v / step ) * step;

		//Held down, Alt is a drag that lines up with nothing.
		var reach = Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Alt ) ? 0 : SnapReach / MathF.Max( 0.01f, _canvas.Fit );

		_overlay?.GuideX.Clear();
		_overlay?.GuideY.Clear();

		_values.Clear();

		if ( _grip is null )
		{
			//Taken out of the flow by the drag: said once, with the place it had.
			if ( !_absolute ) _values["position"] = float.NaN;

			//The box as moved, then its edges pulled to whatever is near.
			var left = _left + delta.x;
			var top = _top + delta.y;

			var dx = Nearest( _snapX, left, left + _width * 0.5f, left + _width, reach, out var guideX );
			var dy = Nearest( _snapY, top, top + _height * 0.5f, top + _height, reach, out var guideY );

			left = SnapX( left + dx );
			top = SnapY( top + dy );

			if ( guideX is { } gx ) _overlay?.GuideX.Add( _blockInStage.x + gx );
			if ( guideY is { } gy ) _overlay?.GuideY.Add( _blockInStage.y + gy );

			//A stretched panel moves both its edges, so its size stays.
			if ( _stretchedX ) { _values["left"] = left; _values["right"] = Snap( _right - ( left - _left ) ); }
			else if ( _fromRight ) _values["right"] = Snap( _right - ( left - _left ) );
			else _values["left"] = left;

			if ( _stretchedY ) { _values["top"] = top; _values["bottom"] = Snap( _bottom - ( top - _top ) ); }
			else if ( _fromBottom ) _values["bottom"] = Snap( _bottom - ( top - _top ) );
			else _values["top"] = top;
		}
		else
		{
			var east = _grip.Contains( 'e' );
			var west = _grip.Contains( 'w' );
			var south = _grip.Contains( 's' );
			var north = _grip.Contains( 'n' );

			//The far edge is what snaps to the grid, and the size follows from it - or, on a panel stretched
			//between its parent's edges, the inset from that edge does, since that is what the rule says.
			if ( east )
			{
				var right = SnapX( _left + _width + delta.x );
				if ( _stretchedX ) _values["right"] = _right - ( right - _left - _width );
				else _values["width"] = MathF.Max( 1, right - _left );
			}

			if ( south )
			{
				var bottom = SnapY( _top + _height + delta.y );
				if ( _stretchedY ) _values["bottom"] = _bottom - ( bottom - _top - _height );
				else _values["height"] = MathF.Max( 1, bottom - _top );
			}

			if ( west )
			{
				var left = SnapX( _left + delta.x );

				if ( _stretchedX ) _values["left"] = left;
				else
				{
					_values["width"] = MathF.Max( 1, _left + _width - left );

					//The right edge stays: the left one moves with the pointer.
					if ( _absolute && !_fromRight ) _values["left"] = left;
				}
			}

			if ( north )
			{
				var top = SnapY( _top + delta.y );

				if ( _stretchedY ) _values["top"] = top;
				else
				{
					_values["height"] = MathF.Max( 1, _top + _height - top );

					if ( _absolute && !_fromBottom ) _values["top"] = top;
				}
			}
		}

		//The edges as the rule says them: less the margin on each side.
		if ( _values.ContainsKey( "left" ) ) _values["left"] -= _margin.Left;
		if ( _values.ContainsKey( "top" ) ) _values["top"] -= _margin.Top;
		if ( _values.ContainsKey( "right" ) ) _values["right"] -= _margin.Right;
		if ( _values.ContainsKey( "bottom" ) ) _values["bottom"] -= _margin.Bottom;

		foreach ( var (name, value) in _values )
			SetInline( name, value );
	}

	//The value written for a name: a length in pixels, the word for position, a turn for rotate.
	static string Written( string name, float value ) => name switch
	{
		"position" => "absolute",
		"rotate" => $"rotate( {value:0.#}deg )",
		_ => $"{value:0.##}px",
	};

	//The property a name is written under: a turn goes under transform.
	static string PropertyOf( string name ) => name == "rotate" ? "transform" : name;

	//How far the nearest of three edges - the box's start, middle and end - is from
	//a line it could hold to, when one is within reach; and which line that is.
	static float Nearest( List<float> lines, float start, float middle, float end, float reach, out float? line )
	{
		line = null;
		if ( reach <= 0 ) return 0;

		var best = reach;
		var shift = 0.0f;

		foreach ( var candidate in lines )
		{
			foreach ( var edge in new[] { start, middle, end } )
			{
				var d = candidate - edge;

				if ( MathF.Abs( d ) < MathF.Abs( best ) || ( MathF.Abs( d ) == MathF.Abs( best ) && line is null ) )
				{
					if ( MathF.Abs( d ) > reach ) continue;

					best = d;
					shift = d;
					line = candidate;
				}
			}
		}

		return shift;
	}

	/// <summary>The pointer let go: what the panel wears now goes into the sheet.</summary>
	public void Finish()
	{
		if ( _values.Count == 0 ) return;

		var declarations = _values.Select( x => ( PropertyOf( x.Key ), Written( x.Key, x.Value ) ) ).ToList();

		if ( !_canvas.Window.WriteStyles( _panel, declarations ) ) return;

		//The sheet already has the values - NoteSheetWritten pushed the file in - so the inline
		//copy can go now. Waiting for the watcher left a window where the old rules showed through.
		foreach ( var name in _values.Keys )
			ClearInline( _panel, PropertyOf( name ) );
	}

	void SetInline( string name, float value )
	{
		var length = Length.Pixels( value );

		switch ( name )
		{
			case "position": _panel.Style.Position = PositionMode.Absolute; break;
			case "left": _panel.Style.Left = length; break;
			case "top": _panel.Style.Top = length; break;
			case "right": _panel.Style.Right = length; break;
			case "bottom": _panel.Style.Bottom = length; break;
			case "width": _panel.Style.Width = length; break;
			case "height": _panel.Style.Height = length; break;
		}
	}

	/// <summary>Take an inline value off a panel, so the sheet's stands.</summary>
	public static void ClearInline( Panel panel, string name )
	{
		if ( !panel.IsValid() ) return;

		switch ( name )
		{
			case "position": panel.Style.Position = null; break;
			case "transform": panel.Style.Transform = null; break;
			case "left": panel.Style.Left = null; break;
			case "top": panel.Style.Top = null; break;
			case "right": panel.Style.Right = null; break;
			case "bottom": panel.Style.Bottom = null; break;
			case "width": panel.Style.Width = null; break;
			case "height": panel.Style.Height = null; break;
		}
	}
}
