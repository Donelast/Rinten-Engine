using System.Reflection;

namespace Rinten.UI;

public partial class Panel
{
	internal struct EventCallback
	{
		public string EventName;
		public Action BaseAction;
		public Action<PanelEvent> Action;
		public bool Automatic;

		public Action<EventCallback, PanelEvent> Event;
		public Panel Panel;
		public Panel Context;
	}

	internal List<EventCallback> EventListeners { get; set; }

	//One method a panel type marked with PanelEventAttribute: the event it answers, the method, and
	//what it takes - nothing, the PanelEvent itself, or the event's value converted to this type.
	sealed record PanelEventMethod( string Name, MethodInfo Method, int Arguments, Type Argument );

	//Event methods per panel type, computed once and cached weakly so hotloaded types get their own
	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Type, PanelEventMethod[]> panelEvents = new();

	static PanelEventMethod[] PanelEventsOf( Type type )
	{
		if ( panelEvents.TryGetValue( type, out var known ) ) return known;

		if ( Game.TypeLibrary == null || !Game.TypeLibrary.TryGetType( type, out var typeDescription ) )
			return null;

		var found = new List<PanelEventMethod>();

		foreach ( var methodDesc in typeDescription.Members.OfType<MethodDescription>() )
		{
			if ( methodDesc.IsStatic ) continue;

			var pea = methodDesc.Attributes.OfType<PanelEventAttribute>().FirstOrDefault();
			if ( pea == null ) continue;

			var method = methodDesc.MemberInfo as MethodInfo;
			var event_name = methodDesc.Name.ToLower();

			var args = method.GetParameters();

			if ( event_name.EndsWith( "event" ) ) event_name = event_name[..^5];
			if ( pea.Name != null ) event_name = pea.Name.ToLower();

			if ( args.Length > 1 )
			{
				Log.Warning( $"PanelEvent {method} - couldn't set up (too many arguments)" );
				continue;
			}

			found.Add( new PanelEventMethod( event_name, method, args.Length, args.Length == 1 ? args[0].ParameterType : null ) );
		}

		var made = found.ToArray();
		panelEvents.AddOrUpdate( type, made );

		return made;
	}

	//Called on creation and hotload to delete and re-initialize event listeners.
	protected virtual void InitializeEvents()
	{
		EventListeners?.RemoveAll( x => x.Automatic );

		if ( PanelEventsOf( GetType() ) is not { } methods ) return;

		foreach ( var one in methods )
		{
			var method = one.Method;

			if ( one.Arguments == 1 )
			{
				var argType = one.Argument;
				var argcache = new object[1];

				if ( argType == typeof( PanelEvent ) )
				{
					AddAutomaticEventListener( one.Name, ( x ) => { argcache[0] = x; method.Invoke( this, argcache ); } );
				}
				else
				{
					AddAutomaticEventListener( one.Name, ( x ) =>
					{
						argcache[0] = Convert.ChangeType( x.Value, argType );
						var response = method.Invoke( this, argcache );

						//If the response is a bool, and false, stop propogation
						if ( response != null && response is bool bReturnedValue )
						{
							x.Propagate = x.Propagate && bReturnedValue;
						}

					} );

				}
			}
			else
			{
				AddAutomaticEventListener( one.Name, ( x ) =>
				{
					var response = method.Invoke( this, null );

					//If the response is a bool, and false, stop propogation
					if ( response != null && response is bool bReturnedValue )
					{
						x.Propagate = x.Propagate && bReturnedValue;
					}

				} );
			}
		}
	}

	internal void AddAutomaticEventListener( string name, Action<PanelEvent> e )
	{
		EventListeners ??= new List<EventCallback>();

		var ev = new EventCallback
		{
			EventName = name,
			Action = e,
			Automatic = true
		};

		EventListeners.Add( ev );
	}

	internal void RemoveEventListener( string name )
	{
		EventListeners?.RemoveAll( x => x.EventName == name );
	}

