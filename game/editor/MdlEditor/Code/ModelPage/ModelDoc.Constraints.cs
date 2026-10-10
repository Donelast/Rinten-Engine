using Rinten.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MdlEditor;

//Procedural constraints as nodes: one per constraint, with its driver bones under it
sealed class ConstraintDoc : ModelDocHandler
{
	public override ModelDocKind Kind => ModelDocKind.Constraints;

	public override IEnumerable<ModelDocKind> AlsoHandles
		=> [ModelDocKind.Constraint, ModelDocKind.ConstraintDriver];

	public override string Icon => "polyline";

	public override bool CanDelete( ModelDocItem item )
		=> item.Kind is ModelDocKind.Constraint or ModelDocKind.ConstraintDriver;

	public override bool CanToggle( ModelDocItem item ) => item.Kind == ModelDocKind.Constraint;

	public override string IconFor( ModelDocItem item )
	{
		if ( !string.IsNullOrWhiteSpace( item.Icon ) ) return item.Icon;

		return item.Kind switch
		{
			ModelDocKind.ConstraintDriver => "arrow_forward",
			ModelDocKind.Constraint => "polyline",
			_ => Icon,
		};
	}

	//The icon a kind of constraint is drawn with, in the tree and in the menu.
	static string IconOf( ModelDefinition.ConstraintKind kind ) => kind switch
	{
		ModelDefinition.ConstraintKind.TiltTwist => "rotate_right",
		ModelDefinition.ConstraintKind.Parent => "open_with",
		ModelDefinition.ConstraintKind.Orient => "3d_rotation",
		ModelDefinition.ConstraintKind.Aim => "my_location",
		_ => "polyline",
	};

	//-- the tree ---------------------------------------------------------------

	public override void Collect( ModelDocContext ctx, List<ModelDocItem> items )
	{
		var constraints = ctx.Definition.Constraints;
		if ( !ModelDocContext.AddFolder( items, Kind, "Constraints", constraints.Count ) ) return;
		if ( ctx.IsCollapsed( Kind ) ) return;

		ModelDocFolders.Collect( items, ctx, Kind, constraints, x => x.Folder, ( i, constraint ) => new ModelDocItem
		{
			Kind = ModelDocKind.Constraint,
			Title = constraint.Title,
			Depth = 1,
			Index = i,
			Enabled = constraint.Enabled,
			Icon = IconOf( constraint.Kind ),
		}, Listens );
	}

	//What a constraint listens to, under the constraint that listens.
	static void Listens( int index, ModelDefinition.Constraint constraint, List<ModelDocItem> items, int depth )
	{
		if ( constraint?.Drivers is not { Count: > 0 } ) return;

		for ( var at = 0; at < constraint.Drivers.Count; at++ )
		{
			items.Add( new ModelDocItem
			{
				Kind = ModelDocKind.ConstraintDriver,
				Title = constraint.Drivers[at].Title,
				Depth = depth,
				Index = at,
				ParentIndex = index,
			} );
		}
	}

	//-- making them ------------------------------------------------------------

	public override IEnumerable<ModelDocAdd> AddActions( ModelDocContext ctx )
	{
		foreach ( var kind in new[]
		{
			ModelDefinition.ConstraintKind.TiltTwist,
			ModelDefinition.ConstraintKind.Parent,
			ModelDefinition.ConstraintKind.Orient,
			ModelDefinition.ConstraintKind.Aim,
		} )
		{
			yield return new( "Skeleton", "accessibility_new",
				$"{ModelDefinition.Constraint.Named( kind )} Constraint", IconOf( kind ),
				() => Make( ctx, kind ) );
		}
	}

	//New constraint; starts driven by the picked bone's parent so it does something right away
	static void Make( ModelDocContext ctx, ModelDefinition.ConstraintKind kind )
	{
		var bone = ConstraintBones.InHand( ctx );

		var constraint = new ModelDefinition.Constraint
		{
			Kind = kind,
			Bone = bone,
		};

		if ( ConstraintBones.ParentOf( ctx, bone ) is { Length: > 0 } above )
			constraint.Drivers.Add( new ModelDefinition.Constraint.Driver { Bone = above } );

		ctx.Definition.Constraints.Add( constraint );
		ctx.Host.Outliner.OnStructureChanged( ModelDocKind.Constraint, ctx.Definition.Constraints.Count - 1 );
	}

