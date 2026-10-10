using Rinten.UI;
using System;
using System.Globalization;

namespace Editor.Mcp;

//Deeper UI inspection: matched css rules, css previews, hit tests, pixels, text measure, draw cost
internal static partial class PanelTools
{
	[McpTool.ReadOnly( "ui_matched_rules" )]
	[Description( "Every style rule that matched a panel, in the order the cascade applied them - so for any property the last rule to set it won. Each rule comes with the selector that matched (of the rule's list), its specificity, the file and line, and its declarations, each marked with what beat it when something later - another rule or the panel's inline style - set the same property. Inline declarations (from a style attribute, code, or ui_set_style) come last; they beat every rule. For 'why is my css not applying'." )]
	public static MatchedRules UiMatchedRules( [Description( "The panel's id, from panel_tree or panel_find." )] string id )
	{
		var panel = PanelHandles.Resolve( id );
		var selectors = panel.Style?.LastActiveRules?.ToArray() ?? [];
		var inline = panel.Style?.RawValues?.Values.ToArray() ?? [];

		//Walked from the winner back, so each declaration knows who set it after.
		var winner = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var value in inline )
		{
			if ( value.IsValid ) winner.TryAdd( value.Name, "inline" );
		}

		var rules = new MatchedRule[selectors.Length];

		for ( var i = selectors.Length - 1; i >= 0; i-- )
		{
			var selector = selectors[i];
			var block = selector.Block;
			var label = $"{selector.AsString} ({block?.FileName}:{block?.FileLine})";
			var declarations = block?.GetRawValues() ?? [];

			rules[i] = new MatchedRule
			{
				Order = i,
				Selector = selector.AsString,
				AllSelectors = block is null ? null : string.Join( ", ", block.SelectorStrings ),
				Specificity = selector.SelfScore,
				Source = block?.FileName is null ? null : $"{block.FileName}:{block.FileLine}",
				Declarations = declarations.Select( value => Declaration( value, winner ) ).ToArray(),
			};

			foreach ( var value in declarations )
			{
				if ( value.IsValid ) winner.TryAdd( value.Name, label );
			}
		}

