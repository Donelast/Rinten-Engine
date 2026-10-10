using Rinten.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MdlEditor;

//Named bone weight lists on the model, which graphs reference by name (e.g. Bone Mask)
sealed class WeightListDoc : ModelDocHandler
{
	public override ModelDocKind Kind => ModelDocKind.WeightLists;
	public override IEnumerable<ModelDocKind> AlsoHandles => [ModelDocKind.WeightList, ModelDocKind.BoneWeight];
	public override string Icon => "filter_alt";

	public override bool CanDelete( ModelDocItem item )
		=> item.Kind is ModelDocKind.WeightList or ModelDocKind.BoneWeight;

	public override bool CanToggle( ModelDocItem item ) => item.Kind == ModelDocKind.WeightList;

	public override string IconFor( ModelDocItem item ) => item.Kind switch
	{
		ModelDocKind.BoneWeight => "share",
		ModelDocKind.WeightList => "filter_alt",
		_ => Icon,
	};

	public override void Collect( ModelDocContext ctx, List<ModelDocItem> items )
	{
		var lists = ctx.Definition.WeightLists;
		if ( !ModelDocContext.AddFolder( items, Kind, "Weight Lists", lists.Count ) ) return;
		if ( ctx.IsCollapsed( Kind ) ) return;

		for ( var i = 0; i < lists.Count; i++ )
		{
			var list = lists[i];

			items.Add( new ModelDocItem
			{
				Kind = ModelDocKind.WeightList,
				Title = string.IsNullOrWhiteSpace( list.Name ) ? $"Weight List {i}" : list.Name,
				Depth = 1,
				Index = i,
				Enabled = list.Enabled,
			} );

			for ( var b = 0; b < list.Bones.Count; b++ )
			{
				var entry = list.Bones[b];

				items.Add( new ModelDocItem
				{
					Kind = ModelDocKind.BoneWeight,
					Title = Row( entry ),
					Depth = 2,
					Index = b,
					ParentIndex = i,
				} );
			}
		}
	}

	//How one entry reads in the tree: the bone, its weight, and how far it reaches.
	static string Row( ModelDefinition.WeightList.BoneWeight entry )
	{
		var bone = string.IsNullOrWhiteSpace( entry.Bone ) ? "no bone" : entry.Bone;
		var reach = entry.Descendants ? " ↓" : "";

		return $"{bone}  ·  {entry.Weight:0.##}{reach}";
	}

	public override IEnumerable<ModelDocAdd> AddActions( ModelDocContext ctx )
	{
		yield return new( "Animation", "movie", "Weight List", "filter_alt", () =>
		{
			ctx.Definition.WeightLists.Add( new ModelDefinition.WeightList { Name = Unused( ctx, "weight_list" ) } );
			ctx.Host.Outliner.OnStructureChanged( ModelDocKind.WeightList, ctx.Definition.WeightLists.Count - 1 );
		} );
	}

	static string Unused( ModelDocContext ctx, string wanted )
	{
		var taken = ctx.Definition.WeightLists.Select( x => x.Name ?? "" ).ToList();

		if ( !taken.Contains( wanted, StringComparer.OrdinalIgnoreCase ) ) return wanted;

		for ( var i = 2; ; i++ )
		{
			var name = $"{wanted}_{i}";
			if ( !taken.Contains( name, StringComparer.OrdinalIgnoreCase ) ) return name;
		}
	}

	public override void AddContextOptions( Menu menu, ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.Kind == ModelDocKind.WeightList )
		{
			if ( !ModelDocContext.InRange( ctx.Definition.WeightLists, item.Index ) ) return;

			var list = ctx.Definition.WeightLists[item.Index];

			menu.AddOption( "Add Bone", "add", () =>
			{
				list.Bones.Add( new ModelDefinition.WeightList.BoneWeight() );
				ctx.Host.Outliner.OnStructureChanged( ModelDocKind.BoneWeight, list.Bones.Count - 1, item.Index );
			} );

			menu.AddOption( "Invert Weights", "flip", () =>
			{
				list.BaseWeight = 1.0f - list.BaseWeight;

				foreach ( var entry in list.Bones )
					entry.Weight = 1.0f - entry.Weight;

				ctx.Host.NotifyDefinitionChanged( rebuildInspector: true );
			} );

			menu.AddOption( "Clear Bones", "backspace", () =>
			{
				list.Bones.Clear();
				ctx.Host.Outliner.OnStructureChanged( ModelDocKind.WeightList, item.Index );
			} );

			return;
		}

