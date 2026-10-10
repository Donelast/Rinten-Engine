namespace Rinten;

internal static class ReflectionUtility
{
	public static void RunAllStaticConstructors( string assemblyName )
	{
		var asm = Assembly.Load( assemblyName );
		RunAllStaticConstructors( asm );
	}

	public static void RunAllStaticConstructors( Assembly asm )
	{
		List<Exception> exceptions = null;

		foreach ( var t in asm.GetTypes() )
		{
			try
			{
				System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor( t.TypeHandle );
			}
			catch ( Exception ex )
			{
				exceptions ??= new List<Exception>();
				exceptions.Add( ex );
			}
		}

		if ( exceptions is not { Count: > 0 } ) return;

		throw exceptions.Count == 1 ? exceptions[0] : new AggregateException( exceptions );
	}

	/// <summary>Finds all static fields assignable to <paramref name="targetType"/> across all types in the given assembly and sets them to null. Useful for releasing references during teardown.</summary>
	public static void NullStaticReferencesOfType( Assembly asm, Type targetType )
	{
		var logger = Logging.GetLogger( "Reflection" );

		//GetTypes() can throw ReflectionTypeLoadException on assemblies whose AssemblyLoadContext
		//has started unloading. Fall back to the subset of types that loaded successfully.
		Type[] types;

		try
		{
			types = asm.GetTypes();
		}
		catch ( ReflectionTypeLoadException e )
		{
			types = e.Types.Where( t => t is not null ).ToArray();
		}

		//Check field types via metadata so asking doesn't load an assembly nothing has loaded
		using var reach = JitReach.For( asm );

		foreach ( var type in types )
		{
			if ( type.IsGenericTypeDefinition ) continue;
			if ( type.GetCustomAttribute<SkipHotloadAttribute>() != null ) continue;

			var fields = type.GetFields( BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic );

			foreach ( var field in fields )
			{
				if ( field.IsLiteral ) continue; //skip constants
				if ( field.GetCustomAttribute<SkipHotloadAttribute>() != null ) continue;
				if ( reach?.FieldReaches( field ) == true ) continue;

				try
				{
					//Direct match — null the field itself
					if ( targetType.IsAssignableFrom( field.FieldType ) )
					{
						if ( field.IsInitOnly )
						{
							//This is a tiny bit dangerous ideally we just dont have any readonly statuc fields holding on to resources
							if ( !ForceNullStaticField( field ) ) logger.Warning( $"Failed to force-clear readonly {type.Name}.{field.Name}" );
							continue;
						}

						field.SetValue( null, null );
						continue;
					}

					//Read only a field that could hold a collection by its declared type. Reading a static runs its type's constructor, and at teardown
					//that ran ones nothing had used: WaylandLayer.Interfaces, a class of nints, loading libwayland on Windows and failing once per field.
					var declared = field.FieldType;
					if ( declared == typeof( string ) ) continue;
					if ( !typeof( System.Collections.IEnumerable ).IsAssignableFrom( declared ) && ( declared.IsValueType || declared.IsSealed ) ) continue;

					if ( !TryClearCollectionOfType( field.GetValue( null ), targetType ) ) continue;
				}
				catch ( Exception e )
				{
					logger.Warning( e, $"Failed to clean {type.Name}.{field.Name}" );
				}
			}
		}
	}

	//If value is a collection containing elements assignable to targetType, remove
	//those elements (or clear the whole collection). Returns true if any work was done.
	private static bool TryClearCollectionOfType( object value, Type targetType )
	{
		if ( value is null ) return false;

		var valueType = value.GetType();
		//Default ImmutableArray<T> throws when accessed through IList.
		if ( valueType.IsGenericType &&
			valueType.GetGenericTypeDefinition() == typeof( System.Collections.Immutable.ImmutableArray<> ) &&
			value.Equals( Activator.CreateInstance( valueType ) ) )
			return false;

		//Arrays are fixed size: null matching elements in place; skip multidimensional ones
		if ( value is Array array )
		{
			if ( array.Rank != 1 ) return false;
			if ( valueType.GetElementType()?.IsValueType != false ) return false;

			var cleared = false;

			for ( var i = 0; i < array.Length; i++ )
			{
				if ( array.GetValue( i ) is null || !targetType.IsInstanceOfType( array.GetValue( i ) ) ) continue;

				array.SetValue( null, i );
				cleared = true;
			}

			return cleared;
		}

		//Check if this is a generic collection whose element type matches
		if ( HasGenericElementOfType( valueType, targetType ) )
		{
			//IDictionary<K,V> — clear if either key or value type matches
			if ( value is System.Collections.IDictionary dict )
			{
				dict.Clear();
				return true;
			}

			//IList — remove matching items in reverse to avoid index shifting
			if ( value is System.Collections.IList list )
			{
				for ( var i = list.Count - 1; i >= 0; i-- )
				{
					if ( list[i] is not null && targetType.IsInstanceOfType( list[i] ) )
						list.RemoveAt( i );
				}

				return true;
			}

			//Generic fallback — covers HashSet<T>, ConcurrentBag<T>, HashSetEx<T>, etc. that don't implement IDictionary or IList but have a parameterless Clear().
			var clearMethod = valueType.GetMethod( "Clear", Type.EmptyTypes );
			if ( clearMethod is not null && !clearMethod.IsStatic )
			{
				clearMethod.Invoke( value, null );
				return true;
			}
		}

		//Non-generic IList fallback (e.g. ArrayList) — remove matching items
		if ( value is System.Collections.IList nonGenericList )
		{
			var removed = false;

			for ( var i = nonGenericList.Count - 1; i >= 0; i-- )
			{
				if ( nonGenericList[i] is not null && targetType.IsInstanceOfType( nonGenericList[i] ) )
				{
					nonGenericList.RemoveAt( i );
					removed = true;
				}
			}

			return removed;
		}

		return false;
	}

