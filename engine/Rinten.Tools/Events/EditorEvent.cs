using System;
using System.Reflection;

namespace Editor;

public static partial class EditorEvent
{
	/// <summary>Called every frame for tools</summary>
	public class FrameAttribute : EventAttribute { public FrameAttribute() : base( "tool.frame" ) { } }

	public class HotloadAttribute : EventAttribute { public HotloadAttribute() : base( "hotloaded" ) { } }


	static Rinten.Internal.EventSystem eventManager;

	internal static void Init()
	{
		eventManager?.Dispose();
		eventManager = new Rinten.Internal.EventSystem();
	}


	//Register an assembly. If old assembly is valid, we try to remove all of the old event hooks from this assembly, while retaining a list of objects.
	internal static void UnregisterAssembly( Assembly outgoing )
	{
		eventManager.UnregisterAssembly( outgoing );
	}

	//Register an assembly. If old assembly is valid, we try to remove all of the old event hooks from this assembly, while retaining a list of objects.
	internal static void RegisterAssembly( Assembly incoming )
	{
		eventManager.RegisterAssembly( incoming );
	}

	/// <summary>Register an object, start receiving events</summary>
	public static void Register( object obj ) => eventManager?.Register( obj );

	/// <summary>Unregister an object, stop receiving events</summary>
	public static void Unregister( object obj ) => eventManager?.Unregister( obj );

	/// <summary>Run an event.</summary>
	public static void Run( string name ) => eventManager?.Run( name );

	/// <summary>Run an event with an argument of arbitrary type.</summary>
	public static void Run<T>( string name, T arg0 )
	{
		eventManager?.Run( name, arg0 );
	}

	/// <summary>Run an event with 2 arguments of arbitrary type.</summary>
	public static void Run<T, U>( string name, T arg0, U arg1 )
	{
		eventManager?.Run( name, arg0, arg1 );
	}

	/// <summary>Run an interface based event</summary>
	public static void RunInterface<T>( Action<T> action )
	{
		eventManager?.RunInterface<T>( action );
	}

	/// <summary>Run an event with 3 arguments of arbitrary type.</summary>
	public static void Run<T, U, V>( string name, T arg0, U arg1, V arg2 )
	{
		eventManager?.Run( name, arg0, arg1, arg2 );
	}

	public interface IEventListener;

	public interface ISceneEdited : IEventListener
	{
		/// <summary>Called when a property on a <see cref="GameObject"/> is about to be edited, so the old value can be inspected.</summary>
		void GameObjectPreEdited( GameObject go, string propertyName ) { }

		/// <summary>Called when a <see cref="GameObject"/> has been edited, so the new value can be inspected.</summary>
		void GameObjectEdited( GameObject go, string propertyName );

		/// <summary>Called when a property on a <see cref="Component"/> is about to be edited, so the old value can be inspected.</summary>
		void ComponentPreEdited( Component cmp, string propertyName ) { }

		/// <summary>Called when a <see cref="Component"/> has been edited, so the new value can be inspected.</summary>
		void ComponentEdited( Component cmp, string propertyName );
	}

	/// <summary>Event args for <see cref="ISceneView.ShowContextMenu"/> events.</summary>
	public sealed record ShowContextMenuEvent( SceneEditorSession Session, Menu Menu, Ray CursorRay, SceneTraceResult? Trace );

	/// <summary>Allows tools to inject behaviour in the scene editor.</summary>
	public interface ISceneView : IEventListener
	{
		/// <summary>Called when a scene editor viewport is drawing gizmos.</summary>
		void DrawGizmos( Scene scene ) { }

		/// <summary>Called when a scene editor viewport wants to show a context menu.</summary>
		void ShowContextMenu( ShowContextMenuEvent ev ) { }
	}
}
