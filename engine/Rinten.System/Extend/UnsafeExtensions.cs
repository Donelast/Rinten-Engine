using System.Reflection;
using System.Runtime.CompilerServices;

namespace Rinten;

public static partial class RintenSystemExtensions
{
	private static readonly ReflectionCache<Type, int> _managedSizeCache = new( ComputeManagedSize,
		new KeyValuePair<Type, int>( typeof( void ), 0 ) );

	/// <summary>IL-level sizeof(t): pointer size for reference types, differs from Marshal.SizeOf for char/bool</summary>
	public static int GetManagedSize( this Type t ) => _managedSizeCache[t];

	private static MethodInfo _getManagedSizeHelper;

	private static int ComputeManagedSize( Type t )
	{
		_getManagedSizeHelper ??= typeof( Unsafe ).GetMethod( nameof( Unsafe.SizeOf ) )!;

		return _getManagedSizeHelper.MakeGenericMethod( t ).CreateDelegate<Func<int>>()();
	}
}
