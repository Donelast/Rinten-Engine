
using Rinten.Engine;

namespace Rinten.UI;

public partial class Panel
{
	/// <summary>Whether <see cref="Delete"/> was called on this panel.</summary>
	[Hide]
	public bool IsDeleting { get; internal set; }


	private bool IsDeleted { get; set; }
	HashSet<Panel> renderTreeDeletion;
	bool renderTreeDeletePending;

	//A razor render tree is done with this panel. While an ancestor is playing its outro the panel
	//is kept as it is - it is part of what the outro shows - and is deleted when that ancestor is.
	internal void DeleteFromRenderTree( Panel outroParent, bool immediate )
	{
		if ( IsDeleted ) return;
		if ( IsDeleting )
		{
			if ( immediate )
			{
				//Its virtual Delete already ran when its independent outro started.
				Parent = null;
				IsVisible = false;
				OnDeleteRecursive();
			}
			return;
		}

		if ( outroParent is not null && outroParent != this && IsAncestor( outroParent ) )
		{
			//Keep physical content alive, including resources released by virtual Delete overrides.
			outroParent.renderTreeDeletion ??= new();
			outroParent.renderTreeDeletion.Add( this );
			renderTreeDeletePending = true;
			return;
		}

		renderTreeDeletePending = false;
		try
		{
			Delete( immediate );
		}
		catch ( System.Exception ex )
		{
			Log.Error( ex, "Error when deleting a render-tree panel" );
			//An override may throw before base.Delete. Finish base cleanup without replaying
			//it, and don't let this panel prevent its siblings or owner from being cleaned up.
			try
			{
				Parent = null;
				IsVisible = false;
				IsDeleting = true;
				OnDeleteRecursive();
			}
			catch ( System.Exception cleanupException )
			{
				Log.Error( cleanupException, "Error when cleaning up a failed render-tree panel deletion" );
			}
		}
	}

	/// <summary>Deletes the panel.</summary>
	public virtual void Delete( bool immediate = false )
	{
		if ( IsDeleted )
			return;

		_deleteTokenSource?.Cancel();

		if ( immediate )
		{
			Parent = null;
			IsVisible = false;
			IsDeleting = true;
			OnDeleteRecursive();
			return;
		}

		if ( IsDeleting )
			return;

		IsDeleting = true;
		RequestTick();
		Transitions.Clear(); //stop any intros
		Switch( PseudoClass.Outro, true );
		GlobalContext.Current.UISystem.AddDeferredDeletion( this );
	}

	/// <summary>Called when the panel is about to be deleted.</summary>
	public virtual void OnDeleted()
	{

	}

	//Called on delete.
	internal void OnDeleteRecursive()
	{
		if ( IsDeleted ) return;
		IsDeleted = true;

		try
		{
			RemoveFromLists();
			RemoveFromSceneIndex();

			Task.Expire();

			//Clear logical ownership before physical recursion bypasses children's Delete overrides. The razor render tree - Block.ElementPanel holds strong
			//refs to dynamically-created child panels whose Style.StyleBlocks keep parsed stylesheet textures (gradients, masks, etc.) alive past shutdown.
			renderTree?.Clear( immediate: true );
			renderTree = null;

			var pending = renderTreeDeletion;
			renderTreeDeletion = null;
			if ( pending is not null )
			{
				foreach ( var panel in pending )
					panel.DeleteFromRenderTree( null, true );
			}

			foreach ( var child in Children.ToArray() )
			{
				if ( child is null ) continue;

				if ( child.renderTreeDeletePending && !child.IsDeleting )
					child.DeleteFromRenderTree( null, true );
				else
					child.OnDeleteRecursive();
			}

			try
			{

				OnDeleted();
			}
			catch ( System.Exception ex )
			{
				Log.Error( ex, "Error when calling OnDeleted" );
			}

			//Clear any focus we may have
			//TODO: Ideally this would cascade to parents who accept focus, but we'd need to change how Panels are removed.
			if ( InputFocus.Current == this )
			{
				InputFocus.Clear( this );
			}

			if ( MouseCapture == this )
			{
				SetMouseCapture( false );
			}

			LayoutTree?.Dispose();
			LayoutTree = null;

			if ( CachedDescriptors != null )
			{
				RenderLayer.Return( CachedDescriptors );
				CachedDescriptors = null;
			}

			ComputedStyle = null;
			StyleSheet = default;
			GameObject = null;

			//Drop the PanelStyle — its StyleBlocks[] cache holds StyleBlock refs whose Styles._backgroundImage/_maskImage keep textures alive.
			Style = null;

			_renderChildren = null;
			_childrenHash = null;
			_children = null;
			_parent = null;
		}
		catch ( System.Exception e )
		{
			Log.Error( e );
		}
	}


}
