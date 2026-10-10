using Rinten.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MdlEditor;

//Jiggle bones and their collision spheres as nodes, one row per bone so each can be tuned
sealed class JiggleDoc : ModelDocHandler
{
	public override ModelDocKind Kind => ModelDocKind.JiggleBones;

	public override IEnumerable<ModelDocKind> AlsoHandles
		=> [ModelDocKind.JiggleBone, ModelDocKind.JiggleColliders, ModelDocKind.JiggleCollider];

	public override string Icon => "waves";

	public override bool CanDelete( ModelDocItem item )
		=> item.Kind is ModelDocKind.JiggleBone or ModelDocKind.JiggleCollider;

	public override bool CanToggle( ModelDocItem item )
		=> item.Kind is ModelDocKind.JiggleBone or ModelDocKind.JiggleCollider;

	public override string IconFor( ModelDocItem item )
		=> item.Kind is ModelDocKind.JiggleColliders or ModelDocKind.JiggleCollider ? "radio_button_unchecked" : "waves";

	//-- the tree ---------------------------------------------------------------

	public override void Collect( ModelDocContext ctx, List<ModelDocItem> items )
	{
		var bones = ctx.Definition.JiggleBones ?? new();

		if ( ModelDocContext.AddFolder( items, ModelDocKind.JiggleBones, "Jiggle Bones", bones.Count )
			&& !ctx.IsCollapsed( ModelDocKind.JiggleBones ) )
		{
			ModelDocFolders.Collect( items, ctx, ModelDocKind.JiggleBones, bones, x => x.Folder, ( i, jiggle ) => new ModelDocItem
			{
				Kind = ModelDocKind.JiggleBone,
				Title = jiggle.Title,
				Depth = 1,
				Index = i,
				Enabled = jiggle.Enabled,
			} );
		}

		var colliders = ctx.Definition.JiggleColliders ?? new();

		if ( ModelDocContext.AddFolder( items, ModelDocKind.JiggleColliders, "Jiggle Colliders", colliders.Count )
			&& !ctx.IsCollapsed( ModelDocKind.JiggleColliders ) )
		{
			ModelDocFolders.Collect( items, ctx, ModelDocKind.JiggleColliders, colliders, x => x.Folder, ( i, collider ) => new ModelDocItem
			{
				Kind = ModelDocKind.JiggleCollider,
				Title = collider.Title,
				Depth = 1,
				Index = i,
				Enabled = collider.Enabled,
			} );
		}
	}

	//-- making them ------------------------------------------------------------

	public override IEnumerable<ModelDocAdd> AddActions( ModelDocContext ctx )
	{
		yield return new( "Animation", "movie", "Jiggle Bone", "waves", () => AddBones( ctx, chain: false ) );
		yield return new( "Animation", "movie", "Jiggle Chain", "waves", () => AddBones( ctx, chain: true ) );
		yield return new( "Animation", "movie", "Jiggle Collider", "radio_button_unchecked", () => AddCollider( ctx ) );
	}

	//The bone somebody is pointing at: picked in the hierarchy dock, or the jiggle
	//row being looked at - which is what adding the next one of a pair looks like.
	static string InHand( ModelDocContext ctx )
	{
		if ( ConstraintBones.Picked( ctx ) is { Length: > 0 } picked ) return picked;

		var item = ctx.Host.Outliner?.Selected;
		if ( item is null ) return "";

		if ( item.Kind == ModelDocKind.JiggleBone && JiggleAt( ctx, item.Index ) is { } jiggle ) return jiggle.Bone ?? "";
		if ( item.Kind == ModelDocKind.JiggleCollider && ColliderAt( ctx, item.Index ) is { } collider ) return collider.Bone ?? "";

		return "";
	}

	//A jiggle bone for the bone in hand - or one for it and one for every bone down the run under it, which is what a tail or a strand of hair is.
	static void AddBones( ModelDocContext ctx, bool chain )
	{
		var bone = InHand( ctx );
		var run = chain ? ChainFrom( ctx.Host.CompiledModel, bone ) : new List<string>();

		if ( run.Count == 0 ) run.Add( bone );

		var list = ctx.Definition.JiggleBones;
		var folder = run.Count > 1 ? $"{run[0]} chain" : "";

		foreach ( var one in run )
			list.Add( new ModelDefinition.JiggleBone { Bone = one, Folder = folder } );

		ctx.Host.Outliner.OnStructureChanged( ModelDocKind.JiggleBone, list.Count - 1 );
	}

