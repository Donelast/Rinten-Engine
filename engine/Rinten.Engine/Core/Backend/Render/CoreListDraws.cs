namespace Rinten;

//Command list draws, run at a camera stage into a pass or collected from a scene object's draw
internal static class CoreListDraws
{
	internal enum Place
	{
		/// <summary>Neither: a list run by hand outside any frame.</summary>
		Nowhere,

		/// <summary>At one of a camera's stages, with the frame open.</summary>
		Stage,

		/// <summary>Inside a scene object's own drawing, which is collected.</summary>
		Walk,
	}

	//Where a list being run right now draws.
	internal static Place Where
		=> CorePostBlit.Ready && CoreSceneDrawer.Drawing is not null ? Place.Stage
			: Graphics.IsActive && CoreImmediate.Capturing ? Place.Walk
			: Place.Nowhere;

	private static readonly HashSet<string> said = new();

	//Says once that a command had nowhere to draw.
	internal static void Nowhere( string what )
	{
		lock ( said )
		{
			if ( !said.Add( what ) ) return;
		}

		Log.Warning( $"CommandList.{what} was run outside a camera's stages and outside a scene object's drawing - there is nothing to draw it into" );
	}

	//-- models ----------------------------------------------------------------

	//A model drawn now, at a stage, with its own materials, into whatever the list has open: once for each place it stands, or as many copies
	//as it is told - see CoreSceneDrawer.QueueModelNow. Tested against the world's depth, and skipped where the frame has none at this stage.
	internal static void Model( Rinten.Rendering.CommandList list, Model model, ReadOnlySpan<Transform> transforms,
		RenderAttributes attributes, int instances = 0, GpuBuffer countFrom = null, long countAt = 0, int level = -1 )
	{
		var drawer = CoreSceneDrawer.Drawing;
		if ( drawer is null || model is null ) return;

		var mesh = CoreResources.Mesh( model );
		if ( mesh is null ) return;

		//A draw lands on the frame, so the point effects held for it go on first.
		CorePostBlit.Flush();

		var countHandle = countFrom?.Handle ?? 0;

		if ( countFrom is not null && countHandle == 0 ) return;

		if ( drawer.QueueModelNow( mesh, transforms, attributes ?? list?.Values, instances, countHandle, countAt, level ) )
		{
			//The copies' arguments are put with a copy, which no pass may be open for.
			CorePasses.Close();
			drawer.PrepareNamed();
		}

		if ( !CorePasses.OpenForDraw( depth: true, orNone: false, out var target, out var format, out _, out var finishes ) ) return;

		drawer.SubmitNamed( target, format, noDepth: false, finishes );
	}

	//-- boxes -----------------------------------------------------------------

	//The boxes a list has drawn since the last thing that was not a box - one draw for a run of them, which is what a HUD is. See FlushBoxes.
	private static readonly List<CoreUiRenderer.UiVertex> boxes = new();

	private static CoreRender.TargetKind boxTarget;
	private static int boxFormat;
	private static int boxWidth;
	private static int boxHeight;
	private static ulong boxTexture;

	//Whether a box can carry its own picture, out of the table, rather than binding it.
	private static bool Bindless => bindless ??= CoreRenderDevice.BindlessSlots > 0;
	private static bool? bindless;

	//Everything boxed up so far, drawn - by the list before whatever it runs next that is not a box, and when it ends.
	internal static void FlushBoxes()
	{
		if ( boxes.Count == 0 ) return;

		CoreSceneDrawer.DrawBoxes( System.Runtime.InteropServices.CollectionsMarshal.AsSpan( boxes ), boxTexture,
			boxTarget, boxFormat, boxWidth, boxHeight );

		boxes.Clear();
	}

	//Where a box goes, open, and the size of the pixels it is laid out in: a picture of the list's own
	//is its own size; the frame is the view's, whatever size the picture it is in has been drawn at.
	private static bool OpenBoxes( ulong texture, out uint slot )
	{
		slot = 0;

		var drawer = CoreSceneDrawer.Drawing;
		if ( drawer is null ) return false;

		CorePostBlit.Flush();

		if ( !CorePasses.OpenForDraw( depth: false, orNone: true, out var target, out var format, out _, out _ ) )
		{
			boxes.Clear();
			return false;
		}

		var (width, height) = target == CoreRender.TargetKind.Picture && CorePasses.Wanted is { } picture
			? ( picture.Width, picture.Height )
			: drawer.ViewSize;

		//Out of the table where there is one, so a run of boxes wearing different pictures is still one draw; bound where there is not.
		if ( texture != 0 && Bindless ) slot = CoreRender.TextureIndex( texture );

		var bound = slot != 0 ? 0ul : texture;

		if ( boxes.Count > 0 &&
			( boxTarget != target || boxFormat != format || boxWidth != width || boxHeight != height || boxTexture != bound ) )
		{
			FlushBoxes();
		}

		boxTarget = target;
		boxFormat = format;
		boxWidth = width;
		boxHeight = height;
		boxTexture = bound;

		return true;
	}

