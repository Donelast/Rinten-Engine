using Editor.Inspectors;
using Facepunch.ActionGraphs;
using Rinten;
using Rinten.ActionGraphs;
using Rinten.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Editor.ActionGraphs;

/// <summary>A delegate property: one action, or a list of them where the delegate can have more than one subscriber.</summary>
[StyleSheet( "ActionControl.cs.scss" )]
[CustomEditor( typeof( Delegate ) )]
public sealed class DelegateControl : BaseControl
{
	internal sealed class DelegateWrapper : IList<IActionGraphDelegate>, IList
	{
		private readonly List<IActionGraphDelegate> _inner = new();

		public SerializedProperty ParentProperty { get; }

		public DelegateWrapper( SerializedProperty property )
		{
			ParentProperty = property;

			ReadFromProperty();
		}

		public void ReadFromProperty()
		{
			_inner.Clear();

			if ( ParentProperty.GetValue<Delegate>() is not { } deleg ) return;

			foreach ( var inst in deleg.GetActionGraphInstances() )
			{
				_inner.Add( inst );
			}
		}

		private void WriteToProperty()
		{
			if ( _inner.Count == 0 )
			{
				ParentProperty.SetValue<Delegate>( null );
				return;
			}

			var delegates = _inner.Select( x => x.Delegate ).ToArray();

			ParentProperty.SetValue( Delegate.Combine( delegates ) );
		}

		public IEnumerator<IActionGraphDelegate> GetEnumerator()
		{
			return _inner.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public void Add( IActionGraphDelegate item )
		{
			Insert( Count, item );
		}

		int IList.Add( object value )
		{
			Add( value as IActionGraphDelegate );
			return Count - 1;
		}

		public void Clear()
		{
			_inner.Clear();
			WriteToProperty();
		}

		bool IList.Contains( object value )
		{
			return ((IList)_inner).Contains( value );
		}

		int IList.IndexOf( object value )
		{
			return ((IList)_inner).IndexOf( value );
		}

		void IList.Insert( int index, object value )
		{
			Insert( index, value as IActionGraphDelegate );
		}

		void IList.Remove( object value )
		{
			Remove( value as IActionGraphDelegate );
		}

		public bool Contains( IActionGraphDelegate item )
		{
			return _inner.Contains( item );
		}

		public void CopyTo( IActionGraphDelegate[] array, int arrayIndex )
		{
			_inner.CopyTo( array, arrayIndex );
		}

		public bool Remove( IActionGraphDelegate item )
		{
			if ( !_inner.Remove( item ) ) return false;

			WriteToProperty();
			return true;
		}

		void ICollection.CopyTo( Array array, int index )
		{
			((ICollection)_inner).CopyTo( array, index );
		}

		public int Count => _inner.Count;
		bool ICollection.IsSynchronized => ((ICollection)_inner).IsSynchronized;

		object ICollection.SyncRoot => ((ICollection)_inner).SyncRoot;

		public bool IsReadOnly => !ParentProperty.IsEditable;

		object IList.this[int index]
		{
			get => ((IList)_inner)[index];
			set
			{
				this[index] = (IActionGraphDelegate)value;
			}
		}

		public int IndexOf( IActionGraphDelegate item )
		{
			return _inner.IndexOf( item );
		}

		public void Insert( int index, IActionGraphDelegate item )
		{
			var delegType = ParentProperty.PropertyType;
			var graph = ActionControl.PrepareGraphForEditing( item?.Graph, ParentProperty, delegType );

			item ??= graph.CreateDelegate( delegType );

			_inner.Insert( index, item );
			WriteToProperty();
		}

		public void RemoveAt( int index )
		{
			_inner.RemoveAt( index );
			WriteToProperty();
		}

		bool IList.IsFixedSize => false;

		public IActionGraphDelegate this[int index]
		{
			get => _inner[index];
			set
			{
				_inner[index] = value;
				WriteToProperty();
			}
		}
	}

