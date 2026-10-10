using Rinten.Resources;

namespace Editor.SpriteEditor;

/// <summary>Everything that can be done to the frames of the selected animation, each as one undo step.</summary>
public sealed class FrameCommands
{
	private readonly ISpriteDocument _document;
	private readonly IAnimationSelection _selection;
	private readonly ISpritePlayback _playback;

	public FrameCommands( ISpriteDocument document, IAnimationSelection selection, ISpritePlayback playback )
	{
		_document = document;
		_selection = selection;
		_playback = playback;
	}

	/// <summary>The animation whose frames these are, or null.</summary>
	public Sprite.Animation Animation => _selection.Selected;

	private List<Sprite.Frame> Frames
	{
		get
		{
			var animation = Animation;
			if ( animation is null ) return null;

			return animation.Frames ??= new();
		}
	}

	public int Count => Frames?.Count ?? 0;

	public void AddEmpty()
	{
		if ( Frames is not { } frames ) return;

		_document.Edit( "Add Frame", () => frames.Add( new Sprite.Frame() ) );
	}

	/// <summary>The frame at an index of the selected animation, or null.</summary>
	public Sprite.Frame At( int index )
		=> Frames is { } frames && index >= 0 && index < frames.Count ? frames[index] : null;

	/// <summary>Makes a frame another cell of its sheet.</summary>
	public void SetRegion( int index, RectInt region )
	{
		if ( At( index ) is not { } frame ) return;

		_document.Edit( "Pick Cell", () => frame.Region = region );
	}

	/// <summary>Gives a frame another picture, all of it.</summary>
	public void SetPicture( int index, Texture texture )
	{
		if ( At( index ) is not { } frame ) return;

		_document.Edit( "Change Picture", () =>
		{
			frame.Texture = texture;
			frame.Region = default;
		} );
	}

	/// <summary>One frame per image file, appended in the order given.</summary>
	public void ImportImages( IEnumerable<string> relativePaths )
	{
		if ( Frames is not { } frames ) return;

		var textures = relativePaths.Select( path => FrameSources.FromImage( path ) ).Where( t => t is not null ).ToList();
		if ( textures.Count == 0 ) return;

		_document.Edit( "Import Frames", () =>
		{
			foreach ( var texture in textures )
				frames.Add( new Sprite.Frame { Texture = texture } );
		} );
	}

	/// <summary>One frame per rectangle of a sheet, in the order given - each the sheet itself with the rectangle as its region, so every frame is the one picture and a sprite playing them is drawn from one texture whichever frame it is on.</summary>
	public void ImportSpritesheet( string path, IReadOnlyList<Rect> cells )
	{
		if ( Frames is not { } frames ) return;
		if ( cells.Count == 0 ) return;

		var sheet = Texture.LoadFromFileSystem( path, FileSystem.Mounted );
		if ( sheet is null )
		{
			Log.Warning( $"SpritesheetImporter: failed to load texture '{path}'" );
			return;
		}

		var texture = FrameSources.FromImage( path );
		if ( texture is null ) return;

		var regions = new List<RectInt>();

		foreach ( var cell in cells )
		{
			//A cell that is partly off the sheet is cut down to what is on it; one that is wholly off it is nothing.
			var left = Math.Clamp( (int)cell.Left, 0, sheet.Width );
			var top = Math.Clamp( (int)cell.Top, 0, sheet.Height );
			var right = Math.Clamp( (int)cell.Left + (int)cell.Width, 0, sheet.Width );
			var bottom = Math.Clamp( (int)cell.Top + (int)cell.Height, 0, sheet.Height );

			if ( right <= left || bottom <= top ) continue;

			regions.Add( new RectInt( left, top, right - left, bottom - top ) );
		}

		if ( regions.Count == 0 ) return;

		var plural = regions.Count == 1 ? "" : "s";

		_document.Edit( $"Import Spritesheet ({regions.Count} frame{plural})", () =>
		{
			foreach ( var region in regions )
				frames.Add( new Sprite.Frame { Texture = texture, Region = region } );
		} );
	}