		return new MatchedRules
		{
			Id = PanelHandles.Id( panel ),
			Panel = Line( panel ),
			PseudoClasses = panel.PseudoClass.ToString(),
			Rules = rules,
			Inline = inline.Select( value => $"{value.Name}: {value.Value}{(value.IsValid ? "" : "  (didn't parse)")}" ).ToArray(),
		};

		static string Declaration( Rinten.UI.IStyleBlock.StyleProperty value, Dictionary<string, string> winner )
		{
			var text = $"{value.Name}: {value.Value}";

			if ( !value.IsValid ) return text + "  (didn't parse - ignored)";

			return winner.TryGetValue( value.Name, out var by ) ? $"{text}  (overridden by {by})" : text;
		}
	}

	[McpTool( "ui_set_style" )]
	[Description( "Set css on a live panel, the way a browser's style editor does - change it, look with window_screenshot, change it again, no file edit and no hotload in between. It's an inline style: it beats every rule, and stays until the panel is rebuilt or its markup sets its style again. There's no taking a declaration back off: set the value it had (ui_matched_rules says) to undo. Answers with the declarations that took, the ones that didn't parse, and the panel's box a frame later." )]
	public static async Task<StyleChange> UiSetStyle(
		[Description( "The panel's id, from panel_tree or panel_find." )] string id,
		[Description( "The css to set, e.g. 'padding: 8px; background-color: #e8b04a'." )] string css )
	{
		if ( string.IsNullOrWhiteSpace( css ) )
			throw new Exception( "Give some css to set, like 'border-radius: 4px'." );

		var panel = PanelHandles.Resolve( id );
		var before = panel.Box.Rect;
		var names = css.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
			.Select( x => x.Split( ':', 2 )[0].Trim() )
			.Where( x => x.Length > 0 )
			.ToArray();

		panel.Style.Set( css );
		panel.Style.Dirty();

		await Settle( panel );

		var raw = panel.Style.RawValues;

		return new StyleChange
		{
			Id = PanelHandles.Id( panel ),
			Panel = Line( panel ),
			Took = names.Where( n => raw.TryGetValue( n, out var v ) && v.IsValid ).Select( n => $"{n}: {raw[n].Value}" ).ToArray(),
			Refused = names.Where( n => !raw.TryGetValue( n, out var v ) || !v.IsValid ).ToArray(),
			RectBefore = RectText( before ),
			Rect = RectText( panel.Box.Rect ),
		};
	}

	[McpTool( "ui_set_pseudo" )]
	[Description( "Force a pseudo class on a panel - hover, active, focus, checked... - so its styles can be read with ui_matched_rules and seen with window_screenshot without a pointer. Clear it again with on=false. Hover and active are the pointer's: the next real pointer move over the window takes them back." )]
	public static async Task<StyleChange> UiSetPseudo(
		[Description( "The panel's id, from panel_tree or panel_find." )] string id,
		[Description( "Which one, e.g. 'hover', 'active', 'focus'." )] string pseudo,
		[Description( "Set it (default) or clear it." )] bool on = true )
	{
		if ( !Enum.TryParse<PseudoClass>( pseudo?.Trim().TrimStart( ':' ), true, out var flag ) || flag == PseudoClass.None )
			throw new Exception( $"'{pseudo}' isn't a pseudo class - one of {string.Join( ", ", Enum.GetNames<PseudoClass>().Where( x => x != nameof( PseudoClass.None ) ) )}." );

		var panel = PanelHandles.Resolve( id );
		var before = panel.Box.Rect;

		panel.PseudoClass = on ? panel.PseudoClass | flag : panel.PseudoClass & ~flag;

		await Settle( panel );

		return new StyleChange
		{
			Id = PanelHandles.Id( panel ),
			Panel = Line( panel ),
			PseudoClasses = panel.PseudoClass.ToString(),
			Rules = panel.ActiveStyleBlocks.Count(),
			RectBefore = RectText( before ),
			Rect = RectText( panel.Box.Rect ),
		};
	}

	[McpTool.ReadOnly( "ui_hit_test" )]
	[Description( "What is under a point in a window, asked the way input asks it - transforms, clipping, rounded corners and all. Answers with the panel a click there would reach (the topmost whose pointer-events isn't none), the topmost panel drawn there whatever its pointer-events, and the chain from that one up to the root with each panel's pointer-events - so 'what is eating my clicks' is the first line and 'why does my click fall through' is the chain." )]
	public static HitTestResult UiHitTest(
		[Description( "The point as 'x,y', in the window's pixels (for 'game', the game screen's)." )] string at,
		[Description( "Which window, as window_list names it; empty is the main window, 'game' the game's UI." )] string window = "" )
	{
		var point = ParsePoint( at );
		var game = string.Equals( window?.Trim(), "game", StringComparison.OrdinalIgnoreCase );

		//Later roots draw over earlier ones, so they're asked first.
		var roots = Roots( window ).Reverse().ToArray();

		Panel clicked = null, drawn = null;

		foreach ( var root in roots )
		{
			clicked ??= root.PanelUnder( point );
			drawn ??= root.PanelUnder( point, selectable: _ => true );
		}

		var chain = new List<string>();

		for ( var up = drawn; up is not null; up = up.Parent )
		{
			var events = up.ComputedStyle?.PointerEvents?.ToString() ?? "auto";
			chain.Add( $"{Line( up )}  pointer-events: {events}" );
		}

		return new HitTestResult
		{
			At = $"{point.x:0},{point.y:0}",
			Window = game ? "game" : WindowTools.Resolve( window ).Title,
			Clicks = clicked is null ? "nothing - no panel there takes the pointer" : Line( clicked ),
			Drawn = drawn is null ? "nothing" : Line( drawn ),
			Chain = chain.ToArray(),
		};
	}

	[McpTool.ReadOnly( "ui_pixels" )]
	[Description( "The exact pixels a panel came out as - the numbers behind window_screenshot, for when looking at a picture isn't an answer: a colour that should match a hex, an edge that should be crisp, text that should be there. Reads points relative to the panel's top left (default its centre), each as hex and 0-255 channels straight off the window's picture, and sums up the whole panel: the mean colour, and the darkest and brightest pixel. Editor windows only; the game's UI is in camera_screenshot." )]
	public static PixelsResult UiPixels(
		[Description( "The panel's id, from panel_tree or panel_find." )] string id,
		[Description( "Points to read as 'x,y x,y', relative to the panel's top left. Empty reads the centre." )] string points = "" )
	{
		var panel = PanelHandles.Resolve( id );
		var shell = CoreUiWindow.Holding( panel )
			?? throw new Exception( $"Panel {id} is the game's, not an editor window's - camera_screenshot shows the game's UI." );

		if ( !panel.IsVisible )
			throw new Exception( $"Panel {id} isn't visible - nothing of it is drawn. panel_inspect says why." );

		var rect = panel.Box.Rect;
		var (width, height) = shell.Size;
		var left = (int)MathF.Round( rect.Left );
		var top = (int)MathF.Round( rect.Top );
		var right = Math.Clamp( (int)MathF.Round( rect.Right ), 0, width );
		var bottom = Math.Clamp( (int)MathF.Round( rect.Bottom ), 0, height );
		var x0 = Math.Clamp( left, 0, width );
		var y0 = Math.Clamp( top, 0, height );

		if ( right - x0 < 1 || bottom - y0 < 1 )
			throw new Exception( $"Panel {id} lies outside its window ({RectText( rect )}) or has no size." );

		using var bitmap = shell.Shoot()
			?? throw new Exception( $"Window '{shell.Title}' couldn't be drawn - it may be minimized or not yet shown." );

		var pixels = bitmap.GetPixels32();
		var stride = bitmap.Width;

		var wanted = string.IsNullOrWhiteSpace( points )
			? [new Vector2( MathF.Floor( rect.Width / 2 ), MathF.Floor( rect.Height / 2 ) )]
			: points.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Select( ParsePoint ).ToArray();

		var read = wanted.Select( point =>
		{
			var x = left + (int)point.x;
			var y = top + (int)point.y;

			if ( x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height )
				throw new Exception( $"{point.x:0},{point.y:0} from the panel's corner is {x},{y} in the window, which is outside it." );

			return Pixel( point, pixels[y * stride + x] );
		} ).ToArray();

		//The whole box, summed: a flat fill reads as min = max = mean.
		double r = 0, g = 0, b = 0, a = 0;
		var darkest = (Luma: int.MaxValue, Colour: default( Color32 ));
		var brightest = (Luma: int.MinValue, Colour: default( Color32 ));

		for ( var y = y0; y < bottom; y++ )
		{
			for ( var x = x0; x < right; x++ )
			{
				var c = pixels[y * stride + x];
				r += c.r; g += c.g; b += c.b; a += c.a;

				var luma = c.r * 2126 + c.g * 7152 + c.b * 722;
				if ( luma < darkest.Luma ) darkest = (luma, c);
				if ( luma > brightest.Luma ) brightest = (luma, c);
			}
		}

		var count = (double)(right - x0) * (bottom - y0);
		var mean = new Color32( (byte)Math.Round( r / count ), (byte)Math.Round( g / count ), (byte)Math.Round( b / count ), (byte)Math.Round( a / count ) );

		return new PixelsResult
		{
			Id = PanelHandles.Id( panel ),
			Panel = Line( panel ),
			Rect = RectText( rect ),
			Points = read,
			Mean = mean.Hex,
			Darkest = darkest.Colour.Hex,
			Brightest = brightest.Colour.Hex,
		};
	}

	[McpTool.ReadOnly( "text_measure" )]
	[Description( "What a piece of text measures to through the text stack the UI lays out and draws with (RichTextKit, the engine's font manager) - single line, or wrapped at a width, with how many lines it came to. For 'why does this label wrap' or 'how wide must this column be'. Sizes are in the UI's pixels before the root's scale." )]
	public static TextMeasured TextMeasure(
		[Description( "The text to measure." )] string text,
		[Description( "Font family, e.g. 'Inter' or 'Roboto'. Unknown families fall back silently, the way the UI does." )] string font = "Roboto",
		[Description( "Font size in pixels. Default 14." )] float size = 14,
		[Description( "Font weight, 100 to 900. Default 400." ), Range( 100, 900 )] int weight = 400,
		[Description( "The italic face." )] bool italic = false,
		[Description( "Line height as a multiple of the font size. Default 1." )] float lineHeight = 1,
		[Description( "Extra pixels between letters." )] float letterSpacing = 0,
		[Description( "Extra pixels between words." )] float wordSpacing = 0,
		[Description( "Width to wrap at, in pixels. 0 measures one line." ), Range( 0, 8192 )] int wrapWidth = 0 )
	{
		if ( string.IsNullOrEmpty( text ) )
			throw new Exception( "Give some text to measure." );

		var scope = new Rinten.TextRendering.Scope( text, Color.White, size, font, weight )
		{
			FontItalic = italic,
			LineHeight = lineHeight,
			LetterSpacing = letterSpacing,
			WordSpacing = wordSpacing,
		};

		var single = Measure( scope, null );
		var wrapped = wrapWidth > 0 ? Measure( scope, wrapWidth ) : single;

		return new TextMeasured
		{
			Width = MathF.Round( wrapped.Width, 2 ),
			Height = MathF.Round( wrapped.Height, 2 ),
			Lines = wrapped.Lines,
			SingleLineWidth = MathF.Round( single.Width, 2 ),
			SingleLineHeight = MathF.Round( single.Height, 2 ),
		};

		static (float Width, float Height, int Lines) Measure( in Rinten.TextRendering.Scope scope, float? width )
		{
			var style = new Topten.RichTextKit.Style();
			scope.ToStyle( style );

			var block = new Topten.RichTextKit.TextBlock
			{
				FontMapper = Rinten.FontManager.Instance,
				MaxWidth = width,
			};

			block.AddText( scope.Text, style );

			return (block.MeasuredWidth, block.MeasuredHeight, block.Lines.Count);
		}
	}

	[McpTool.ReadOnly( "ui_text_stats" )]
	[Description( "Cumulative text-to-picture counters (label pictures and the scope cache): rebuilds, ms split into layout/paint/upload, bytes uploaded, pictures held. Subtract two calls. 'reset' restarts the worst-frame maxima." )]
	public static string UiTextStats( [Description( "Restart the worst-frame maxima after reading." )] bool reset = false )
	{
		var s = Rinten.UI.TextStats.Take( reset );
		return System.Text.Json.JsonSerializer.Serialize( new
		{
			s.Frames, s.Restyles, s.RestyleTicks, s.MeasureMisses, s.MeasureTicks, s.SizeHits, s.PictureHits, s.PictureHitTicks, s.SizeReleases,
			s.Rebuilds, s.LayoutTicks, s.PaintTicks, s.UploadTicks, s.NewTextures, s.ReusedTextures, s.UploadBytes,
			s.LiveTextures, s.LiveBytes, s.MaxRebuildsInFrame, s.MaxTicksInFrame, s.FramesWithRebuild,
			s.ScopeRasters, s.ScopeTicks, s.ScopeBytes,
			TicksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000.0,
			Text = Rinten.UI.TextStats.Describe( s ),
		} );
	}

	[McpTool.ReadOnly( "ui_batch_stats" )]
	[Description( "What a window's interface cost to draw on its last walk, in counts rather than milliseconds: panels visited and skipped, boxes made, the batches they went into and how many cuts a texture change forced (boxes per batch is the whole of how well it batches), distinct textures and whether each got its own slot, backdrop copies (each copies the window), text rasterised, texture uploads, the pixels the boxes cover and how much was dropped under something opaque, panels replayed from the geometry cache against recorded afresh, and how many frames in a row the whole frame was put out again. Plus the device's own draw count for the frame. With 'game', the game screen's walk." )]
	public static BatchStats UiBatchStats(
		[Description( "Which window, as window_list names it; empty is the main window, 'game' the game's screen." )] string window = "" )
	{
		if ( string.IsNullOrWhiteSpace( window ) is false && string.Equals( window.Trim(), "game", StringComparison.OrdinalIgnoreCase ) )
		{
			return Stats( "game", CoreUiRenderer.LastWalk, CoreUiRenderer.LastTextCount, null, kept: false );
		}

		var shell = WindowTools.Resolve( window );
		var frame = shell.Root?.Frame;
		var kept = frame is { Kept: true };
		var (profile, _) = shell.Profiles();

		return Stats( shell.Title, kept ? frame.Counts : CoreUiRenderer.LastWindowWalk, kept ? frame.TextCount : CoreUiRenderer.LastTextCount, profile?.Draws, kept );

		static BatchStats Stats( string title, CoreUiRenderer.WalkCounts walk, int texts, int? draws, bool kept ) => new()
		{
			Window = title,
			Source = kept ? "this window's kept frame" : "the last walk of any window - this one kept no frame",
			Visited = walk.Visited,
			Skipped = walk.Skipped,
			Culled = walk.Culled,
			Unbounded = walk.Unbounded,
			Boxes = walk.Boxes,
			Batches = walk.Batches,
			BoxesPerBatch = walk.Batches > 0 ? MathF.Round( walk.Boxes / (float)walk.Batches, 2 ) : 0,
			TextureCuts = walk.TextureCuts,
			DistinctTextures = walk.DistinctTextures,
			DistinctSlots = walk.DistinctSlots,
			Slotted = walk.Slotted,
			Unslotted = walk.Unslotted,
			Backdrops = walk.Backdrops,
			BakedBackgroundImages = walk.BakedBackgroundImages,
			Texts = texts,
			TextRasterised = walk.TextRasterised,
			TextureUploads = walk.TextureUploads,
			Replayed = walk.Replayed,
			Recorded = walk.Recorded,
			Reused = walk.Reused,
			AreaMegapixels = MathF.Round( (float)(walk.Area / 1.0e6), 3 ),
			Covered = walk.Covered,
			CoveredMegapixels = MathF.Round( (float)(walk.CoveredArea / 1.0e6), 3 ),
			Biggest = walk.Biggest?.Where( x => x.Area > 0 ).Select( x => $"{x.Area / 1.0e6:0.###} MP {x.Kind}{(x.Textured ? " picture" : "")} alpha {x.Alpha}" ).ToArray(),
			BiggestEmitted = walk.BiggestEmitted?.Where( x => x.Area > 0 ).Select( x => $"{x.Area / 1.0e6:0.###} MP {x.Panel} at {x.Order}" ).ToArray(),
			DeviceDraws = draws,
			FramesReusedTotal = CoreUiRenderer.FramesReused,
			FramesWalkedTotal = CoreUiRenderer.FramesWalked,
		};
	}

	//── Helpers ──────────────────────────────────────────────────────────────

	//A style or pseudo class change lands on a later tick: the panel's window draws a frame or two, or for the game's UI a moment passes.
	static async Task Settle( Panel panel )
	{
		if ( CoreUiWindow.Holding( panel ) is { } shell )
			await UiInputTools.Frames( shell, 2 );
		else
			await Frame.Next( 4 );
	}

	static Vector2 ParsePoint( string text )
	{
		var parts = (text ?? "").Split( ',', StringSplitOptions.TrimEntries );

		if ( parts.Length != 2
			|| !float.TryParse( parts[0], CultureInfo.InvariantCulture, out var x )
			|| !float.TryParse( parts[1], CultureInfo.InvariantCulture, out var y ) )
			throw new Exception( $"'{text}' isn't a point - they look like '120,40'." );

		return new Vector2( x, y );
	}

	static PixelRead Pixel( Vector2 point, Color32 c ) => new()
	{
		At = $"{point.x:0},{point.y:0}",
		Hex = c.Hex,
		R = c.r,
		G = c.g,
		B = c.b,
		A = c.a,
	};
}

//What ui_matched_rules reports.
internal class MatchedRules
{
	public string Id { get; set; }
	public string Panel { get; set; }
	public string PseudoClasses { get; set; }