	private DelegateWrapper _wrapper;

	public DelegateControl()
	{
		ShowBackground = false;
	}

	public override void Rebuild()
	{
		DeleteChildren();

		_wrapper = null;

		if ( Property is null ) return;

		var delegateType = Property.PropertyType;

		//Not every delegate can be a graph. Say so rather than offering a line that throws from inside the node library the moment it is pressed.
		if ( !DelegateSupport.CanEdit( delegateType ) )
		{
			var note = AddChild<Rinten.UI.Label>( "note" );

			note.Tokenize = false;
			note.Text = $"No action editor for {delegateType.Name}";

			return;
		}

		//An action delegate can have any number of subscribers, so it is edited as a list.
		if ( DelegateSupport.IsAction( delegateType ) && !Property.HasAttribute<SingleActionAttribute>() )
		{
			MultipleRows = true;

			_wrapper = new DelegateWrapper( Property );

			var collection = (SerializedCollection)EditorTypeLibrary.GetSerializedObject( _wrapper );
			collection.ParentProperty = Property;

			//The subscribers are read out of the delegate rather than held by it, so the list is given the property as the collection of them.
			var list = AddChild<ListControl>();

			list.ReadOnly = IsReadOnly;
			list.Property = new SubscribersProperty( Property, collection );

			return;
		}

		//One that returns a value can only have one subscriber, so it is edited as one action.
		MultipleRows = false;

		var action = AddChild<ActionControl>();

		action.ReadOnly = IsReadOnly;
		action.Property = Property;
	}

	protected override void OnReadOnlyChanged()
	{
		foreach ( var control in ChildrenOfType<BaseControl>() )
			control.ReadOnly = IsReadOnly;
	}

	//Wraps the delegate property so the list editor sees its subscribers as a collection
	private sealed class SubscribersProperty : SerializedProperty
	{
		private readonly SerializedProperty _inner;
		private readonly SerializedCollection _subscribers;

		public SubscribersProperty( SerializedProperty inner, SerializedCollection subscribers )
		{
			_inner = inner;
			_subscribers = subscribers;
		}

		public override SerializedObject Parent => _inner.Parent;
		public override string Name => _inner.Name;
		public override string DisplayName => _inner.DisplayName;
		public override string Description => _inner.Description;
		public override string GroupName => _inner.GroupName;
		public override Type PropertyType => _inner.PropertyType;
		public override bool IsEditable => _inner.IsEditable;
		public override bool IsValid => _inner.IsValid;
		public override bool HasChanges => _inner.HasChanges;

		public override T GetValue<T>( T defaultValue = default ) => _inner.GetValue( defaultValue );
		public override void SetValue<T>( T value ) => _inner.SetValue( value );
		public override IEnumerable<Attribute> GetAttributes() => _inner.GetAttributes();

		public override bool TryGetAsObject( out SerializedObject obj )
		{
			obj = _subscribers;
			return true;
		}
	}

}

//Cached node-library support per delegate type, since unsupported ones throw deep inside
internal static class DelegateSupport
{
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, NodeKind?> kinds = new();

	/// <summary>Whether a graph can be made for this delegate at all.</summary>
	public static bool CanEdit( Type delegateType ) => KindOf( delegateType ) is not null;

	/// <summary>Whether it returns nothing, and so can have any number of subscribers - as opposed to one that returns a value, which can only have one.</summary>
	public static bool IsAction( Type delegateType ) => KindOf( delegateType ) == NodeKind.Action;

	//The kind of node the delegate makes, or null where it can't be one.
	private static NodeKind? KindOf( Type delegateType )
	{
		if ( delegateType is null ) return null;

		return kinds.GetOrAdd( delegateType, Describe );
	}

	private static NodeKind? Describe( Type delegateType )
	{
		try
		{
			return NodeBinding.FromDelegateType( delegateType, EditorNodeLibrary ).Kind;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Action graphs can't describe {delegateType}, so it has no editor." );
			return null;
		}
	}
}
