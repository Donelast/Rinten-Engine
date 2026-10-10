using Rinten.UI.Construct;

namespace Rinten.UI;

public partial class Panel
{
	/// <summary>Quickly add common panels with certain values as children.</summary>
	[Hide]
	public PanelCreator Add => new( this );

	internal List<Panel> _children;

	internal bool _renderChildrenDirty;
	internal List<Panel> _renderChildren;
	internal HashSet<Panel> _childrenHash;

	internal Panel _parent;

	/// <summary>List of panels that are attached/<see cref="Parent">parented</see> directly to this one.</summary>
	[Hide]
	public ChildList Children => new( _children );

	/// <summary>The children of a panel, skipping the nulls a hotload leaves behind when the type of a live panel is deleted.</summary>
	public readonly struct ChildList : IEnumerable<Panel>
	{
		private readonly List<Panel> children;

		internal ChildList( List<Panel> children ) => this.children = children;

		/// <summary>The enumerator <c>foreach</c> binds to, which allocates nothing.</summary>
		public Enumerator GetEnumerator() => new( children );

		IEnumerator<Panel> IEnumerable<Panel>.GetEnumerator() => GetEnumerator();

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

		/// <summary>Walks the list, passing over anything that is no longer there.</summary>
		public struct Enumerator : IEnumerator<Panel>
		{
			private readonly List<Panel> children;
			private int index;

			internal Enumerator( List<Panel> children )
			{
				this.children = children;
				index = -1;
				Current = null;
			}

			public Panel Current { get; private set; }

			object System.Collections.IEnumerator.Current => Current;

			public bool MoveNext()
			{
				if ( children is null ) return false;

				while ( ++index < children.Count )
				{
					//The list is written to while it is walked - a panel deleting itself in a style
					//pass is ordinary - so the count is read each time round rather than kept.
					var child = children[index];
					if ( child is null ) continue;

					Current = child;
					return true;
				}

				Current = null;
				return false;
			}

			public void Reset()
			{
				index = -1;
				Current = null;
			}

			public void Dispose()
			{
			}
		}
	}

	/// <summary>Whether this panel has any <see cref="Children">child panels</see> at all.</summary>
	[Hide]
	public bool HasChildren => _children is not null && _children.Count > 0;

	/// <summary>The panel we are directly attached to. This panel will be positioned relative to the given parent, and therefore move with it, typically also be hidden by the parents bounds.</summary>
	[Hide]
	public Panel Parent
	{
		get => _parent;
		set
		{
			if ( this is RootPanel && value != null )
				throw new Exception( "Can't parent a RootPanel" );

			if ( value == this )
				throw new Exception( "Can't parent a panel to itself" );

			if ( _parent == value )
				return;

			//These types can't have children, so set them as siblings of us.
			if ( value is Label || value is Image )
			{
				Parent = value.Parent;
				Parent.SetChildIndex( this, Parent.GetChildIndex( value ) + 1 );
				return;
			}

			var oldParent = _parent;
			_parent = null;

			if ( oldParent != null )
			{
				oldParent.RemoveChild( this );
			}

			_parent = value;

			if ( _parent != null )
			{
				_parent.InternalAddChild( this );

				if ( oldParent == null )
				{
					AddToLists();
					ReloadTemplateStyleSheetWhenParented();
				}

				//We can set some inherited stuff here to get us started
				ScaleToScreen = _parent.ScaleToScreen;
			}

			NoteStructureChanged();

			//Shown or hidden with where it now is, rather than where it was - see InheritVisibility.
			InheritVisibility();

			ParentHasChanged = true;

			//Asked afresh from here: a panel marks itself before it has a parent, and a mark that stops at an already-marked panel would never reach the new chain.
			tickRequested = false;
			RequestTick();

			UpdateSceneIndex();
		}
	}

	bool ParentHasChanged;
	bool IndexesDirty;

	//Called internally when a child is removed, to remove from our Children list
	private void RemoveChild( Panel p )
	{
		if ( IsDeleted )
			return;

		if ( _children == null )
			throw new System.Exception( "RemoveChild but no children!" );

		if ( _childrenHash.Remove( p ) )
		{
			_children.Remove( p );
			_renderChildren.Remove( p );
			_renderChildrenDirty = true;

			if ( p.LayoutTree is not null )
			{
				LayoutTree?.RemoveChild( p.LayoutTree );
			}

			OnChildRemoved( p );
			UpdateChildrenIndexes();
			SetNeedsPreLayout();

			//Gone from the walk and from under the pointer.
			MoveGeneration();

			//The child's parent is already null, so the count comes off this chain.
			if ( p.TickDemand != 0 ) AdjustTickDemand( -p.TickDemand );
		}
		else
		{
			throw new Exception( "Removed Child but didn't have child!" );
		}
	}

