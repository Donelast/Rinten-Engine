using Editor;
using NativeEngine;

namespace Rinten.Engine;

internal unsafe interface IToolsDll
{
	public static IToolsDll Current { get; set; }


	public void Bootstrap();
	public Task Initialize();
	public void Tick();
	public void RunEvent( string name );
	public void RunEvent( string name, object argument );
	public void RunEvent( string name, object arg0, object arg1 );
	public void RunEvent<T>( Action<T> action );
	public void Exiting();
	public bool ConsoleFocus();
	public void SetPlaying();
	public void ExitPlaymode();

	public void Spin();
	public void RunFrame();
	public void OnRender();

	public void OnFunctionKey( ButtonCode key, KeyboardModifiers modifiers );

	//Registers exclusive Rinten.Tools Rinten.IHandle types
	public int RegisterHandle( IntPtr ptr, uint type );

	//Load the startup project for the first time
	public Task LoadProject();

	//Work done between two frames rather than inside one: opening a project, which holds the thread for as long as it takes and pumps the windows itself.
	public void BetweenFrames();

	public object InspectedObject { get; set; }

	//Is the game view visible, or is it in a tab in the background?
	public bool IsGameViewVisible { get; }

	//A public interface to the active editor system
	public EditorSystem ActiveEditor { get; }

	//Called after the host network system is initialised, used to add additional package references etc. to dev servers
	public Task OnInitializeHost();

	//Get a thumbnail for the specified asset.Can return null if not immediately available.
	Task<Bitmap> GetThumbnail( string filename );
}
