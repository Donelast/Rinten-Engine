using Rinten.Internal;
using Rinten.Utility;
using System.Threading;

namespace Rinten;

/// <summary>A GameObject can have many components, which are the building blocks of the game.</summary>
[Expose, ActionGraphIgnore, ActionGraphExposeWhenCached, Icon( "category" )]
public abstract partial class Component : IJsonConvert, IComponentLister, IValid
{
	//Invokes the callback for the given callback type. Called internally by CallbackBatch to avoid delegate allocations.
	internal void InvokeCallback( CommonCallback callback )
	{
		switch ( callback )
		{
			case CommonCallback.Awake: InternalOnAwake(); break;
			case CommonCallback.Enable: DispatchOnEnabled(); break;
			case CommonCallback.Disable: DispatchOnDisabled(); break;
			case CommonCallback.Destroy: OnDestroyInternal(); break;
			case CommonCallback.Validate: OnValidateInternal(); break;
			case CommonCallback.Dirty: OnDirtyInternal(); break;
			case CommonCallback.Loading: LaunchLoader(); break;
		}
	}

	/// <summary>The scene this Component is in. This is a shortcut for `GameObject.Scene`.</summary>
	[ActionGraphInclude]
	public Scene Scene => GameObject?.Scene;

	/// <summary>The transform of the GameObject this component belongs to. Components don't have their own transforms but they can access the transform of the GameObject they belong to. This is a shortcut for `GameObject.Transform`.</summary>
	[ActionGraphInclude]
	public GameTransform Transform => GameObject?.Transform;

	/// <summary>The GameObject this component belongs to.</summary>
	[ActionGraphInclude]
	public GameObject GameObject { get; internal set; }

	//Allow creating tasks that are automatically cancelled when the GameObject is destroyed.
	protected TaskSource Task => GameObject?.Task ?? TaskSource.Cancelled;

	/// <summary>Access components on this component's GameObject</summary>
	public ComponentList Components => GameObject?.Components;

	bool _isInitialized = false;

	//Called to call Awake, once, at startup.
	internal void InitializeComponent()
	{
		if ( _isInitialized ) return;
		if ( GameObject is null ) return;
		if ( !GameObject.Active ) return;

		SceneMetrics.CountComponentCreated();
		_isInitialized = true;

		if ( !GameObject.Flags.Contains( GameObjectFlags.Deserializing ) )
			CheckRequireComponent();

		if ( ShouldExecute )
		{
			CallbackBatch.Add( CommonCallback.Awake, this, "OnAwake" );
		}
	}

	bool _enabledState = false;
	bool _enabled = false;
	bool _onEnabled = false;

	/// <summary>The enable state of this <see cref="Component"/>. This doesn't tell you whether the component is actually active because its parent <see cref="Rinten.GameObject"/> might be disabled. This merely tells you what the component wants to be. You should use <see cref="Active"/> to determine whether the object is truly active in the scene.</summary>
	[ActionGraphInclude]
	public bool Enabled
	{
		get => _enabled;

		set
		{
			if ( _enabled == value ) return;

			_enabled = value;

			UpdateEnabledStatus();
		}
	}

	/// <summary>True if this Component is enabled, and all of its ancestor GameObjects are enabled</summary>
	[ActionGraphInclude]
	public bool Active
	{
		get => _enabledState;
	}

	public bool IsValid => GameObject is not null && Scene is not null;

	//Should this component execute? Should OnUpdate, OnEnabled get called?
	private bool ShouldExecute
	{
		get
		{
			//PrefabCacheScene don't want to OnEnabled or Update or anything
			if ( Scene is PrefabCacheScene ) return false;

			//No scene? No execute.
			if ( Scene is null ) return false;

			//If we're an editor scene, only execute if ExecuteInEditor is enabled
			if ( Scene.IsEditor && this is not ExecuteInEditor ) return false;

			//If we're a dedicated server, don't execute if DontExecuteOnServer is enabled
			if ( Application.IsDedicatedServer && this is DontExecuteOnServer ) return false;

			//Maybe Scene.ExecutionEnabled should exist
			return true;
		}

	}

	//Called once per component
	protected virtual void OnAwake() { }