	/// <summary>Runs given callback when the given event is triggered.</summary>
	public void AddEventListener( string eventName, Action<PanelEvent> e )
	{
		AddEventListener( new EventCallback
		{
			EventName = eventName,
			Action = e
		} );
	}

	/// <summary>Runs given callback when the given event is triggered, without access to the <see cref="PanelEvent"/>.</summary>
	public void AddEventListener( string eventName, Action action )
	{
		AddEventListener( new EventCallback
		{
			EventName = eventName,
			BaseAction = action
		} );
	}

	internal void AddEventListener( EventCallback eventCallback )
	{
		EventListeners ??= new List<EventCallback>();
		EventListeners.Add( eventCallback );
	}

	List<PanelEvent> PendingEvents;

	internal void RunPendingEvents()
	{
		if ( PendingEvents is null || PendingEvents.Count == 0 ) return;

		for ( int i = 0; i < PendingEvents.Count; i++ )
		{
			var e = PendingEvents[i];
			if ( e.Time > TimeNow ) continue;

			PendingEvents.RemoveAt( i );
			i--;

			try
			{
				OnEvent( e );
			}
			catch ( System.Exception ex )
			{
				Log.Error( ex, $"\"{ex.Message}\" when running panel event \"{e.Name}\" from \"{e.Target}\"" );
			}
		}
	}

	//Total events ever queued, so the drain walk is skipped when nothing was queued since the last one
	private static long eventsQueued;

	//What eventsQueued stood at when this panel last walked its subtree.
	private long eventsWalkedAt = -1;

	//Run queued events on this panel and every descendant.
	internal void RunPendingEventsDeep()
	{
		if ( eventsWalkedAt == eventsQueued ) return;

		eventsWalkedAt = eventsQueued;

		RunPendingEvents();

		if ( _children is not { Count: > 0 } children )
			return;

		var count = children.Count;
		var snapshot = System.Buffers.ArrayPool<Panel>.Shared.Rent( count );

		try
		{
			children.CopyTo( snapshot, 0 );

			for ( int i = 0; i < count; i++ )
			{
				var child = snapshot[i];
				if ( child is null ) continue;
				if ( child.Parent != this ) continue;
				if ( !child.IsValid ) continue;

				child.RunPendingEventsDeep();
			}
		}
		finally
		{
			System.Buffers.ArrayPool<Panel>.Shared.Return( snapshot, clearArray: true );
		}
	}

	/// <summary>Create a new event and pass it to the panels event queue.</summary>
	public virtual void CreateEvent( string name, object value = null, float? debounce = null )
	{
		var e = PendingEvents?.FirstOrDefault( x => x.Name == name );
		if ( e == null )
		{
			e = new PanelEvent( name, this );
			CreateEvent( e );
		}

		e.Value = value;

		if ( debounce.HasValue )
		{
			e.Time = (float)(TimeNow + debounce.Value);
		}
	}


	/// <summary>Pass given event to the event queue.</summary>
	public virtual void CreateEvent( PanelEvent evnt )
	{
		PendingEvents ??= new List<PanelEvent>();
		PendingEvents.Add( evnt );

		//Somebody has something to run: the walk that runs it may skip itself until this moves - see RunPendingEventsDeep.
		eventsQueued++;

		RequestTick();
	}

