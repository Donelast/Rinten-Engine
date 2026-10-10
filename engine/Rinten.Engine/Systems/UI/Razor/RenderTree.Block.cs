using Microsoft.AspNetCore.Components;

namespace Rinten.UI;

/// <summary>This is a tree renderer for panels. If we ever use razor on other ui we'll want to make a copy of this class and do the specific things to that.</summary>
public partial class PanelRenderTreeBuilder : Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder
{
	Block RootBlock;
	Block CurrentBlock => CurrentScope.Block ?? RootBlock;

	class Block
	{
		public int Hash;
		public List<Block> Children;

		public bool IsRootElement;
		public Panel ElementPanel;
		public Action ReferenceClearer;
		public List<Func<bool>> Binds;

		public List<Panel> MarkupPanels;

		public bool WasSeen;

		public Block()
		{

		}

		internal void Destroy( Panel outroParent = null, bool immediate = false )
		{
			//While a panel above plays its outro, what it holds stays alive until it is gone.
			var owner = immediate ? null : (IsRootElement ? outroParent : ElementPanel ?? outroParent);

			if ( Children != null )
			{
				foreach ( var child in Children )
				{
					child?.Destroy( owner, immediate );
				}

				Children = null;
			}

			if ( MarkupPanels != null )
			{
				foreach ( var panel in MarkupPanels )
				{
					panel?.DeleteFromRenderTree( owner, true );
				}

				MarkupPanels.Clear();
				MarkupPanels = null;
			}

			if ( !IsRootElement )
			{
				ElementPanel?.DeleteFromRenderTree( immediate ? null : outroParent, immediate );
			}

			ElementPanel = null;

			try
			{
				ReferenceClearer?.Invoke();
			}
			catch ( System.Exception )
			{
				//totally possible for things to fuck up here because we're going to be setting a reference to null that may very well not even exist anymore
			}


		}

		//Built-in panel for an element name (div, img, label...), resolved without a type library
		internal static Panel Builtin( string elementName ) => elementName switch
		{
			"div" or "p" or "span" or "i" or "b" or "section" or "header" or "footer" => new Panel(),
			"img" or "image" => new Image(),
			"label" or "text" => new Label(),
			"svg" => new SvgPanel(),
			_ => null,
		};

		//What an element name means inside an <svg> : a group or a shape, placed in the view
		//box rather than laid out. Outside one, rect and line mean what they always did.
		internal static Panel InSvg( string elementName, Panel parent )
		{
			if ( parent is not ( SvgPanel or SvgElement ) ) return null;

			return elementName == "g" ? new SvgGroup() : SvgShape.Create( elementName );
		}

		public Panel FindOrCreateElement( string elementName, Panel parent )
		{
			if ( !ElementPanel.IsValid() )
			{
				Panel panel = InSvg( elementName, parent ) ?? Builtin( elementName );

				//The type library knows what a game registered; it does not exist in a process
				//that never loaded one, and the editor's own panels are up long before it does.
				panel ??= Game.TypeLibrary?.Create<Panel>( elementName, false );
				panel ??= new Panel();
				panel.ElementName = elementName;
				panel.Parent = parent;
				ElementPanel = panel;
			}

			if ( ElementPanel.Parent != parent )
			{
				//can't have children
				if ( parent is Label ) return ElementPanel;
				if ( parent is Image ) return ElementPanel;

				Log.Warning( $"Fixing parent of {ElementPanel}" );
				ElementPanel.Parent = parent;
			}

			return ElementPanel;
		}

		public Panel FindOrCreateElement<T>( Panel parent ) where T : IComponent, new()
		{
			if ( !ElementPanel.IsValid() )
			{
				IComponent component = new T();

				if ( component is Panel panel )
				{
					panel.Parent = parent;
					ElementPanel = panel;
				}
			}

			return ElementPanel;
		}

		/// <summary>Whether this block or any below it holds a bind.</summary>
		public bool HasBinds()
		{
			if ( Binds is { Count: > 0 } ) return true;
			if ( Children is null ) return false;

			for ( int i = 0; i < Children.Count; i++ )
			{
				if ( Children[i].HasBinds() ) return true;
			}

			return false;
		}

		public bool UpdateBinds()
		{
			bool b = false;

			if ( Children is not null )
			{
				for ( int i = 0; i < Children.Count; i++ )
				{
					b = Children[i].UpdateBinds() || b;
				}
			}

			if ( Binds is not null )
			{
				for ( int i = 0; i < Binds.Count; i++ )
				{
					try
					{
						b = Binds[i]() || b;
					}
					catch ( System.Exception e )
					{
						Log.Warning( e, $"Razor bind exception ({e.Message})" );
						Binds.RemoveAt( i );
						return b;
					}
				}
			}

			return b;
		}

		//Reset to an unseen, unlooped state
		internal void Reset()
		{
			WasSeen = false;
			increments?.Clear();

			if ( Children == null )
				return;

			foreach ( var child in Children )
			{
				child.Reset();
			}
		}

		internal bool DestroyUnseen()
		{
			//An unseen block is destroyed whole, from the top, so its children know whose outro they are part of rather than being deleted first on their own.
			if ( !WasSeen )
			{
				Destroy();
				return true;
			}

			if ( Children != null )
			{
				//Backwards and in place. This ran once a frame for every block of every render tree in the editor - the report's allocation table named it,
				//ListWhereIterator<Block>, thirty-seven allocations a frame on a still window - and each block it did find was then removed by a linear search through the same list.
				for ( int i = Children.Count - 1; i >= 0; i-- )
				{
					if ( Children[i].DestroyUnseen() )
						Children.RemoveAt( i );
				}
			}

			return false;
		}

		internal Block GetChild( int hash )
		{
			Children ??= new();

			//Not FirstOrDefault - the predicate would capture hash and allocate a closure every call.
			Block child = null;

			foreach ( var candidate in Children )
			{
				if ( candidate.Hash != hash ) continue;

				child = candidate;
				break;
			}

			if ( child == null )
			{
				child = new Block();
				child.Hash = hash;
				Children.Add( child );
			}

			child.WasSeen = true;
			return child;
		}

		Dictionary<int, int> cache;

		/// <summary>Allows caching a block so you can avoid repeating unnecessary steps. Calling this will return true if it's already cached, false if it's not. If it's not it'll add to the cache so that next time it will return true.</summary>
		public bool CheckCacheValue( int i, int hashcode )
		{
			cache ??= new();

			if ( cache.TryGetValue( i, out var code ) && hashcode == code )
				return true;

			cache[i] = hashcode;
			return false;
		}

		//For loops, how many times has this been seen
		Dictionary<int, int> increments;

		public int Increment( int sequence )
		{
			increments ??= new Dictionary<int, int>();

			if ( !increments.TryGetValue( sequence, out int counter ) )
				counter = 0;

			counter++;
			increments[sequence] = counter;
			return counter;
		}
	}
}