	static void AddCollider( ModelDocContext ctx )
	{
		var list = ctx.Definition.JiggleColliders;

		list.Add( new ModelDefinition.JiggleCollider
		{
			Bone = InHand( ctx ),
			Radius = MathF.Round( ModelDocPhysics.DefaultRadius( ctx.Host.CompiledModel ) * 0.5f, 3 ),
		} );

		ctx.Host.Outliner.OnStructureChanged( ModelDocKind.JiggleCollider, list.Count - 1 );
	}

	//A bone and every bone below it while there is only one way down - the end bones an export adds to
	//every chain left out, since those are where the last bone points rather than bones that swing.
	static List<string> ChainFrom( Model model, string bone )
	{
		var run = new List<string>();

		var at = model is { IsValid: true } && !string.IsNullOrWhiteSpace( bone ) ? model.Bones?.GetBone( bone ) : null;

		while ( at is not null && run.Count < 64 )
		{
			if ( IsEnd( at.Name ) ) break;

			run.Add( at.Name );

			var below = at.Children.Where( x => !IsEnd( x.Name ) ).ToList();
			if ( below.Count != 1 ) break;

			at = below[0];
		}

		return run;
	}

	static bool IsEnd( string name ) => name?.EndsWith( "_end", StringComparison.OrdinalIgnoreCase ) == true;

	//-- the list -----------------------------------------------------------------

	static ModelDefinition.JiggleBone JiggleAt( ModelDocContext ctx, int index )
		=> ModelDocContext.InRange( ctx.Definition.JiggleBones, index ) ? ctx.Definition.JiggleBones[index] : null;

	static ModelDefinition.JiggleCollider ColliderAt( ModelDocContext ctx, int index )
		=> ModelDocContext.InRange( ctx.Definition.JiggleColliders, index ) ? ctx.Definition.JiggleColliders[index] : null;

	public override bool TrySetEnabled( ModelDocContext ctx, ModelDocItem item, bool enabled )
	{
		if ( item.Kind == ModelDocKind.JiggleCollider && ColliderAt( ctx, item.Index ) is { } collider )
		{
			collider.Enabled = enabled;
			return true;
		}

		if ( item.Kind == ModelDocKind.JiggleBone && JiggleAt( ctx, item.Index ) is { } jiggle )
		{
			jiggle.Enabled = enabled;
			return true;
		}

		return false;
	}

	public override bool TryDelete( ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.Kind == ModelDocKind.JiggleCollider && ModelDocContext.InRange( ctx.Definition.JiggleColliders, item.Index ) )
		{
			ctx.Definition.JiggleColliders.RemoveAt( item.Index );
			return true;
		}

		if ( item.Kind == ModelDocKind.JiggleBone && ModelDocContext.InRange( ctx.Definition.JiggleBones, item.Index ) )
		{
			ctx.Definition.JiggleBones.RemoveAt( item.Index );
			return true;
		}

