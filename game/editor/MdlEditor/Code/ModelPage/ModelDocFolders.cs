using Rinten.UI;
﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MdlEditor;

//Outliner folders: just a name stored on each node, so empty folders simply vanish
static class ModelDocFolders
{
	//Loose nodes first and folders after them, each folder in the order its first node appears - so making a folder moves the nodes in it together and leaves everything else where it was.
	/// <summary>Lays a handler's nodes out under their folders.</summary>
	public static void Collect<T>( List<ModelDocItem> items, ModelDocContext ctx, ModelDocKind kind,
		IReadOnlyList<T> nodes, Func<T, string> folderOf, Func<int, T, ModelDocItem> row,
		Action<int, T, List<ModelDocItem>, int> children = null )
	{
		var order = new List<string>();
		var grouped = new Dictionary<string, List<int>>( StringComparer.OrdinalIgnoreCase );

		//Made and not filled yet. Drawn all the same, or making one would look like nothing happening.
		foreach ( var empty in Declared( ctx.Definition, kind ) )
		{
			if ( grouped.ContainsKey( empty ) ) continue;

			grouped[empty] = new List<int>();
			order.Add( empty );
		}

		for ( var i = 0; i < nodes.Count; i++ )
		{
			var folder = Named( folderOf( nodes[i] ) );

			if ( folder.Length == 0 )
			{
				var loose = row( i, nodes[i] );

				items.Add( loose );
				children?.Invoke( i, nodes[i], items, loose.Depth + 1 );

				continue;
			}

			if ( !grouped.TryGetValue( folder, out var inside ) )
			{
				grouped[folder] = inside = new List<int>();
				order.Add( folder );
			}

			inside.Add( i );
		}

		foreach ( var folder in order )
		{
			var inside = grouped[folder];

			items.Add( new ModelDocItem
			{
				Kind = ModelDocKind.Folder,
				Owner = kind,
				Title = $"{folder}  ·  {inside.Count}",
				SubName = folder,
				Depth = 1,
				IsFolder = true,
				Icon = "folder",
			} );

			if ( ctx.IsFolderCollapsed( kind, folder ) ) continue;

			foreach ( var i in inside )
			{
				var item = row( i, nodes[i] );

				items.Add( new ModelDocItem
				{
					Kind = item.Kind,
					Owner = kind,
					Title = item.Title,
					Depth = item.Depth + 1,
					Index = item.Index,
					ParentIndex = item.ParentIndex,
					IsFolder = item.IsFolder,
					SubName = folder,
					Enabled = item.Enabled,
					Icon = item.Icon,
				} );

				children?.Invoke( i, nodes[i], items, item.Depth + 2 );
			}
		}
	}

	//How a folder of one list is written down on the definition.
	static string Key( ModelDocKind kind, string folder ) => $"{Owner( kind )}/{Named( folder )}";

	//The list a kind belongs to, so a node and its folder row agree on one name.
	static ModelDocKind Owner( ModelDocKind kind ) => kind switch
	{
		ModelDocKind.Hull or ModelDocKind.Hulls => ModelDocKind.Hulls,
		ModelDocKind.Joint or ModelDocKind.Joints => ModelDocKind.Joints,
		ModelDocKind.Animation or ModelDocKind.Animations => ModelDocKind.Animations,
		ModelDocKind.Attachment or ModelDocKind.Attachments => ModelDocKind.Attachments,
		ModelDocKind.Constraint or ModelDocKind.Constraints => ModelDocKind.Constraints,
		ModelDocKind.JiggleBone or ModelDocKind.JiggleBones => ModelDocKind.JiggleBones,
		ModelDocKind.JiggleCollider or ModelDocKind.JiggleColliders => ModelDocKind.JiggleColliders,
		_ => kind,
	};

