namespace Editor.Gallery;

//Found by type: write a class, give it a [Title], [Icon] and [Description], and it is in the list. [Description] is what is written along the bottom of the viewport while the test runs.
/// <summary>One gizmo scenario for the gizmo page: set the camera up once, then draw every frame.</summary>
public interface ISceneTest
{
	void Initialize( CameraComponent camera ) { }
	void Frame();
}
