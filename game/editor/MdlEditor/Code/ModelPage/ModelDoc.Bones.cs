using Rinten.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MdlEditor;

//Extra bones the definition adds to the skeleton, compiled in like any other bone
sealed class ExtraBoneDoc : ModelDocHandler
{
	public override ModelDocKind Kind => ModelDocKind.Bones;

	public override IEnumerable<ModelDocKind> AlsoHandles => [ModelDocKind.Bone];

	public override string Icon => "account_tree";

	public override bool CanDelete( ModelDocItem item ) => !item.IsFolder && item.Kind == ModelDocKind.Bone;

	//-- the tree --------------------------------------------------------------

	public override void Collect( ModelDocContext ctx, List<ModelDocItem> items )
	{
		ctx.Definition.Bones ??= new List<ModelDefinition.ExtraBone>();

		var bones = ctx.Definition.Bones;
		if ( !ModelDocContext.AddFolder( items, Kind, "Bones", bones.Count ) ) return;
		if ( ctx.IsCollapsed( Kind ) ) return;

		ModelDocFolders.Collect( items, ctx, Kind, bones, x => x.Folder, ( i, bone ) => new ModelDocItem
		{
			Kind = ModelDocKind.Bone,
			Title = Titled( bone ),
			Depth = 1,
			Index = i,
		} );
	}

	static string Titled( ModelDefinition.ExtraBone bone )
	{
		var name = string.IsNullOrWhiteSpace( bone.Name ) ? "Unnamed" : bone.Name;

		return string.IsNullOrWhiteSpace( bone.Parent ) ? $"{name}  ·  model" : $"{name}  ·  {bone.Parent}";
	}

	//-- adding and removing ---------------------------------------------------

	public override IEnumerable<ModelDocAdd> AddActions( ModelDocContext ctx )
	{
		yield return new( "Skeleton", "device_hub", "Bone", "account_tree", () =>
		{
			var parent = SelectedBone( ctx );

			ctx.Definition.Bones ??= new List<ModelDefinition.ExtraBone>();
			ctx.Definition.Bones.Add( new ModelDefinition.ExtraBone
			{
				Name = Unique( ctx, "bone" ),
				Parent = parent,
			} );

			ctx.Host.Outliner.OnStructureChanged( ModelDocKind.Bone, ctx.Definition.Bones.Count - 1 );
		} );
	}

	//The bone a new one hangs off: the one picked in the tree, or whatever the picked row is on. Nothing picked puts it on the model itself.
	static string SelectedBone( ModelDocContext ctx )
	{
		var item = ctx.Host.Outliner?.Selected;
		if ( item is null || item.IsFolder ) return "";

		if ( item.Kind == ModelDocKind.Bone && ModelDocContext.InRange( ctx.Definition.Bones, item.Index ) )
			return ctx.Definition.Bones[item.Index].Name ?? "";

		if ( item.Kind == ModelDocKind.Attachment && ModelDocContext.InRange( ctx.Definition.Attachments, item.Index ) )
			return ctx.Definition.Attachments[item.Index].Parent ?? "";

		return ModelDocPhysics.SelectedBone( ctx );
	}

	//Every name a bone of this model already goes by - the art's, the compiled model's and the added ones.
	static HashSet<string> Taken( ModelDocContext ctx, ModelDefinition.ExtraBone except = null )
	{
		var taken = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var name in ModelSourceCatalog.Bones( ctx.Host.Asset, ctx.Definition ) )
			taken.Add( name );

		foreach ( var bone in ctx.Definition.Bones ?? [] )
		{
			if ( bone is null || ReferenceEquals( bone, except ) ) continue;
			if ( bone.Name is { Length: > 0 } name ) taken.Add( name );
		}

