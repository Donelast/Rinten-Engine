using Rinten.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MdlEditor;

//Body groups: sets of mesh choices, one showing per group; meshes no choice claims always draw
sealed class BodyGroupDoc : ModelDocHandler
{
	public override ModelDocKind Kind => ModelDocKind.BodyGroups;
	public override IEnumerable<ModelDocKind> AlsoHandles => [ModelDocKind.BodyGroup, ModelDocKind.BodyGroupChoice];
	public override string Icon => "layers";

	public override bool CanDelete( ModelDocItem item )
		=> !item.IsFolder && item.Kind is ModelDocKind.BodyGroup or ModelDocKind.BodyGroupChoice;

	public override bool CanToggle( ModelDocItem item ) => item.Kind == ModelDocKind.BodyGroup;

	public override string IconFor( ModelDocItem item ) => item.Kind switch
	{
		ModelDocKind.BodyGroupChoice => !string.IsNullOrWhiteSpace( item.Icon ) ? item.Icon : "check_box_outline_blank",
		ModelDocKind.BodyGroup => "layers",
		_ => Icon,
	};

	//-- the tree --------------------------------------------------------------

	public override void Collect( ModelDocContext ctx, List<ModelDocItem> items )
	{
		var groups = ctx.Definition.BodyGroups;
		if ( !ModelDocContext.AddFolder( items, Kind, "Body Groups", groups.Count ) ) return;
		if ( ctx.IsCollapsed( Kind ) ) return;

		for ( var g = 0; g < groups.Count; g++ )
		{
			var group = groups[g];
			if ( group is null ) continue;

			var choices = group.Choices ?? new List<ModelDefinition.BodyChoice>();

			items.Add( new ModelDocItem
			{
				Kind = ModelDocKind.BodyGroup,
				Title = $"{Named( group.Name, $"Group {g}" )}  ·  {Count( choices.Count, "choice", "choices" )}",
				Depth = 1,
				Index = g,
				Enabled = group.Enabled,
			} );

			for ( var c = 0; c < choices.Count; c++ )
			{
				var choice = choices[c];
				var meshes = Claimed( choice );

				items.Add( new ModelDocItem
				{
					Kind = ModelDocKind.BodyGroupChoice,

					//The two that matter are marked, because both are position rather than a property:
					//the first choice is the default and the last empty one is what takes the part off.
					Title = Row( Named( choice?.Name, $"Choice {c}" ), c, meshes ),
					Depth = 2,
					Index = c,
					ParentIndex = g,
					Icon = meshes == 0 ? "visibility_off" : "check_box_outline_blank",
				} );
			}
		}
	}

	static string Row( string name, int index, int meshes )
	{
		var about = meshes == 0 ? "hides the part" : Count( meshes, "mesh", "meshes" );

		return index == 0 ? $"{name}  ·  {about}  ·  default" : $"{name}  ·  {about}";
	}

	static string Named( string name, string fallback )
		=> string.IsNullOrWhiteSpace( name ) ? fallback : name;

	static string Count( int many, string one, string more )
		=> many == 1 ? $"1 {one}" : $"{many} {more}";

	//How many meshes a choice actually names, blanks not counted.
	static int Claimed( ModelDefinition.BodyChoice choice )
		=> choice?.Meshes?.Count( x => !string.IsNullOrWhiteSpace( x ) ) ?? 0;

	//-- adding ----------------------------------------------------------------

	public override IEnumerable<ModelDocAdd> AddActions( ModelDocContext ctx )
	{
		yield return new( "Appearance", "palette", "Body Group", "layers", () =>
		{
			//Two choices, the second empty: one choice is a group that cannot swap anything, and
			//the pair is what nearly every group turns out to be - the part, and the part taken off.
			ctx.Definition.BodyGroups.Add( new ModelDefinition.BodyGroup
			{
				Name = Unused( ctx, "group" ),
				Choices =
				[
					new ModelDefinition.BodyChoice { Name = "default" },
					new ModelDefinition.BodyChoice { Name = "none" },
				],
			} );

			ctx.Host.Outliner.OnStructureChanged( ModelDocKind.BodyGroup, ctx.Definition.BodyGroups.Count - 1 );
		} );
	}