	//A child panel has been removed from this panel.
	protected virtual void OnChildRemoved( Panel child )
	{

	}

	/// <summary>Deletes all child panels via <see cref="Delete"/>.</summary>
	public void DeleteChildren( bool immediate = false )
	{
		foreach ( var child in Children.ToArray() )
		{
			child.Delete( immediate );
		}
	}

	/// <summary>Add given panel as a child to this panel.</summary>
	public T AddChild<T>( T p ) where T : Panel
	{
		Assert.False( IsDeleted );

		p.Parent = this;
		return p;
	}

	//Called internally when a child is added, to add to our children list.
	private void InternalAddChild( Panel child )
	{
		if ( LayoutTree?.IsMeasureDefined == true )
			throw new Exception( $"{this} can not have children." );

		_children ??= new( 4 );
		_renderChildren ??= new( 4 );
		_childrenHash ??= new( 4 );

		if ( _childrenHash.Contains( child ) )
			throw new Exception( "AddChild but already have child!" );

		LayoutTree?.AddChild( child.LayoutTree );

		_childrenHash.Add( child );
		_children.Add( child );
		_renderChildren.Add( child );
		_renderChildrenDirty = true;

		//New in the walk's order, whatever it will draw.
		MoveGeneration();

		var childCount = _children.Count;
		if ( childCount >= 2 )
		{
			//Update the previous last-child immediately so it has the correct pseudo-classes BEFORE BuildStyleRules() runs.
			//Without this, the previous last child temporarily keeps :last-child, causing incorrect styles for a single frame.
			_children[childCount - 2].UpdateSiblingIndex( childCount - 2, childCount );
		}

		child.UpdateSiblingIndex( childCount - 1, childCount );
		OnChildAdded( child );
		SetNeedsPreLayout();

		if ( child.TickDemand != 0 ) AdjustTickDemand( child.TickDemand );

		//The tick re-pins ::before and ::after around the newcomer.
		RequestTick();

		IndexesDirty = true;
	}

	//A child panel has been added to this panel.
	protected virtual void OnChildAdded( Panel child )
	{

	}

	/// <summary>Sort the <see cref="Children">children</see> using given comparison function.</summary>
	public void SortChildren( Comparison<Panel> sorter )
	{
		if ( _children == null || _children.Count <= 0 )
			return;

		_children.RemoveAll( x => x is null );
		_children.Sort( sorter );

		//Paint order is children order.
		MoveGeneration();

		int i = 0;
		foreach ( var child in _children )
		{
			child.UpdateSiblingIndex( i++, _children.Count );
			LayoutTree.RemoveChild( child.LayoutTree );
			LayoutTree.AddChild( child.LayoutTree );
		}

		RequestTick();
		IndexesDirty = true;
	}

	/// <summary>Sort the <see cref="Children">children</see> using given comparison function.</summary>
	public void SortChildren<TargetType>( Func<TargetType, int> sorter )
	{
		if ( _children == null || _children.Count <= 0 )
			return;

		_children.RemoveAll( x => x is null );

		var sorted = _children.OrderBy( x => { if ( x is TargetType tt ) { return sorter( tt ); } return 0; } ).ToArray();
		_children.Clear();
		_children.AddRange( sorted );
		RequestTick();
		MoveGeneration();

		foreach ( var child in _children )
		{
			LayoutTree.RemoveChild( child.LayoutTree );
			LayoutTree.AddChild( child.LayoutTree );
		}

		IndexesDirty = true;
	}

	/// <summary>Sort the <see cref="Children">children</see> using given comparison function.</summary>
	public void SortChildren( Func<Panel, int> sorter ) => SortChildren<Panel>( sorter );

	//Can be overridden by children to determine whether the panel is empty, and the :empty pseudo-class should be applied.
	protected virtual bool IsPanelEmpty()
	{
		return ChildrenCount == 0;
	}

