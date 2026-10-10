using Rinten.MovieMaker;
using Rinten.UI;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

/// <summary>A track of strings: the value written along the block, once per stretch it holds still.</summary>
public sealed class StringBlockItem : PropertyBlockItem<string?>
{
	private readonly ChildPool<Rinten.UI.Label> _labels;

	public StringBlockItem()
	{
		_labels = new ChildPool<Rinten.UI.Label>( this, "block-text" );
	}

	protected override void OnResize() => Rebuild();

	protected override void OnBlockChanged( MovieTimeRange timeRange ) => Rebuild();

	private void Rebuild()
	{
		_labels.Begin();

		foreach ( var range in GetPaintBlocks( Block.TimeRange ) )
		{
			if ( Block.GetValue( range.Start ) is not { } value ) continue;

			var label = _labels.Next();
			var rect = GetRect( range );

			label.Style.Left = Length.Pixels( rect.Left );
			label.Style.Width = Length.Pixels( rect.Width );
			label.Text = value;
		}

		_labels.End();
	}
}
