using HalfEdgeMesh;

namespace Editor.MeshEditor;

partial class VertexTool
{
	public enum MergeRange
	{
		[Description( "Merge all vertices regardless of distance." )]
		Infinite,
		[Description( "Merge vertices within the current grid spacing." )]
		Grid,
		[Description( "Merge vertices within a fixed distance." )]
		Fixed,
	}

	//No label: the three buttons say what they are, and the row is the Merge group's.
	[WideMode( HasLabel = false )]
	public MergeRange MergeRangeMode
	{
		get => _mergeRangeMode;
		set
		{
			_mergeRangeMode = value;
			EditorCookie.Set( "VertexTool.MergeRange", value );
		}
	}
	MergeRange _mergeRangeMode = EditorCookie.Get( "VertexTool.MergeRange", MergeRange.Infinite );

	/// <summary>Fixed merge distance.</summary>
	[Step( 0.1f ), WideMode( HasLabel = false )]
	public float MergeDistance
	{
		get => _mergeDistance;
		set
		{
			_mergeDistance = value;
			EditorCookie.Set( "VertexTool.MergeDistance", value );
		}
	}
	float _mergeDistance = EditorCookie.Get( "VertexTool.MergeDistance", 0.1f );

	public override Panel CreateToolSidebarPanel()
	{
		return new VertexSelectionPanel( GetSerializedSelection(), this );
	}

	public class VertexSelectionPanel : ToolSettingsPanel
	{
		private readonly MeshVertex[] _vertices;
		private readonly List<IGrouping<MeshComponent, MeshVertex>> _vertexGroups;
		private readonly List<MeshComponent> _components;
		readonly VertexTool _tool;
		SidebarButtonPanel _connectButton;
		string _connectToolTip;

		//Why the selection can't be connected, or null if it can.
		string _connectError;

		//The fixed merge distance, which only means anything while the range is Fixed.
		readonly Inspectors.ControlSheet _distanceSheet;

		public VertexSelectionPanel( SerializedObject so, VertexTool tool )
		{
			_tool = tool;

			var toolSo = tool.GetSerialized();

			AddTitle( "Vertex Mode", "workspaces" );

			_vertices = [.. so.Targets.OfType<MeshVertex>()];
			_vertexGroups = _vertices.GroupBy( x => x.Component ).ToList();
			_components = _vertexGroups.Select( x => x.Key ).ToList();

			this.AddPivotGroup( tool );
			this.AddPivotButtons( tool, _vertices.Length > 0 );

			{
				var group = AddGroup( "Merge" );

				AddSheet( group.Content ).AddPropertyRow( toolSo.GetProperty( nameof( MergeRangeMode ) ) );

				//The button and the distance it merges within, side by side.
				var row = group.AddRow();

				AddButton( AddButtons( row, 1 ), "Merge", "merge", "mesh.merge", Merge, () => _vertices.Length > 1 );

				_distanceSheet = AddSheet( row );
				_distanceSheet.AddPropertyRow( toolSo.GetProperty( nameof( MergeDistance ) ) );

				SyncDistance();
			}

			{
				var grid = AddButtons( AddGroup( "Modify" ) );

				_connectButton = AddButton( grid, "Connect", "timeline", "mesh.connect", Connect, () => _connectError is null );
				_connectToolTip = _connectButton.Tooltip;
				RefreshConnectButton();

				AddButton( grid, "Bevel", "rounded_corner", "mesh.bevel", Bevel, () => _vertices.Length > 0 );
				AddButton( grid, "Snap To Vertex", "adjust", "mesh.snap_to_vertex", SnapToVertex, () => _vertices.Length > 1 );
				AddButton( grid, "Weld UVs", "link", "mesh.vertex-weld-uvs", WeldUVs, () => _vertices.Length > 0 );
				AddButton( grid, "Edge Cut Tool", "content_cut", "mesh.edge-cut-tool", OpenEdgeCutTool );
			}

			AddStretch();

			AddSheet( AddGroup( "Visualization" ).Content )
				.AddPropertyRow( tool.GetSerialized().GetProperty( nameof( ShowSelectionBounds ) ) );

			AddShortcuts(
				("Lasso Select", "Alt+Shift+Drag"),
				("Lasso Deselect", "Alt+Ctrl+Drag"),
				("Grow Selection", "Numpad +"),
				("Shrink Selection", "Numpad -"),
				("Snap to Grid", "Ctrl+B")
			);
		}

		//The range is picked in the row above, and nothing tells the distance when it changes - so it looks.
		public override void Tick()
		{
			base.Tick();

			SyncDistance();
		}

