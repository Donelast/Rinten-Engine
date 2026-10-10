using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil;
using Rinten.Upgraders;

namespace Rinten
{
	public partial class Hotload
	{
		/// <summary>Custom instance upgrader for matching types; auto-created per assembly, ordered by attributes</summary>
		public interface IInstanceUpgrader
		{
			bool IsInitialized { get; }
			void Initialize( Hotload hotload );

			void HotloadStart();
			void HotloadComplete();
			void ClearCache();

			bool ShouldProcessType( Type type );

			bool TryCreateNewInstance( object oldInstance, out object newInstance );
			bool TryUpgradeInstance( object oldInstance, object newInstance );
		}

		public interface IInstanceProcessor
		{
			int ProcessInstance( object oldInstance, object newInstance );
		}

#pragma warning disable CA1034 //Nested types should not be visible
		public abstract partial class InstanceUpgrader : IInstanceUpgrader, IInstanceProcessor
#pragma warning restore CA1034 //Nested types should not be visible
		{
			private Hotload Hotload;

			protected DefaultUpgrader DefaultUpgrader { get; private set; }
			protected CachedUpgrader CachedUpgrader { get; private set; }

			public bool IsInitialized => Hotload != null;

			public bool TracePaths => Hotload.TracePaths;

			public ReferencePath CurrentPath
			{
				get => Hotload.CurrentPath;
				set => Hotload.CurrentPath = value;
			}

			public FieldInfo CurrentSrcField
			{
				get => Hotload.CurrentSrcField;
				set => Hotload.CurrentSrcField = value;
			}

			public FieldInfo CurrentDstField
			{
				get => Hotload.CurrentDstField;
				set => Hotload.CurrentDstField = value;
			}

			void IInstanceUpgrader.Initialize( Hotload hotload )
			{
				Hotload = hotload;
				DefaultUpgrader = hotload.GetUpgrader<DefaultUpgrader>();
				CachedUpgrader = hotload.GetUpgrader<CachedUpgrader>();
				OnInitialize();
			}

			void IInstanceUpgrader.HotloadStart()
			{
				OnHotloadStart();
			}

			void IInstanceUpgrader.HotloadComplete()
			{
				OnHotloadComplete();
			}

			void IInstanceUpgrader.ClearCache()
			{
				OnClearCache();
			}

			//A mapping of assembles to swap with new versions.
			protected IReadOnlyDictionary<Assembly, Assembly> Swaps => Hotload.Swaps;

			protected TUpgrader GetUpgrader<TUpgrader>()
				where TUpgrader : IInstanceUpgrader
			{
				return Hotload.GetUpgrader<TUpgrader>();
			}

			protected TUpgrader GetUpgraderOrDefault<TUpgrader>()
				where TUpgrader : IInstanceUpgrader
			{
				return Hotload.TryGetUpgrader<TUpgrader>( out var upgrader ) ? upgrader : default;
			}

			protected bool TryGetUpgrader<TUpgrader>( out TUpgrader upgrader )
				where TUpgrader : IInstanceUpgrader
			{
				return Hotload.TryGetUpgrader( out upgrader );
			}

			protected bool IsAssemblyIgnored( Assembly asm ) => Hotload.IsAssemblyIgnored( asm );

			//When hotswapping this will switch types from the old assembly into the type from the new assembly.
			protected Type GetNewType( Type oldType ) => Hotload.GetNewType( oldType );

			//Upgraded, cached instance of the object; fields may only be filled later in the hotload
			protected object GetNewInstance( object oldInstance ) => Hotload.GetNewInstance( oldInstance );

			protected T GetNewInstance<T>( T oldInstance ) => (T)GetNewInstance( (object)oldInstance );

			protected void AddCachedInstance( object oldInstance, object newInstance )
			{
				CachedUpgrader.AddCachedInstance( oldInstance, newInstance );
			}

			protected void SuppressFinalize( object oldInstance, object newInstance )
			{
				if ( !ReferenceEquals( oldInstance, newInstance ) )
				{
					GC.SuppressFinalize( oldInstance );
				}
			}

			protected bool TryGetDefaultValue( FieldInfo field, out object value ) =>
				Hotload.TryGetDefaultValue( field, out value );

			protected bool IsSwappedType( Type type ) => Hotload.IsSwappedType( type );

			protected bool AreEquivalentTypes( Type oldType, Type newType ) =>
				Hotload.AreEquivalentTypes( oldType, newType );

			protected bool AreEqualTypes( Type a, Type b ) =>
				Hotload.AreEqualTypes( a, b );

			protected MethodBase FindScopeMethod( Type declaringType, int scopeMethodOrdinal ) =>
				Hotload.FindScopeMethod( declaringType, null, scopeMethodOrdinal );

			protected MethodBase FindScopeMethod( Type declaringType, string scopeMethodName, int scopeMethodOrdinal ) =>
				Hotload.FindScopeMethod( declaringType, scopeMethodName, scopeMethodOrdinal );

			protected int GetScopeMethodOrdinal( MethodBase scopeMethod ) =>
				Hotload.GetScopeMethodOrdinal( scopeMethod );

			//Logs a message in the current hotload.
			protected void Log( HotloadEntryType type, FormattableString message, MemberInfo member = null ) =>
				Hotload.Log( type, message, member );

			//Logs an exception in the current hotload.
			protected void Log( Exception exception, FormattableString message = null, MemberInfo member = null ) =>
				Hotload.Log( exception, message, member );

			//Called when this upgrader has been added to a Hotload instance.
			protected virtual void OnInitialize() { }

			protected virtual void OnHotloadStart() { }

			protected virtual void OnHotloadComplete() { }

			//Called between hotloads, should clear up any cached resources that won't be needed in future hotloads.
			protected virtual void OnClearCache() { }

			/// <summary>Check to see if this upgrader can possibly handle the given type.</summary>
			public abstract bool ShouldProcessType( Type type );

			//Catch upgrader exceptions and decline, so one failure doesn't block the old assembly unloading

			public bool TryCreateNewInstance( object oldInstance, out object newInstance )
			{
#if !HOTLOAD_NOCATCH
				try
				{
#endif
					if ( !OnTryCreateNewInstance( oldInstance, out newInstance ) )
					{
						return false;
					}

					OnTryUpgradeInstance( oldInstance, newInstance, false );

					return true;
#if !HOTLOAD_NOCATCH
				}
				catch ( Exception e )
				{
					Log( e, member: oldInstance?.GetType() );

					newInstance = null;
					return false;
				}
#endif
			}

			public bool TryUpgradeInstance( object oldInstance, object newInstance )
			{
#if !HOTLOAD_NOCATCH
				try
				{
#endif
					return OnTryUpgradeInstance( oldInstance, newInstance, true );
#if !HOTLOAD_NOCATCH
				}
				catch ( Exception e )
				{
					Log( e, member: oldInstance?.GetType() );

					return false;
				}
#endif
			}

			protected void ScheduleProcessInstance( object oldInstance, object newInstance )
			{
				Hotload.ScheduleInstanceTask( this, oldInstance, newInstance );
			}

			protected void ScheduleLateProcessInstance( object oldInstance, object newInstance )
			{
				Hotload.ScheduleLateInstanceTask( this, oldInstance, newInstance );
			}

			protected AssemblyDefinition GetAssemblyDefinition( Assembly asm ) => Hotload.GetAssemblyDefinition( asm );
			protected TypeDefinition GetTypeDefinition( Type type ) => Hotload.GetTypeDefinition( type );
			protected MethodDefinition GetMethodDefinition( MethodBase method ) => Hotload.GetMethodDefinition( method );

			//Create an empty replacement for oldInstance if supported; state is copied in OnTryUpgradeInstance
			protected abstract bool OnTryCreateNewInstance( object oldInstance, out object newInstance );

			//Schedule copying state to newInstance; call AddCachedInstance or SuppressFinalize here if needed
			protected abstract bool OnTryUpgradeInstance( object oldInstance, object newInstance, bool createdElsewhere );

			public int ProcessInstance( object oldInstance, object newInstance )
			{
				try
				{
					return OnProcessInstance( oldInstance, newInstance );
				}
#if !HOTLOAD_NOCATCH
				catch ( Exception e )
				{
					Log( e, member: oldInstance.GetType() );
					return 1;
				}
#endif
				finally
				{

				}
			}

			//Perform extra field processing on a new instance that has previously been created by this upgrader in OnTryCreateNewInstance. This is a good
			//place to discover any other instances that should be upgraded that are stored in oldInstance, which can be upgraded by calling GetNewInstance.
			protected virtual int OnProcessInstance( object oldInstance, object newInstance )
			{
				return 1;
			}
		}
	}
}
