namespace SceneTests;

//The editor picks objects in the scene view by their handle - the little sprite an EditorHandleAttribute
//asks for. These are about what that sprite catches, which has to be no more than what it shows.
[TestClass]
[DoNotParallelize]
public class GizmoHandlePickTest : SceneTest
{
	/// <summary>A handle whose picture is not there - which is all of the engine's own, so far.</summary>
	[EditorHandle( "materials/gizmo/there-is-no-such-picture.png" )]
	internal sealed class MissingPictureHandle : Component
	{
	}

	//A handle that draws nothing catches nothing. It used to lay its hitbox down before working out what it was drawing, so a component whose art
	//was missing kept an invisible thirty-two pixel target at the object's origin - and clicking anything behind it selected that object instead.
	[TestMethod]
	public void AHandleWithNoPictureIsNeitherDrawnNorHit()
	{
		Assert.IsNotNull( Game.TypeLibrary.GetType( typeof( MissingPictureHandle ) ),
			"the component has to be one the gizmo walk can read the attribute off" );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.AddComponent<MissingPictureHandle>();

		using var instance = new Gizmo.Instance();
		using var camera = new SceneCamera( "handle pick" );

		//Pointed straight down the object's own axis from a metre away, so the sprite the handle would have drawn is dead under the cursor.
		camera.Position = new Vector3( -1, 0, 0 );
		camera.Rotation = Rotation.Identity;

		instance.Input = new Gizmo.Inputs
		{
			Camera = camera,
			IsHovered = true,
			CursorRay = new Ray( camera.Position, Vector3.Forward ),
		};

		using ( instance.Push() )
		{
			go.DrawGizmos();
		}

		Assert.IsFalse( go.HasGizmoHandle, "nothing was drawn, so there is no handle here" );
		Assert.IsTrue( string.IsNullOrEmpty( instance.builder.HoveredPath ),
			"and nothing that was not drawn should be under the pointer" );

		scene.Destroy();
	}
}
