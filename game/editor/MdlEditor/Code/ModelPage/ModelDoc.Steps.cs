using System;
using System.Collections.Generic;

//Build steps and their Add menu, found by type; unloaded step types round-trip as UnknownStep
sealed class StepDoc : ModelDocHandler
{
	public override ModelDocKind Kind => ModelDocKind.Steps;
	public override IEnumerable<ModelDocKind> AlsoHandles => [ModelDocKind.Step, ModelDocKind.StepPart];
	public override string Icon => "build";
	public override bool CanDelete( ModelDocItem item ) => item.Kind is ModelDocKind.Step or ModelDocKind.StepPart;
	public override bool CanToggle( ModelDocItem item ) => item.Kind == ModelDocKind.Step;

	public override void Collect( ModelDocContext ctx, List<ModelDocItem> items )
	{
		var steps = ctx.Definition.Steps ??= new List<ModelDefinition.BuildStep>();

		if ( !ModelDocContext.AddFolder( items, Kind, "Build Steps", steps.Count ) ) return;
		if ( ctx.IsCollapsed( Kind ) ) return;

		for ( var i = 0; i < steps.Count; i++ )
		{
			var step = steps[i];
			if ( step is null ) continue;

			var unknown = step is ModelDefinition.UnknownStep;

			items.Add( new ModelDocItem
			{
				Kind = ModelDocKind.Step,
				Title = Label( step ),
				Depth = 1,
				Index = i,
				Enabled = step.Enabled,
				Icon = unknown ? "help" : IconOf( step.GetType() ),
			} );

			//The pieces of a step, where it has any - the way the events along a clip hang under it. What
			//they are is the step's business; all this needs is a row to pick and an index to hand back.
			var parts = step.Parts;

			for ( var p = 0; p < parts.Count; p++ )
			{
				var part = parts[p];
				if ( part is null ) continue;

				items.Add( new ModelDocItem
				{
					Kind = ModelDocKind.StepPart,
					Title = string.IsNullOrWhiteSpace( part.Title ) ? $"{p}" : part.Title,
					Depth = 2,
					Index = p,
					ParentIndex = i,
					Icon = string.IsNullOrWhiteSpace( part.Icon ) ? "radio_button_unchecked" : part.Icon,
				} );
			}
		}
	}

	//The step a row belongs to, whether the row is the step or one of its parts.
	static ModelDefinition.BuildStep StepOf( ModelDocContext ctx, ModelDocItem item )
	{
		var index = item.Kind == ModelDocKind.StepPart ? item.ParentIndex : item.Index;

		return ModelDocContext.InRange( ctx.Definition.Steps, index )
			? ctx.Definition.Steps[index]
			: null;
	}

	public override IEnumerable<ModelDocAdd> AddActions( ModelDocContext ctx )
	{
		foreach ( var described in Available() )
		{
			//Captured per entry: the menu runs these later, and a loop variable shared by every closure would add the last one six times.
			var target = described.TargetType;
			var title = string.IsNullOrWhiteSpace( described.Title ) ? described.Name : described.Title;
			var icon = string.IsNullOrWhiteSpace( described.Icon ) ? "build" : described.Icon;

			yield return new ModelDocAdd( "Build", "build", title, icon, () => Add( ctx, target, title ) );
		}
	}

	//Every step type loaded, minus the ones nobody can mean to add.
	static IEnumerable<TypeDescription> Available()
	{
		var library = Game.TypeLibrary;
		if ( library is null ) yield break;

		foreach ( var described in library.GetTypes<ModelDefinition.BuildStep>() )
		{
			if ( described?.TargetType is not { } target ) continue;
			if ( described.IsAbstract ) continue;
			if ( target == typeof( ModelDefinition.UnknownStep ) ) continue;

			yield return described;
		}
	}

	static void Add( ModelDocContext ctx, Type type, string title )
	{
		if ( Game.TypeLibrary?.Create<ModelDefinition.BuildStep>( type ) is not { } step ) return;

		if ( string.IsNullOrWhiteSpace( step.Name ) ) step.Name = title;

		ctx.Definition.Steps ??= new List<ModelDefinition.BuildStep>();
		ctx.Definition.Steps.Add( step );

		ctx.Host.Outliner.OnStructureChanged( ModelDocKind.Step, ctx.Definition.Steps.Count - 1 );
	}

	public override bool TrySetEnabled( ModelDocContext ctx, ModelDocItem item, bool enabled )
	{
		if ( !ModelDocContext.InRange( ctx.Definition.Steps, item.Index ) ) return false;

		ctx.Definition.Steps[item.Index].Enabled = enabled;
		return true;
	}

