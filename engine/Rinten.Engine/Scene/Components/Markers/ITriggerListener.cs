namespace Rinten;

public abstract partial class Component
{
	/// <summary>A <see cref="Component"/> with this interface can react to interactions with triggers.</summary>
	public interface ITriggerListener
	{
		/// <summary>Called when a collider enters the trigger.</summary>
		void OnTriggerEnter( Collider other ) { }

		/// <summary>Called when a collider enters the trigger.</summary>
		void OnTriggerEnter( Collider self, Collider other ) => OnTriggerEnter( other );

		/// <summary>Called when a collider exits the trigger.</summary>
		void OnTriggerExit( Collider other ) { }

		/// <summary>Called when a collider exits the trigger.</summary>
		void OnTriggerExit( Collider self, Collider other ) => OnTriggerExit( other );

		/// <summary>Called when a game object enters the trigger.</summary>
		void OnTriggerEnter( GameObject other ) { }

		/// <summary>Called when a game object enters the trigger.</summary>
		void OnTriggerEnter( Collider self, GameObject other ) => OnTriggerEnter( other );

		/// <summary>Called when a game object exits the trigger.</summary>
		void OnTriggerExit( GameObject other ) { }

		/// <summary>Called when a game object exits the trigger.</summary>
		void OnTriggerExit( Collider self, GameObject other ) => OnTriggerExit( other );
	}
}
