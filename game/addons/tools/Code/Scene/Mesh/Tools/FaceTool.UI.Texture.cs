using HalfEdgeMesh;

namespace Editor.MeshEditor;

partial class FaceTool
{
	public partial class FaceSelectionPanel
	{
		//Titled for the same reason as the filtered selection's: each is a row of a sheet in a
		//settings popup, and the row writes the name the widget used to put beside the tick by hand.
		[Title( "Treat as one" )]
		public bool TextureTreatAsOne { get; set; } = false;

		[Range( 0, 128, slider: false ), Step( 1 ), WideMode]
		public Vector2Int TextureFit { get; set; } = 1;

		[Title( "Tiling" )]
		public bool HotspotTiling { get; set; } = false;

		[Title( "Conforming" )]
		public bool HotspotConforming { get; set; } = true;

		[Title( "Use Active Material" )]
		public bool HotspotUseActiveMaterial { get; set; } = true;

		[Title( "Mirror Horizontally" )]
		public bool HotspotAllowMirrorHorizontal { get; set; } = false;

		[Title( "Mirror Vertically" )]
		public bool HotspotAllowMirrorVertical { get; set; } = false;

		private void LoadTextureSettings()
		{
			HotspotTiling = EditorCookie.Get( nameof( HotspotTiling ), HotspotTiling );
			HotspotConforming = EditorCookie.Get( nameof( HotspotConforming ), HotspotConforming );
			HotspotUseActiveMaterial = EditorCookie.Get( nameof( HotspotUseActiveMaterial ), HotspotUseActiveMaterial );
			HotspotAllowMirrorHorizontal = EditorCookie.Get( nameof( HotspotAllowMirrorHorizontal ), HotspotAllowMirrorHorizontal );
			HotspotAllowMirrorVertical = EditorCookie.Get( nameof( HotspotAllowMirrorVertical ), HotspotAllowMirrorVertical );
			TextureFit = EditorCookie.Get( nameof( TextureFit ), TextureFit );
			TextureTreatAsOne = EditorCookie.Get( nameof( TextureTreatAsOne ), TextureTreatAsOne );
			_activePanel = EditorCookie.Get<string>( "TextureActivePanel", null );
		}

		private void SaveTextureSettings()
		{
			EditorCookie.Set( nameof( HotspotTiling ), HotspotTiling );
			EditorCookie.Set( nameof( HotspotConforming ), HotspotConforming );
			EditorCookie.Set( nameof( HotspotUseActiveMaterial ), HotspotUseActiveMaterial );
			EditorCookie.Set( nameof( HotspotAllowMirrorHorizontal ), HotspotAllowMirrorHorizontal );
			EditorCookie.Set( nameof( HotspotAllowMirrorVertical ), HotspotAllowMirrorVertical );
			EditorCookie.Set( nameof( TextureFit ), TextureFit );
			EditorCookie.Set( nameof( TextureTreatAsOne ), TextureTreatAsOne );
			EditorCookie.Set( "TextureActivePanel", _activePanel );
		}

		string _activePanel = null;
		Panel _panelContainer;
		SerializedObject _textureTarget;

		private void BuildTextureUI( SerializedObject so, SerializedObject target )
		{
			_textureTarget = target;

			bool hasSelectedFaces = _faces.Length > 0;

			{
				var group = AddGroup( "Texture", collapsible: true );

				//Six short words, so three across; what the chosen one can do is laid out under them two across like everything else.
				var modes = AddButtons( group, 3 );

				AddModeButton( modes, "Align", "texture", "align" );
				AddModeButton( modes, "Scale", "photo_size_select_large", "scale" );
				AddModeButton( modes, "Shift", "open_with", "shift" );
				AddModeButton( modes, "Fit", "fit_screen", "fit" );
				AddModeButton( modes, "Justify", "format_align_justify", "justify" );
				AddModeButton( modes, "Hotspot", "ads_click", "hotspot" );

				_panelContainer = group.Content.AddChild<Panel>( "column gap-2" );

				RebuildPanel();
			}

			if ( hasSelectedFaces )
			{
				var group = AddGroup( "Texture Selection", collapsible: true );

				//Rows of a sheet, which name themselves: the widget had a bare icon in front of each control instead of its name.
				var sheet = AddSheet( group.Content );
				sheet.AddPropertyRow( so.GetProperty( nameof( MeshFace.TextureOffset ) ) );
				sheet.AddPropertyRow( so.GetProperty( nameof( MeshFace.TextureScale ) ) );

				AddButton( AddButtons( group, 1 ), "Apply Material", "format_color_fill", "mesh.apply-material", ApplyMaterial );
			}
		}

