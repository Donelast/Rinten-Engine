using Rinten.Tiles;

namespace Editor.Tiles;

//What it is painting - which map, which layer, which tile, which terrain - is static, because the brushes and the sidebar are separate objects looking at the same thing. None of the painting is here: every stroke ends in the map's own layer or TilePainter, which a game can call just the same.
/// <summary>Painting a <see cref="TileMap"/>: a mode of its own, the way the terrain brush is, with the sheet to pick tiles off in the sidebar.</summary>
[EditorTool]
[Title( "Tile Map" )]
[Icon( "grid_on" )]
[Alias( "tilemap" )]
[Group( "Scene" )]
public sealed class TileMapTool : EditorTool
{
	/// <summary>The map being painted.</summary>
	public static TileMap Map { get; set; }

	/// <summary>Which of its layers.</summary>
	public static int LayerIndex { get; set; }

	/// <summary>The tile a brush puts down, counted from one.</summary>
	public static int Tile { get; set; } = 1;

	/// <summary>The terrain the terrain brush lays, by name; the first of the set's where none is chosen.</summary>
	public static string TerrainName { get; set; }

	/// <summary>The layer being painted, or null.</summary>
	public static TileMapLayer Layer => Map.IsValid() ? Map.Layer( LayerIndex ) : null;

	public override IEnumerable<EditorTool> GetSubtools()
	{
		yield return new TilePaintBrush();
		yield return new TileRectangleBrush();
		yield return new TileFillBrush();
		yield return new TileTerrainBrush();
		yield return new TilePickBrush();
	}

	public override void OnEnabled()
	{
		AllowGameObjectSelection = false;
		FindMap();
	}

	public override void OnSelectionChanged() => FindMap();

	public override Panel CreateToolSidebarPanel() => new TileMapToolPanel( this );

	//Finds the map to paint: selected, current, else first; skips maps built from code at runtime
	private void FindMap()
	{
		var selected = GetSelectedComponent<TileMap>();
		var before = Map;

		if ( selected.IsValid() && IsKept( selected ) )
		{
			Map = selected;
		}
		else if ( !Map.IsValid() || Map.Scene != Scene || !IsKept( Map ) )
		{
			Map = Scene?.GetAllComponents<TileMap>().FirstOrDefault( IsKept );
		}

		//Another map starts on its first layer, the one a map is laid out with - not whichever place the
		//last map's layer had, which on a map of other layers painted and rubbed out on the wrong one.
		if ( Map != before ) LayerIndex = 0;

		if ( Map.IsValid() && ( LayerIndex < 0 || LayerIndex >= Map.Layers.Count ) ) LayerIndex = 0;
	}

	/// <summary>Whether a map is saved with the scene: nothing it hangs under is marked not to be.</summary>
	public static bool IsKept( TileMap map )
	{
		if ( !map.IsValid() ) return false;

		for ( var go = map.GameObject; go.IsValid(); go = go.Parent )
		{
			if ( go.Flags.Contains( GameObjectFlags.NotSaved ) ) return false;
		}

		return true;
	}

	//A map in the scene that is built by code and so is not painted, for the sidebar to name.
	internal TileMap MadeByCode => Scene?.GetAllComponents<TileMap>().FirstOrDefault( m => !IsKept( m ) );

	/// <summary>The tile set a new map starts with; the project's first where none is chosen.</summary>
	public static TileSet NewMapSet { get; set; }

	//The project's tile sets, as assets - loaded to be offered, not before.
	internal static List<(Asset Asset, TileSet Set)> ProjectTileSets()
	{
		var sets = new List<(Asset, TileSet)>();

		foreach ( var asset in AssetSystem.All.Where( a => a.AssetType?.FileExtension == "tileset" ).OrderBy( a => a.Path ) )
		{
			if ( asset.LoadResource<TileSet>() is { } set ) sets.Add( (asset, set) );
		}

		return sets;
	}

	//Paint the layer from another tile set - its numbers now mean that set's tiles.
	internal void SetLayerTileSet( TileSet set )
	{
		var layer = Layer;
		if ( layer is null || ReferenceEquals( layer.TileSet, set ) ) return;

		using var scope = SceneEditorSession.Active.UndoScope( "Tile Layer Set" ).WithComponentChanges( Map ).Push();

		layer.TileSet = set;
	}