		return false;
	}

	public override System.Collections.IList ListFor( ModelDocContext ctx, ModelDocKind kind ) => kind switch
	{
		ModelDocKind.JiggleBone => ctx.Definition.JiggleBones,
		ModelDocKind.JiggleCollider => ctx.Definition.JiggleColliders,
		_ => null,
	};

	//-- the inspector --------------------------------------------------------------

	public override void Bind( ModelDocContext ctx, ModelDocItem item, Inspectors.ControlSheet sheet, Panel body )
	{
		if ( item.IsFolder )
		{
			ModelDocInspect.BindFolder( this, ctx, body, item.Kind == ModelDocKind.JiggleColliders
				? "Spheres on the body that jiggle bones swing round rather than through."
				: "Bones that swing on a spring after the pose - worked out every frame, in the world, whatever posed the model. "
					+ "Pick a bone in the hierarchy first; a chain adds it and every bone down the run under it." );
			return;
		}

		if ( item.Kind == ModelDocKind.JiggleCollider )
		{
			if ( ColliderAt( ctx, item.Index ) is not { } collider ) return;

			body.AddChild( ModelDocInspect.Note(
				"Keeps every jiggle bone's tip out of this sphere. The place is on the bone - drag it in the viewport." ) );

			foreach ( var complaint in Missing( body, collider.Bone, "collider" ) )
				body.AddChild( ModelDocInspect.Note( complaint, Theme.Red.WithAlpha( 0.8f ) ) );

			ctx.BindObject( sheet, collider );
			return;
		}

		if ( JiggleAt( ctx, item.Index ) is not { } jiggle ) return;

		body.AddChild( ModelDocInspect.Note(
			"Its tip is a weight on a spring. Stiffness is how many times a second it swings, Damping how quickly a swing "
			+ "dies away, Gravity how much it sags, World Motion how much the body moving drags it, and Max Angle how far "
			+ "from where the pose points it it may go." ) );

		foreach ( var complaint in Complaints( ctx, body, jiggle ) )
			body.AddChild( ModelDocInspect.Note( complaint, Theme.Red.WithAlpha( 0.8f ) ) );

		ctx.BindObject( sheet, jiggle );
	}

	static IEnumerable<string> Missing( Panel body, string bone, string what )
	{
		if ( string.IsNullOrWhiteSpace( bone ) )
		{
			yield return $"No bone - this {what} is skipped.";
			yield break;
		}

		var known = ConstraintBones.Known( body );

		if ( known.Count > 0 && !known.Contains( bone ) )
			yield return $"{bone} is not in this skeleton - this {what} is skipped.";
	}

	//What is wrong with a jiggle bone, in the order it stops working.
	static IEnumerable<string> Complaints( ModelDocContext ctx, Panel body, ModelDefinition.JiggleBone jiggle )
	{
		foreach ( var missing in Missing( body, jiggle.Bone, "jiggle bone" ) )
			yield return missing;

		if ( jiggle.Axis == ModelDefinition.JiggleAxis.Auto && jiggle.Length <= 0.0f
			&& ctx.Host.CompiledModel is { IsValid: true } model
			&& model.Bones?.GetBone( jiggle.Bone ) is { HasChildren: false } )
		{
			yield return $"{jiggle.Bone} has no child to point at - it swings along its X axis, a tenth of a metre long. "
				+ "Name an axis or a length.";
		}

		if ( jiggle.Weight <= 0.0f )
			yield return "Weight is nought - the pose is left as it is.";
	}

	//-- the viewport ------------------------------------------------------------------

	/// <summary>A collider's place on its bone, dragged where everything else is dragged.</summary>
	public override ScenePlacement Placement( ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.Kind != ModelDocKind.JiggleCollider ) return null;
		if ( ColliderAt( ctx, item.Index ) is not { } collider ) return null;
		if ( string.IsNullOrWhiteSpace( collider.Bone ) ) return null;

		var model = ctx.Host.CompiledModel;

		return new ScenePlacement
		{
			Name = $"jiggle.collider.{item.Index}",
			GetPosition = () => ModelDocPhysics.BoneOf( model, collider.Bone ).PointToWorld( collider.Position ),
			GetRotation = () => ModelDocPhysics.BoneOf( model, collider.Bone ).Rotation.Angles(),
			Set = ( position, rotation ) =>
			{
				collider.Position = ModelDocPhysics.BoneOf( model, collider.Bone ).PointToLocal( position );
				ctx.Host.NotifyDefinitionChanged();
			},
		};
	}

	public override void DrawOverlay( Model model, ModelDefinition definition, ModelDocItem item,
		Action changed = null, bool resize = false )
	{
		if ( definition is null ) return;

		var picked = item.Kind == ModelDocKind.JiggleBone ? item.Index : -1;
		var pickedCollider = item.Kind == ModelDocKind.JiggleCollider ? item.Index : -1;

		var bones = definition.JiggleBones ?? new();

		for ( var i = 0; i < bones.Count; i++ )
		{
			if ( bones[i] is not { } jiggle ) continue;

			//A switched off one is still drawn while it is the one being looked at - it is switched off to be looked at.
			if ( !jiggle.Enabled && i != picked ) continue;

			DrawJiggle( model, jiggle, i == picked, picked >= 0 );
		}

		var colliders = definition.JiggleColliders ?? new();

		for ( var i = 0; i < colliders.Count; i++ )
		{
			if ( colliders[i] is not { } collider ) continue;
			if ( !collider.Enabled && i != pickedCollider ) continue;

			DrawCollider( model, collider, i == pickedCollider );
		}
	}

	//Where a jiggle bone points, and - for the one picked - the rim of how far it may swing.
	static void DrawJiggle( Model model, ModelDefinition.JiggleBone jiggle, bool picked, bool another )
	{
		if ( string.IsNullOrWhiteSpace( jiggle.Bone ) ) return;

		var bone = ModelDocPhysics.BoneOf( model, jiggle.Bone );
		var tip = TipOf( model, jiggle, bone );

		Gizmo.Draw.LineThickness = picked ? 2.5f : 1.5f;
		Gizmo.Draw.Color = Theme.Primary.WithAlpha( picked ? 0.95f : another ? 0.25f : 0.6f );
		Gizmo.Draw.Line( bone.Position, tip );
		Gizmo.Draw.LineSphere( new Sphere( tip, MathF.Max( jiggle.Radius, 0.005f ) ), 8 );

		if ( !picked ) return;

		var along = tip - bone.Position;
		var reach = along.Length;
		if ( reach < 1e-5f ) return;

		var direction = along / reach;
		var angle = Math.Clamp( jiggle.MaxAngle, 0.0f, 179.0f ) * MathF.PI / 180.0f;

		var side = Vector3.Cross( direction, MathF.Abs( direction.y ) < 0.9f ? new Vector3( 0, 1, 0 ) : new Vector3( 1, 0, 0 ) ).Normal;
		var up = Vector3.Cross( direction, side ).Normal;

		var centre = bone.Position + direction * ( reach * MathF.Cos( angle ) );
		var radius = reach * MathF.Sin( angle );

		Gizmo.Draw.LineThickness = 1.5f;
		Gizmo.Draw.Color = Theme.Yellow.WithAlpha( 0.8f );

		var last = Vector3.Zero;

		for ( var s = 0; s <= 24; s++ )
		{
			var turn = s / 24.0f * MathF.Tau;
			var point = centre + ( side * MathF.Cos( turn ) + up * MathF.Sin( turn ) ) * radius;

			if ( s > 0 ) Gizmo.Draw.Line( last, point );
			if ( s % 6 == 0 ) Gizmo.Draw.Line( bone.Position, point );

			last = point;
		}
	}

	static void DrawCollider( Model model, ModelDefinition.JiggleCollider collider, bool picked )
	{
		if ( string.IsNullOrWhiteSpace( collider.Bone ) ) return;

		var at = ModelDocPhysics.BoneOf( model, collider.Bone ).PointToWorld( collider.Position );

		Gizmo.Draw.LineThickness = picked ? 2.5f : 1.5f;
		Gizmo.Draw.Color = Theme.Green.WithAlpha( picked ? 0.9f : 0.35f );
		Gizmo.Draw.LineSphere( new Sphere( at, MathF.Max( collider.Radius, 0.002f ) ), 10 );
	}

	//Where a jiggle bone's tip rests - the same answer the runtime works out: its child, or along the axis it names, as long as it says.
	static Vector3 TipOf( Model model, ModelDefinition.JiggleBone jiggle, Transform bone )
	{
		var child = ChildOf( model, jiggle.Bone );
		var toChild = child is null ? Vector3.Zero : ModelDocPhysics.BoneOf( model, child ).Position - bone.Position;
		var measured = toChild.Length > 1e-5f;

		var axis = jiggle.Axis switch
		{
			ModelDefinition.JiggleAxis.X => new Vector3( 1, 0, 0 ),
			ModelDefinition.JiggleAxis.Y => new Vector3( 0, 1, 0 ),
			ModelDefinition.JiggleAxis.Z => new Vector3( 0, 0, 1 ),
			ModelDefinition.JiggleAxis.NegativeX => new Vector3( -1, 0, 0 ),
			ModelDefinition.JiggleAxis.NegativeY => new Vector3( 0, -1, 0 ),
			ModelDefinition.JiggleAxis.NegativeZ => new Vector3( 0, 0, -1 ),
			_ => Vector3.Zero,
		};

		var direction = axis == Vector3.Zero
			? ( measured ? toChild : bone.Rotation * new Vector3( 0.1f, 0, 0 ) )
			: bone.Rotation * axis * ( measured ? toChild.Length : 0.1f );

		if ( jiggle.Length > 0.0f )
			direction = direction.Normal * jiggle.Length;

		return bone.Position + direction;
	}

	//The child a jiggle bone points at: the one named after it, or the first.
	static string ChildOf( Model model, string bone )
	{
		if ( model is not { IsValid: true } || string.IsNullOrWhiteSpace( bone ) ) return null;

		var children = model.Bones?.GetBone( bone )?.Children;
		if ( children is not { Count: > 0 } ) return null;

		return ( children.FirstOrDefault( x => x.Name.StartsWith( bone, StringComparison.OrdinalIgnoreCase ) ) ?? children[0] ).Name;
	}
}
