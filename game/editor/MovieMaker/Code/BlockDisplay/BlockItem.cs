using Rinten.MovieMaker;
using Rinten.MovieMaker.Compiled;
using Rinten.UI;
using System.Collections.Generic;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

//A block is placed on the timeline in scene pixels and everything it shows is a panel inside it, in its own coordinates. What that is depends on what the track holds, which is what the subclasses are for.
/// <summary>One stretch of a track that holds something: keyframes, a curve, a clip.</summary>
public abstract partial class BlockItem : TimelineItem, ISnapSource
{
	private ITrackBlock? _block;
	private float _prevWidth;

	public TimelineTrack Track { get; private set; } = null!;

	public Session Session => Track.Session;

	public bool IsPreview
	{
		get => HasClass( "preview" );
		set => SetClass( "preview", value );
	}

	public ITrackBlock Block
	{
		get => _block ?? throw new InvalidOperationException();
		set
		{
			if ( ReferenceEquals( _block, value ) ) return;

			if ( _block is IDynamicBlock oldBlock )
			{
				oldBlock.Changed -= Block_Changed;
			}

			_block = value;

			if ( _block is IDynamicBlock newBlock )
			{
				newBlock.Changed += Block_Changed;
			}

			if ( _block is not null )
			{
				OnBlockChanged( _block.TimeRange );
			}
		}
	}

	public MovieTime Offset { get; set; }

	protected IProjectTrack ProjectTrack => Track.View.Track;

	protected MovieTimeRange TimeRange => Block.TimeRange + Offset;

	protected BlockItem()
		: base( null )
	{
		AddClass( "block-item" );
	}

	private void Initialize( TimelineTrack track, ITrackBlock block, MovieTime offset )
	{
		Track = track;
		Timeline = track.Timeline;

		Block = block;
		Offset = offset;
	}

	private void Block_Changed( MovieTimeRange timeRange ) => OnBlockChanged( timeRange );

	protected virtual void OnBlockChanged( MovieTimeRange timeRange ) { }

	public override void OnDeleted()
	{
		//So the changed event is unhooked.
		Block = null!;

		Timeline.Forget( this );

		base.OnDeleted();
	}

	/// <summary>Puts this block where its time range says, on its track's row.</summary>
	public void Layout()
	{
		var width = Timeline.TimeToPixels( TimeRange.Duration );

		SetSceneRect( new Rect(
			Timeline.TimeToPixels( TimeRange.Start ),
			Track.SceneRect.Top + (MovieLayout.TrackHeight - MovieLayout.BlockHeight) * 0.5f,
			width,
			MovieLayout.BlockHeight ) );

		if ( MathF.Abs( width - _prevWidth ) > 0.1f )
		{
			_prevWidth = width;

			OnResize();
		}

		UpdateAppearance();
	}

	protected virtual void OnResize() { }

	protected virtual Color BackgroundColor => MovieColors.ChannelBackground.WithAlpha( 0.75f );

	private Color? _appliedColor;

	private void UpdateAppearance()
	{
		var locked = Track.View.IsLocked;
		var color = BackgroundColor.Lighten( locked ? 0.2f : 0f );

		SetClass( "locked", locked );

		if ( _appliedColor == color ) return;

		_appliedColor = color;

		Style.BackgroundColor = color;
	}

	/// <summary>A box across part of this block, in the block's own coordinates.</summary>
	public Rect GetRect( MovieTimeRange range )
	{
		var origin = Timeline.TimeToPixels( Block.TimeRange.Start );
		var left = Timeline.TimeToPixels( range.Start ) - origin;
		var right = Timeline.TimeToPixels( range.End ) - origin;

		return new Rect( left, 0f, right - left, MovieLayout.BlockHeight );
	}

	public virtual IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary ) =>
		IsPreview ? [] : [Block.TimeRange.Start, Block.TimeRange.End];
}

public interface IBlockItem
{
	MovieTime Offset { get; }
}

public interface IBlockItem<T> : IBlockItem;

public abstract class BlockItem<T> : BlockItem, IBlockItem<T>
	where T : ITrackBlock
{
	public new T Block => (T)base.Block;
}

public interface IPropertyBlockItem : IBlockItem
{
	IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange );
}

public interface IPropertyBlockItem<T> : IBlockItem<T>, IPropertyBlockItem;

public abstract class PropertyBlockItem<T> : BlockItem<IPropertyBlock<T>>, IPropertyBlockItem<T>
{
	public IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange )
	{
		var clamped = Block.TimeRange.Clamp( timeRange );

		return Block switch
		{
			IPaintHintBlock paintHintBlock => paintHintBlock.GetPaintHints( clamped ),
			CompiledSampleBlock<T> => [clamped],
			_ => []
		};
	}

	/// <summary>Gets time regions that should be shown as separate blocks, within <paramref name="timeRange"/>.</summary>
	public IEnumerable<MovieTimeRange> GetPaintBlocks( MovieTimeRange timeRange )
	{
		var hints = GetPaintHints( timeRange );

		var prev = timeRange.Start;

		foreach ( var hint in hints )
		{
			if ( hint.Start > prev )
			{
				yield return (prev, hint.Start);
				prev = hint.Start;
			}

			if ( hint.End > prev )
			{
				yield return (prev, hint.End);
				prev = hint.End;
			}
		}

		if ( prev < timeRange.End )
		{
			yield return (prev, timeRange.End);
		}
	}
}
