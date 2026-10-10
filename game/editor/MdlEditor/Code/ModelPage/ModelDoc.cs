using System;
using Rinten.UI;
﻿using System;
using System.Collections.Generic;
using System.IO;

namespace Editor.MdlEditor;

enum ModelDocKind
{
	Transform,
	Mirror,
	Mesh,
	Animations,
	Animation,
	BodyGroups,
	BodyGroup,
	BodyGroupChoice,
	Hulls,
	Hull,
	Joints,
	Joint,
	MaterialGroups,
	DefaultMaterials,
	MaterialGroup,
	LookAt,
	LookChain,
	Attachments,
	Attachment,

	/// <summary>One bone an attachment is pulled by, where it hangs off more than one.</summary>
	AttachmentInfluence,

	WeightLists,
	WeightList,
	BoneWeight,

	IkChains,
	IkChain,

	Morphs,
	Morph,

	Bounds,
	Constraints,
	Constraint,
	ConstraintDriver,

	/// <summary>One somebody made to sort a list with - see ModelDocFolders.</summary>
	Folder,

	/// <summary>Something marked along a clip: an event at a moment, or a tag over a span.</summary>
	AnimEvent,

	/// <summary>Where the build steps hang.</summary>
	Steps,

	/// <summary>One of them - a type a project wrote, not one this knows about.</summary>
	Step,

	/// <summary>A piece inside a step that can be picked on its own.</summary>
	StepPart,

	JiggleBones,
	JiggleBone,
	JiggleColliders,
	JiggleCollider,

	/// <summary>Bones the definition adds to the skeleton - see ExtraBoneDoc.</summary>
	Bones,
	Bone,
}

sealed class ModelDocItem
{
	public ModelDocKind Kind { get; init; }
	public string Title { get; init; }
	public int Depth { get; init; }
	public int Index { get; init; } = -1;
	public int ParentIndex { get; init; } = -1;
	public bool IsFolder { get; init; }
	public string SubName { get; init; }

	/// <summary>Which list a folder row is sorting, since a folder row has no index of its own.</summary>
	public ModelDocKind Owner { get; init; }
	public bool Enabled { get; init; } = true;
	public string Icon { get; init; }
}

//Page or inspector the model nodes are being edited in.
interface IModelDocHost
{
	Asset Asset { get; }
	ModelDefinition Definition { get; }

	//The compiled model beside the definition, where there is one.
	Model CompiledModel { get; }

	//The list the nodes are drawn in, so a handler can point at what it just added.
	ModelOutliner Outliner { get; }

	//Whether the file is written on a button rather than as it is edited.
	bool CanSave { get; }

	void SaveModel();

	void NotifyDefinitionChanged( bool rebuildInspector = false );

	//Mark a structural edit so End can insert undo from the last snapshot.
	void BeginDefinitionEdit( string name ) { }

	//Insert undo if the definition changed since the last committed snapshot.
	void EndDefinitionEdit( string name = null ) { }

	//Where a bone is in the pose being played, or nothing where none is. Only the page has a viewport to
	//ask; an inspector standing on its own has no pose and answers nothing, which reads as the bind pose.
	Transform? BoneNow( string bone ) => null;
}

sealed class ModelDocContext
{
	public IModelDocHost Host { get; init; }
	public Func<ModelDocKind, bool> IsCollapsed { get; init; }

	/// <summary>Whether a folder somebody made is shut, which is per name rather than per kind.</summary>
	public Func<ModelDocKind, string, bool> IsFolderCollapsed { get; init; } = static ( _, _ ) => false;

	public Asset Asset => Host.Asset;
	public ModelDefinition Definition => Host.Definition;

	/// <summary>Binds one node's fields to the sheet, writing edits to the definition and undo stack</summary>
	public void BindObject( Inspectors.ControlSheet sheet, object target, Action<SerializedProperty> changed = null )
	{
		var so = target.GetSerialized();

		RecordEdits( so, changed );

		//Telling the sheet what it is showing is what fills it; it reads its own target and empties itself when that changes.
		sheet.Target = so;
		sheet.Rebuild();
	}