		//One of the six texture modes. Lit while it is the one whose buttons are under the row, which is asked of the button rather than pushed to it.
		void AddModeButton( ButtonGridPanel grid, string title, string icon, string panel )
		{
			AddButton( grid, title, icon, null, () =>
			{
				if ( _activePanel == panel ) return;

				_activePanel = panel;
				EditorCookie.Set( "TextureActivePanel", _activePanel );

				RebuildPanel();
			},
			enabled: HasFaces,
			active: () => _faces.Length > 0 && (_activePanel ?? "align") == panel );
		}

		private void RebuildPanel()
		{
			if ( _panelContainer is null ) return;

			_panelContainer.DeleteChildren( true );

			if ( _faces.Length == 0 )
				return;

			_activePanel ??= "align";

			var grid = AddButtons( _panelContainer );

			switch ( _activePanel )
			{
				case "scale":
					AddButton( grid, "Scale X Up", "chevron_right", null, () => DoScaleX( true ) );
					AddButton( grid, "Scale X Down", "chevron_left", null, () => DoScaleX( false ) );
					AddButton( grid, "Scale Y Up", "expand_less", null, () => DoScaleY( true ) );
					AddButton( grid, "Scale Y Down", "expand_more", null, () => DoScaleY( false ) );
					break;

				case "shift":
					AddButton( grid, "Shift Left", "arrow_back", null, () => DoShiftX( true ) );
					AddButton( grid, "Shift Right", "arrow_forward", null, () => DoShiftX( false ) );
					AddButton( grid, "Shift Up", "arrow_upward", null, () => DoShiftY( true ) );
					AddButton( grid, "Shift Down", "arrow_downward", null, () => DoShiftY( false ) );
					break;

				case "fit":
					AddButton( grid, "Fit Both", "fit_screen", null, () => DoFit( TextureFit.x, TextureFit.y ) );
					AddButton( grid, "Fit X", "swap_horiz", null, () => DoFit( TextureFit.x, -1 ) );
					AddButton( grid, "Fit Y", "swap_vert", null, () => DoFit( -1, TextureFit.y ) );
					AddSettings( grid, sheet => sheet.AddPropertyRow( _textureTarget.GetProperty( nameof( TextureFit ) ) ) );
					break;

				case "justify":
					AddButton( grid, "Left", "align_horizontal_left", null, () => DoJustify( PolygonMesh.TextureJustification.Left ) );
					AddButton( grid, "Right", "align_horizontal_right", null, () => DoJustify( PolygonMesh.TextureJustification.Right ) );
					AddButton( grid, "Top", "vertical_align_top", null, () => DoJustify( PolygonMesh.TextureJustification.Top ) );
					AddButton( grid, "Bottom", "vertical_align_bottom", null, () => DoJustify( PolygonMesh.TextureJustification.Bottom ) );
					AddButton( grid, "Center", "filter_center_focus", null, () => DoJustify( PolygonMesh.TextureJustification.Center ) );
					AddSettings( grid, sheet => sheet.AddPropertyRow( _textureTarget.GetProperty( nameof( TextureTreatAsOne ) ) ) );
					break;

				case "hotspot":
					AddButton( grid, "Apply Hotspot", "ads_click", "mesh.apply-hotspot", () => ApplyMaterialByHotspot( _meshTool.ActiveMaterial, false ) );
					AddButton( grid, "Per Face", "ads_click", "mesh.apply-hotspot-per-face", () => ApplyMaterialByHotspot( _meshTool.ActiveMaterial, true ) );
					AddSettings( grid, sheet =>
					{
						sheet.AddPropertyRow( _textureTarget.GetProperty( nameof( HotspotUseActiveMaterial ) ) );
						sheet.AddPropertyRow( _textureTarget.GetProperty( nameof( HotspotTiling ) ) );
						sheet.AddPropertyRow( _textureTarget.GetProperty( nameof( HotspotConforming ) ) );
						sheet.AddPropertyRow( _textureTarget.GetProperty( nameof( HotspotAllowMirrorHorizontal ) ) );
						sheet.AddPropertyRow( _textureTarget.GetProperty( nameof( HotspotAllowMirrorVertical ) ) );
					} );
					break;

				default:
					AddButton( grid, "Align to Grid", "grid_on", null, AlignToGrid );
					AddButton( grid, "Align to Face", "texture", null, AlignToFace );
					AddButton( grid, "Align to View", "visibility", null, AlignToView );
					AddButton( grid, "Rotate CW", "rotate_right", null, () => DoRotate( true ) );
					AddButton( grid, "Rotate CCW", "rotate_left", null, () => DoRotate( false ) );
					break;
			}
		}

