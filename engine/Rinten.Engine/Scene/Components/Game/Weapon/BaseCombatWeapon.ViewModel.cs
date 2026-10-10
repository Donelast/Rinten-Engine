namespace Rinten;

public partial class BaseCombatWeapon
{
	/// <summary>First-person view model prefab. Spawned only on the client holding this item.</summary>
	[Property, Feature( "ViewModel" )] public GameObject ViewModelPrefab { get; set; }

	/// <summary>The spawned view model instance, or null. Owner-only and not networked - each client makes its own.</summary>
	public GameObject ViewModel { get; protected set; }

	//Spawns the view model from ViewModelPrefab and plays its deploy presentation. Owner only, idempotent - safe to call when one already exists.
	protected virtual void CreateViewModel()
	{
		//Already have one - creating is idempotent so it can be called defensively.
		if ( ViewModel.IsValid() )
			return;

		if ( ViewModelPrefab is null )
			return;

		//View models are first person and local only - never spawned on proxies.
		if ( IsProxy || !IsHeld )
			return;

		ViewModel = ViewModelPrefab.Clone( new CloneConfig
		{
			Parent = GameObject,
			StartEnabled = false,
			Transform = global::Transform.Zero
		} );

		ViewModel.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Absolute;
		ViewModel.Tags.Add( "firstperson", "viewmodel" );
		ViewModel.Enabled = true;

		//Play the model's deploy presentation (draw animation, deploy sound).
		ViewModel.GetComponentInChildren<BaseWeaponModel>()?.OnDeploy();
	}

	//Destroys the spawned view model, if any.
	protected virtual void DestroyViewModel()
	{
		ViewModel?.Destroy();
		ViewModel = null;
	}
}
