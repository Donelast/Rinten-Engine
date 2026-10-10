using Rinten.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MdlEditor;

//Model attachments; the viewport draws the compiled model's, reloaded on every edit
sealed class AttachmentDoc : ModelDocHandler
{
	public override ModelDocKind Kind => ModelDocKind.Attachments;

	public override IEnumerable<ModelDocKind> AlsoHandles
		=> [ModelDocKind.Attachment, ModelDocKind.AttachmentInfluence];

	public override string Icon => "push_pin";

	public override string IconFor( ModelDocItem item ) => item.Kind switch
	{
		ModelDocKind.AttachmentInfluence => "device_hub",
		_ => "push_pin",
	};

	public override bool CanDelete( ModelDocItem item )
		=> !item.IsFolder && item.Kind is ModelDocKind.Attachment or ModelDocKind.AttachmentInfluence;

	public override bool CanToggle( ModelDocItem item )
		=> !item.IsFolder && item.Kind == ModelDocKind.Attachment;

	//-- the tree --------------------------------------------------------------

	public override void Collect( ModelDocContext ctx, List<ModelDocItem> items )
	{
		ctx.Definition.Attachments ??= new List<ModelDefinition.Attachment>();

		var attachments = ctx.Definition.Attachments;
		if ( !ModelDocContext.AddFolder( items, Kind, "Attachments", attachments.Count ) ) return;
		if ( ctx.IsCollapsed( Kind ) ) return;

		ModelDocFolders.Collect( items, ctx, Kind, attachments, x => x.Folder, ( i, attachment ) => new ModelDocItem
		{
			Kind = ModelDocKind.Attachment,
			Title = Titled( attachment ),
			Depth = 1,
			Index = i,
			Enabled = attachment.Enabled,
		}, Pulled );
	}

	static string Titled( ModelDefinition.Attachment attachment )
	{
		var name = string.IsNullOrWhiteSpace( attachment.Name ) ? "Unnamed" : attachment.Name;

		if ( attachment.Influences is { Count: > 1 } )
			return $"{name}  ·  {attachment.Influences.Count} bones";

		return string.IsNullOrWhiteSpace( attachment.Parent ) ? name : $"{name}  ·  {attachment.Parent}";
	}

	//The bones one is pulled between, under the attachment they pull.
	static void Pulled( int index, ModelDefinition.Attachment attachment, List<ModelDocItem> items, int depth )
	{
		if ( attachment?.Influences is not { Count: > 0 } ) return;

		for ( var at = 0; at < attachment.Influences.Count; at++ )
		{
			var one = attachment.Influences[at];
			var bone = string.IsNullOrWhiteSpace( one?.Bone ) ? "No bone" : one.Bone;

			items.Add( new ModelDocItem
			{
				Kind = ModelDocKind.AttachmentInfluence,
				Title = $"{bone}  ·  {one?.Weight ?? 0.0f:0.##}",
				Depth = depth,
				Index = at,
				ParentIndex = index,
				Icon = "device_hub",
			} );
		}
	}

	//-- adding and removing ---------------------------------------------------

	public override IEnumerable<ModelDocAdd> AddActions( ModelDocContext ctx )
	{
		yield return new( "Skeleton", "device_hub", "Attachment", "push_pin", () =>
		{
			var bone = SelectedBone( ctx );

			ctx.Definition.Attachments ??= new List<ModelDefinition.Attachment>();
			ctx.Definition.Attachments.Add( new ModelDefinition.Attachment
			{
				Name = Unique( ctx.Definition, string.IsNullOrWhiteSpace( bone ) ? "attachment" : bone ),
				Parent = bone,
			} );

			ctx.Host.Outliner.OnStructureChanged( ModelDocKind.Attachment, ctx.Definition.Attachments.Count - 1 );
		} );
	}

	//The bone whatever is picked in the tree sits on, so a new one lands there.
	static string SelectedBone( ModelDocContext ctx )
	{
		var item = ctx.Host.Outliner?.Selected;
		if ( item is null ) return "";

		if ( item.Kind == ModelDocKind.Attachment && ModelDocContext.InRange( ctx.Definition.Attachments, item.Index ) )
			return ctx.Definition.Attachments[item.Index].Parent ?? "";

		if ( item.Kind == ModelDocKind.AttachmentInfluence && Owner( ctx, item ) is { } owner
			&& ModelDocContext.InRange( owner.Influences, item.Index ) )
			return owner.Influences[item.Index].Bone ?? "";

		return ModelDocPhysics.SelectedBone( ctx );
	}