		//Settings button at the end of the grid that opens a popup sheet of the mode's properties
		void AddSettings( ButtonGridPanel grid, Action<Editor.Inspectors.ControlSheet> build )
		{
			var button = AddButton( grid, "Settings", "settings", null, null );

			button.Pressed = () =>
			{
				var popup = new Rinten.UI.Popup();
				popup.SetPositioning( button, Rinten.UI.Popup.PositionMode.BelowLeft, 2 );
				popup.CloseWhenParentIsHidden = true;
				popup.AddClass( "editor-popup" );
				popup.Style.Set( "width: 220px; padding: 8px;" );

				build( popup.AddChild( new Editor.Inspectors.ControlSheet() ) );
			};
		}

		[Shortcut( "mesh.apply-material", "SHIFT+T", typeof( SceneViewPanel ) )]
		void ApplyMaterial()
		{
			var paint = _meshTool.ActivePaint;
			if ( !paint.IsValid ) return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Apply Material" )
				.WithComponentChanges( _components )
				.Push() )
			{
				//Material and colour together: what the palette says is one thing, and applying half of
				//it would leave a face wearing the last colour somebody used with a different material.
				foreach ( var group in _faces.GroupBy( x => x.Component.Mesh ) )
				{
					paint.Apply( group.Key, group.Select( x => x.Handle ).ToArray() );
				}
			}
		}

		//How much world a texture covers, in metres, at the scale a face gets by default.
		static Vector2 CalculateTextureSize( Material material )
		{
			Vector2 textureSize = 512 * PolygonMesh.DefaultTextureScale;
			if ( material is null )
				return textureSize;

			var width = material.Attributes.GetInt( "WorldMappingWidth" );
			var height = material.Attributes.GetInt( "WorldMappingHeight" );
			var texture = material.FirstTexture;

			if ( texture != null )
			{
				textureSize.x = width > 0 ? width : (texture.Size.x * PolygonMesh.DefaultTextureScale);
				textureSize.y = height > 0 ? height : (texture.Size.y * PolygonMesh.DefaultTextureScale);
			}
			else
			{
				if ( width > 0 ) textureSize.x = width;
				if ( height > 0 ) textureSize.y = height;
			}

			return textureSize;
		}

		static readonly RectEditor.RectAssetData EmptyRectData = new();

		[Shortcut( "mesh.apply-hotspot", "Alt+H", typeof( SceneViewPanel ) )]
		void ApplyMaterialByHotspot() => ApplyMaterialByHotspot( _meshTool.ActiveMaterial, false );

		[Shortcut( "mesh.apply-hotspot-per-face", "Alt+T", typeof( SceneViewPanel ) )]
		void ApplyMaterialByHotspotPerFace() => ApplyMaterialByHotspot( _meshTool.ActiveMaterial, true );

		void ApplyMaterialByHotspot( Material material, bool perFace )
		{
			using var scope = SceneEditorSession.Scope();
			if ( HotspotUseActiveMaterial && (material is null || !material.IsValid()) ) return;

			using ( SceneEditorSession.Active.UndoScope( "Apply Material By Hotspot" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var group in _faceGroups )
				{
					var mesh = group.Key.Mesh;
					if ( HotspotUseActiveMaterial )
					{
						var faces = group.Select( x => x.Handle ).ToArray();

						//The palette's colour with it: this branch is the active material being applied, and the tint is half of what applying the active material means.
						new MaterialPaint( material, _meshTool.ActivePaint.Tint ).Apply( mesh, faces );

						ApplyHotspotForFaces( mesh, group.Key.WorldTransform, faces, material, perFace );
					}
					else
					{
						foreach ( var materialGroup in group.GroupBy( face => mesh.GetFaceMaterial( face.Handle ) ) )
						{
							var faces = materialGroup.Select( x => x.Handle ).ToArray();
							ApplyHotspotForFaces( mesh, group.Key.WorldTransform, faces, materialGroup.Key, perFace );
						}
					}
				}
			}
		}

