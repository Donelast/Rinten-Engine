using System.Threading.Tasks;

namespace Editor;

public partial class SceneViewportPanel : IEditorDropTarget
{
	private readonly List<BaseDropObject> drops = new();
	private BaseDropObject currentDrop;
	private Task<BaseDropObject> creatingDrop;
	private float? lastHeight;

	//Drop objects still being created when their drag ended; deleted as each finishes
	private readonly List<Task<BaseDropObject>> abandonedDrops = new();

	public bool AcceptEditorDrops => true;

	private void UpdateDragDrops()
	{
		for ( var i = abandonedDrops.Count - 1; i >= 0; i-- )
		{
			var pending = abandonedDrops[i];
			if ( !pending.IsCompleted ) continue;

			abandonedDrops.RemoveAt( i );
			if ( pending.IsCompletedSuccessfully ) pending.Result?.Delete();
		}

		drops.RemoveAll( x => x.Deleted );

		//TODO: make this work better with multiviewport (draw gizmos in correct viewport, use correct gizmo settings etc)
		foreach ( var drop in drops )
		{
			drop.Tick();
		}
	}

	public void OnEditorDragDrop( Widget.DragEvent ev )
	{
		AbandonCreatingDrop();

		if ( currentDrop is not null )
		{
			currentDrop.Dropped = true;
			currentDrop = null;
		}

		pointer.Dropped( IsActiveWindow, Renderer.IsUnderMouse );

		Renderer.Focus();
	}

	public DropAction OnEditorDragHover( Widget.DragEvent ev )
	{
		//TODO: Use DragAssetData and deprecate StartInitialize using string

		//Dragging an asset while one is selected will drag them both, in that situation we only want the first one.
		var file = ev.Data.OfType<Asset>().FirstOrDefault()?.Path;

		if ( string.IsNullOrWhiteSpace( file ) )
			file = ev.Data.Files.FirstOrDefault();

		if ( string.IsNullOrWhiteSpace( file ) )
		{
			var split = ev.Data.Text.Split( "\n" );
			if ( split is null || split.Length == 0 )
				return DropAction.Ignore;

			file = split.FirstOrDefault();
			if ( string.IsNullOrWhiteSpace( file ) )
				return DropAction.Ignore;
		}

		var action = DropAction.Copy;

		SceneView.TabSession.MakeActive();

		using var sceneScope = SceneEditorSession.Scope();

		if ( currentDrop is null )
		{
			creatingDrop ??= BaseDropObject.CreateDropFor( file );

			if ( !creatingDrop.IsCompleted )
			{
				return action;
			}

			currentDrop = creatingDrop.Result;
			creatingDrop = default;

			if ( currentDrop is not null )
			{
				_ = currentDrop.StartInitialize( file );
				drops.Add( currentDrop );
			}
			else
			{
				action = DropAction.Ignore;
			}

			lastHeight = null;
		}

		if ( currentDrop is not null )
		{
			PlaceDrop( ev.LocalPosition - placedPicture.Position );
		}

		return action;
	}

	//Puts whatever is being carried where the pointer is pointing in the scene, falling back to a flat plane where the ray hits nothing.
	private void PlaceDrop( Vector2 pictureLocalPosition )
	{
		var forMaterial = currentDrop is MaterialDropObject;

		//TODO: Render meshes don't support tags, material drop doesn't need it though
		var ray = SceneEditorSession.Active.Scene.Trace
						.WithoutTags( "isdragdrop", "trigger" )
						.UseRenderMeshes( forMaterial )
						.UsePhysicsWorld( !forMaterial )
						.Ray( ScreenPixelToRay( pictureLocalPosition ), ActiveCamera.ZFar + MathF.Abs( ActiveCamera.ZNear ) );

		//For materials, take the nearest hit that accepts one, skipping componentless scene objects
		var tr = forMaterial ? FirstThatTakesAMaterial( ray ) : ray.Run();

		if ( !tr.Hit )
		{
			var dist = 0.0f;
			if ( !State.Is2D )
				dist = lastHeight ?? 0.0f;

			var plane = new Plane( State.Is2D ? State.CameraRotation.Backward : Vector3.Up, dist );
			if ( plane.TryTrace( new Ray( tr.StartPosition, tr.Direction ), out tr.EndPosition, twosided: true ) )
			{
				tr.Normal = plane.Normal;
				tr.HitPosition = tr.EndPosition;
			}
		}
		else
		{
			lastHeight = tr.HitPosition.y;
		}

		currentDrop.UpdateDrag( tr, GizmoInstance.Settings );
	}

	//The first thing along the ray that a material can actually be put on, falling back to
	//the nearest hit so the ground-plane placement below still has something to work from.
	private static SceneTraceResult FirstThatTakesAMaterial( SceneTrace ray )
	{
		SceneTraceResult nearest = default;
		var found = false;

		foreach ( var hit in ray.RunAll() )
		{
			if ( !found )
			{
				nearest = hit;
				found = true;
			}

			if ( TakesAMaterial( hit ) ) return hit;
		}

		return nearest;
	}

	//Whether this hit resolves to something that sets materials.
	private static bool TakesAMaterial( SceneTraceResult hit )
	{
		if ( hit.Component is Component.IMaterialSetter ) return true;

		//The component the ray landed on is the one that answers, but a hit that names only the object is still
		//worth taking: it is the same GameObject either way, and one of its renderers is what would be painted.
		return hit.GameObject.IsValid()
			&& hit.GameObject.Components.Get<Component.IMaterialSetter>( FindMode.EnabledInSelf ) is not null;
	}

	public void OnEditorDragLeave()
	{
		AbandonCreatingDrop();

		if ( currentDrop is null ) return;

		currentDrop.Delete();
		drops.Remove( currentDrop );

		currentDrop = null;
	}

	//A drop object still being made belongs to a drag that is over - see abandonedDrops.
	private void AbandonCreatingDrop()
	{
		if ( creatingDrop is null ) return;

		abandonedDrops.Add( creatingDrop );
		creatingDrop = null;
	}
}