	public override void AddContextOptions( Menu menu, ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.Kind == ModelDocKind.ConstraintDriver )
		{
			if ( At( ctx, item.ParentIndex ) is not { } owner ) return;

			menu.AddOption( "Add Driver", "add", () => AddDriver( ctx, item.ParentIndex, "" ) );

			if ( owner.Drivers.Count > 1 )
				menu.AddOption( "Balance Shares", "balance", () => Balance( ctx, item.ParentIndex ) );

			return;
		}

		if ( item.Kind != ModelDocKind.Constraint ) return;
		if ( At( ctx, item.Index ) is not { } constraint ) return;

		menu.AddOption( "Add Driver", "add", () => AddDriver( ctx, item.Index, "" ) );

		if ( ConstraintBones.ParentOf( ctx, constraint.Bone ) is { Length: > 0 } above
			&& !constraint.Drivers.Any( d => Same( d.Bone, above ) ) )
			menu.AddOption( $"Add Driver · {above}", "device_hub", () => AddDriver( ctx, item.Index, above ) );

		var picked = ConstraintBones.Picked( ctx );
		if ( picked.Length > 0 && !Same( picked, constraint.Bone )
			&& !constraint.Drivers.Any( d => Same( d.Bone, picked ) ) )
			menu.AddOption( $"Add Driver · {picked}", "device_hub", () => AddDriver( ctx, item.Index, picked ) );

