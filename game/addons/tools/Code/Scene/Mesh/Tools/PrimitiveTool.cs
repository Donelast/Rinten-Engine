namespace Editor.MeshEditor;

/// <summary>Create different types of primitive meshes.</summary>
[Title( "Primitive Tool" )]
[Icon( "add_box" )]
[Alias( "tools.primitive-tool" )]
public partial class PrimitiveTool( MeshTool tool ) : EditorTool
{
	const string EditorTypeCookie = "PrimitiveTool.EditorType";

	/// <summary>The mapping tool this belongs to, or null where it is somebody else's sub-tool - see <see cref="BlockTool"/>, which is the object mode's.</summary>
	public MeshTool MeshTool { get; private init; } = tool;

	public PrimitiveEditor Editor { get; private set; }

	//It used to be the mapping tool's, falling back to the dev grid where there was no mapping tool - so the block tool under object mode drew everything in dev grid and there was no way to tell it otherwise.
	/// <summary>What a face is made with, which is whatever the bar over the view says.</summary>
	public Material ActiveMaterial => MaterialSelection.Current.Active;

	//What is painted with, both halves: the material and the colour it lands at, so a shape
	//drawn here comes out wearing the palette's tint as well as its material. See MaterialPaint.
	internal MaterialPaint ActivePaint => MaterialSelection.Current.Paint;

	public override void OnEnabled()
	{
		var savedType = EditorCookie.GetString( EditorTypeCookie, null );
		var type = EditorTypeLibrary.GetTypes<PrimitiveEditor>()
			.FirstOrDefault( x => !x.IsAbstract && x.FullName == savedType )
			?.TargetType ?? typeof( BlockEditor );

		SelectEditor( type );
	}

	public override void OnDisabled()
	{
		if ( Game.IsPlaying )
			Cancel();
		else
			Create();

		Editor = null;
	}

	internal void SelectEditor( Type type, bool remember = true )
	{
		if ( Editor?.GetType() == type )
			return;

		Editor = EditorTypeLibrary.Create<PrimitiveEditor>( type, [this] );

		if ( Editor is not null && remember )
			EditorCookie.SetString( EditorTypeCookie, type.FullName );
	}

	public void Create()
	{
		if ( Editor is null ) return;
		if ( !Editor.CanBuild )
		{
			Cancel();
			return;
		}

		var mesh = Editor.Build();
		if ( mesh is null ) return;

		var name = Editor.Title;

		using var scope = SceneEditorSession.Scope();
		using ( SceneEditorSession.Active.UndoScope( $"Create {name}" )
			.WithGameObjectCreations()
			.Push() )
		{
			var bounds = mesh.CalculateBounds();
			mesh.ApplyTransform( new Transform( -bounds.Center ) );

			var rotation = Editor.BuildRotation;

			var go = new GameObject( true, name );
			go.WorldPosition = rotation * bounds.Center;
			go.WorldRotation = rotation;
			go.IsStatic = true;
			var c = go.Components.Create<MeshComponent>( false );
			c.Mesh = mesh;
			c.SmoothingAngle = 40.0f;

			Editor.OnCreated( c );

			c.Enabled = true;
		}
	}

	public override void OnUpdate()
	{
		Editor?.OnUpdate( MeshTrace );
	}

	public void Cancel()
	{
		Editor?.OnCancel();
	}
}
