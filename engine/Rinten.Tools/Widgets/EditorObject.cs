using System;

namespace Editor
{
	/// <summary>The base of everything in the editor's widget layer.</summary>
	public class EditorObject : IValid
	{
		/// <summary>Whether this object has been destroyed.</summary>
		public bool IsDestroyed { get; private set; }

		bool IValid.IsValid => IsValid;

		public bool IsValid => !IsDestroyed;

		public virtual void OnDestroyed()
		{
		}

		public void Destroy()
		{
			if ( IsDestroyed )
				return;

			IsDestroyed = true;

			EditorEvent.Unregister( this );

			//Unregister shortcuts too, or destroyed widgets keep catching them
			EditorShortcuts.Unregister( this );

			OnDestroyingLater();
			OnDestroyed();
		}

		internal virtual void OnDestroyingLater()
		{
		}


		public void SetProperty( string name, bool value ) => SetProperty( name, value ? "true" : "false" );

		public void SetProperty( string name, float value ) => SetProperty( name, value.ToString() );

		public void SetProperty( string name, string value )
		{
			properties[name] = value;

			OnPropertySet( name );
		}

		private readonly Dictionary<string, string> properties = new();

		//What a property was set to, or null.
		internal string GetManagedProperty( string name )
		{
			return properties.TryGetValue( name, out var value ) ? value : null;
		}

		//A widget puts the value on its panel as a class.
		private protected virtual void OnPropertySet( string name )
		{
		}

		public Rinten.Bind.Builder Bind( string targetName, Action onChanged = null )
		{
			var bb = new Rinten.Bind.Builder
			{
				system = Rinten.Internal.GlobalToolsNamespace.BindSystem
			};

			return bb.Set( this, targetName, onChanged );
		}
	}
}