		if ( constraint.Drivers.Count > 1 )
			menu.AddOption( "Balance Shares", "balance", () => Balance( ctx, item.Index ) );
	}

	static bool Same( string a, string b ) => string.Equals( a, b, StringComparison.OrdinalIgnoreCase );

	//The constraint a row stands for, or nothing where the list moved under it.
	static ModelDefinition.Constraint At( ModelDocContext ctx, int index )
		=> ModelDocContext.InRange( ctx.Definition.Constraints, index )
			? ctx.Definition.Constraints[index]
			: null;

	//The driver a row stands for.
	static ModelDefinition.Constraint.Driver DriverAt( ModelDocContext ctx, ModelDocItem item )
	{
		if ( At( ctx, item.ParentIndex ) is not { } constraint ) return null;

		return ModelDocContext.InRange( constraint.Drivers, item.Index )
			? constraint.Drivers[item.Index]
			: null;
	}

	static void AddDriver( ModelDocContext ctx, int index, string bone )
	{
		if ( At( ctx, index ) is not { } constraint ) return;

		constraint.Drivers.Add( new ModelDefinition.Constraint.Driver { Bone = bone ?? "" } );
		ctx.Host.Outliner.OnStructureChanged( ModelDocKind.ConstraintDriver, constraint.Drivers.Count - 1, index );
	}

	//Every driver worth the same, which is what most of them are.
	static void Balance( ModelDocContext ctx, int index )
	{
		if ( At( ctx, index ) is not { Drivers.Count: > 0 } constraint ) return;

		ctx.Host.BeginDefinitionEdit( "Balance Shares" );

		var each = 1.0f / constraint.Drivers.Count;
		foreach ( var driver in constraint.Drivers )
			driver.Weight = each;

		ctx.Host.NotifyDefinitionChanged( rebuildInspector: true );
		ctx.Host.EndDefinitionEdit( "Balance Shares" );
	}

	//-- the list -----------------------------------------------------------------

	public override bool TrySetEnabled( ModelDocContext ctx, ModelDocItem item, bool enabled )
	{
		if ( At( ctx, item.Index ) is not { } constraint ) return false;

		constraint.Enabled = enabled;
		return true;
	}

	public override bool TryDelete( ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.Kind == ModelDocKind.ConstraintDriver )
		{
			if ( At( ctx, item.ParentIndex ) is not { } owner ) return false;
			if ( !ModelDocContext.InRange( owner.Drivers, item.Index ) ) return false;

			owner.Drivers.RemoveAt( item.Index );
			return true;
		}

		if ( !ModelDocContext.InRange( ctx.Definition.Constraints, item.Index ) ) return false;

		ctx.Definition.Constraints.RemoveAt( item.Index );
		return true;
	}

	public override System.Collections.IList ListFor( ModelDocContext ctx, ModelDocKind kind )
		=> kind == ModelDocKind.Constraint ? ctx.Definition.Constraints : null;

	//-- the inspector --------------------------------------------------------------

	public override void Bind( ModelDocContext ctx, ModelDocItem item, Inspectors.ControlSheet sheet, Panel body )
	{
		if ( item.IsFolder )
		{
			ModelDocInspect.BindFolder( this, ctx, body,
				"Bones another bone drives, worked out on the finished pose rather than animated." );
			return;
		}

		if ( item.Kind == ModelDocKind.ConstraintDriver )
		{
			BindDriver( ctx, item, sheet, body );
			return;
		}

		if ( At( ctx, item.Index ) is not { } constraint ) return;

		body.AddChild( ModelDocInspect.Note( Explains( constraint.Kind ) ) );

		foreach ( var complaint in Complaints( body, constraint ) )
			body.AddChild( ModelDocInspect.Note( complaint, Theme.Red.WithAlpha( 0.8f ) ) );

		ctx.BindObject( sheet, constraint );

		var index = item.Index;

		var add = body.AddChild<TextButtonPanel>();
		add.Text = "Add Driver";
		add.Icon = "add";
		add.Pressed = () => AddDriver( ctx, index, "" );

		if ( ConstraintBones.ParentOf( ctx, constraint.Bone ) is { Length: > 0 } above
			&& !constraint.Drivers.Any( d => Same( d.Bone, above ) ) )
		{
			var fromParent = body.AddChild<TextButtonPanel>();
			fromParent.Text = $"Add Driver · {above}";
			fromParent.Icon = "device_hub";
			fromParent.Pressed = () => AddDriver( ctx, index, above );
		}

		if ( constraint.Drivers.Count > 1 )
		{
			var balance = body.AddChild<TextButtonPanel>();
			balance.Text = "Balance Shares";
			balance.Icon = "balance";
			balance.Pressed = () => Balance( ctx, index );
		}
	}

	static void BindDriver( ModelDocContext ctx, ModelDocItem item, Inspectors.ControlSheet sheet, Panel body )
	{
		if ( At( ctx, item.ParentIndex ) is not { } owner ) return;
		if ( DriverAt( ctx, item ) is not { } driver ) return;

		body.AddChild( ModelDocInspect.Note( Reads( owner.Kind ) ) );

		if ( !string.IsNullOrWhiteSpace( driver.Bone )
			&& ConstraintBones.Known( body ) is { Count: > 0 } known
			&& !known.Contains( driver.Bone ) )
		{
			body.AddChild( ModelDocInspect.Note(
				$"{driver.Bone} is not in this skeleton - this driver is dropped when the model is read.",
				Theme.Red.WithAlpha( 0.8f ) ) );
		}

		var whole = owner.Drivers.Sum( d => d.Weight );
		if ( owner.Drivers.Count > 1 && whole > 0.0f )
		{
			body.AddChild( ModelDocInspect.Note(
				$"Worth {driver.Weight / whole:P0} of the answer, of {owner.Drivers.Count} drivers." ) );
		}

		ctx.BindObject( sheet, driver );
	}

	//What this kind of constraint does, in the words the runtime does it in.
	static string Explains( ModelDefinition.ConstraintKind kind ) => kind switch
	{
		ModelDefinition.ConstraintKind.TiltTwist =>
			"Takes a share of how far the driver has turned about itself. What a twist bone is for - "
			+ "the amount is the share of that turn, half of it being the usual answer.",
		ModelDefinition.ConstraintKind.Parent =>
			"Sits where its drivers sit, averaged by share. Position only - the turning is left alone. "
			+ "An amount past one pushes it further than the drivers went, which is what holds an elbow helper out of the skin.",
		ModelDefinition.ConstraintKind.Orient =>
			"Turns the way its drivers turn, by amount. One is the driver's whole turn; a negative amount turns it back.",
		ModelDefinition.ConstraintKind.Aim =>
			"Points the bone's chosen axis at where its drivers sit, averaged by share. The amount is how far of the way there, 0 to 1.",
		_ => "",
	};

	//Which of a driver's fields the constraint above it actually reads.
	static string Reads( ModelDefinition.ConstraintKind kind ) => kind switch
	{
		ModelDefinition.ConstraintKind.TiltTwist =>
			"A twist reads how far this bone turned, and its share. The place below is not read.",
		ModelDefinition.ConstraintKind.Orient =>
			"An orient reads how this bone turned, and its share. The place below is not read.",
		_ => "The place below is where on this bone the constraint reads - drag it in the viewport.",
	};

	//What is wrong with a constraint, in the order it stops working.
	static IEnumerable<string> Complaints( Panel body, ModelDefinition.Constraint constraint )
	{
		if ( string.IsNullOrWhiteSpace( constraint.Bone ) )
			yield return "No driven bone - this constraint is skipped.";

		if ( constraint.Drivers.Count == 0 )
			yield return "Nothing drives this - a constraint with no drivers is skipped.";
		else if ( constraint.Drivers.All( d => d.Weight == 0.0f ) )
			yield return "Every share is zero - this constraint is skipped.";

		var unnamed = constraint.Drivers.Count( d =>
			string.IsNullOrWhiteSpace( d.Bone ) && string.IsNullOrWhiteSpace( d.Attachment ) );

		if ( unnamed > 0 )
			yield return $"{unnamed} driver(s) name neither a bone nor an attachment - those are dropped.";

		var known = ConstraintBones.Known( body );
		if ( known.Count == 0 ) yield break;

		if ( !string.IsNullOrWhiteSpace( constraint.Bone ) && !known.Contains( constraint.Bone ) )
			yield return $"{constraint.Bone} is not in this skeleton - this constraint is skipped.";

		var missing = constraint.Drivers
			.Where( d => !string.IsNullOrWhiteSpace( d.Bone ) && !known.Contains( d.Bone ) )
			.Select( d => d.Bone )
			.ToList();

		if ( missing.Count > 0 )
			yield return $"Not in this skeleton: {string.Join( ", ", missing )}";
	}

	//-- the viewport ------------------------------------------------------------------

	//The driven bone itself gets no handle: where it ends up is what the constraint works out, so a handle on it would be one that snaps back the moment the pose is stepped.
	/// <summary>A driver's place on its bone, dragged where everything else is dragged.</summary>
	public override ScenePlacement Placement( ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.Kind != ModelDocKind.ConstraintDriver ) return null;
		if ( DriverAt( ctx, item ) is not { } driver ) return null;
		if ( string.IsNullOrWhiteSpace( driver.Bone ) ) return null;

		var model = ctx.Host.CompiledModel;

		return new ScenePlacement
		{
			Name = $"constraint.{item.ParentIndex}.driver.{item.Index}",
			GetPosition = () => ModelDocPhysics.BoneOf( model, driver.Bone ).PointToWorld( driver.Position ),
			GetRotation = () => ( ModelDocPhysics.BoneOf( model, driver.Bone ).Rotation
				* driver.Rotation.ToRotation() ).Angles(),
			Set = ( position, rotation ) =>
			{
				var bone = ModelDocPhysics.BoneOf( model, driver.Bone );
				driver.Position = bone.PointToLocal( position );
				driver.Rotation = ( bone.Rotation.Inverse * rotation.ToRotation() ).Angles();
				ctx.Host.NotifyDefinitionChanged();
			},
		};
	}

	public override void DrawOverlay( Model model, ModelDefinition definition, ModelDocItem item,
		Action changed = null, bool resize = false )
	{
		if ( definition?.Constraints is not { Count: > 0 } constraints ) return;

		var selected = item.Kind switch
		{
			ModelDocKind.Constraint => item.Index,
			ModelDocKind.ConstraintDriver => item.ParentIndex,
			_ => -1,
		};

		var driver = item.Kind == ModelDocKind.ConstraintDriver ? item.Index : -1;

		for ( var i = 0; i < constraints.Count; i++ )
		{
			//The whole list while the folder is picked, so a rig can be looked over; only the
			//one being edited otherwise, because thirty six of these drawn at once is a haystack.
			if ( selected >= 0 && i != selected ) continue;
			if ( constraints[i] is not { } constraint ) continue;

			//A switched off one is still drawn while it is the one being looked at - it is switched off to be looked at.
			if ( !constraint.Enabled && i != selected ) continue;

			Draw( model, constraint, i == selected, driver );
		}
	}

	static void Draw( Model model, ModelDefinition.Constraint constraint, bool picked, int driver )
	{
		if ( string.IsNullOrWhiteSpace( constraint.Bone ) ) return;

		var bone = ModelDocPhysics.BoneOf( model, constraint.Bone );
		var size = ModelDocPhysics.DefaultRadius( model );

		Gizmo.Draw.LineThickness = picked ? 2.5f : 1.5f;
		Gizmo.Draw.Color = Theme.Primary.WithAlpha( picked ? 0.95f : 0.35f );
		Gizmo.Draw.LineSphere( new Sphere( bone.Position, size * 0.25f ), 6 );

		//Which axis the bone twists about or points along, for the two kinds that use one - the number in the field means nothing on screen otherwise.
		if ( constraint.Kind is ModelDefinition.ConstraintKind.TiltTwist or ModelDefinition.ConstraintKind.Aim )
		{
			Gizmo.Draw.Color = Theme.Green.WithAlpha( picked ? 0.9f : 0.3f );
			Gizmo.Draw.Line( bone.Position, bone.Position + bone.Rotation * Along( constraint.BoneAxis ) * size * 1.5f );
		}

		for ( var i = 0; i < constraint.Drivers.Count; i++ )
		{
			var one = constraint.Drivers[i];
			if ( one is null || string.IsNullOrWhiteSpace( one.Bone ) ) continue;

			var at = ModelDocPhysics.BoneOf( model, one.Bone ).PointToWorld( one.Position );
			var strong = picked && ( driver < 0 || driver == i );

			Gizmo.Draw.LineThickness = strong ? 2.5f : 1.5f;
			Gizmo.Draw.Color = Theme.Yellow.WithAlpha( strong ? 0.9f : picked ? 0.4f : 0.25f );
			Gizmo.Draw.LineSphere( new Sphere( at, size * 0.15f ), 6 );
			Gizmo.Draw.Line( at, bone.Position );
		}
	}

	//Which way an axis points, in the bone's own space.
	static Vector3 Along( ModelDefinition.ConstraintAxis axis ) => axis switch
	{
		ModelDefinition.ConstraintAxis.Y => Vector3.Up,

		//+Z, which is Backward. Forward is -Z, so the line was drawn down the axis the field does not name.
		ModelDefinition.ConstraintAxis.Z => Vector3.Backward,
		_ => Vector3.Right,
	};
}