		void SyncDistance()
		{
			_distanceSheet?.SetClass( "is-disabled", _tool.MergeRangeMode != MergeRange.Fixed );
		}

		//The sidebar is only rebuilt when the tool or the selection changes, so operations that
		//edit a mesh without touching the selection have to re-check the connect button themselves.
		private void RefreshConnectButton()
		{
			_connectError = GetConnectError();

			if ( _connectButton is null )
				return;

			_connectButton.Tooltip = _connectError is null ? _connectToolTip : $"{_connectToolTip}\n\n{_connectError}";
		}

		[Shortcut( "mesh.select-all", "CTRL+A", typeof( SceneViewPanel ) )]
		private void SelectAll()
		{
			using var scope = SceneEditorSession.Scope();
			using var undoScope = SceneEditorSession.Active.UndoScope( "Select All Vertices" ).Push();

			var selection = SceneEditorSession.Active.Selection;
			selection.Clear();

			foreach ( var vertexGroup in _vertexGroups )
			{
				var vertices = vertexGroup.Key.Mesh.VertexHandles;

				foreach ( var vertex in vertices )
				{
					selection.Add( new MeshVertex( vertexGroup.Key, vertex ) );
				}
			}
		}

		[Shortcut( "mesh.edge-cut-tool", "C", typeof( SceneViewPanel ) )]
		void OpenEdgeCutTool()
		{
			var edgeCut = new EdgeCutTool( nameof( VertexTool ) );
			edgeCut.Manager = _tool.Tool.Manager;
			_tool.Tool.CurrentTool = edgeCut;
		}

		[Shortcut( "mesh.connect", "V", typeof( SceneViewPanel ) )]
		private void Connect()
		{
			using var scope = SceneEditorSession.Scope();

			var pairs = BuildConnectPairs();
			if ( pairs.Count == 0 )
				return;

			using ( SceneEditorSession.Active.UndoScope( "Connect Vertices" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var changed = new HashSet<PolygonMesh>();

				foreach ( var (mesh, hVertexA, hVertexB) in pairs )
				{
					if ( mesh.ConnectVertices( hVertexA, hVertexB, out _ ) )
						changed.Add( mesh );
				}

				foreach ( var mesh in changed )
					mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			RefreshConnectButton();
		}

		//For every selected vertex, pair it with the next selected vertex found walking
		//around each face it belongs to. These are the edges a connect would try to create.
		private List<(PolygonMesh Mesh, VertexHandle A, VertexHandle B)> BuildConnectPairs()
		{
			var pairs = new List<(PolygonMesh, VertexHandle, VertexHandle)>();

			foreach ( var group in _vertexGroups )
			{
				if ( !group.Key.IsValid() )
					continue;

				var mesh = group.Key.Mesh;

				//Handles are only meaningful within their own mesh, so match against this group alone.
				var selected = group.Select( x => x.Handle ).ToHashSet();

				foreach ( var hVertex in group )
				{
					if ( !mesh.GetFacesConnectedToVertex( hVertex.Handle, out var connectedFaces ) )
						continue;

					foreach ( var hFace in connectedFaces )
					{
						var hFaceVertex = mesh.FindFaceVertexConnectedToVertex( hVertex.Handle, hFace );
						var hNextFaceVertex = mesh.GetNextVertexInFace( hFaceVertex );

						while ( hNextFaceVertex != hFaceVertex )
						{
							var hNextVertex = mesh.GetVertexConnectedToFaceVertex( hNextFaceVertex );

							if ( selected.Contains( hNextVertex ) )
							{
								pairs.Add( (mesh, hVertex.Handle, hNextVertex) );
								break;
							}

							hNextFaceVertex = mesh.GetNextVertexInFace( hNextFaceVertex );
						}
					}
				}
			}

			return pairs;
		}

		//Null if the current selection can be connected, otherwise why it can't.
		private string GetConnectError()
		{
			if ( _vertices.Length < 2 )
				return "Select at least two vertices.";

			var pairs = BuildConnectPairs();
			if ( pairs.Count == 0 )
				return DescribeConnectFailure( PolygonMesh.ConnectVerticesResult.NoSharedFace );

			var failure = PolygonMesh.ConnectVerticesResult.Success;

			foreach ( var (mesh, hVertexA, hVertexB) in pairs )
			{
				var result = mesh.CanConnectVertices( hVertexA, hVertexB );

				//Connect is happy as long as a single pair works.
				if ( result == PolygonMesh.ConnectVerticesResult.Success )
					return null;

				if ( failure == PolygonMesh.ConnectVerticesResult.Success )
					failure = result;
			}

			return DescribeConnectFailure( failure );
		}

		private static string DescribeConnectFailure( PolygonMesh.ConnectVerticesResult result ) => result switch
		{
			PolygonMesh.ConnectVerticesResult.AlreadyConnected => "The selected vertices are already connected by an edge.",
			PolygonMesh.ConnectVerticesResult.BothVerticesInternal => "Both vertices are inside a face. At least one of them has to be on the edge of a face.",
			PolygonMesh.ConnectVerticesResult.NoSharedFace => "The selected vertices aren't on the same face, so there's no face to split. Connecting them would leave an edge with no face attached.",
			PolygonMesh.ConnectVerticesResult.OutsideFace => "The new edge would fall outside the face. This happens when a face is concave.",
			_ => "The selected vertices can't be connected.",
		};

		[Shortcut( "mesh.snap_to_vertex", "B", typeof( SceneViewPanel ) )]
		private void SnapToVertex()
		{
			if ( _vertices.Length < 2 )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Snap Vertices" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var position = _vertices[^1].PositionWorld;
				foreach ( var vertex in _vertices )
					vertex.Component.Mesh.SetVertexPosition( vertex.Handle, vertex.Transform.PointToLocal( position ) );
			}

			RefreshConnectButton();
		}

		[Shortcut( "mesh.vertex-weld-uvs", "CTRL+F", typeof( SceneViewPanel ) )]
		private void WeldUVs()
		{
			if ( _vertices.Length < 1 )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Weld UVs" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var group in _vertexGroups )
				{
					var component = group.Key;
					var mesh = component.Mesh;
					mesh.AverageVertexUVs( group.Select( x => x.Handle ).ToList() );
				}
			}
		}