	/// <summary>Copies frames. One frame is copied to right after itself; several are copied to the end, in order.</summary>
	public void Duplicate( IReadOnlyCollection<int> indices )
	{
		if ( Frames is not { } frames ) return;

		var ordered = indices.Where( i => i >= 0 && i < frames.Count ).OrderBy( i => i ).ToList();
		if ( ordered.Count == 0 ) return;

		var name = ordered.Count == 1 ? $"Duplicate Frame {ordered[0]}" : $"Duplicate {ordered.Count} Frames";

		_document.Edit( name, () =>
		{
			if ( ordered.Count == 1 )
			{
				frames.Insert( ordered[0] + 1, Copy( frames[ordered[0]] ) );
				return;
			}

			foreach ( var index in ordered )
				frames.Add( Copy( frames[index] ) );
		} );
	}

	public void Delete( IReadOnlyCollection<int> indices )
	{
		if ( Frames is not { } frames ) return;

		var ordered = indices.Where( i => i >= 0 && i < frames.Count ).OrderByDescending( i => i ).ToList();
		if ( ordered.Count == 0 ) return;

		var name = ordered.Count == 1 ? $"Delete Frame {ordered[0]}" : $"Delete {ordered.Count} Frames";

		_document.Edit( name, () =>
		{
			foreach ( var index in ordered )
				frames.RemoveAt( index );

			ClampPlayhead( frames );
		} );
	}

	/// <summary>Moves frames so they sit at a target frame, before or after it, in their own order. Returns where they ended up.</summary>
	public List<int> Move( IReadOnlyCollection<int> indices, int target, bool after )
	{
		var placed = new List<int>();

		if ( Frames is not { } frames ) return placed;

		var moving = indices.Where( i => i >= 0 && i < frames.Count ).Distinct().OrderBy( i => i ).ToList();
		if ( moving.Count == 0 || target < 0 || target >= frames.Count ) return placed;
		if ( moving.Contains( target ) ) return placed;

		_document.Edit( $"Reorder {moving.Count} Frames", () =>
		{
			var lifted = moving.Select( i => frames[i] ).ToList();
			var insertAt = after ? target + 1 : target;

			//Taking a frame out from before the target pulls the target back one.
			insertAt -= moving.Count( i => i < insertAt );

			foreach ( var index in moving.OrderByDescending( i => i ) )
				frames.RemoveAt( index );

			frames.InsertRange( insertAt, lifted );

			for ( var i = 0; i < lifted.Count; i++ )
				placed.Add( insertAt + i );
		} );

		return placed;
	}

	private static Sprite.Frame Copy( Sprite.Frame frame ) => Json.Deserialize<Sprite.Frame>( Json.Serialize( frame ) );

	private void ClampPlayhead( List<Sprite.Frame> frames )
	{
		if ( _playback.CurrentFrame >= frames.Count )
			_playback.CurrentFrame = Math.Max( 0, frames.Count - 1 );
	}

	//The loop range. Dragged live and committed on release, so a drag is one step rather than one per frame it passed over.

	/// <summary>Where the loop starts, from a frame index; -1 means "the first frame, whatever that is".</summary>
	public void SetLoopStart( int index )
	{
		if ( Animation is not { } animation ) return;

		var clamped = Math.Clamp( index, 0, animation.EffectiveLoopEnd );

		animation.LoopStart = clamped == 0 ? -1 : clamped;
	}

	/// <summary>Where the loop ends, from a frame index; -1 means "the last frame, whatever that is".</summary>
	public void SetLoopEnd( int index )
	{
		if ( Animation is not { } animation ) return;

		var last = Math.Max( 0, Count - 1 );
		var clamped = Math.Clamp( index, animation.EffectiveLoopStart, last );

		animation.LoopEnd = clamped == last ? -1 : clamped;
	}

	/// <summary>Commits whatever the loop handles were dragged to.</summary>
	public void CommitLoopRange( bool start ) => _document.Commit( start ? "Move Loop Start" : "Move Loop End" );
}