//The bones a constraint is filled in from, and the ones it can name.
static class ConstraintBones
{
	/// <summary>The bone picked in the hierarchy dock, where there is one.</summary>
	public static string Picked( ModelDocContext ctx )
		=> ( ctx.Host as ModelPage )?.PickedBone ?? "";

	//The hierarchy dock first, since picking a bone there is somebody saying which bone they mean; the constraint or driver being looked at after that, which is what adding a second constraint to the same bone looks like.
	/// <summary>Whatever bone the editor is already pointing at.</summary>
	public static string InHand( ModelDocContext ctx )
	{
		if ( Picked( ctx ) is { Length: > 0 } picked ) return picked;

		var item = ctx.Host.Outliner?.Selected;
		if ( item is null ) return "";

		var constraints = ctx.Definition.Constraints;

		if ( item.Kind == ModelDocKind.Constraint && ModelDocContext.InRange( constraints, item.Index ) )
			return constraints[item.Index].Bone ?? "";

		if ( item.Kind == ModelDocKind.ConstraintDriver && ModelDocContext.InRange( constraints, item.ParentIndex ) )
			return constraints[item.ParentIndex].Bone ?? "";

		return "";
	}

	/// <summary>The bone one hangs off in the compiled skeleton, for a first driver.</summary>
	public static string ParentOf( ModelDocContext ctx, string bone )
	{
		if ( string.IsNullOrWhiteSpace( bone ) ) return "";
		if ( ctx.Host.CompiledModel is not { IsValid: true } model ) return "";

		return model.Bones?.GetBone( bone )?.Parent?.Name ?? "";
	}

	/// <summary>Every bone name this model has, for telling somebody a name is not one.</summary>
	public static HashSet<string> Known( Panel from )
		=> new( ModelDocBones.Names(), StringComparer.OrdinalIgnoreCase );
}