	//A map with one layer, when the scene has none - somebody who has just picked the tool should not have to build one by hand before it does anything.
	internal void CreateMap( TileSet set )
	{
		if ( Scene is null ) return;

		using var scope = SceneEditorSession.Active.UndoScope( "New Tile Map" ).WithGameObjectCreations().Push();

		var go = Scene.CreateObject();
		go.Name = "Tile Map";

		var map = go.Components.Create<TileMap>();
		map.AddLayer( "Ground", set ?? NewMapSet ?? ProjectTileSets().Select( x => x.Set ).FirstOrDefault() );

		Map = map;
		LayerIndex = 0;
		Selection.Set( go );
	}

	//Another layer in front of the others, on the same set as the one being painted.
	internal void AddLayer()
	{
		if ( !Map.IsValid() ) return;

		using var scope = SceneEditorSession.Active.UndoScope( "New Tile Layer" ).WithComponentChanges( Map ).Push();

		Map.AddLayer( $"Layer {Map.Layers.Count + 1}", Layer?.TileSet );
		LayerIndex = Map.Layers.Count - 1;
	}
}

/// <summary>What every tile brush shares: which cell the pointer is over, the outline under it, and the press-drag-release that becomes a stroke. Left paints, Ctrl and left rubs out.</summary>
public abstract class TileBrush : EditorTool
{
	protected static TileMap Map => TileMapTool.Map;
	protected static TileMapLayer Layer => TileMapTool.Layer;

	//Which cell the pointer is over.
	protected Vector2Int Hover { get; private set; }

	//Whether it is over the map's plane at all.
	protected bool Hovering { get; private set; }

	//Where the drag started.
	protected Vector2Int From { get; private set; }

	protected bool Dragging { get; private set; }

	//Whether the stroke is made when the button comes back up, over the area dragged.
	protected virtual bool DragsArea => false;

	//What happens in a cell.
	protected abstract void Apply( TileMapLayer layer, Vector2Int cell, bool erase );

	//What happens over an area, where DragsArea.
	protected virtual void ApplyArea( TileMapLayer layer, Vector2Int from, Vector2Int to, bool erase ) { }

	//Whether a stroke changes the map. The picker only reads it, so it keeps no undo step - there was one for every click, each undoing nothing.
	protected virtual bool Edits => true;

	private bool wasDown;
	private bool erasing;
	private Vector2Int lastApplied;

	//The undo step of the stroke being made: open from the press to the release, so a drag across twenty cells is one step and not twenty.
	private IDisposable strokeScope;

	public override void OnEnabled() => AllowGameObjectSelection = false;

	public override void OnDisabled() => EndStroke();

	public override void OnUpdate()
	{
		var layer = Layer;

		if ( layer is null )
		{
			Hovering = false;
			EndStroke();
			return;
		}

		UpdateHover();
		DrawGrid();

		var down = Gizmo.IsLeftMouseDown;

		if ( Hovering && down && !wasDown )
		{
			From = Hover;
			Dragging = true;
			erasing = Gizmo.IsCtrlPressed;

			if ( Edits )
			{
				strokeScope = SceneEditorSession.Active.UndoScope( erasing ? "Erase Tiles" : "Paint Tiles" )
					.WithComponentChanges( Map ).Push();
			}

			if ( !DragsArea ) Stroke( layer, From, Hover );
		}
		else if ( Hovering && down && Dragging && !DragsArea && Hover != lastApplied )
		{
			//Every cell between this one and the last, or a quick drag leaves gaps.
			Stroke( layer, lastApplied, Hover );
		}

		if ( Dragging && !down )
		{
			if ( DragsArea && Hovering ) Stroke( layer, From, Hover );

			Dragging = false;
			EndStroke();
		}

		wasDown = down;
	}

	//The stroke is over: what it changed becomes one undo step, or none if it changed nothing.
	private void EndStroke()
	{
		strokeScope?.Dispose();
		strokeScope = null;
	}

	private void Stroke( TileMapLayer layer, Vector2Int from, Vector2Int to )
	{
		if ( DragsArea )
		{
			ApplyArea( layer, from, to, erasing );
		}
		else
		{
			foreach ( var cell in TilePainter.Line( from, to ) ) Apply( layer, cell, erasing );
		}

		lastApplied = to;
	}