	//One box, as the interface's shader draws it: its shape measured where it was laid out, its corners wherever transform puts them.
	private static void AddBox( Rect box, Vector4 radii, Color fill, Color border, float borderWidth,
		uint slot, CoreUiRenderer.UiFill kind, Matrix? transform, float angle = 0.0f )
	{
		var rect = new Vector4( box.Left, box.Top, box.Width, box.Height );
		var misc = new Vector4( borderWidth, (float)(int)kind, 0.0f, 0.0f );

		var centre = box.Center;
		var cos = MathF.Cos( angle.DegreeToRadian() );
		var sin = MathF.Sin( angle.DegreeToRadian() );

		CoreUiRenderer.UiVertex Corner( float x, float y, float u, float v )
		{
			var at = new Vector2( x, y );

			//Turned about its middle, then moved as the list's matrix says.
			if ( angle != 0.0f )
			{
				var from = at - centre;
				at = centre + new Vector2( ( from.x * cos ) - ( from.y * sin ), ( from.x * sin ) + ( from.y * cos ) );
			}

			if ( transform is { } matrix )
			{
				var moved = matrix.Transform( new Vector3( at.x, at.y, 0.0f ) );
				at = new Vector2( moved.x, moved.y );
			}

			return new CoreUiRenderer.UiVertex
			{
				Position = at,
				Local = new Vector2( x, y ),
				Uv = new Vector2( u, v ),
				Rect = rect,
				Radii = radii,
				Fill = fill,
				Border = border,
				Misc = misc,
				Texture = slot,
			};
		}

		boxes.Add( Corner( box.Left, box.Top, 0, 0 ) );
		boxes.Add( Corner( box.Right, box.Top, 1, 0 ) );
		boxes.Add( Corner( box.Left, box.Bottom, 0, 1 ) );
		boxes.Add( Corner( box.Left, box.Bottom, 0, 1 ) );
		boxes.Add( Corner( box.Right, box.Top, 1, 0 ) );
		boxes.Add( Corner( box.Right, box.Bottom, 1, 1 ) );
	}

	//A number set on a list's attributes, however it was set - an int, a float, a vector or a colour - as the
	//four lanes it holds. A list writes an int it is given as a float, so asking for one by its type misses.
	private static Vector4 Numbers( RenderAttributes values, string name, Vector4 fallback )
	{
		if ( values is null || !values.TryGetHeld( name, out var held ) ) return fallback;

		return held.Kind switch
		{
			RenderAttributes.HeldKind.Reference => fallback,
			RenderAttributes.HeldKind.Int or RenderAttributes.HeldKind.UInt or
				RenderAttributes.HeldKind.Vector2Int or RenderAttributes.HeldKind.Vector3Int
				=> new Vector4( held.Integers.x, held.Integers.y, held.Integers.z, 0.0f ),
			_ => held.Numbers,
		};
	}

	//A picture of the game's on the device, uploaded where it has to be.
	private static ulong DeviceOf( Texture texture ) => texture?.core?.Device() ?? 0;

	//A pixel-space HUD quad; UI materials draw as the UI does, others as a tinted picture
	internal static void Quad( Rinten.Rendering.CommandList list, Rect rect, Material material, Color color, float angle = 0.0f )
	{
		var values = list?.Values;
		var transform = values is not null && values.TryGetHeld( "TransformMat", out var held ) && held.Is<Matrix>( out var matrix )
			? matrix
			: (Matrix?)null;

		//Which of the engine's own shapes this is, where it is one of them at all - see Material.EngineDraw. It used to be asked by comparing
		//the material against the handful of static ones by reference; the answer is on the material now, so a copy of one is still a box.
		var draws = material?.Draws ?? Material.EngineDraw.None;

		if ( draws == Material.EngineDraw.Line )
		{
			Line( values, color, transform );
			return;
		}

		var box = rect;
		var radii = Vector4.Zero;
		var border = Color.Transparent;
		var borderWidth = 0.0f;
		Texture picture = values?.GetTexture( "Texture" );

		if ( draws == Material.EngineDraw.Box && values is not null )
		{
			//The box the painter laid out, which is what the quad was drawn around.
			var at = Numbers( values, "BoxPosition", new Vector4( rect.Left, rect.Top, 0, 0 ) );
			var size = Numbers( values, "BoxSize", new Vector4( rect.Width, rect.Height, 0, 0 ) );

			box = new Rect( at.x, at.y, size.x, size.y );
			radii = Numbers( values, "BorderRadius", Vector4.Zero );

			if ( Numbers( values, "HasBorder", Vector4.Zero ).x != 0.0f )
			{
				//One width all round is what the shader draws: the widest side asked for.
				var widths = Numbers( values, "BorderSize", Vector4.Zero );

				borderWidth = MathF.Max( MathF.Max( widths.x, widths.y ), MathF.Max( widths.z, widths.w ) );
				border = Numbers( values, "BorderColorT", Vector4.Zero );
			}

			//A box shows a picture only where it was told to.
			if ( values.GetComboInt( "D_BACKGROUND_IMAGE", 0 ) == 0 ) picture = null;
		}

		var texture = DeviceOf( picture );

		//Any other material shows its own picture, where it has one. Not one of the engine's own shapes, though:
		//those carry no file to read a picture out of, and the box has already taken what it needed above.
		if ( texture == 0 && picture is null && material is not null &&
			draws == Material.EngineDraw.None && !string.IsNullOrWhiteSpace( material.ResourcePath ) )
		{
			texture = CoreResources.Colour( material.ResourcePath );
		}

		if ( !OpenBoxes( texture, out var slot ) ) return;

		AddBox( box, radii, color, border, borderWidth, slot,
			texture != 0 ? CoreUiRenderer.UiFill.Texture : CoreUiRenderer.UiFill.None, transform, angle );
	}