		if ( item.Kind != ModelDocKind.BoneWeight ) return;
		if ( Entry( ctx, item ) is not { } written ) return;

		foreach ( var (title, icon, weight) in new[] { ("Full", "check_circle", 1.0f), ("Half", "adjust", 0.5f), ("None", "block", 0.0f) } )
		{
			menu.AddOption( title, icon, () =>
			{
				written.Weight = weight;
				ctx.Host.NotifyDefinitionChanged( rebuildInspector: true );
			} );
		}

		var reaches = menu.AddOption( "Affects Children", "account_tree", () =>
		{
			written.Descendants = !written.Descendants;
			ctx.Host.NotifyDefinitionChanged( rebuildInspector: true );
		} );

		reaches.Checkable = true;
		reaches.Checked = written.Descendants;
	}

	static ModelDefinition.WeightList.BoneWeight Entry( ModelDocContext ctx, ModelDocItem item )
	{
		if ( !ModelDocContext.InRange( ctx.Definition.WeightLists, item.ParentIndex ) ) return null;

		var bones = ctx.Definition.WeightLists[item.ParentIndex].Bones;

		return ModelDocContext.InRange( bones, item.Index ) ? bones[item.Index] : null;
	}

	public override bool TrySetEnabled( ModelDocContext ctx, ModelDocItem item, bool enabled )
	{
		if ( !ModelDocContext.InRange( ctx.Definition.WeightLists, item.Index ) ) return false;

		ctx.Definition.WeightLists[item.Index].Enabled = enabled;
		return true;
	}

	public override bool TryDelete( ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.Kind == ModelDocKind.WeightList && ModelDocContext.InRange( ctx.Definition.WeightLists, item.Index ) )
		{
			ctx.Definition.WeightLists.RemoveAt( item.Index );
			return true;
		}

		if ( item.Kind != ModelDocKind.BoneWeight ) return false;
		if ( !ModelDocContext.InRange( ctx.Definition.WeightLists, item.ParentIndex ) ) return false;

		var bones = ctx.Definition.WeightLists[item.ParentIndex].Bones;
		if ( !ModelDocContext.InRange( bones, item.Index ) ) return false;

		bones.RemoveAt( item.Index );
		return true;
	}

	public override System.Collections.IList ListFor( ModelDocContext ctx, ModelDocKind kind )
		=> kind == ModelDocKind.WeightList ? ctx.Definition.WeightLists : null;

	public override void Bind( ModelDocContext ctx, ModelDocItem item, Inspectors.ControlSheet sheet, Panel body )
	{
		if ( item.IsFolder )
		{
			ModelDocInspect.BindFolder( this, ctx, body,
				"Named bone weights a graph's Bone Mask node asks this model for." );
			return;
		}

		if ( item.Kind == ModelDocKind.WeightList )
		{
			BindList( ctx, item, body );
			return;
		}

		if ( Entry( ctx, item ) is not { } entry ) return;

		body.AddChild( ModelDocInspect.Note(
			"The weight reaches this bone and, unless Affects Children is off, everything under it." ) );

		ctx.BindObject( sheet, entry );
	}

	void BindList( ModelDocContext ctx, ModelDocItem item, Panel body )
	{
		if ( !ModelDocContext.InRange( ctx.Definition.WeightLists, item.Index ) ) return;

		var list = ctx.Definition.WeightLists[item.Index];

		//The sheet the inspector passes in is added below whatever a handler puts in the body, and the skeleton
		//belongs under the name rather than over it - so the two rows worth showing are bound here instead.
		var head = new Inspectors.ControlSheet { ShowPropertyNameInTooltip = true };
		var so = list.GetSerialized();
		so.OnPropertyChanged += _ => ctx.Host.NotifyDefinitionChanged();

		foreach ( var name in new[] { nameof( ModelDefinition.WeightList.Name ), nameof( ModelDefinition.WeightList.BaseWeight ) } )
		{
			if ( so.GetProperty( name ) is { } property )
				head.AddPropertyRow( property );
		}

		body.AddChild( head );

		var model = ctx.Host.CompiledModel;

		body.AddChild( new WeightListPainter( model, list,
			() => ctx.Host.NotifyDefinitionChanged(),
			() => ctx.Host.NotifyDefinitionChanged( rebuildInspector: true ) ) );

		body.AddChild( ModelDocInspect.Note(
			"Click a name to switch a bone in or out, drag the bar for a part weight. Only the bones you change are written down." ) );

		if ( Duplicated( ctx, list ) )
		{
			body.AddChild( ModelDocInspect.Note(
				"Another list has this name - a graph asking for it gets whichever comes first.",
				Theme.Red.WithAlpha( 0.7f ) ) );
		}
		else if ( string.IsNullOrWhiteSpace( list.Name ) )
		{
			body.AddChild( ModelDocInspect.Note(
				"A list with no name is one no graph can ask for.",
				Theme.Red.WithAlpha( 0.7f ) ) );
		}
	}

	static bool Duplicated( ModelDocContext ctx, ModelDefinition.WeightList list )
		=> !string.IsNullOrWhiteSpace( list.Name )
			&& ctx.Definition.WeightLists.Count( x => string.Equals( x.Name, list.Name, StringComparison.OrdinalIgnoreCase ) ) > 1;

	/// <summary>The skeleton in the viewport, each bone coloured by what the selected list weighs it - which is the half of editing a mask that a list of names cannot show.</summary>
	public override void DrawOverlay( Model model, ModelDefinition definition, ModelDocItem item, Action changed = null, bool resize = false )
	{
		if ( model is not { IsValid: true } || definition?.WeightLists is not { Count: > 0 } lists ) return;

		var at = item.Kind switch
		{
			ModelDocKind.WeightList => item.Index,
			ModelDocKind.BoneWeight => item.ParentIndex,
			_ => -1,
		};

		if ( at < 0 || at >= lists.Count ) return;

		var list = lists[at];
		var weights = WeightListPainter.Resolve( model, list );

		var selected = item.Kind == ModelDocKind.BoneWeight
			&& ModelDocContext.InRange( list.Bones, item.Index )
			? list.Bones[item.Index].Bone
			: "";

		//Composed once each, rather than once for the bone and again for every child that draws a line back up to it.
		var places = new Dictionary<string, Vector3>( StringComparer.OrdinalIgnoreCase );

		foreach ( var bone in model.Bones?.AllBones ?? [] )
			places[bone.Name] = ModelDocPhysics.BoneOf( model, bone.Name ).Position;

		foreach ( var bone in model.Bones?.AllBones ?? [] )
		{
			var weight = weights.TryGetValue( bone.Name, out var found ) ? found : list.BaseWeight;
			var origin = places[bone.Name];

			//Red where nothing gets through, green where all of it does, and the bones in between show the falloff somebody wrote.
			Gizmo.Draw.Color = Theme.Red.LerpTo( Theme.Green, weight ).WithAlpha( 0.25f + weight * 0.7f );
			Gizmo.Draw.LineThickness = weight > 0.001f ? 2.0f : 1.0f;

			if ( bone.Parent is not null && places.TryGetValue( bone.Parent.Name, out var above ) )
				Gizmo.Draw.Line( above, origin );

			if ( string.Equals( bone.Name, selected, StringComparison.OrdinalIgnoreCase ) )
			{
				Gizmo.Draw.Color = Theme.Primary;
				Gizmo.Draw.LineSphere( new Sphere( origin, ModelDocPhysics.DefaultRadius( model ) * 0.5f ), 8 );
			}
		}
	}
}