	[Description( "The rules that matched, in cascade order - later beats earlier." )]
	public MatchedRule[] Rules { get; set; }

	[Description( "The panel's own inline declarations, which beat every rule." )]
	public string[] Inline { get; set; }
}

internal class MatchedRule
{
	public int Order { get; set; }

	[Description( "The selector of the rule's list that matched this panel." )]
	public string Selector { get; set; }
	public string AllSelectors { get; set; }

	[Description( "Its specificity: 1000 an id, 10 a class or pseudo class, 1 an element." )]
	public int Specificity { get; set; }
	public string Source { get; set; }
	public string[] Declarations { get; set; }
}

//What ui_set_style and ui_set_pseudo report.
internal class StyleChange
{
	public string Id { get; set; }
	public string Panel { get; set; }
	public string[] Took { get; set; }

	[Description( "Properties that didn't parse and were ignored." )]
	public string[] Refused { get; set; }
	public string PseudoClasses { get; set; }

	[Description( "How many rules match now." )]
	public int? Rules { get; set; }
	public string RectBefore { get; set; }
	public string Rect { get; set; }
}

//What ui_hit_test reports.
internal class HitTestResult
{
	public string At { get; set; }
	public string Window { get; set; }

	[Description( "The panel a click at the point reaches." )]
	public string Clicks { get; set; }

