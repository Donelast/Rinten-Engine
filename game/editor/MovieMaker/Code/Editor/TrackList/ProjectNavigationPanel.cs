using Editor.MovieMaker.BlockDisplays;
using Rinten.MovieMaker;
using Rinten.UI;
using System.IO;

namespace Editor.MovieMaker;

#nullable enable

//One per clip in the chain, so a sequence opened inside another shows what it is inside; clicking one of the outer ones goes back out to it.
/// <summary>The row at the top of the track list naming the clip being edited.</summary>
public sealed class ProjectNavigationPanel : Panel
{
	private readonly FilmStripPanel _strip;
	private readonly Rinten.UI.Label _title;

	public Session Session { get; }

	public bool IsActive { get; }

	public ProjectNavigationPanel( Session session, bool isActive )
	{
		Session = session;
		IsActive = isActive;

		AddClass( "project-navigation" );
		SetClass( "active", isActive );

		_strip = AddChild( new FilmStripPanel() );

		_title = AddChild<Rinten.UI.Label>( "title" );
		_title.Text = session.Title;

		if ( session.Context is { } context )
		{
			AddChild<Rinten.UI.Label>( "time start" ).Text = context.TimeRange.Start.ToString();
			AddChild<Rinten.UI.Label>( "time end" ).Text = context.TimeRange.End.ToString();
		}

		var location = session.Resource switch
		{
			MovieResource res => $"\n{Path.GetFileName( res.ResourcePath )}",
			_ => ""
		};

		Tooltip = (isActive
			? "Currently open movie clip."
			: "Parent movie clip containing the currently open one.") + location;

		UpdateTint();
	}

	public override void Tick()
	{
		base.Tick();

		UpdateTint();
	}

	private void UpdateTint()
	{
		_strip.Tint = IsActive
			? Theme.SurfaceLightBackground
			: HasHovered ? Theme.SelectedBackground : Theme.SurfaceBackground;

		_title.Style.FontColor = Theme.TextControl.Darken( IsActive ? 0f : HasHovered ? 0.1f : 0.25f );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		if ( IsActive ) return;

		while ( Session.Editor.Session != Session && Session.Editor.Session?.Parent is not null )
		{
			Session.Editor.ExitSequence();
		}
	}
}
