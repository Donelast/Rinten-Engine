using Rinten.Rendering;
using Rinten.UI;

namespace Editor.SpriteEditor;

//The sheet, with the grid the settings describe drawn over it and the picked cells marked. Cells
//are picked by pressing or dragging over them, and unpicked the same way with the right button.
internal sealed class SpritesheetSheetView : SceneRenderingPanel
{
	private static readonly Color GridColor = Color.White;
	private static readonly Color PickedColor = Color.Yellow;

	/// <summary>How the sheet is cut. Handed in by the importer before the view is drawn.</summary>
	public SpritesheetImportSettings Settings { get; set; }

	/// <summary>Which cells are picked. Handed in by the importer before the view is drawn.</summary>
	public SpritesheetSelection Selection { get; set; }

	private readonly SpriteRenderer _sheet;

	private Vector2 _textureSize;
	private SheetGeometry _geometry;

	private bool _dragging;
	private bool _unpicking;

	/// <summary>How the sheet is sampled - the same as the editor's preview.</summary>
	public FilterMode Filter { get; set; } = FilterMode.Trilinear;

	/// <summary>The sheet's size in pixels, or zero before one is set.</summary>
	public Vector2 TextureSize => _textureSize;

	//No arguments: it stands in the importer's markup, which hands it the settings and the selection.
	public SpritesheetSheetView()
	{
		AddClass( "sheet-view" );

		Scene = Scene.CreateEditorScene();

		using ( Scene.Push() )
		{
			Camera = FlatSceneParts.AddOrthographicCamera();

			//Straight on: the sheet lies in the world's own x and y at z zero and the camera stands off it along +z, which is the way it faces.
			Camera.WorldRotation = Rotation.Identity;
			Camera.WorldPosition = new Vector3( 0, 0, 115 );

			var light = Camera.GameObject.Components.Create<AmbientLight>();
			light.Color = Color.White;

			_sheet = FlatSceneParts.AddSprite( "sheet", Vector3.Zero );
			FlatSceneParts.AddBackdrop( new Vector3( 0, 0, -1 ) );
		}

		OnPreFrame += Frame;
	}

	public override void OnDeleted()
	{
		OnPreFrame -= Frame;

		base.OnDeleted();

		Scene?.Destroy();
		Scene = null;
	}

	public void SetTexture( Texture texture )
	{
		if ( texture is null ) return;

		_textureSize = new Vector2( texture.Width, texture.Height );

		_sheet.Sprite = Sprite.FromTexture( texture );
		_sheet.PlayAnimation( "Default" );
	}

	//Once a frame, with the gizmo scope open: the scene ticks, and the grid is drawn over it.
	private void Frame()
	{
		if ( !Scene.IsValid() || Settings is null || Selection is null ) return;

		_sheet.TextureFilter = Filter;

		Scene.EditorTick( RealTime.Now, RealTime.Delta );

		_geometry = new SheetGeometry( Settings, _textureSize, FlatSceneParts.ViewHeight );
		if ( !_geometry.IsValid ) return;

		UpdateGizmoInputs( IsUnderMouse );

		//Drawn over the picture, into this view's gizmo frame.
		for ( var row = 0; row < _geometry.Rows; row++ )
			for ( var column = 0; column < _geometry.Columns; column++ )
				DrawCell( column, row );
	}

	private void DrawCell( int column, int row )
	{
		var cell = _geometry.Cell( column, row );

		DrawBox( cell, GridColor.WithAlpha( 0.35f ) );

		var order = Selection.IndexOf( new Vector2Int( column, row ) );
		if ( order < 0 ) return;

		//Picked: a second outline just inside the first, and its number in the order.
		var inset = _geometry.CellExtent * 0.06f;

		DrawBox( cell.Shrink( inset ), PickedColor );

		var label = new Vector3( cell.Left + inset * 4f, -(cell.Top + inset * 4f), Above );
		Gizmo.Draw.Text( label, $"{order + 1}", 18, TextFlag.Center, PickedColor, overlay: true );
	}

	//How far in front of the sheet the grid is drawn, towards the camera. On the sheet's own plane
	//the picture was drawn over it, and the grid and the picked cells were not there to see at all.
	private const float Above = 1.0f;

	//A cell's outline on the plane, its top on the picture being its top on screen.
	private static void DrawBox( Rect cell, Color color )
	{
		var left = cell.Left;
		var right = cell.Right;
		var top = -cell.Top;
		var bottom = -cell.Bottom;

		Span<Vector3> ring =
		[
			new( left, top, Above ), new( right, top, Above ),
			new( right, top, Above ), new( right, bottom, Above ),
			new( right, bottom, Above ), new( left, bottom, Above ),
			new( left, bottom, Above ), new( left, top, Above ),
		];

		Gizmo.Draw.Lines( ring, color, overlay: true );
	}

	//Picking

	//The cell under the pointer, or null.
	private Vector2Int? CellUnderPointer()
	{
		if ( !_geometry.IsValid || Camera is null ) return null;

		var ray = GetRay( FromScreen( Application.CursorPosition ) );

		if ( MathF.Abs( ray.Forward.z ) < 0.0001f ) return null;

		var distance = -ray.Position.z / ray.Forward.z;
		var onPlane = ray.Position + ray.Forward * distance;

		return _geometry.CellAt( new Vector2( onPlane.x, onPlane.y ) );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton != MouseButtons.Left && e.MouseButton != MouseButtons.Right ) return;

		_dragging = true;
		_unpicking = e.MouseButton == MouseButtons.Right;

		Touch();
		e.StopPropagation();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( _dragging ) Touch();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		_dragging = false;
	}

	//Picks or unpicks whatever cell the pointer is on.
	private void Touch()
	{
		if ( Selection is null || CellUnderPointer() is not { } cell ) return;

		if ( _unpicking ) Selection.Deselect( cell );
		else Selection.Select( cell );
	}
}