	[Description( "The topmost panel drawn at the point, whatever its pointer-events." )]
	public string Drawn { get; set; }

	[Description( "The drawn panel and its parents up to the root, each with its pointer-events." )]
	public string[] Chain { get; set; }
}

//What ui_pixels reports.
internal class PixelsResult
{
	public string Id { get; set; }
	public string Panel { get; set; }
	public string Rect { get; set; }
	public PixelRead[] Points { get; set; }

	[Description( "The mean colour over the panel's box." )]
	public string Mean { get; set; }
	public string Darkest { get; set; }
	public string Brightest { get; set; }
}

internal class PixelRead
{
	[Description( "Relative to the panel's top left." )]
	public string At { get; set; }
	public string Hex { get; set; }
	public int R { get; set; }
	public int G { get; set; }
	public int B { get; set; }
	public int A { get; set; }
}

//What text_measure reports.
internal class TextMeasured
{
	public float Width { get; set; }
	public float Height { get; set; }
	public int Lines { get; set; }
	public float SingleLineWidth { get; set; }
	public float SingleLineHeight { get; set; }
}

//What ui_batch_stats reports.
internal class BatchStats
{
	public string Window { get; set; }

	[Description( "Where the counts came from: the window's own kept frame, or the last walk of any." )]
	public string Source { get; set; }
	public int Visited { get; set; }
	public int Skipped { get; set; }
	public int Culled { get; set; }
	public int Unbounded { get; set; }
	public int Boxes { get; set; }
	public int Batches { get; set; }
	public float BoxesPerBatch { get; set; }
	public int TextureCuts { get; set; }
	public int DistinctTextures { get; set; }
	public int DistinctSlots { get; set; }
	public int Slotted { get; set; }
	public int Unslotted { get; set; }
	public int Backdrops { get; set; }
	public int BakedBackgroundImages { get; set; }
	public int Texts { get; set; }
	public int TextRasterised { get; set; }
	public int TextureUploads { get; set; }
	public int Replayed { get; set; }
	public int Recorded { get; set; }

	[Description( "Frames in a row the whole frame was put out again without a walk." )]
	public int Reused { get; set; }
	public float AreaMegapixels { get; set; }
	public int Covered { get; set; }
	public float CoveredMegapixels { get; set; }
	public string[] Biggest { get; set; }
	public string[] BiggestEmitted { get; set; }

	[Description( "The device's draw calls for the window's whole last frame, world included." )]
	public int? DeviceDraws { get; set; }
	public long FramesReusedTotal { get; set; }
	public long FramesWalkedTotal { get; set; }
}