	private void InternalOnAwake()
	{
		//Fires when a GameObject was created outside the active scene; fix the scope, don't hide it
		{
			//Only pay to build the diagnostic strings when the assert would actually fire.
			if ( Game.ActiveScene is null || GameObject.Scene != Game.ActiveScene )
			{
				var name = $"{GetType().Name} on ({GameObject?.Name ?? "null"})";
				Assert.NotNull( Game.ActiveScene, $"Calling awake on {name} but active scene is null - not {GameObject.Scene}" );
				Assert.AreEqual( GameObject.Scene, Game.ActiveScene, $"Calling awake on {name} but active scene is {Game.ActiveScene}, not {GameObject.Scene}" );
			}
		}

		//Disable any interpolation during OnAwake. We might be created in a Fixed Update context.
		using ( GameTransform.DisableInterpolation() )
		{
			OnAwake();
		}
	}

	//Dispatches OnEnabledInternal if we haven't called it since becoming enabled.
	private void DispatchOnEnabled()
	{
		//make sure we only fire this once, and ensure the component is still enabled
		if ( _onEnabled || !_enabledState || GameObject == null || GameObject.IsDestroyed ) return;

		_onEnabled = true;

		OnEnabledInternal();
	}

	internal virtual void OnEnabledInternal()
	{
		//Disable any interpolation during OnEnabled. We might be created in a Fixed Update context.
		using ( GameTransform.DisableInterpolation() )
		{
			OnEnabled();
			OnComponentEnabled?.Invoke();
		}
	}

	//Called after Awake or whenever the component switches to being enabled (because a gameobject hierarchy active change, or the component changed)
	protected virtual void OnEnabled() { }

	//Dispatches OnDisabledInternal if we haven't called it since becoming disabled.
	private void DispatchOnDisabled()
	{
		//make sure we only fire this once, and ensure the component is still disabled
		if ( !_onEnabled || _enabledState ) return;

		_onEnabled = false;

		OnDisabledInternal();
	}

	internal virtual void OnDisabledInternal()
	{
		//Disable any interpolation during OnDisabled.
		using ( GameTransform.DisableInterpolation() )
		{
			OnDisabled();
			OnComponentDisabled?.Invoke();
		}
	}

	internal virtual void OnDestroyInternal()
	{
		try { OnDestroy(); }
		catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'OnDestroy' on {this}" ); }

		try { OnComponentDestroy?.Invoke(); }
		catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'OnDestroy' on {this}" ); }

		//Unlink from GameObject now so we're no longer valid
		GameObject = null;

		SceneMetrics.CountComponentDestroyed();
	}

	protected virtual void OnDisabled() { }


	//Called once, when the component or gameobject is destroyed
	protected virtual void OnDestroy() { }

	//When enabled, called every frame, does not get called on a dedicated server
	protected virtual void OnPreRender() { }

	internal void OnPreRenderInternal()
	{
		if ( !ShouldExecute )
			return;

		try { OnPreRender(); }
		catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'OnPreRender' on {this}" ); }
	}

	private Action _onComponentUpdate;
	private Action _onComponentFixedUpdate;

	[Group( "Component" )]
	[Property]
	public Action OnComponentEnabled { get; set; }

	[Group( "Component" )]
	[Property]
	public Action OnComponentStart { get; set; }

	[Group( "Component" )]
	[Property]
	public Action OnComponentUpdate
	{
		get => _onComponentUpdate;
		set => SetUpdateAction<IUpdateSubscriber>( ref _onComponentUpdate, value, Scene.updateComponents );
	}

	[Group( "Component" )]
	[Property]
	public Action OnComponentFixedUpdate
	{
		get => _onComponentFixedUpdate;
		set => SetUpdateAction<IFixedUpdateSubscriber>( ref _onComponentFixedUpdate, value, Scene.fixedUpdateComponents );
	}

	[Group( "Component" )]
	[Property]
	public Action OnComponentDisabled { get; set; }

	[Group( "Component" )]
	[Property]
	public Action OnComponentDestroy { get; set; }

	internal void UpdateEnabledStatus()
	{
		using var batch = CallbackBatch.Batch();

		var state = _enabled && Scene is not null && GameObject is not null && GameObject.Active;
		if ( state == _enabledState ) return;

		_enabledState = state;

		if ( _enabledState )
		{
			InitializeComponent();

			if ( ShouldExecute )
			{
				CallbackBatch.Add( CommonCallback.Enable, this, "OnEnabled" );
			}

			Scene.RegisterComponent( this );
		}
		else
		{
			if ( ShouldExecute )
			{
				CallbackBatch.Add( CommonCallback.Disable, this, "OnDisabled" );
			}

			Scene.UnregisterComponent( this );
		}
	}