	//Should be called if overriding IsEmpty to notify the panel that its empty state has changed.
	protected void EmptyStateChanged()
	{
		UpdateChildrenIndexes();
	}

	void UpdateChildrenIndexes()
	{
		IndexesDirty = false;

		Switch( PseudoClass.Empty, IsPanelEmpty() );

		var count = ChildrenCount;
		if ( count == 0 )
			return;

		//Scrollbars aren't siblings of the content, so the real last child keeps :last-child
		var siblings = count - ScrollbarCount;

		for ( int i = 0; i < count; i++ )
		{
			_children[i].UpdateSiblingIndex( i, siblings );
		}
	}

	internal void UpdateSiblingIndex( int index, int siblings )
	{
		SiblingIndex = index;

		if ( this is ScrollBar )
			return;

		Switch( PseudoClass.FirstChild, index == 0 );
		Switch( PseudoClass.LastChild, index == siblings - 1 );
		Switch( PseudoClass.OnlyChild, index == 0 && siblings == 1 );
	}

	/// <summary>The index of this panel in its parent's child list.</summary>
	[Hide]
	public int SiblingIndex { get; internal set; } = -1;


	/// <summary>Creates a panel of given type and makes it our child.</summary>
	public T AddChild<T>( string classnames = null ) where T : Panel, new()
	{
		var t = new T();
		t.Parent = this;

		if ( classnames != null )
			t.AddClass( classnames );

		return t;
	}

	/// <summary>Creates a panel of given type and makes it our child, returning it as an out argument.</summary>
	public bool AddChild<T>( out T outPanel, string classnames = null ) where T : Panel, new()
	{
		var t = new T();
		t.Parent = this;

		if ( classnames != null )
			t.AddClass( classnames );

		outPanel = t;
		return true;
	}

	/// <summary>Returns this panel and all its <see cref="Ancestors">ancestors</see>, i.e. the <see cref="Parent">Parent</see>, parent of its parent, etc.</summary>
	[Hide]
	public IEnumerable<Panel> AncestorsAndSelf
	{
		get
		{
			var p = this;

			while ( p != null )
			{
				yield return p;
				p = p.Parent;
			}
		}
	}

	/// <summary>Returns all ancestors, i.e. the parent, parent of our parent, etc.</summary>
	[Hide]
	public IEnumerable<Panel> Ancestors
	{
		get
		{
			var p = this.Parent;

			while ( p != null )
			{
				yield return p;
				p = p.Parent;
			}
		}
	}

	/// <summary>List of all panels that are attached to this panel, recursively, i.e. all <see cref="Children">children</see> of this panel, children of those children, etc.</summary>
	[Hide]
	public IEnumerable<Panel> Descendants
	{
		get
		{
			foreach ( var child in Children )
			{
				yield return child;

				foreach ( var descendant in child.Descendants )
				{
					yield return descendant;
				}
			}
		}
	}

	/// <summary>Is the given panel a parent, grandparent, etc.</summary>
	public bool IsAncestor( Panel panel )
	{
		if ( panel == this ) return true;
		if ( Parent != null ) return Parent.IsAncestor( panel );
		return false;
	}

	/// <summary>Returns the <see cref="RootPanel"/> we are ultimately attached to, if any.</summary>
	public RootPanel FindRootPanel()
	{
		if ( this is RootPanel rp ) return rp;
		return Parent?.FindRootPanel();
	}

	/// <summary>Returns the first <see cref="Ancestors">ancestor</see> panel that has no parent.</summary>
	public virtual Panel FindPopupPanel()
	{
		if ( Parent == null ) return this;
		return Parent?.FindPopupPanel();
	}

	/// <summary>Returns the editor overlay on this window, or <see cref="FindPopupPanel"/>.</summary>
	public Panel FindOverlayPanel()
	{
		if ( ResolveOverlayPanelHandler != null )
		{
			var overlay = ResolveOverlayPanelHandler( this );

			if ( overlay.IsValid() )
				return overlay;
		}

		var root = FindRootPanel();

		if ( root is not null )
		{
			foreach ( var child in root.Children )
			{
				if ( child.HasClass( "editor-overlay" ) )
					return child;
			}
		}

		return FindPopupPanel();
	}

	//Editor hook: returns the overlay panel for popups on this window.
	internal static Func<Panel, Panel> ResolveOverlayPanelHandler;