	//Nulls a static readonly (initonly) field by emitting a DynamicMethod that uses stsfld
	//directly, bypassing the CLR verifier check that normal reflection enforces on initonly fields.
	private static bool ForceNullStaticField( FieldInfo field )
	{
		if ( field.DeclaringType is null ) return false;

		try
		{
			var dm = new System.Reflection.Emit.DynamicMethod(
				$"__ForceNull_{field.DeclaringType.Name}_{field.Name}",
				typeof( void ),
				Type.EmptyTypes,
				field.DeclaringType.Module,
				skipVisibility: true );

			var il = dm.GetILGenerator();
			il.Emit( System.Reflection.Emit.OpCodes.Ldnull );
			il.Emit( System.Reflection.Emit.OpCodes.Stsfld, field );
			il.Emit( System.Reflection.Emit.OpCodes.Ret );

			((Action)dm.CreateDelegate( typeof( Action ) ))();
			return true;
		}
		catch
		{
			return false;
		}
	}

	//Returns true if the type itself or any of its implemented generic interfaces (IEnumerable<T>, IDictionary<K,V>, etc.) has a generic
	//argument assignable to targetType. This covers types like HashSetEx<T> that don't implement standard collection interfaces directly.
	private static bool HasGenericElementOfType( Type collectionType, Type targetType )
	{
		//Check the type's own generic arguments first (e.g. HashSetEx<T>, HashSet<T>)
		if ( collectionType.IsGenericType )
		{
			foreach ( var arg in collectionType.GetGenericArguments() )
			{
				if ( targetType.IsAssignableFrom( arg ) )
					return true;
			}
		}

		foreach ( var iface in collectionType.GetInterfaces() )
		{
			if ( !iface.IsGenericType ) continue;

			foreach ( var arg in iface.GetGenericArguments() )
			{
				if ( targetType.IsAssignableFrom( arg ) )
					return true;
			}
		}

		return false;
	}

	/// <summary>Pre-compile all methods in the assembly on a background thread to reduce the risk of on-demand JIT stalls during gameplay. Returns immediately. Cancels automatically if the assembly's load context begins unloading, so type references are released promptly and don't delay hotloads.</summary>
	public static System.Threading.Tasks.Task PreJITAsync( Assembly asm )
	{
		//Skip on mobile: Mono pre-JIT of everything is slow and can kill the process
		if ( OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() )
			return System.Threading.Tasks.Task.CompletedTask;

		var logger = Logging.GetLogger( "PreJit" );
		var alc = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( asm );

		//cts is always disposed in the Task's finally block below.
#pragma warning disable CA2000
		var cts = new System.Threading.CancellationTokenSource();
#pragma warning restore CA2000
		Action<System.Runtime.Loader.AssemblyLoadContext> unloadHandler = null;

		if ( alc is not null && alc != System.Runtime.Loader.AssemblyLoadContext.Default )
		{
			unloadHandler = _ => cts.Cancel();
			alc.Unloading += unloadHandler;
		}

		return System.Threading.Tasks.Task.Run( () =>
		{
			try
			{
				Type[] types;
				try
				{
					types = asm.GetTypes();
				}
				catch ( ReflectionTypeLoadException e )
				{
					types = e.Types.Where( t => t is not null ).ToArray();
				}

				const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;

				//What the assembly names that nothing has loaded yet - asked after the types, which are loaded
				//whatever happens. A method that would bring one of those in is left to be compiled when it is called.
				using var reach = JitReach.For( asm );

				foreach ( var t in types )
				{
					if ( cts.IsCancellationRequested ) break;
					if ( t.IsGenericTypeDefinition ) continue;
					if ( t.BaseType == typeof( MulticastDelegate ) ) continue;

					try
					{
						foreach ( var method in t.GetMethods( flags ) )
						{
							if ( method.IsAbstract ) continue;
							if ( method.ContainsGenericParameters ) continue;
							if ( method.Attributes.HasFlag( MethodAttributes.PinvokeImpl ) ) continue;
							if ( HoldsBack( reach, method ) ) continue;

							//Preparing a p/invoke resolves its native library right here, which throws for anything that isn't
							//shipped on this platform. There's no managed code to JIT anyway, so there's nothing to gain by trying.
							if ( (method.Attributes & MethodAttributes.PinvokeImpl) != 0 ) continue;

							try
							{
								System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod( method.MethodHandle );
							}
							catch ( Exception e )
							{
								logger.Warning( e, $"{e.Message} - {t}.{method}" );
							}
						}

						foreach ( var ctor in t.GetConstructors( flags ) )
						{
							if ( ctor.ContainsGenericParameters ) continue;
							if ( HoldsBack( reach, ctor ) ) continue;

							try
							{
								System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod( ctor.MethodHandle );
							}
							catch ( Exception e )
							{
								logger.Warning( e, $"{e.Message} - {t}.{ctor}" );
							}
						}
					}
					catch ( Exception e )
					{
						logger.Warning( e, $"PreJIT failed for type {t}" );
					}
				}
			}
			finally
			{
				if ( unloadHandler is not null )
					alc.Unloading -= unloadHandler;

				cts.Dispose();
			}
		} );
	}

	//Whether compiling this now would be what brings an assembly in - see JitReach.
	//A method the metadata does not answer for is compiled, as every one was before.
	private static bool HoldsBack( JitReach reach, MethodBase method )
	{
		if ( reach is null ) return false;

		try
		{
			return reach.Reaches( method );
		}
		catch ( Exception e ) when ( e is BadImageFormatException or ArgumentException or InvalidOperationException )
		{
			return false;
		}
	}
}