	//Called when various PanelEvents happen. Handles event listeners and many standard events by default.
	protected virtual void OnEvent( PanelEvent e )
	{
		e.This = this;

		//Answered once, by the panel it was made on. GetClipboardValue and OnPaste walk up to the parents themselves when a panel has nothing of its own, so
		//the event going on up as well asked the whole chain a second time - and a parent with something to copy of its own wrote over what was just copied.
		if ( e is CopyEvent || e is CutEvent )
		{
			var text = GetClipboardValue( e is CutEvent );
			if ( text != null )
			{
				EnginePlatform.SetClipboardText( text );
			}

			e.StopPropagation();
		}

		if ( e is PasteEvent paste )
		{
			OnPaste( paste.ClipboardValue );
			e.StopPropagation();
		}

		if ( e is MousePanelEvent mpe )
		{
			if ( e.Is( "onclick" ) ) OnClick( mpe );
			if ( e.Is( "onmiddleclick" ) ) OnMiddleClick( mpe );
			if ( e.Is( "onrightclick" ) ) OnRightClick( mpe );
			if ( e.Is( "onmousedown" ) ) OnMouseDown( mpe );
			if ( e.Is( "onmouseup" ) ) OnMouseUp( mpe );
			if ( e.Is( "ondoubleclick" ) ) OnDoubleClick( mpe );
			if ( e.Is( "onmousemove" ) ) OnMouseMove( mpe );
			if ( e.Is( "onmouseover" ) ) OnMouseOver( mpe );
			if ( e.Is( "onmouseout" ) ) OnMouseOut( mpe );

			if ( !e.Is( "onmousemove" ) )
			{
				//Asked for, not assumed: a tree that is not ticked every frame would otherwise sit dirty until something else came by.
				razorTreeDirty = true;
				RequestTick();
			}
		}

		if ( e is DragEvent de )
		{
			InternalDragEvent( de );
		}

		if ( e.Is( "ondrop" ) ) OnDrop( e );
		if ( e.Is( "ondragenter" ) ) OnDragEnter( e );
		if ( e.Is( "ondragleave" ) ) OnDragLeave( e );

		if ( e.Is( "onfocus" ) ) OnFocus( e );
		if ( e.Is( "onblur" ) ) OnBlur( e );
		if ( e.Is( "onback" ) ) OnBack( e );
		if ( e.Is( "onforward" ) ) OnForward( e );
		if ( e.Is( "onescape" ) ) OnEscape( e );

		if ( e is SelectionEvent se )
		{
			if ( e.Is( "ondragselect" ) ) OnDragSelect( se );
		}

		if ( !e.Propagate )
			return;

		if ( EventListeners != null )
		{
			foreach ( var listener in EventListeners )
			{
				if ( !e.Is( listener.EventName ) ) continue;

				listener.Event?.Invoke( listener, e );
				listener.Action?.Invoke( e );
				listener.BaseAction?.Invoke();
			}
		}

		if ( !e.Propagate ) return;

		Parent?.OnEvent( e );
	}

	//Called when the player releases their left mouse button (Mouse 1) while hovering this panel.
	protected virtual void OnClick( MousePanelEvent e ) { }

	//Called when the player releases their middle mouse button (Mouse 3) while hovering this panel.
	protected virtual void OnMiddleClick( MousePanelEvent e ) { }

	//Called when the player releases their right mouse button (Mouse 2) while hovering this panel.
	protected virtual void OnRightClick( MousePanelEvent e ) { }

	//Called when the player presses down the left or right mouse buttons while hovering this panel.
	protected virtual void OnMouseDown( MousePanelEvent e ) { }

	//Called when the player releases left or right mouse button.
	protected virtual void OnMouseUp( MousePanelEvent e ) { }

	//Called when the player double clicks the panel with the left mouse button.
	protected virtual void OnDoubleClick( MousePanelEvent e ) { }

	//Called when the cursor moves while hovering this panel.
	protected virtual void OnMouseMove( MousePanelEvent e ) { }

	//Called when the cursor enters this panel.
	protected virtual void OnMouseOver( MousePanelEvent e ) { }

	//Called when the cursor leaves this panel.
	protected virtual void OnMouseOut( MousePanelEvent e ) { }

	//Called when the player presses the "Back" button while hovering this panel, which is typically "mouse 5", aka one of the mouse buttons on its side.
	protected virtual void OnBack( PanelEvent e ) { }


	//Called when the player presses the "Forward" button while hovering this panel, which is typically "mouse 4", aka one of the mouse buttons on its side.
	protected virtual void OnForward( PanelEvent e ) { }

	//Called when the escape key is pressed
	protected virtual void OnEscape( PanelEvent e )
	{
		if ( HasFocus )
			Blur();
	}

	//Called when this panel receives input focus.
	protected virtual void OnFocus( PanelEvent e ) { }

	//Called when this panel loses input focus.
	protected virtual void OnBlur( PanelEvent e ) { }
}