	/// <summary>The same, split across two sheets: the rows <paramref name="first"/> takes in <paramref name="sheet"/>, and the rest in <paramref name="rest"/> - so a node can stand something of its own between them. Edits in either are written down once, through the one object.</summary>
	public void BindObject( Inspectors.ControlSheet sheet, Inspectors.ControlSheet rest, object target,
		Func<SerializedProperty, bool> first, Func<SerializedProperty, bool> shown = null,
		Action<SerializedProperty> changed = null )
	{
		var so = target.GetSerialized();

		RecordEdits( so, changed );

		//Handed over as lists: a sheet given a list lays it out the way it lays out an object -
		//hidden rows dropped, the rest sorted - and two sheets can each hold part of one object.
		var all = so.Where( property => shown?.Invoke( property ) ?? true ).ToList();

		sheet.Target = all.Where( first ).ToList();
		sheet.Rebuild();

		rest.Target = all.Where( property => !first( property ) ).ToList();
		rest.Rebuild();
	}

	//Records inspector edits in undo, one entry per bracketed drag, and marks the definition changed
	private void RecordEdits( SerializedObject so, Action<SerializedProperty> changed = null )
	{
		var bracketed = false;

		so.OnPropertyStartEdit += property =>
		{
			bracketed = true;
			Host.BeginDefinitionEdit( EditName( property ) );
		};

		so.OnPropertyChanged += property =>
		{
			Host.NotifyDefinitionChanged();

			changed?.Invoke( property );

			if ( !bracketed ) Host.EndDefinitionEdit( EditName( property ) );
		};

		so.OnPropertyFinishEdit += property =>
		{
			bracketed = false;
			Host.EndDefinitionEdit( EditName( property ) );
		};
	}

	//What the entry is called in the history, from the row that made it.
	private static string EditName( SerializedProperty property )
	{
		var name = property?.DisplayName ?? property?.Name;

		return string.IsNullOrWhiteSpace( name ) ? "Edit Model" : $"Edit {name}";
	}

	public void BindDefinition( Inspectors.ControlSheet sheet, params string[] names )
	{
		var so = Definition.GetSerialized();

		RecordEdits( so );

		foreach ( var name in names )
		{
			if ( so.GetProperty( name ) is { } prop )
				sheet.AddPropertyRow( prop );
		}
	}

	public static bool InRange<T>( IList<T> list, int index )
		=> list is not null && index >= 0 && index < list.Count;

	public static string ToAssetsPath( string absolute )
	{
		var root = Project.Current.GetAssetsPath();
		if ( string.IsNullOrWhiteSpace( root ) )
			return absolute.Replace( '\\', '/' );

		var relative = Path.GetRelativePath( root, absolute ).Replace( '\\', '/' );

		//Outside the project - an export straight out of a modelling folder - it is written as it is. A path climbing out of the assets with ../ is found by
		//nothing that reads it: the compile looks beside the definition and in the project (AssetSources.Resolve), and looks for a full path where it says.
		return relative == ".." || relative.StartsWith( "../" ) || Path.IsPathRooted( relative )
			? absolute.Replace( '\\', '/' )
			: relative;
	}

	public static ModelDocItem Folder( ModelDocKind kind, string title, int count )
		=> new() { Kind = kind, Title = $"{title}  ·  {count}", IsFolder = true };

	/// <summary>Adds a group header only when it has something in it.</summary>
	public static bool AddFolder( List<ModelDocItem> items, ModelDocKind kind, string title, int count )
	{
		if ( count <= 0 ) return false;

		items.Add( Folder( kind, title, count ) );
		return true;
	}
}

readonly record struct ModelDocAdd( string Group, string GroupIcon, string Title, string Icon, Action Run );

//One kind of outliner node. Add a handler here instead of another switch.
abstract class ModelDocHandler
{
	static float _nextHue;
	readonly float _hue;