	//A name nothing else has, since code asks for one of these by name.
	static string Unique( ModelDefinition definition, string wanted )
	{
		var taken = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var attachment in definition.Attachments ?? [] )
		{
			if ( attachment?.Name is { Length: > 0 } name ) taken.Add( name );
		}

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
		if ( kind != ModelDocKind.Attachment ) return;
		if ( !ModelDocContext.InRange( ctx.Definition.Attachments, index ) ) return;

		var attachment = ctx.Definition.Attachments[index];
		var taken = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		for ( var i = 0; i < ctx.Definition.Attachments.Count; i++ )
		{
			if ( i == index ) continue;
			if ( ctx.Definition.Attachments[i]?.Name is { Length: > 0 } name ) taken.Add( name );
		}

		if ( !taken.Contains( attachment.Name ?? "" ) ) return;

		attachment.Name = Unique( ctx.Definition, attachment.Name );
	}

	public override void AddContextOptions( Menu menu, ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.IsFolder ) return;

		var attachment = item.Kind == ModelDocKind.Attachment
			&& ModelDocContext.InRange( ctx.Definition.Attachments, item.Index )
				? ctx.Definition.Attachments[item.Index]
				: Owner( ctx, item );

		if ( attachment is null ) return;

		if ( menu.HasOptions ) menu.AddSeparator();

		menu.AddOption( "Add Influence", "device_hub", () => Edit( ctx, "Add Influence", () => AddInfluence( attachment ) ) );

		if ( attachment.Influences is { Count: > 0 } )
			menu.AddOption( "Clear Influences", "device_hub", () => Edit( ctx, "Clear Influences", attachment.Influences.Clear ) );

		if ( item.Kind != ModelDocKind.Attachment ) return;

		menu.AddOption( "Snap to Bone", "center_focus_strong", () => Edit( ctx, "Snap to Bone", () =>
		{
			attachment.Position = Vector3.Zero;
			attachment.Rotation = default;
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

	//Adds an influence bone; the first call also seeds the attachment's own bone so it doesn't jump
	static void AddInfluence( ModelDefinition.Attachment attachment )
	{
		attachment.Influences ??= new List<ModelDefinition.Attachment.Influence>();

		if ( attachment.Influences.Count == 0 )
		{
			attachment.Influences.Add( new ModelDefinition.Attachment.Influence
			{
				Bone = attachment.Parent ?? "",
				Position = attachment.Position,
				Rotation = attachment.Rotation,
			} );
		}

		var last = attachment.Influences[^1];

		attachment.Influences.Add( new ModelDefinition.Attachment.Influence
		{
			Bone = last.Bone,
			Position = last.Position,
			Rotation = last.Rotation,
		} );
	}

	public override bool TrySetEnabled( ModelDocContext ctx, ModelDocItem item, bool enabled )
	{
		if ( !ModelDocContext.InRange( ctx.Definition.Attachments, item.Index ) ) return false;

		ctx.Definition.Attachments[item.Index].Enabled = enabled;
		return true;
	}

	public override bool TryDelete( ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.Kind == ModelDocKind.AttachmentInfluence )
		{
			if ( Owner( ctx, item ) is not { } attachment ) return false;
			if ( !ModelDocContext.InRange( attachment.Influences, item.Index ) ) return false;

			attachment.Influences.RemoveAt( item.Index );

			//One left is one that is ignored, and an attachment reading as pulled while it is not is worse than one that never claimed to be.
			if ( attachment.Influences.Count == 1 )
				attachment.Influences.Clear();

			return true;
		}

		if ( !ModelDocContext.InRange( ctx.Definition.Attachments, item.Index ) ) return false;

		ctx.Definition.Attachments.RemoveAt( item.Index );
		return true;
	}

	public override System.Collections.IList ListFor( ModelDocContext ctx, ModelDocKind kind )
		=> kind == ModelDocKind.Attachment ? ctx.Definition.Attachments : null;

	//The attachment an influence row hangs off.
	static ModelDefinition.Attachment Owner( ModelDocContext ctx, ModelDocItem item )
		=> item.Kind == ModelDocKind.AttachmentInfluence
			&& ModelDocContext.InRange( ctx.Definition.Attachments, item.ParentIndex )
				? ctx.Definition.Attachments[item.ParentIndex]
				: null;

	//-- the inspector ---------------------------------------------------------

	public override void Bind( ModelDocContext ctx, ModelDocItem item, Inspectors.ControlSheet sheet, Panel body )
	{
		if ( item.IsFolder )
		{
			ModelDocInspect.BindFolder( this, ctx, body,
				"Named places on the model, asked for by name - a muzzle, an eye, a hand." );
			return;
		}

		if ( item.Kind == ModelDocKind.AttachmentInfluence )
		{
			if ( Owner( ctx, item ) is not { } owner ) return;
			if ( !ModelDocContext.InRange( owner.Influences, item.Index ) ) return;

			body.AddChild( ModelDocInspect.Note(
				"Where this bone alone would put the attachment, and how much of the answer it is." ) );

			var one = new Inspectors.ControlSheet { ShowPropertyNameInTooltip = true };
			ctx.BindObject( one, owner.Influences[item.Index] );
			body.AddChild( one );
			return;
		}

		if ( !ModelDocContext.InRange( ctx.Definition.Attachments, item.Index ) ) return;

		var attachment = ctx.Definition.Attachments[item.Index];

		var fields = new Inspectors.ControlSheet { ShowPropertyNameInTooltip = true };
		ctx.BindObject( fields, attachment );
		body.AddChild( fields );

		body.AddChild( ModelDocInspect.Note( attachment.Influences is { Count: > 1 }
			? $"Pulled between {attachment.Influences.Count} bones - the parent and offset above are what "
				+ "anything that has not heard of that reads. Each bone is edited on its own row."
			: "On one bone. Add influences to hang it between several - the point between both hands is "
				+ "half of each." ) );

		var add = body.AddChild<TextButtonPanel>();
		add.Text = "Add Influence";
		add.Icon = "device_hub";
		add.Pressed = () => Edit( ctx, "Add Influence", () => AddInfluence( attachment ) );

		if ( Wrong( ctx, attachment ) is { } wrong )
			body.AddChild( ModelDocInspect.Note( wrong, Theme.Yellow ) );
	}

	//What is wrong with one, where the model in front of us can say.
	static string Wrong( ModelDocContext ctx, ModelDefinition.Attachment attachment )
	{
		if ( string.IsNullOrWhiteSpace( attachment.Name ) )
			return "Unnamed - nothing can ask for it.";

		var model = ctx.Host.CompiledModel;
		if ( model is not { IsValid: true } ) return null;

		bool Known( string bone )
			=> string.IsNullOrWhiteSpace( bone )
			|| model.Bones?.AllBones?.Any( x => x.IsNamed( bone ) ) == true
			|| ctx.Definition.Bones?.Any( x =>
				string.Equals( x?.Name, bone, StringComparison.OrdinalIgnoreCase ) ) == true;

		if ( attachment.Influences is { Count: > 1 } )
		{
			var unknown = attachment.Influences
				.Where( x => x is not null && !Known( x.Bone ) )
				.Select( x => string.IsNullOrWhiteSpace( x.Bone ) ? "(none)" : x.Bone )
				.ToList();

			//A list with a hole in it is dropped whole rather than averaged over what is left, so
			//this is the attachment falling back to its parent bone - not a slightly wrong position.
			return unknown.Count > 0
				? $"This model has no {string.Join( ", ", unknown )} - the pull is dropped and the parent bone is used."
				: null;
		}

		return Known( attachment.Parent )
			? null
			: $"This model has no bone called {attachment.Parent} - it hangs off the model itself.";
	}

	//-- the viewport ----------------------------------------------------------

	public override ScenePlacement Placement( ModelDocContext ctx, ModelDocItem item )
	{
		if ( item.IsFolder ) return null;

		var model = ctx.Host.CompiledModel;
		var definition = ctx.Definition;

		if ( item.Kind == ModelDocKind.AttachmentInfluence )
		{
			if ( Owner( ctx, item ) is not { } owner ) return null;
			if ( !ModelDocContext.InRange( owner.Influences, item.Index ) ) return null;

			var influence = owner.Influences[item.Index];

			Transform Bone() => Frame( model, definition, influence.Bone, ctx.Host.BoneNow );

			return new ScenePlacement
			{
				Name = $"attachment.{item.ParentIndex}.{item.Index}",
				GetPosition = () => Bone().ToWorld( influence.Local ).Position,
				GetRotation = () => Bone().ToWorld( influence.Local ).Rotation.Angles(),
				Set = ( position, rotation ) =>
				{
					var local = Bone().ToLocal( new Transform( position, rotation.ToRotation() ) );
					influence.Position = local.Position;
					influence.Rotation = local.Rotation.Angles();
					ctx.Host.NotifyDefinitionChanged();
				}
			};
		}

		if ( item.Kind != ModelDocKind.Attachment ) return null;
		if ( !ModelDocContext.InRange( definition.Attachments, item.Index ) ) return null;

		var attachment = definition.Attachments[item.Index];

		//A place averaged over several bones cannot be dragged as one thing: there is no single number
		//for the drag to be written back into. Each bone pulling it has a handle of its own instead.
		if ( attachment.Influences is { Count: > 1 } ) return null;

		Transform Parent() => Frame( model, definition, attachment.Parent, ctx.Host.BoneNow );

		return new ScenePlacement
		{
			Name = $"attachment.{item.Index}",
			GetPosition = () => Parent().ToWorld( attachment.Local ).Position,
			GetRotation = () => Parent().ToWorld( attachment.Local ).Rotation.Angles(),
			Set = ( position, rotation ) =>
			{
				var local = Parent().ToLocal( new Transform( position, rotation.ToRotation() ) );
				attachment.Position = local.Position;
				attachment.Rotation = local.Rotation.Angles();
				ctx.Host.NotifyDefinitionChanged();
			}
		};
	}

	//Frame a bone name hangs off, walking added bones down to a real one; live uses the playing pose
	internal static Transform Frame( Model model, ModelDefinition definition, string parent,
		Func<string, Transform?> live = null )
	{
		var offset = Transform.Zero;

		//A chain cannot be longer than the definition has bones in it, and one that names itself would otherwise be a hang rather than a mistake.
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var name = parent;

		while ( !string.IsNullOrWhiteSpace( name ) && seen.Add( name ) )
		{
			if ( live?.Invoke( name ) is { } now )
				return now.ToWorld( offset );

			if ( model is { IsValid: true }
				&& model.Bones?.AllBones?.FirstOrDefault( x => x.IsNamed( name ) ) is { } bone )
				return model.GetBoneTransform( bone.Index ).ToWorld( offset );

			var extra = definition?.Bones?.FirstOrDefault( x =>
				string.Equals( x?.Name, name, StringComparison.OrdinalIgnoreCase ) );

			if ( extra is null ) break;

			offset = extra.Local.ToWorld( offset );
			name = extra.Parent;
		}

		return offset;
	}

	//Where one sits, in the pose being played where there is one.
	internal static Transform Placed( Model model, ModelDefinition definition,
		ModelDefinition.Attachment attachment, Func<string, Transform?> live = null )
		=> Frame( model, definition, attachment.Parent, live ).ToWorld( attachment.Local );

	//One attachment, wherever the caller has worked out that it is. Handed the place rather than working it out: the viewport
	//draws these off the renderer - a muzzle is where the arm holding it is this frame - and this only knows how to draw one.
	internal static void Draw( Transform at, string name, bool facesOneWay, float size, bool selected, bool label )
	{
		var alpha = selected ? 1.0f : 0.35f;

		Gizmo.Draw.LineThickness = selected ? 2.0f : 1.0f;
		Axes( at, size, alpha );

		//One that faces one way is carried about by its bones but does not turn with them, so the ring says the tripod is not going to follow.
		if ( facesOneWay )
		{
			Gizmo.Draw.Color = Color.White.WithAlpha( alpha * 0.8f );
			Gizmo.Draw.LineCircle( at.Position, at.Rotation.Forward, size * 0.45f );
		}

		if ( !label || string.IsNullOrWhiteSpace( name ) ) return;

		Gizmo.Draw.Color = Color.White.WithAlpha( selected ? 1.0f : 0.6f );
		Gizmo.Draw.ScreenText( name, at.Position, new Vector2( 10.0f, -8.0f ), size: 12.0f );
	}

	//The bones a place is pulled between, each drawn back to the place itself. What each bone on its own would say, and how far
	//the answer sits from it - which is the whole reason for pulling a place between several rather than hanging it off one.
	internal static void DrawInfluences( Model model, ModelDefinition definition,
		ModelDefinition.Attachment attachment, Transform at, float size, int lit,
		Func<string, Transform?> live = null )
	{
		if ( attachment?.Influences is not { Count: > 1 } ) return;

		for ( var i = 0; i < attachment.Influences.Count; i++ )
		{
			var influence = attachment.Influences[i];
			if ( influence is null ) continue;

			var pull = Frame( model, definition, influence.Bone, live ).ToWorld( influence.Local );
			var on = i == lit;

			Gizmo.Draw.Color = Theme.Primary.WithAlpha( on ? 0.95f : 0.5f );
			Gizmo.Draw.LineThickness = on ? 2.0f : 1.0f;
			Gizmo.Draw.Line( pull.Position, at.Position );
			Gizmo.Draw.LineSphere( pull.Position, size * 0.12f, 6 );
		}
	}

	//Draws X/Y/Z axes as red/green/blue to match the transform handles; Z is the longer arm
	internal static void Axes( Transform at, float size, float alpha )
	{
		Gizmo.Draw.Color = Color.Red.WithAlpha( alpha );
		Gizmo.Draw.Line( at.Position, at.Position + at.Rotation.Right * size );
		Gizmo.Draw.Color = Color.Green.WithAlpha( alpha );
		Gizmo.Draw.Line( at.Position, at.Position + at.Rotation.Up * size );
		Gizmo.Draw.Color = Color.Blue.WithAlpha( alpha );
		Gizmo.Draw.Line( at.Position, at.Position + at.Rotation.Backward * size * 1.6f );
	}
}