	//The painter's line - a quad from one point to another, as thick as it was asked.
	private static void Line( RenderAttributes values, Color tint, Matrix? transform )
	{
		if ( values is null ) return;

		var start = Numbers( values, "LineStart", Vector4.Zero );
		var end = Numbers( values, "LineEnd", Vector4.Zero );
		var thickness = MathF.Max( Numbers( values, "LineThickness", Vector4.One ).x, 0.0f );
		var colour = (Color)Numbers( values, "ColorStart", Vector4.One ) * tint;

		var along = new Vector2( end.x - start.x, end.y - start.y );
		var length = along.Length;

		if ( length <= 0.0f || thickness <= 0.0f ) return;
		if ( !OpenBoxes( 0, out var slot ) ) return;

		//Laid out flat along x from the start, then turned and moved onto the line - the shape is measured flat, where it has square ends.
		Matrix turn = System.Numerics.Matrix4x4.CreateRotationZ( MathF.Atan2( along.y, along.x ) )
			* System.Numerics.Matrix4x4.CreateTranslation( start.x, start.y, 0.0f );

		if ( transform is { } matrix ) turn *= matrix;

		AddBox( new Rect( 0.0f, -thickness * 0.5f, length, thickness ), Vector4.Zero, colour, Color.Transparent, 0.0f,
			slot, CoreUiRenderer.UiFill.None, turn );
	}

	//The painter's line material - see HudPainter.DrawLine, which is the only thing that draws with it.
	private static Material LineMaterial => Rinten.Rendering.HudPainter.LineMaterial;

	//Words, at a stage: the text drawn as a picture - out of the cache the interface
	//and the gizmo labels share - and laid in the rectangle the way the flags align it.
	internal static void Text( Rinten.Rendering.CommandList list, TextRendering.TextBlock block, Rect rect, TextFlag flags, float angle )
	{
		if ( block is null || block.IsEmpty ) return;

		var (texture, size) = CoreSceneDrawer.TextPicture( block.Scope );
		if ( texture == 0 ) return;

		var values = list?.Values;
		var transform = values is not null && values.TryGetHeld( "TransformMat", out var held ) && held.Is<Matrix>( out var matrix )
			? matrix
			: (Matrix?)null;

		if ( !OpenBoxes( texture, out var slot ) ) return;

		AddBox( rect.Align( size, flags ).Floor(), Vector4.Zero, Color.White, Color.Transparent, 0.0f,
			slot, CoreUiRenderer.UiFill.Texture, transform, angle );
	}

	//The same words from inside a scene object's drawing: what Graphics.DrawText does, with what the list set on its attributes laid over the view's.
	internal static void TextInWalk( Rinten.Rendering.CommandList list, TextRendering.TextBlock block, Rect rect, TextFlag flags, float angle )
	{
		if ( block is null || block.IsEmpty ) return;

		list?.Values.MergeTo( Graphics.Attributes );

		block.MakeReady();

		var texture = block.Texture;
		if ( texture is null ) return;

		Graphics.Attributes.Set( "Texture", texture );
		Graphics.Attributes.Set( "SamplerIndex", Rinten.Rendering.SamplerState.GetBindlessIndex( new Rinten.Rendering.SamplerState() { Filter = block.FilterMode } ) );

		var at = rect.Align( texture.Size, flags );

		if ( angle == 0.0f ) Graphics.DrawQuad( at.Floor(), Material.UI.Text, Color.White );
		else Graphics.DrawQuad( at, angle, Material.UI.Text, Color.White );
	}
}