		[Shortcut( "mesh.bevel", "F", typeof( SceneViewPanel ) )]
		private void Bevel()
		{
			if ( _vertices.Length <= 0 )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Bevel Vertices" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var bevelWidth = EditorScene.GizmoSettings.GridSpacing;

				foreach ( var group in _vertexGroups )
				{
					if ( !group.Key.Mesh.BevelVertices( group.Select( x => x.Handle ).ToArray(), bevelWidth, out var newVertices ) )
						continue;

					var selection = SceneEditorSession.Active.Selection;
					selection.Clear();

					foreach ( var hVertex in newVertices )
						selection.Add( new MeshVertex( group.Key, hVertex ) );
				}
			}
		}

		[Shortcut( "mesh.merge", "M", typeof( SceneViewPanel ) )]
		private void Merge()
		{
			if ( _vertices.Length < 2 )
				return;

			using var scope = SceneEditorSession.Scope();

			var gameObjects = _components.Skip( 1 ).Select( x => x.GameObject ).ToList();
			var meshA = _components[0];
			var vertices = _vertexGroups[0].ToList();

			using ( SceneEditorSession.Active.UndoScope( "Merge Vertices" )
				.WithComponentChanges( _components )
				.WithGameObjectDestructions( gameObjects )
				.Push() )
			{
				foreach ( var group in _vertexGroups.Skip( 1 ) )
				{
					var meshB = group.Key;
					var transform = meshA.WorldTransform.ToLocal( meshB.WorldTransform );
					meshA.Mesh.MergeMesh( meshB.Mesh, transform, out var remapVertices, out _, out _ );
					vertices.AddRange( group.Select( v => new MeshVertex( meshA, remapVertices[v.Handle] ) ) );

					meshB.DestroyGameObject();
				}

				var mergeDistance = _tool.MergeRangeMode switch
				{
					MergeRange.Grid => EditorScene.GizmoSettings.GridSpacing,
					MergeRange.Fixed => _tool.MergeDistance,
					_ => -1.0f
				};

				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				if ( meshA.Mesh.MergeVerticesWithinDistance( vertices.Select( x => x.Handle ).ToList(), mergeDistance, true, false, out var finalVertices ) > 0 )
				{
					foreach ( var hVertex in finalVertices )
						selection.Add( new MeshVertex( meshA, hVertex ) );

					meshA.Mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
				else
				{
					foreach ( var hVertex in vertices )
						selection.Add( hVertex );
				}
			}
		}

		[Shortcut( "editor.delete", "DEL", typeof( SceneViewPanel ) )]
		private void DeleteSelection()
		{
			var groups = _vertices.GroupBy( face => face.Component );

			if ( !groups.Any() )
				return;

			var components = groups.Select( x => x.Key ).ToArray();

			using ( SceneEditorSession.Active.UndoScope( "Delete Vertices" ).WithComponentChanges( components ).Push() )
			{
				foreach ( var group in groups )
					group.Key.Mesh.RemoveVertices( group.Select( x => x.Handle ) );
			}
		}

		[Shortcut( "mesh.grow-selection", "KP_ADD", typeof( SceneViewPanel ) )]
		private void GrowSelection()
		{
			if ( _vertices.Length == 0 ) return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Grow Selection" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				var newVertices = new HashSet<MeshVertex>();

				foreach ( var vertex in _vertices )
				{
					if ( !vertex.IsValid() )
						continue;

					newVertices.Add( vertex );
				}

				foreach ( var vertex in _vertices )
				{
					if ( !vertex.IsValid() )
						continue;

					var mesh = vertex.Component.Mesh;

					mesh.GetEdgesConnectedToVertex( vertex.Handle, out var edges );

					foreach ( var edge in edges )
					{
						mesh.GetEdgeVertices( edge, out var vertexA, out var vertexB );

						var otherVertex = vertexA == vertex.Handle ? vertexB : vertexA;
						if ( otherVertex.IsValid )
							newVertices.Add( new MeshVertex( vertex.Component, otherVertex ) );
					}
				}

				selection.Clear();
				foreach ( var vertex in newVertices )
				{
					if ( vertex.IsValid() )
						selection.Add( vertex );
				}
			}
		}