	protected ModelDocHandler()
	{
		_hue = _nextHue;
		_nextHue = ( _nextHue + 137.50776f ) % 360f;
	}

	/// <summary>Hue for this kind, stepped by the golden angle so later handlers stay apart.</summary>
	public Color Tint => new ColorHsv( _hue, 0.38f, 0.75f ).ToColor();

	public abstract ModelDocKind Kind { get; }
	public virtual string Icon => "circle";
	public virtual string IconFor( ModelDocItem item )
		=> !string.IsNullOrWhiteSpace( item.Icon ) ? item.Icon : Icon;
	public virtual bool CanDelete( ModelDocItem item ) => false;
	public virtual bool CanToggle( ModelDocItem item ) => false;

	public virtual IEnumerable<ModelDocKind> AlsoHandles => [];

	public abstract void Collect( ModelDocContext ctx, List<ModelDocItem> items );

	public virtual IEnumerable<ModelDocAdd> AddActions( ModelDocContext ctx ) => [];

	public virtual void AddContextOptions( Menu menu, ModelDocContext ctx, ModelDocItem item ) { }

	public virtual bool TrySetEnabled( ModelDocContext ctx, ModelDocItem item, bool enabled ) => false;

	public virtual bool TryDelete( ModelDocContext ctx, ModelDocItem item ) => false;

	/// <summary>Called after a paste or duplicate lands one of these in its list.</summary>
	public virtual void OnInserted( ModelDocContext ctx, ModelDocKind kind, int index ) { }

	/// <summary>The list this kind's items live in, for shared copy/paste/duplicate; null if not a list entry</summary>
	public virtual System.Collections.IList ListFor( ModelDocContext ctx, ModelDocKind kind ) => null;

	/// <summary>Whether a file dragged onto one of these rows is one it could take.</summary>
	public virtual bool CanTake( ModelDocContext ctx, ModelDocItem item, string path ) => false;

	/// <summary>Takes it. Only ever called where <see cref="CanTake"/> said yes.</summary>
	public virtual void Take( ModelDocContext ctx, ModelDocItem item, string path ) { }

	public virtual void Bind( ModelDocContext ctx, ModelDocItem item, Inspectors.ControlSheet sheet, Panel body ) { }

	public virtual ScenePlacement Placement( ModelDocContext ctx, ModelDocItem item ) => null;

	public virtual ModelDefinition.ModelClip Clip( ModelDocContext ctx, ModelDocItem item ) => null;

	public virtual void DrawOverlay( Model model, ModelDefinition definition, ModelDocItem item, Action changed = null, bool resize = false ) { }
}

static class ModelDoc
{
	public static readonly ModelDocHandler[] All =
	[
		new TransformDoc(),
		new MirrorDoc(),
		new MaterialsDoc(),
		new MeshDoc(),
		new AnimationDoc(),
		new BodyGroupDoc(),
		new HullDoc(),
		new JointDoc(),
		new LookAtDoc(),
		new ExtraBoneDoc(),
		new AttachmentDoc(),
		new ConstraintDoc(),
		new WeightListDoc(),
		new IkChainDoc(),
		new JiggleDoc(),
		new MorphDoc(),
		new BoundsDoc(),
		new StepDoc(),
	];

	static readonly Dictionary<ModelDocKind, ModelDocHandler> _byKind = BuildIndex();

	static Dictionary<ModelDocKind, ModelDocHandler> BuildIndex()
	{
		var map = new Dictionary<ModelDocKind, ModelDocHandler>();
		foreach ( var handler in All )
		{
			map[handler.Kind] = handler;
			foreach ( var extra in handler.AlsoHandles )
				map[extra] = handler;
		}

		return map;
	}

	public static ModelDocHandler Of( ModelDocKind kind )
		=> _byKind.TryGetValue( kind, out var handler ) ? handler : null;

	public static IEnumerable<ModelDocItem> Collect( ModelDocContext ctx )
	{
		var items = new List<ModelDocItem>();
		foreach ( var handler in All )
			handler.Collect( ctx, items );
		return items;
	}
}
