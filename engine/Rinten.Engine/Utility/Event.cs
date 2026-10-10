using Rinten.Engine;
using System.Reflection;

namespace Rinten;

//The event system.
internal static partial class Event
{
	public static Internal.EventSystem EventSystem => GlobalContext.Current.EventSystem;

	//Register an assembly. If old assembly is valid, we try to remove all of the old event hooks from this assembly, while retaining a list of objects.
	internal static void UnregisterAssembly( Assembly outgoing )
	{
		EventSystem.UnregisterAssembly( outgoing );
	}

	//Register an assembly. If old assembly is valid, we try to remove all of the old event hooks from this assembly, while retaining a list of objects.
	internal static void RegisterAssembly( Assembly incoming )
	{
		EventSystem.RegisterAssembly( incoming );
	}

	/// <summary>Register an object to start receiving events</summary>
	public static void Register( object obj )
	{
		EventSystem?.Register( obj );
	}

	/// <summary>Unregister an object, stop reviving events</summary>
	public static void Unregister( object obj )
	{
		EventSystem?.Unregister( obj );
	}

	/// <summary>Run an event.</summary>
	public static void Run( string name )
	{
		EventSystem?.Run( name );
	}

	/// <summary>Run an event with an argument of arbitrary type.</summary>
	public static void Run<T>( string name, T arg0 )
	{
		EventSystem?.Run( name, arg0 );
	}

	/// <summary>Run an event with 2 arguments of arbitrary type.</summary>
	public static void Run<T, U>( string name, T arg0, U arg1 )
	{
		EventSystem?.Run( name, arg0, arg1 );
	}

	/// <summary>Run an event with 3 arguments of arbitrary type.</summary>
	public static void Run<T, U, V>( string name, T arg0, U arg1, V arg2 )
	{
		EventSystem?.Run( name, arg0, arg1, arg2 );
	}
}