	//Editor hook: returns a source rect in overlay window pixels.
	internal static Func<Panel, Rect> OverlayAnchorRectHandler;

	//Editor hook: clamps a wanted point on the overlay.
	internal static Func<Panel, Vector2, Vector2, Vector2> OverlayPlaceHandler;

	//Editor hook: dismisses overlay popups when a press lands on this panel.
	internal static Action<Panel> PopupPressedHandler;

	/// <summary>Editor hook: when true, popup dismissal and global press handlers are skipped for this target.</summary>
	[Hide]
	public static Func<Panel, bool> SuppressPopupDismissal;

	/// <summary>Overlay window pixels for popup anchoring, or <see cref="Box.Rect"/>.</summary>
	public Rect GetOverlayAnchorRect()
	{
		if ( OverlayAnchorRectHandler != null )
			return OverlayAnchorRectHandler( this );

		return Box.Rect;
	}

	/// <summary>Mounts on the widget surface above a scroll ancestor inside it.</summary>
	public Panel FindScrollPopupMount()
	{
		Panel scrollPanel = null;

		for ( var panel = this; panel is not null; panel = panel.Parent )
		{
			if ( panel.ComputedStyle?.Overflow == OverflowMode.Scroll )
				scrollPanel = panel;

			if ( panel.GetType().Name == "WidgetSurface" )
				return scrollPanel is not null ? panel : null;
		}

		if ( scrollPanel?.Parent is not null )
			return scrollPanel.Parent;

		return null;
	}

	/// <summary>Clamps a popup position on the editor overlay.</summary>
	public Vector2 PlaceOverlayPopup( Vector2 wanted, Vector2 size )
	{
		if ( OverlayPlaceHandler != null )
			return OverlayPlaceHandler( this, wanted, size );

		return wanted;
	}

	/// <summary>Returns the scene that this panel belongs to</summary>
	public Scene Scene
	{
		get => GameObject?.Scene;

		[Obsolete( "Setting Scene is no longer supported. This should be done automatically, internally" )]
		set => GameObject = value;
	}

	/// <summary>Returns the GameObject that this panel belongs to</summary>
	public GameObject GameObject
	{
		get
		{
			if ( field is not null )
				return field;

			//Resolved once per reshape of the tree, not once per read. This is the first question a panel's tick asks - UpdateSceneIndex reads Scene, and
			//Scene reads this - so walking to the root here cost the depth of the tree for every panel every frame, to be told what it was told last frame.
			if ( inheritedAt != StructureVersion )
			{
				inherited = Parent?.GameObject;
				inheritedAt = StructureVersion;
			}

			return inherited;
		}

		internal set
		{
			field = value;
			NoteStructureChanged();
			RequestTick();
		}
	}

	private GameObject inherited;
	private int inheritedAt = -1;

	//Global counter bumped on tree reshape; panels re-resolve their inherited GameObject only on change
	internal static int StructureVersion { get; private set; }

	internal static void NoteStructureChanged() => StructureVersion++;

	/// <summary>Returns the index at which the given panel is <see cref="Parent">parented</see> to this panel, or -1 if it is not.</summary>
	public int GetChildIndex( Panel panel )
	{
		if ( panel == null || panel.Parent != this )
			return -1;

		if ( _children == null || _children.Count == 0 )
			return -1;

		return _children.IndexOf( panel );
	}

	/// <summary>Return a child at given index.</summary>
	public Panel GetChild( int index, bool loop = false )
	{
		if ( _children == null || _children.Count == 0 )
			return null;

		if ( loop )
		{
			index = index.UnsignedMod( Children.Count() );
		}
		else
		{
			if ( index < 0 ) return null;
			if ( index >= _children.Count ) return null;
		}

		return _children[index];
	}

	/// <summary>Amount of panels directly <see cref="Parent">parented</see> to this panel.</summary>
	[Hide]
	public int ChildrenCount => _children?.Count ?? 0;

	/// <summary>Returns a list of <see cref="Children">child panels</see> of given type.</summary>
	public IEnumerable<T> ChildrenOfType<T>() where T : Panel
	{
		if ( _children == null || _children.Count == 0 )
			yield break;

		var c = _children.Count;

		for ( int i = c - 1; i >= 0; i-- )
		{
			var child = _children[i];
			if ( child is T t )
			{
				yield return t;
			}
		}
	}
}
