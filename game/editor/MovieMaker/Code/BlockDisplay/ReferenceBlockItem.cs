using Rinten.MovieMaker;
using Rinten.MovieMaker.Properties;
using Rinten.UI;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

/// <summary>A block that points at another track, showing the path it points down.</summary>
public sealed class ReferenceBlockItem<T> : PropertyBlockItem<BindingReference<T>>
	where T : class, IValid
{
	private readonly Rinten.UI.Label _label;

	public ReferenceBlockItem()
	{
		_label = AddChild<Rinten.UI.Label>( "block-text" );
	}

	protected override Color BackgroundColor => Theme.Green.Darken( 0.5f ).WithAlpha( 0.75f );

	protected override void OnResize() => Rebuild();

	protected override void OnBlockChanged( MovieTimeRange timeRange ) => Rebuild();

	private void Rebuild()
	{
		if ( Block.GetValue( Block.TimeRange.Start ).TrackId is { } trackId )
		{
			var track = ProjectTrack.Project.GetTrack( trackId ) as IReferenceTrack;

			_label.Text = track?.GetPathString() ?? "unknown";
		}
		else
		{
			_label.Text = "null";
		}
	}
}