		return taken;
	}

	//A name no other bone has, since everything finds a bone by its name.
	static string Unique( ModelDocContext ctx, string wanted, ModelDefinition.ExtraBone except = null )
	{
		var taken = Taken( ctx, except );

		if ( !taken.Contains( wanted ) ) return wanted;

		for ( var i = 2; ; i++ )
		{
			var tried = $"{wanted}_{i}";
			if ( !taken.Contains( tried ) ) return tried;
		}
	}

	/// <summary>A pasted or duplicated one under its own name, not the original's.</summary>
	public override void OnInserted( ModelDocContext ctx, ModelDocKind kind, int index )
	{
		if ( kind != ModelDocKind.Bone ) return;
		if ( !ModelDocContext.InRange( ctx.Definition.Bones, index ) ) return;

		var bone = ctx.Definition.Bones[index];
		bone.Name = Unique( ctx, string.IsNullOrWhiteSpace( bone.Name ) ? "bone" : bone.Name, except: bone );
	}

	public override void AddContextOptions( Menu menu, ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.IsFolder || item.Kind != ModelDocKind.Bone ) return;
		if ( !ModelDocContext.InRange( ctx.Definition.Bones, item.Index ) ) return;

		var bone = ctx.Definition.Bones[item.Index];

		if ( menu.HasOptions ) menu.AddSeparator();

		menu.AddOption( "Add Bone Here", "account_tree", () => Edit( ctx, "Add Bone", () =>
		{
			ctx.Definition.Bones.Add( new ModelDefinition.ExtraBone { Name = Unique( ctx, "bone" ), Parent = bone.Name } );
		} ) );

		menu.AddOption( "Add Attachment Here", "push_pin", () => Edit( ctx, "Add Attachment", () =>
		{
			ctx.Definition.Attachments ??= new List<ModelDefinition.Attachment>();
			ctx.Definition.Attachments.Add( new ModelDefinition.Attachment { Name = bone.Name, Parent = bone.Name } );
		} ) );

		menu.AddOption( "Snap to Parent", "center_focus_strong", () => Edit( ctx, "Snap to Parent", () =>
		{
			bone.Position = Vector3.Zero;
			bone.Rotation = default;
		} ) );
	}

	//One undo step around a change that redraws the tree and the sheet.
	static void Edit( ModelDocContext ctx, string name, Action change )
	{
		ctx.Host.BeginDefinitionEdit( name );
		change();
		ctx.Host.NotifyDefinitionChanged( rebuildInspector: true );
		ctx.Host.EndDefinitionEdit( name );
		ctx.Host.Outliner?.RefreshTree();
	}

	/// <summary>Removes a bone, re-parenting its children with its offset folded in so nothing moves</summary>
	public override bool TryDelete( ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.Kind != ModelDocKind.Bone ) return false;
		if ( !ModelDocContext.InRange( ctx.Definition.Bones, item.Index ) ) return false;

		var gone = ctx.Definition.Bones[item.Index];
		ctx.Definition.Bones.RemoveAt( item.Index );

		if ( string.IsNullOrWhiteSpace( gone.Name ) ) return true;

		foreach ( var child in ctx.Definition.Bones )
		{
			if ( child is null || !Named( child.Parent, gone.Name ) ) continue;

			var local = gone.Local.ToWorld( child.Local );
			child.Parent = gone.Parent;
			child.Position = local.Position;
			child.Rotation = local.Rotation.Angles();
		}

		foreach ( var attachment in ctx.Definition.Attachments ?? [] )
		{
			if ( attachment is null ) continue;

			if ( Named( attachment.Parent, gone.Name ) )
			{
				var local = gone.Local.ToWorld( attachment.Local );
				attachment.Parent = gone.Parent;
				attachment.Position = local.Position;
				attachment.Rotation = local.Rotation.Angles();
			}

			foreach ( var influence in attachment.Influences ?? [] )
			{
				if ( influence is null || !Named( influence.Bone, gone.Name ) ) continue;

				var local = gone.Local.ToWorld( influence.Local );
				influence.Bone = gone.Parent;
				influence.Position = local.Position;
				influence.Rotation = local.Rotation.Angles();
			}
		}

		return true;
	}

	public override System.Collections.IList ListFor( ModelDocContext ctx, ModelDocKind kind )
		=> kind == ModelDocKind.Bone ? ctx.Definition.Bones : null;

	static bool Named( string a, string b ) => string.Equals( a, b, StringComparison.OrdinalIgnoreCase );

	//-- the inspector ---------------------------------------------------------

	public override void Bind( ModelDocContext ctx, ModelDocItem item, Inspectors.ControlSheet sheet, Panel body )
	{
		if ( item.IsFolder )
		{
			ModelDocInspect.BindFolder( this, ctx, body,
				"Bones added to the skeleton - a grip, a sheath on a back - moved here rather than in the modelling program." );
			return;
		}

		if ( !ModelDocContext.InRange( ctx.Definition.Bones, item.Index ) ) return;

		var bone = ctx.Definition.Bones[item.Index];
		var named = bone.Name;

		var fields = new Inspectors.ControlSheet { ShowPropertyNameInTooltip = true };

		//A rename takes everything that named the bone along with it: a child bone or an attachment still naming the old one would fall off it.
		ctx.BindObject( fields, bone, changed =>
		{
			if ( changed.Name != nameof( ModelDefinition.ExtraBone.Name ) ) return;

			Renamed( ctx.Definition, named, bone.Name );
			named = bone.Name;
		} );

		body.AddChild( fields );

		body.AddChild( ModelDocInspect.Note(
			"A bone of the skeleton: the game asks the model for it by name, and attachments and other bones can "
			+ "hang off it. Nothing of the mesh follows it - which vertex follows which bone is painted with the art." ) );

		if ( Wrong( ctx, bone ) is { } wrong )
			body.AddChild( ModelDocInspect.Note( wrong, Theme.Yellow ) );
	}

	//Everything in the definition that named from, pointed at to.
	static void Renamed( ModelDefinition definition, string from, string to )
	{
		if ( string.IsNullOrWhiteSpace( from ) || Named( from, to ) ) return;

		foreach ( var other in definition.Bones ?? [] )
		{
			if ( other is not null && Named( other.Parent, from ) ) other.Parent = to;
		}

		foreach ( var attachment in definition.Attachments ?? [] )
		{
			if ( attachment is null ) continue;

			if ( Named( attachment.Parent, from ) ) attachment.Parent = to;

			foreach ( var influence in attachment.Influences ?? [] )
			{
				if ( influence is not null && Named( influence.Bone, from ) ) influence.Bone = to;
			}
		}
	}

	//What would keep it out of the skeleton, where that can be said.
	static string Wrong( ModelDocContext ctx, ModelDefinition.ExtraBone bone )
	{
		if ( string.IsNullOrWhiteSpace( bone.Name ) )
			return "Unnamed - it is left out of the skeleton.";

		var source = ModelSourceCatalog.Bones( ctx.Host.Asset, ctx.Definition );

		if ( source.Any( x => Named( x, bone.Name ) ) )
			return $"The art already has a bone called {bone.Name} - that one is kept and this one is left out. Give it a name of its own.";

		if ( ( ctx.Definition.Bones ?? [] ).Count( x => x is not null && Named( x.Name, bone.Name ) ) > 1 )
			return $"Another added bone is called {bone.Name} too - only one of them is kept.";

		if ( string.IsNullOrWhiteSpace( bone.Parent ) ) return null;

		//Up the chain of added bones to the art's, the way the compiler hangs them.
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase ) { bone.Name };
		var parent = bone.Parent;

		while ( !string.IsNullOrWhiteSpace( parent ) )
		{
			if ( !seen.Add( parent ) )
				return "It hangs off itself through the bones below it - it is left out of the skeleton.";

			if ( source.Any( x => Named( x, parent ) ) ) return null;

			var above = ctx.Definition.Bones?.FirstOrDefault( x => x is not null && Named( x.Name, parent ) );

			if ( above is null )
				return $"This model has no bone called {parent} - it is left out of the skeleton.";

			parent = above.Parent;
		}

		return null;
	}

	//-- the viewport ----------------------------------------------------------

	/// <summary>A handle on the picked bone, moving and turning it relative to its parent - written back as the offset the definition keeps.</summary>
	public override ScenePlacement Placement( ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.IsFolder || item.Kind != ModelDocKind.Bone ) return null;

		var definition = ctx.Definition;
		if ( !ModelDocContext.InRange( definition.Bones, item.Index ) ) return null;

		var model = ctx.Host.CompiledModel;
		var bone = definition.Bones[item.Index];

		Transform Parent() => AttachmentDoc.Frame( model, definition, bone.Parent, ctx.Host.BoneNow );

		return new ScenePlacement
		{
			Name = $"bone.{item.Index}",
			GetPosition = () => Parent().ToWorld( bone.Local ).Position,
			GetRotation = () => Parent().ToWorld( bone.Local ).Rotation.Angles(),
			Set = ( position, rotation ) =>
			{
				var local = Parent().ToLocal( new Transform( position, rotation.ToRotation() ) );
				bone.Position = local.Position;
				bone.Rotation = local.Rotation.Angles();
				ctx.Host.NotifyDefinitionChanged();
			}
		};
	}
}