	//Replaces currentAction with newAction, and adds / removes this component from the given updateSet,
	//depending on whether the new action is null, and this type implements the given TSubscriber interface.
	private void SetUpdateAction<TSubscriber>( ref Action currentAction, Action newAction, HashSetEx<Component> updateSet )
	{
		var hadAction = currentAction is not null;
		var hasAction = newAction is not null;

		currentAction = newAction;

		if ( !_enabledState ) return;
		if ( this is TSubscriber || hadAction == hasAction ) return;

		if ( hasAction )
		{
			updateSet.Add( this );
		}
		else
		{
			updateSet.Remove( this );
		}
	}

	/// <summary>Destroy this component, if it isn't already destroyed. The component will be removed from its GameObject and will stop existing. You should avoid interating with the component after calling this.</summary>
	[ActionGraphInclude]
	public void Destroy()
	{
		using var batch = CallbackBatch.Batch();
		//already destroyed
		if ( !IsValid )
			return;

		GameObject.Components.OnDestroyedInternal( this );
		CallbackBatch.Add( CommonCallback.Destroy, this, "OnDestroy" );

		if ( _enabledState )
		{
			_enabledState = false;
			_enabled = false;
			Scene.UnregisterComponent( this );

			if ( ShouldExecute )
			{
				CallbackBatch.Add( CommonCallback.Disable, this, "OnDisabled" );
			}
		}
	}

	/// <summary>Destroy the parent GameObject. This really only exists so when you're typing Destroy you realise that calling Destroy only destroys the Component - not the whole GameObject.</summary>
	public void DestroyGameObject() => GameObject?.Destroy();

	[ActionGraphInclude]
	public virtual void Reset()
	{
		var t = Game.TypeLibrary.GetType( GetType() );
		var so = Game.TypeLibrary.GetSerializedObject( this );

		foreach ( var field in t.Fields.Where( x => x.HasAttribute<PropertyAttribute>() ) )
		{
			var serialized = so.GetProperty( field.Name );
			serialized.SetValue( serialized.GetDefault() );
		}

		foreach ( var prop in t.Properties.Where( x => x.HasAttribute<PropertyAttribute>() ) )
		{
			var serialized = so.GetProperty( prop.Name );
			serialized.SetValue( serialized.GetDefault() );
		}
	}

	//Called immediately after deserializing, and when a property is changed in the editor.
	protected virtual void OnValidate()
	{

	}

	//Called immediately after being refreshed from a network snapshot.
	protected virtual void OnRefresh()
	{

	}

	internal void OnValidateInternal()
	{
		try { OnValidate(); }
		catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'OnValidate' on {this}" ); }
	}

	internal void Validate()
	{
		CallbackBatch.Add( CommonCallback.Validate, this, "OnValidate" );
	}

	internal void OnRefreshInternal()
	{
		OnRefresh();
	}

	/// <summary>Called when something on the component has been edited</summary>
	[Obsolete( "EditLog is obsolete use Scene.Editor.UndoScope or Scene.Editor.AddUndo instead." )]
	public void EditLog( string name, object source )
	{
		OnValidateInternal();
	}

	/// <inheritdoc cref="GameObject.Tags"/>
	public ITagSet Tags => GameObject.Tags;

	//When tags have been updated
	protected virtual void OnTagsChanged()
	{

	}

	internal virtual void OnTagsUpdatedInternal()
	{
		OnTagsChanged();
	}


	//The parent has changed from one parent to another
	protected virtual void OnParentChanged( GameObject oldParent, GameObject newParent )
	{

	}

	internal void OnParentChangedInternal( GameObject oldParent, GameObject newParent )
	{
		OnParentChanged( oldParent, newParent );
	}

	/// <summary>Invoke a method in x seconds. Won't be invoked if the component is no longer active.</summary>
	public async void Invoke( float secondsDelay, Action action, CancellationToken ct = default )
	{
		try
		{
			await Task.DelaySeconds( secondsDelay, ct );
		}
		catch ( OperationCanceledException )
		{
			return;
		}

		if ( !this.IsValid() ) return;
		if ( !this.Active ) return;

		action.InvokeWithWarning();
	}



	internal void OnParentDestroyInternal()
	{
		OnParentDestroy();
	}

	/// <summary>The parent object is being destroyed. This is a nice place to switch to a healthier parent.</summary>
	public virtual void OnParentDestroy()
	{

	}
}
