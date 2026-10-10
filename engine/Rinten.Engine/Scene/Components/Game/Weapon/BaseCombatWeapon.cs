namespace Rinten;

//HoldType names an option of the holder's animgraph "holdtype" enum; the engine ships none
/// <summary>Base for held items: view model, world model, aim ray and ShootBullet helpers.</summary>
[Title( "Weapon" )]
[Group( "Game" )]
[Icon( "sports_martial_arts" )]
[Alias( "Rinten.BaseWeapon" )]
public partial class BaseCombatWeapon : BaseInventoryItem
{
	/// <summary>The player controller holding this, or null if it isn't held by one (it's in the world, or held by something that isn't a <see cref="PlayerController"/>). Derived from the hierarchy.</summary>
	public PlayerController Owner => GetComponentInParent<PlayerController>( true );

	/// <summary>True while held by a player.</summary>
	public bool IsHeld => Owner.IsValid();

	/// <summary>The body renderer the world model attaches to - the holder's renderer.</summary>
	public SkinnedModelRenderer HolderRenderer => Owner?.Renderer;

	/// <summary>Where this weapon is aiming. When held by a player: the holder's eye-forward, or the camera-forward in third person (so you aim where the camera looks, not where the head points). When not held it defers to <see cref="UnheldAimRay"/>.</summary>
	public virtual Ray AimRay
	{
		get
		{
			//Owner walks the hierarchy - resolve it once.
			var owner = Owner;

			if ( owner.IsValid() )
			{
				if ( owner.ThirdPerson && Scene.Camera.IsValid() )
					return Scene.Camera.WorldTransform.ForwardRay;

				return owner.EyeTransform.ForwardRay;
			}

			return UnheldAimRay;
		}
	}

	//The aim ray to use when this weapon isn't held by a player - placed in the world, mounted,
	//or controlled from a seat. Base fires from the muzzle; override for camera-targeted aim.
	protected virtual Ray UnheldAimRay
	{
		get
		{
			var muzzle = GetMuzzleTransform();
			return new Ray( muzzle.Position, muzzle.Rotation.Forward );
		}
	}

	/// <summary>The <see cref="BaseWeaponModel"/> on the currently-shown model - the view model when it's being drawn, otherwise the world model, otherwise the weapon's own hierarchy (standalone weapons). Null when none carries one.</summary>
	public BaseWeaponModel WeaponModel
	{
		get
		{
			var go = ViewModel;

			//The view model still exists in third person, it's just not drawn - effects (muzzle flash, tracers) should come from the world model's muzzle instead.
			if ( Scene.Camera.IsValid() && Scene.Camera.RenderExcludeTags.Has( "firstperson" ) )
				go = null;

			if ( !go.IsValid() )
				go = WorldModel;

			if ( go.IsValid() && go.GetComponentInChildren<BaseWeaponModel>() is { } model )
				return model;

			//Standalone weapons may carry a model in their own hierarchy without spawning view/world models.
			return GameObject.GetComponentInChildren<BaseWeaponModel>();
		}
	}

	/// <summary>The transform that shots and muzzle effects fire from. Resolves the active weapon model's muzzle attachment, then the weapon itself. Override to add other model-driven resolution.</summary>
	public virtual Transform GetMuzzleTransform()
	{
		var model = WeaponModel;
		if ( model.IsValid() && model.MuzzleGameObject.IsValid() )
			return model.GetMuzzleTransform();

		return WorldTransform;
	}

	//Drive the holder's body animation for this weapon. Runs every frame while deployed, on every peer, with the
	//holder's renderer. Base sets the hold type; override to drive aiming, leaning and any other body parameters.
	protected virtual void UpdateBodyAnimations( SkinnedModelRenderer body )
	{
		if ( !string.IsNullOrEmpty( HoldType ) )
		{
			body.Set( "holdtype", HoldType );
			body.Set( "holdtype_handedness", (int)Handedness );
		}
	}

	protected override void OnEquipped()
	{
		base.OnEquipped();

		CreateWorldModel();
		CreateViewModel();

		//Can't fire for a moment after switching to the weapon.
		SetNextPrimaryFire( DeployTime );
		SetNextSecondaryFire( DeployTime );
	}

	protected override void OnHolstered()
	{
		base.OnHolstered();

		//Empty hands - the next weapon's equip sets its own hold type.
		if ( !string.IsNullOrEmpty( HoldType ) )
			HolderRenderer?.Set( "holdtype", "none" );

		CancelReload();

		DestroyWorldModel();
		DestroyViewModel();
	}
}