		[Shortcut( "mesh.shrink-selection", "KP_MINUS", typeof( SceneViewPanel ) )]
		private void ShrinkSelection()
		{
			if ( _vertices.Length == 0 ) return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Shrink Selection" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				var verticesToKeep = new HashSet<MeshVertex>();

				foreach ( var vertex in _vertices )
				{
					if ( !vertex.IsValid() )
						continue;

					var mesh = vertex.Component.Mesh;
					mesh.GetEdgesConnectedToVertex( vertex.Handle, out var edges );

					bool isInterior = true;

					foreach ( var edge in edges )
					{
						mesh.GetEdgeVertices( edge, out var vertexA, out var vertexB );
						var otherVertex = vertexA == vertex.Handle ? vertexB : vertexA;

						if ( !otherVertex.IsValid )
						{
							isInterior = false;
							break;
						}

						var otherMeshVertex = new MeshVertex( vertex.Component, otherVertex );
						if ( !_vertices.Contains( otherMeshVertex ) )
						{
							isInterior = false;
							break;
						}
					}

					if ( isInterior )
					{
						verticesToKeep.Add( vertex );
					}
				}

				selection.Clear();
				foreach ( var vertex in verticesToKeep )
				{
					if ( vertex.IsValid() )
						selection.Add( vertex );
				}
			}
		}

		[Shortcut( "mesh.select-loop", "L", typeof( SceneViewPanel ) )]
		private void SelectLoop()
		{
			if ( _vertices.Length < 2 )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Select Vertex Loop" ).Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var group in _vertexGroups )
				{
					var mesh = group.Key.Mesh;
					var verts = group.Select( x => x.Handle ).ToArray();

					var edges = verts.SelectMany( ( v, i ) => verts.Skip( i + 1 )
						.Select( w => mesh.FindEdgeConnectingVertices( v, w ) ) )
						.Where( e => e.IsValid )
						.ToArray();

					if ( edges.Length == 0 )
						continue;

					mesh.FindEdgeLoopForEdges( edges, out var edgeLoop );
					mesh.FindVerticesConnectedToEdges( edgeLoop, out var loopVertices );

					foreach ( var hVertex in loopVertices )
						selection.Add( new MeshVertex( group.Key, hVertex ) );
				}
			}
		}

		[Shortcut( "mesh.snap-to-grid", "CTRL+B", typeof( SceneViewPanel ) )]
		private void SnapToGrid()
		{
			if ( _vertices.Length == 0 )
				return;

			using var scope = SceneEditorSession.Scope();

			var grid = EditorScene.GizmoSettings.GridSpacing;
			if ( grid <= 0 )
				return;

			using ( SceneEditorSession.Active.UndoScope( "Snap Vertices To Grid" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var vertex in _vertices )
				{
					var world = vertex.PositionWorld;

					world = new Vector3(
						MathF.Round( world.x / grid ) * grid,
						MathF.Round( world.y / grid ) * grid,
						MathF.Round( world.z / grid ) * grid
					);

					var local = vertex.Transform.PointToLocal( world );

					vertex.Component.Mesh.SetVertexPosition( vertex.Handle, local );
				}
			}
		}

		[Shortcut( "mesh.frame-selection", "SHIFT+A", typeof( SceneViewPanel ) )]
		private void FrameSelection()
		{
			if ( _vertices.Length == 0 )
				return;

			SelectionFrameUtil.FramePoints( _vertices.Select( v => v.PositionWorld ) );
		}
	}
}