	static string Unused( ModelDocContext ctx, string wanted )
	{
		var taken = ctx.Definition.BodyGroups.Select( x => x?.Name ?? "" ).ToList();

		if ( !taken.Contains( wanted, StringComparer.OrdinalIgnoreCase ) ) return wanted;

		for ( var i = 2; ; i++ )
		{
			var name = $"{wanted}_{i}";
			if ( !taken.Contains( name, StringComparer.OrdinalIgnoreCase ) ) return name;
		}
	}

	public override void AddContextOptions( Menu menu, ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.Kind == ModelDocKind.BodyGroup )
		{
			GroupOptions( menu, ctx, item );
			return;
		}

		if ( item.Kind == ModelDocKind.BodyGroupChoice ) ChoiceOptions( menu, ctx, item );
	}

	void GroupOptions( Menu menu, ModelDocContext ctx, ModelDocItem item )
	{
		if ( Group( ctx, item.Index ) is not { } group ) return;

		menu.AddOption( "Add Choice", "add", () =>
		{
			group.Choices.Add( new ModelDefinition.BodyChoice { Name = "choice" } );
			ctx.Host.Outliner.OnStructureChanged( ModelDocKind.BodyGroupChoice, group.Choices.Count - 1, item.Index );
		} );

		menu.AddOption( "Add Hidden Choice", "visibility_off", () =>
		{
			group.Choices.Add( new ModelDefinition.BodyChoice { Name = "none" } );
			ctx.Host.Outliner.OnStructureChanged( ModelDocKind.BodyGroupChoice, group.Choices.Count - 1, item.Index );
		} ).ToolTip = "A choice that turns no mesh on, put last, which is how the part comes off.";

		menu.AddSeparator();

		AddMove( menu, ctx, ctx.Definition.BodyGroups, item.Index, ModelDocKind.BodyGroup, -1 );
		AddMove( menu, ctx, ctx.Definition.BodyGroups, item.Index, ModelDocKind.BodyGroup, 1 );
	}

	void ChoiceOptions( Menu menu, ModelDocContext ctx, ModelDocItem item )
	{
		if ( Group( ctx, item.ParentIndex ) is not { } group ) return;
		if ( !ModelDocContext.InRange( group.Choices, item.Index ) ) return;

		var choice = group.Choices[item.Index];

		AddMove( menu, ctx, group.Choices, item.Index, ModelDocKind.BodyGroupChoice, -1, item.ParentIndex );
		AddMove( menu, ctx, group.Choices, item.Index, ModelDocKind.BodyGroupChoice, 1, item.ParentIndex );

		menu.AddSeparator();

		if ( item.Index != 0 )
		{
			menu.AddOption( "Make Default", "star", () =>
			{
				Move( ctx, group.Choices, item.Index, 0 );
				ctx.Host.Outliner.OnStructureChanged( ModelDocKind.BodyGroupChoice, 0, item.ParentIndex );
			} ).ToolTip = "The first choice is what the model shows when nobody has picked one.";
		}

		if ( Claimed( choice ) > 0 )
		{
			menu.AddOption( "Turn Nothing On", "visibility_off", () =>
			{
				ctx.Host.BeginDefinitionEdit( "Clear Choice" );
				choice.Meshes.Clear();
				ctx.Host.NotifyDefinitionChanged( rebuildInspector: true );
				ctx.Host.EndDefinitionEdit( "Clear Choice" );
			} );
		}
	}

	//Move an entry one place either way, since where it sits is what it means.
	void AddMove<T>( Menu menu, ModelDocContext ctx, List<T> list, int index, ModelDocKind kind, int by, int parent = -1 )
	{
		var to = index + by;
		if ( !ModelDocContext.InRange( list, index ) || !ModelDocContext.InRange( list, to ) ) return;

		menu.AddOption( by < 0 ? "Move Up" : "Move Down", by < 0 ? "arrow_upward" : "arrow_downward", () =>
		{
			Move( ctx, list, index, to );
			ctx.Host.Outliner.OnStructureChanged( kind, to, parent );
		} );
	}

	static void Move<T>( ModelDocContext ctx, List<T> list, int from, int to )
	{
		ctx.Host.BeginDefinitionEdit( "Reorder" );

		var moved = list[from];
		list.RemoveAt( from );
		list.Insert( to, moved );

		ctx.Host.EndDefinitionEdit( "Reorder" );
	}

	//-- editing ---------------------------------------------------------------

	public override bool TrySetEnabled( ModelDocContext ctx, ModelDocItem item, bool enabled )
	{
		if ( Group( ctx, item.Index ) is not { } group ) return false;

		group.Enabled = enabled;
		return true;
	}

	public override bool TryDelete( ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.Kind == ModelDocKind.BodyGroup && ModelDocContext.InRange( ctx.Definition.BodyGroups, item.Index ) )
		{
			ctx.Definition.BodyGroups.RemoveAt( item.Index );
			return true;
		}

		if ( item.Kind != ModelDocKind.BodyGroupChoice ) return false;
		if ( Group( ctx, item.ParentIndex ) is not { } group ) return false;
		if ( !ModelDocContext.InRange( group.Choices, item.Index ) ) return false;

		group.Choices.RemoveAt( item.Index );
		return true;
	}

	public override System.Collections.IList ListFor( ModelDocContext ctx, ModelDocKind kind )
		=> kind == ModelDocKind.BodyGroup ? ctx.Definition.BodyGroups : null;

	static ModelDefinition.BodyGroup Group( ModelDocContext ctx, int index )
	{
		if ( !ModelDocContext.InRange( ctx.Definition.BodyGroups, index ) ) return null;

		var group = ctx.Definition.BodyGroups[index];
		if ( group is null ) return null;

		group.Choices ??= new List<ModelDefinition.BodyChoice>();
		return group;
	}

	//-- the inspector ---------------------------------------------------------

	public override void Bind( ModelDocContext ctx, ModelDocItem item, Inspectors.ControlSheet sheet, Panel body )
	{
		if ( item.IsFolder )
		{
			BindFolder( ctx, body );
			return;
		}

		if ( item.Kind == ModelDocKind.BodyGroup )
		{
			BindGroup( ctx, item, body );
			return;
		}

		if ( item.Kind == ModelDocKind.BodyGroupChoice ) BindChoice( ctx, item, body );
	}

	void BindFolder( ModelDocContext ctx, Panel body )
	{
		ModelDocInspect.BindFolder( this, ctx, body,
			"Parts of the model that can be swapped or taken off. One choice of each group shows at a time; "
			+ "a mesh no choice claims is always drawn." );

		var layout = ctx.Definition.BodyGroupBits();

		body.AddChild( ModelDocInspect.Note( Budget( layout ), Spent( layout ) ) );
	}

	//How much of the sixty-four-bit mask this model has spent.
	static string Budget( ModelDefinition.BodyGroupLayout layout )
	{
		var spent = $"{layout.Bits} of {ModelDefinition.BodyGroupLayout.MaxChoices} choices used across {Count( layout.Parts.Count, "group", "groups" )}.";

		return layout.Dropped > 0
			? $"{spent} {Count( layout.Dropped, "choice", "choices" )} past the end - the mask is sixty-four bits and those will not be compiled."
			: spent;
	}

	static Color? Spent( ModelDefinition.BodyGroupLayout layout )
	{
		if ( layout.Dropped > 0 ) return Theme.Red.WithAlpha( 0.8f );
		if ( layout.Bits > ModelDefinition.BodyGroupLayout.MaxChoices - 8 ) return Theme.Yellow.WithAlpha( 0.8f );

		return null;
	}

	void BindGroup( ModelDocContext ctx, ModelDocItem item, Panel body )
	{
		if ( Group( ctx, item.Index ) is not { } group ) return;

		//The name belongs over the table rather than under it, so it is bound here instead of on the sheet the inspector adds below the body.
		body.AddChild( Head( ctx, group, nameof( ModelDefinition.BodyGroup.Name ) ) );

		var meshes = ModelDocInspect.MeshSlotNames( ctx )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToList();

		body.AddChild( new BodyGroupPainter( group, meshes,
			() => ctx.Host.NotifyDefinitionChanged(),
			() => ctx.Host.NotifyDefinitionChanged( rebuildInspector: true ) ) );

		body.AddChild( ModelDocInspect.Note(
			"A column per choice, a row per mesh. Click a cell to put that mesh in that choice. "
			+ "A column with nothing in it hides the part." ) );

		foreach ( var (text, colour) in Warnings( ctx, item.Index, group ) )
			body.AddChild( ModelDocInspect.Note( text, colour ) );
	}

	void BindChoice( ModelDocContext ctx, ModelDocItem item, Panel body )
	{
		if ( Group( ctx, item.ParentIndex ) is not { } group ) return;
		if ( !ModelDocContext.InRange( group.Choices, item.Index ) ) return;

		var choice = group.Choices[item.Index];

		body.AddChild( Head( ctx, choice, nameof( ModelDefinition.BodyChoice.Name ) ) );

		body.AddChild( ModelDocInspect.Note( item.Index == 0
			? "The first choice of a group is what the model shows when nobody has picked one."
			: "Nothing ticked hides this group's meshes, which is how the part comes off." ) );

		var meshes = ModelDocInspect.MeshSlotNames( ctx )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToList();

		body.AddChild( new BodyGroupPainter( group, meshes,
			() => ctx.Host.NotifyDefinitionChanged(),
			() => ctx.Host.NotifyDefinitionChanged( rebuildInspector: true ) )
		{
			Highlight = item.Index,
		} );
	}

	//One property bound above whatever the body puts under it.
	static Inspectors.ControlSheet Head( ModelDocContext ctx, object target, params string[] names )
	{
		var head = new Inspectors.ControlSheet { ShowPropertyNameInTooltip = true };
		var so = target.GetSerialized();
		so.OnPropertyChanged += _ => ctx.Host.NotifyDefinitionChanged();

		foreach ( var name in names )
		{
			if ( so.GetProperty( name ) is { } property )
				head.AddPropertyRow( property );
		}

		return head;
	}

	//What is wrong with a group, in the order somebody would fix it. All of these compile. None of them do what the artist meant, and
	//every one of them is invisible until the model is in a scene and wearing the wrong head - which is the case for saying so here.
	static IEnumerable<(string Text, Color? Colour)> Warnings( ModelDocContext ctx, int index, ModelDefinition.BodyGroup group )
	{
		var red = Theme.Red.WithAlpha( 0.7f );

		if ( string.IsNullOrWhiteSpace( group.Name ) )
			yield return ("A group with no name is one no code can ask for by name.", red);
		else if ( ctx.Definition.BodyGroups.Count( x => string.Equals( x?.Name, group.Name, StringComparison.OrdinalIgnoreCase ) ) > 1 )
			yield return ("Another group has this name - code asking for it gets whichever comes first.", red);

		if ( group.Choices.Count < 2 )
			yield return ("A group with one choice never swaps anything. Add the choice it swaps to.", null);

		var layout = ctx.Definition.BodyGroupBits();
		var part = layout.Parts.FirstOrDefault( x => x.Index == PartIndex( ctx, index ) );

		if ( part is not null && part.Hidden >= 0 && !part.HidesLast )
		{
			yield return ("The choice that turns nothing on is not the last one. "
				+ "Anything taking this part off picks the last choice without looking, so it would show the wrong one instead.", red);
		}

		//Twice in one group and the group cannot swap it away: whichever choice is showing, the mesh is drawn.
		var twice = group.Choices
			.SelectMany( x => x?.Meshes ?? new List<string>() )
			.Where( x => !string.IsNullOrWhiteSpace( x ) )
			.GroupBy( x => x, StringComparer.OrdinalIgnoreCase )
			.Where( x => x.Count() > 1 )
			.Select( x => x.Key )
			.ToList();

		if ( twice.Count > 0 )
			yield return ($"In more than one choice of this group, so it is drawn either way: {string.Join( ", ", twice )}", red);

		var known = ModelDocInspect.MeshSlotNames( ctx );

		var missing = group.Choices
			.SelectMany( x => x?.Meshes ?? new List<string>() )
			.Where( x => !string.IsNullOrWhiteSpace( x ) && !known.Contains( x, StringComparer.OrdinalIgnoreCase ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToList();

		if ( missing.Count > 0 )
			yield return ($"Not in the export: {string.Join( ", ", missing )}", red);

		if ( ctx.Host.CompiledModel is { IsValid: true } model
			&& !string.IsNullOrWhiteSpace( group.Name )
			&& model.Parts.Get( group.Name ) is null )
		{
			yield return ("The compiled model beside this file has no such group yet, so the preview cannot show it. Compile the model.", null);
		}
	}

	//Where a group sits among the ones that were given bits, which is not where it sits in the file once something above it is switched off.
	static int PartIndex( ModelDocContext ctx, int index )
	{
		var at = 0;

		for ( var i = 0; i < ctx.Definition.BodyGroups.Count && i < index; i++ )
		{
			var group = ctx.Definition.BodyGroups[i];
			if ( group is { Enabled: true, Choices.Count: > 0 } ) at++;
		}

		return at;
	}
}