	public override bool TryDelete( ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.Kind == ModelDocKind.StepPart )
			return StepOf( ctx, item )?.RemovePart( item.Index ) ?? false;

		if ( !ModelDocContext.InRange( ctx.Definition.Steps, item.Index ) ) return false;

		ctx.Definition.Steps.RemoveAt( item.Index );
		return true;
	}

	public override void AddContextOptions( Menu menu, ModelDocContext ctx, ModelDocItem item )
	{
		if ( StepOf( ctx, item ) is not { CanAddPart: true } step ) return;

		var at = ctx.Definition.Steps.IndexOf( step );

		menu.AddOption( "Add Point", "add_location", () =>
		{
			var made = step.AddPart();
			if ( made < 0 ) return;

			ctx.Host.Outliner.OnStructureChanged( ModelDocKind.Step, at );
		} );
	}

	public override System.Collections.IList ListFor( ModelDocContext ctx, ModelDocKind kind )
		=> kind == ModelDocKind.Step ? ctx.Definition.Steps : null;

	public override void Bind( ModelDocContext ctx, ModelDocItem item, Editor.Inspectors.ControlSheet sheet, Rinten.UI.Panel body )
	{
		if ( item.IsFolder )
		{
			ModelDocInspect.BindFolder( this, ctx, body,
				"Steps this project runs when the model compiles, in order." );
			return;
		}

		if ( StepOf( ctx, item ) is not { } step ) return;

		if ( item.Kind == ModelDocKind.StepPart )
		{
			var parts = step.Parts;

			if ( item.Index >= 0 && item.Index < parts.Count && parts[item.Index]?.Target is { } target )
				ctx.BindObject( sheet, target );

			return;
		}

		if ( step is ModelDefinition.UnknownStep missing )
		{
			body.AddChild( ModelDocInspect.Note(
				$"'{missing.TypeName}' is not a type this build knows. It is kept as it was " +
				"and written back unchanged - load the code that defines it to edit it." ) );
			return;
		}

		ctx.BindObject( sheet, step );
	}

	public override ScenePlacement Placement( ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.IsFolder ) return null;
		if ( StepOf( ctx, item ) is not { Enabled: true } step ) return null;

		var part = item.Kind == ModelDocKind.StepPart ? item.Index : -1;

		if ( step.Placement( part ) is not { IsValid: true } placement ) return null;

		return new ScenePlacement
		{
			Name = $"step.{ctx.Definition.Steps.IndexOf( step )}.{part}.{placement.Name}",
			GetPosition = placement.GetPosition,
			GetRotation = placement.GetRotation ?? ( () => default ),
			CanTurn = placement.CanTurn && placement.GetRotation is not null,
			Set = ( position, rotation ) =>
			{
				placement.Set( position, rotation );
				ctx.Host.NotifyDefinitionChanged();
			},
		};
	}

	public override void DrawOverlay( Model model, ModelDefinition definition,
		ModelDocItem item, Action changed = null, bool resize = false )
	{
		if ( definition?.Steps is not { } steps ) return;

		var index = item.Kind == ModelDocKind.StepPart ? item.ParentIndex : item.Index;

		if ( index < 0 || index >= steps.Count ) return;
		if ( steps[index] is not { Enabled: true } step ) return;

		step.SelectedPart = item.Kind == ModelDocKind.StepPart ? item.Index : -1;

		//A step draws with the same Gizmo everything else does, so it is free to change
		//its state. Put back for whoever draws next rather than trusting every step to.
		var depth = Gizmo.Draw.IgnoreDepth;

		try
		{
			step.DrawGizmos();
		}
		finally
		{
			Gizmo.Draw.IgnoreDepth = depth;
		}
	}

	static string Label( ModelDefinition.BuildStep step )
	{
		if ( !string.IsNullOrWhiteSpace( step.Name ) ) return step.Name;

		if ( step is ModelDefinition.UnknownStep unknown && !string.IsNullOrWhiteSpace( unknown.TypeName ) )
			return unknown.TypeName;

		return TitleOf( step.GetType() );
	}

	static string TitleOf( Type type )
	{
		var described = Game.TypeLibrary?.GetType( type );

		return string.IsNullOrWhiteSpace( described?.Title ) ? type.Name : described.Title;
	}

	static string IconOf( Type type )
	{
		var described = Game.TypeLibrary?.GetType( type );

		return string.IsNullOrWhiteSpace( described?.Icon ) ? "build" : described.Icon;
	}
}