	/// <summary>Folders somebody made in this list, whether or not anything is in them.</summary>
	public static IEnumerable<string> Declared( ModelDefinition definition, ModelDocKind kind )
	{
		if ( definition?.Folders is null ) yield break;

		var prefix = $"{Owner( kind )}/";

		foreach ( var written in definition.Folders )
		{
			if ( written is null || !written.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) ) continue;

			var name = Named( written[prefix.Length..] );

			if ( name.Length > 0 ) yield return name;
		}
	}

	/// <summary>Writes a folder down, so it is there before anything is in it.</summary>
	public static void Declare( ModelDefinition definition, ModelDocKind kind, string folder )
	{
		if ( definition is null || Named( folder ).Length == 0 ) return;

		definition.Folders ??= new List<string>();

		var key = Key( kind, folder );

		if ( definition.Folders.Contains( key, StringComparer.OrdinalIgnoreCase ) ) return;

		definition.Folders.Add( key );
	}

	/// <summary>Takes one off the list, which is all that is left of an empty one.</summary>
	public static void Undeclare( ModelDefinition definition, ModelDocKind kind, string folder )
	{
		if ( definition?.Folders is null ) return;

		var key = Key( kind, folder );

		definition.Folders.RemoveAll( x => string.Equals( x, key, StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>A folder name with the spelling settled: trimmed, and empty for no folder.</summary>
	public static string Named( string folder ) => folder?.Trim() ?? "";

	/// <summary>The nodes of one list, where that list is one folders work on.</summary>
	public static IList Sortable( ModelDefinition definition, ModelDocKind kind ) => kind switch
	{
		ModelDocKind.Hull or ModelDocKind.Hulls => (IList)definition?.Hulls,
		ModelDocKind.Joint or ModelDocKind.Joints => (IList)definition?.Joints,
		ModelDocKind.Animation or ModelDocKind.Animations => (IList)definition?.Clips,
		ModelDocKind.Attachment or ModelDocKind.Attachments => (IList)definition?.Attachments,
		ModelDocKind.Constraint or ModelDocKind.Constraints => (IList)definition?.Constraints,
		ModelDocKind.JiggleBone or ModelDocKind.JiggleBones => (IList)definition?.JiggleBones,
		ModelDocKind.JiggleCollider or ModelDocKind.JiggleColliders => (IList)definition?.JiggleColliders,
		_ => null,
	};

	/// <summary>What folder a node is in.</summary>
	public static string FolderOf( object node ) => node switch
	{
		ModelDefinition.Hull hull => Named( hull.Folder ),
		ModelDefinition.Joint joint => Named( joint.Folder ),
		ModelDefinition.ModelClip clip => Named( clip.Folder ),
		ModelDefinition.Attachment attachment => Named( attachment.Folder ),
		ModelDefinition.Constraint constraint => Named( constraint.Folder ),
		ModelDefinition.JiggleBone jiggle => Named( jiggle.Folder ),
		ModelDefinition.JiggleCollider collider => Named( collider.Folder ),
		_ => "",
	};

	/// <summary>Puts a node in a folder, or in none where the name is empty.</summary>
	public static void SetFolder( object node, string folder )
	{
		folder = Named( folder );

		switch ( node )
		{
			case ModelDefinition.Hull hull: hull.Folder = folder; break;
			case ModelDefinition.Joint joint: joint.Folder = folder; break;
			case ModelDefinition.ModelClip clip: clip.Folder = folder; break;
			case ModelDefinition.Attachment attachment: attachment.Folder = folder; break;
			case ModelDefinition.Constraint constraint: constraint.Folder = folder; break;
			case ModelDefinition.JiggleBone jiggle: jiggle.Folder = folder; break;
			case ModelDefinition.JiggleCollider collider: collider.Folder = folder; break;
		}
	}

	/// <summary>Every folder one list is sorted into, in the order they appear in it.</summary>
	public static List<string> Names( ModelDefinition definition, ModelDocKind kind )
	{
		var found = new List<string>();

		foreach ( var declared in Declared( definition, kind ) )
		{
			if ( !found.Contains( declared, StringComparer.OrdinalIgnoreCase ) ) found.Add( declared );
		}

		var list = Sortable( definition, kind );

		if ( list is null ) return found;

		foreach ( var node in list )
		{
			var folder = FolderOf( node );

			if ( folder.Length == 0 || found.Contains( folder, StringComparer.OrdinalIgnoreCase ) ) continue;

			found.Add( folder );
		}

		return found;
	}

	/// <summary>The node a row stands for, where it is one that can be sorted.</summary>
	public static object Node( ModelDefinition definition, ModelDocItem item )
	{
		if ( item is null || item.IsFolder || item.Index < 0 ) return null;

		var list = Sortable( definition, item.Kind );

		return list is not null && item.Index < list.Count ? list[item.Index] : null;
	}

	/// <summary>Takes a folder apart, leaving what was in it where it was.</summary>
	public static void Ungroup( ModelDefinition definition, ModelDocItem folder )
	{
		var name = Named( folder.SubName );

		Undeclare( definition, folder.Owner, name );

		var list = Sortable( definition, folder.Owner );
		if ( list is null ) return;

		foreach ( var node in list )
		{
			if ( !FolderOf( node ).Equals( name, StringComparison.OrdinalIgnoreCase ) ) continue;

			SetFolder( node, "" );
		}
	}

	/// <summary>Renames one, which is renaming it on everything inside it.</summary>
	public static void Rename( ModelDefinition definition, ModelDocItem folder, string renamed )
	{
		var was = Named( folder.SubName );

		Undeclare( definition, folder.Owner, was );
		Declare( definition, folder.Owner, renamed );

		var list = Sortable( definition, folder.Owner );
		if ( list is null ) return;

		foreach ( var node in list )
		{
			if ( !FolderOf( node ).Equals( was, StringComparison.OrdinalIgnoreCase ) ) continue;

			SetFolder( node, renamed );
		}
	}
}