	private void UpdateHover()
	{
		var map = Map;
		var ray = Gizmo.CurrentRay;

		var transform = map.WorldTransform;
		var normal = transform.Rotation.Backward;
		var facing = Vector3.Dot( ray.Forward, normal );

		if ( MathF.Abs( facing ) < 0.0001f )
		{
			Hovering = false;
			return;
		}

		var distance = Vector3.Dot( transform.Position - ray.Position, normal ) / facing;

		if ( distance < 0.0f )
		{
			Hovering = false;
			return;
		}

		Hover = map.WorldToCell( ray.Position + ray.Forward * distance );
		Hovering = true;
	}

	//The cell - or the area being dragged - outlined on the map's plane.
	private void DrawGrid()
	{
		if ( !Hovering ) return;

		var map = Map;
		var area = DragsArea && Dragging ? TilePainter.Between( From, Hover ) : new RectInt( Hover.x, Hover.y, 1, 1 );
		var size = map.CellSize;

		var min = new Vector3( area.Left * size.x, area.Top * size.y, 0.05f );
		var max = new Vector3( ( area.Left + area.Width ) * size.x, ( area.Top + area.Height ) * size.y, 0.05f );

		using ( Gizmo.Scope( "tile-hover", map.WorldTransform ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.Color = Gizmo.IsCtrlPressed ? Color.Red : Gizmo.Colors.Active;

			Gizmo.Draw.Line( min, new Vector3( max.x, min.y, min.z ) );
			Gizmo.Draw.Line( new Vector3( max.x, min.y, min.z ), max );
			Gizmo.Draw.Line( max, new Vector3( min.x, max.y, min.z ) );
			Gizmo.Draw.Line( new Vector3( min.x, max.y, min.z ), min );
		}
	}
}

/// <summary>One cell at a time, dragged.</summary>
[Title( "Paint" )]
[Icon( "brush" )]
[Alias( "tilemap.paint" )]
public sealed class TilePaintBrush : TileBrush
{
	protected override void Apply( TileMapLayer layer, Vector2Int cell, bool erase )
	{
		if ( erase ) layer.Erase( cell );
		else layer.Set( cell, TileMapTool.Tile );
	}
}

/// <summary>Corner to corner, filled when the button comes up.</summary>
[Title( "Rectangle" )]
[Icon( "crop_square" )]
[Alias( "tilemap.rectangle" )]
public sealed class TileRectangleBrush : TileBrush
{
	protected override bool DragsArea => true;

	protected override void Apply( TileMapLayer layer, Vector2Int cell, bool erase ) { }

	protected override void ApplyArea( TileMapLayer layer, Vector2Int from, Vector2Int to, bool erase )
		=> TilePainter.Rect( layer, from, to, erase ? 0 : TileMapTool.Tile );
}

/// <summary>Everything joined to the cell that holds what it holds.</summary>
[Title( "Fill" )]
[Icon( "format_color_fill" )]
[Alias( "tilemap.fill" )]
public sealed class TileFillBrush : TileBrush
{
	protected override void Apply( TileMapLayer layer, Vector2Int cell, bool erase )
		=> TilePainter.Flood( layer, cell, erase ? 0 : TileMapTool.Tile );
}

/// <summary>The terrain, fitting itself and its neighbours to each other as it goes.</summary>
[Title( "Terrain" )]
[Icon( "terrain" )]
[Alias( "tilemap.terrain" )]
public sealed class TileTerrainBrush : TileBrush
{
	protected override void Apply( TileMapLayer layer, Vector2Int cell, bool erase )
	{
		var set = layer.TileSet;
		var terrain = set?.Terrain( TileMapTool.TerrainName ) ?? set?.Terrains?.FirstOrDefault();

		//Without a terrain there is nothing to lay - the sidebar says so. It used to paint the chosen tile instead, and was the Paint brush by another name.
		if ( terrain is null ) return;

		if ( erase ) TilePainter.EraseTerrain( layer, cell, terrain );
		else TilePainter.Terrain( layer, cell, terrain );
	}
}

/// <summary>Takes the tile under the pointer as the one to paint with.</summary>
[Title( "Pick" )]
[Icon( "colorize" )]
[Alias( "tilemap.pick" )]
public sealed class TilePickBrush : TileBrush
{
	protected override bool Edits => false;

	protected override void Apply( TileMapLayer layer, Vector2Int cell, bool erase )
	{
		var tile = layer.GetTile( cell );
		if ( tile > 0 ) TileMapTool.Tile = tile;
	}
}