		private void ApplyHotspotForFaces( PolygonMesh mesh, Transform transform, FaceHandle[] faces, Material material, bool perFace )
		{
			if ( faces.Length == 0 ) return;

			var resourcePath = material is not null && material.IsValid() ? material.ResourcePath : null;
			var data = !string.IsNullOrEmpty( resourcePath )
				? RectEditor.RectAssetData.Find( AssetSystem.FindByPath( resourcePath ) ) ?? EmptyRectData
				: EmptyRectData;
			var size = CalculateTextureSize( material );
			ComputeHotspotUVsForFaces( mesh, transform, faces, data, size.x, size.y, perFace, HotspotTiling, HotspotConforming, HotspotAllowMirrorHorizontal, HotspotAllowMirrorVertical );
		}


		[Shortcut( "mesh.find-replace-material-tool", "SHIFT+R", typeof( SceneViewPanel ) )]
		void OpenFindReplaceMaterialTool()
		{
			var tool = new FindReplaceMaterialTool( _meshTool, nameof( FaceTool ) );
			tool.Manager = _meshTool.Manager;
			_meshTool.CurrentTool = tool;
		}

		private void AlignToGrid()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Align to Grid" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					face.Component.Mesh.TextureAlignToGrid( face.Transform, face.Handle );
				}
			}
		}

		private void AlignToFace()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Align to Face" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					face.Component.Mesh.TextureAlignToFace( face.Transform, face.Handle );
				}
			}
		}

		private void AlignToView()
		{
			var sceneView = SceneViewPanel.Current?.LastSelectedViewport;
			if ( !sceneView.IsValid() )
				return;

			using var scope = SceneEditorSession.Scope();

			var position = sceneView.State.CameraPosition;
			var rotation = sceneView.State.CameraRotation;
			var uAxis = rotation.Right;
			var vAxis = rotation.Up;
			var offset = new Vector2( uAxis.Dot( position ), vAxis.Dot( position ) );
			var scale = new Vector2( PolygonMesh.DefaultTextureScale, PolygonMesh.DefaultTextureScale );

			using ( SceneEditorSession.Active.UndoScope( "Align to View" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					var mesh = face.Component.Mesh;
					mesh.SetFaceTextureParameters( face.Handle, new Vector4( uAxis, offset.x ), new Vector4( vAxis, offset.y ), scale );
				}
			}
		}

		private void DoRotate( bool clockwise )
		{
			using var scope = SceneEditorSession.Scope();

			var amount = EditorScene.GizmoSettings.AngleSpacing * (clockwise ? 1 : -1);

			using ( SceneEditorSession.Active.UndoScope( "Rotate" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					var mesh = face.Component.Mesh;
					mesh.GetFaceTextureParameters( face.Handle, out var axisU, out var axisV, out var scale );

					Vector3 newAxisU = (Vector3)axisU;
					Vector3 newAxisV = (Vector3)axisV;
					var axis = Vector3.Cross( newAxisU, newAxisV );
					axis = axis.Normal;

					var rotation = Rotation.FromAxis( axis, amount );
					newAxisU *= rotation;
					newAxisV *= rotation;
					newAxisU = newAxisU.Normal;
					newAxisV = newAxisV.Normal;

					mesh.SetFaceTextureParameters( face.Handle, new Vector4( newAxisU, axisU.w ), new Vector4( newAxisV, axisV.w ), scale );
				}
			}
		}

		private void DoShiftX( bool positive )
		{
			using var scope = SceneEditorSession.Scope();

			var gridSpacing = EditorScene.GizmoSettings.GridSpacing;

			using ( SceneEditorSession.Active.UndoScope( "Shift X" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					var mesh = face.Component.Mesh;
					var scale = mesh.GetTextureScale( face.Handle ).x;
					scale = scale.AlmostEqual( 0.0f ) ? PolygonMesh.DefaultTextureScale : scale;
					var amount = gridSpacing / scale;
					var offset = mesh.GetTextureOffset( face.Handle );
					offset = offset.WithX( offset.x + amount * (positive ? 1.0f : -1.0f) );
					mesh.SetTextureOffset( face.Handle, offset );
				}
			}
		}

		private void DoShiftY( bool positive )
		{
			using var scope = SceneEditorSession.Scope();

			var gridSpacing = EditorScene.GizmoSettings.GridSpacing;

			using ( SceneEditorSession.Active.UndoScope( "Shift Y" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					var mesh = face.Component.Mesh;
					var scale = mesh.GetTextureScale( face.Handle ).y;
					scale = scale.AlmostEqual( 0.0f ) ? PolygonMesh.DefaultTextureScale : scale;
					var amount = gridSpacing / scale;
					var offset = mesh.GetTextureOffset( face.Handle );
					offset = offset.WithY( offset.y + amount * (positive ? 1.0f : -1.0f) );
					mesh.SetTextureOffset( face.Handle, offset );
				}
			}
		}

		private void DoScaleX( bool positive )
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Scale X" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					var mesh = face.Component.Mesh;
					var scale = mesh.GetTextureScale( face.Handle );
					scale = scale.WithX( scale.x * (positive ? 2.0f : 0.5f) );
					mesh.SetTextureScale( face.Handle, scale );
				}
			}
		}

		private void DoScaleY( bool positive )
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Scale Y" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					var mesh = face.Component.Mesh;
					var scale = mesh.GetTextureScale( face.Handle );
					scale = scale.WithY( scale.y * (positive ? 2.0f : 0.5f) );
					mesh.SetTextureScale( face.Handle, scale );
				}
			}
		}

		private void DoJustify( PolygonMesh.TextureJustification justification )
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Justify" )
				.WithComponentChanges( _components )
				.Push() )
			{
				JustifyTexturesForFaceSelection( justification );

				foreach ( var group in _faceGroups )
				{
					var mesh = group.Key.Mesh;
					mesh.ComputeFaceTextureCoordinatesFromParameters( group.Select( x => x.Handle ) );
				}
			}
		}

		private void DoFit( int repeatX, int repeatY )
		{
			using var scope = SceneEditorSession.Scope();

			var justification = PolygonMesh.TextureJustification.Fit;
			if ( repeatX == -1 ) justification = PolygonMesh.TextureJustification.FitY;
			else if ( repeatY == -1 ) justification = PolygonMesh.TextureJustification.FitX;

			using ( SceneEditorSession.Active.UndoScope( "Fit" )
				.WithComponentChanges( _components )
				.Push() )
			{
				JustifyTexturesForFaceSelection( justification );

				if ( repeatX > 0 || repeatY > 0 )
				{
					foreach ( var face in _faces )
					{
						var mesh = face.Component.Mesh;
						var scale = mesh.GetTextureScale( face.Handle );

						if ( repeatX > 0 )
							scale.x /= repeatX;

						if ( repeatY > 0 )
							scale.y /= repeatY;

						mesh.SetTextureScale( face.Handle, scale );
					}
				}

				if ( repeatX != -1 )
					JustifyTexturesForFaceSelection( PolygonMesh.TextureJustification.Left );

				if ( repeatY != -1 )
					JustifyTexturesForFaceSelection( PolygonMesh.TextureJustification.Top );

				foreach ( var group in _faceGroups )
				{
					var mesh = group.Key.Mesh;
					mesh.ComputeFaceTextureCoordinatesFromParameters( group.Select( x => x.Handle ) );
				}
			}
		}

		private void JustifyTexturesForFaceSelection( PolygonMesh.TextureJustification justification )
		{
			PolygonMesh.FaceExtents extents = null;

			if ( TextureTreatAsOne )
			{
				extents = new PolygonMesh.FaceExtents();

				foreach ( var group in _faceGroups )
				{
					var mesh = group.Key.Mesh;
					mesh.UnionExtentsForFaces( group.Select( x => x.Handle ), mesh.Transform, extents );
				}
			}

			foreach ( var group in _faceGroups )
			{
				var mesh = group.Key.Mesh;
				mesh.JustifyFaceTextureParameters( group.Select( x => x.Handle ), justification, extents );
			}
		}
	}
}
