using Rinten.Internal;

namespace Editor.Inspectors;

[StyleSheet( "ControlSheetGroup.cs.scss" )]
public class ControlSheetGroup : Panel
{
	public ControlSheetGroupHeader Header { get; private set; }
	public Panel Body { get; private set; }

	internal List<SerializedProperty> Properties { get; private set; }

	Panel connectorVertical;
	Panel connectorFoot;
	float connectorLeft = float.NaN;
	float connectorTop = float.NaN;
	float connectorHeight = float.NaN;
	float connectorFootTop = float.NaN;
	float connectorFootLeft = float.NaN;

	string groupCookie;
	bool expanded = true;
	bool hasHeader;
	bool needsBuild;
	InspectorVisibilityAttribute[] visibility;

	IControlSheet.Group queuedGroup;

	public ControlSheetGroup()
	{
		AddClass( "controlgroup" );
		AddClass( "controlsheetgroup" );
	}

	public void QueueGroup( IControlSheet.Group group )
	{
		queuedGroup = group;
		ApplyQueuedGroup();
	}

	void ApplyQueuedGroup()
	{
		if ( queuedGroup is null ) return;

		var group = queuedGroup;
		queuedGroup = null;

		Properties = group.Properties.ToList();
		DeleteChildren();
		Header = null;
		Body = null;
		connectorVertical = null;
		connectorFoot = null;
		connectorLeft = float.NaN;
		connectorTop = float.NaN;
		connectorHeight = float.NaN;
		connectorFootTop = float.NaN;
		connectorFootLeft = float.NaN;
		needsBuild = true;

		if ( !AnyShown( Properties ) )
		{
			SetClass( "hidden", true );
			return;
		}

		SetClass( "hidden", false );

		var groupName = group.Name;
		hasHeader = !string.IsNullOrWhiteSpace( groupName );
		SetClass( "has-header", hasHeader );
		groupCookie = hasHeader ? $"controlsheetgroup.{groupName}" : null;

		var startClosed = Properties.SelectMany( x => x.GetAttributes<GroupAttribute>() ).Any( x => x.StartFolded );
		if ( groupCookie is not null )
			startClosed = EditorCookie.Get( groupCookie, startClosed );

		expanded = !startClosed;

		if ( hasHeader )
		{
			connectorVertical = CreateConnector( "vertical" );
			connectorFoot = CreateConnector( "foot" );

			Header = AddChild( new ControlSheetGroupHeader( this ) );
			Header.AddClass( "header" );
			Header.Title = groupName;
			Header.OnExpandedChanged = OnHeaderExpanded;

			var toggleGroup = Properties.FirstOrDefault( x => x.HasAttribute<ToggleGroupAttribute>() && x.Name == groupName );
			if ( toggleGroup is not null && toggleGroup.TryGetAttribute<ToggleGroupAttribute>( out var toggleAttr ) )
			{
				Properties.Remove( toggleGroup );

				if ( !string.IsNullOrWhiteSpace( toggleAttr.Label ) )
					Header.Title = toggleAttr.Label;

				Header.ToggleProperty = toggleGroup;
				SetVisibility( toggleGroup.GetAttributes<InspectorVisibilityAttribute>()?.ToArray() );

				if ( !toggleGroup.GetValue<bool>() )
					expanded = false;
			}

			Header.Expanded = expanded;
		}

		Body = AddChild<Panel>( "body" );

		BuildRows();
	}

	void BuildRows()
	{
		if ( Body is null || Properties is null ) return;

		needsBuild = false;
		Body.DeleteChildren();

		foreach ( var prop in Properties )
		{
			if ( prop.HasAttribute<HideAttribute>() )
				continue;

			var row = Body.AddChild<ControlSheetRow>();
			row.Initialize( prop );
		}

		UpdateBodyVisibility();
	}

	void OnHeaderExpanded( bool open )
	{
		expanded = open;

		if ( groupCookie is not null )
			EditorCookie.Set( groupCookie, !open );

		UpdateBodyVisibility();
	}

	bool IsContentVisible()
	{
		if ( !hasHeader ) return true;
		if ( !expanded ) return false;

		if ( Header?.ToggleProperty is not null )
			return Header.ToggleProperty.GetValue<bool>();

		return true;
	}

	void UpdateBodyVisibility()
	{
		var show = IsContentVisible();

		if ( Body is not null )
			Body.SetClass( "hidden", !show );

		Header?.SyncContentVisible( show );
		UpdateConnectors();
	}

	void UpdateConnectors()
	{
		var show = hasHeader && IsContentVisible();

		if ( connectorVertical.IsValid() )
			connectorVertical.SetClass( "hidden", !show );

		if ( connectorFoot.IsValid() )
			connectorFoot.SetClass( "hidden", !show );
	}

	public void SetVisibility( InspectorVisibilityAttribute[] attrs )
	{
		visibility = attrs?.Length > 0 ? attrs : null;
	}

	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );

		if ( queuedGroup is not null )
			ApplyQueuedGroup();
		else if ( needsBuild )
			BuildRows();
	}

	public override void FinalLayout( Vector2 offset )
	{
		base.FinalLayout( offset );
		AlignConnector();
	}

	void AlignConnector()
	{
		if ( !connectorVertical.IsValid() || Header?.Chevron is not { IsValid: true } chevron )
			return;

		if ( chevron.Box.Rect.Width < 1 )
			return;

		//Boxes are measured in screen pixels and styles are not, so anything read off one has to be scaled before it is written to the other.
		var scale = ScaleFromScreen;
		var chevronRect = chevron.Box.Rect;
		var groupRect = Box.Rect;

		const float lineWidth = 3f;
		var left = (chevronRect.Left - groupRect.Left) * scale
			+ MathF.Round( (chevronRect.Width * scale - lineWidth) * 0.5f );
		var top = (chevronRect.Top - groupRect.Top) * scale
			+ MathF.Round( chevronRect.Height * scale * 0.5f );

		if ( connectorLeft != left )
		{
			connectorLeft = left;
			connectorVertical.Style.Left = left;
		}

		if ( connectorTop != top )
		{
			connectorTop = top;
			connectorVertical.Style.Top = top;
		}

		//Line height set in code, since an absolute child sizes against its parent's previous layout
		var height = MathF.Max( groupRect.Height * scale - top, 0f );

		if ( connectorHeight != height )
		{
			connectorHeight = height;
			connectorVertical.Style.Height = height;
		}

		AlignFoot( top + height );
	}

	//The short bar that turns right where the line ends, so the group is closed off rather than trailing away. Flush with the line's own left, so
	//the two meet as a corner. Neither is rounded where they join - only the far end of the foot is - so there is nothing to hide by insetting it.
	void AlignFoot( float bottom )
	{
		if ( !connectorFoot.IsValid() ) return;

		const float footThickness = 3f;

		var top = bottom - footThickness;
		var left = connectorLeft;

		if ( connectorFootTop == top && connectorFootLeft == left ) return;

		connectorFootTop = top;
		connectorFootLeft = left;

		connectorFoot.Style.Left = left;
		connectorFoot.Style.Top = top;
	}

	Panel CreateConnector( string kind )
	{
		var panel = AddChild<Panel>();
		panel.AddClass( "group-connector" );
		panel.AddClass( kind );
		return panel;
	}

	public override void Tick()
	{
		base.Tick();

		if ( queuedGroup is not null )
			ApplyQueuedGroup();
		else if ( needsBuild )
			BuildRows();

		//Loops rather than LINQ. This runs every frame for every group, Any and All box the enumerator each time, and the visibility condition
		//reads the header, so it built a fresh closure and delegate to carry it in. The header is read once now instead of once per rule.
		if ( Properties is not null && !AnyShown( Properties ) )
		{
			SetClass( "hidden", true );
			return;
		}

		if ( visibility is { Length: > 0 } )
			SetClass( "hidden", HiddenByEveryRule( visibility ) );

		UpdateBodyVisibility();
	}

	static bool AnyShown( List<SerializedProperty> properties )
	{
		foreach ( var property in properties )
		{
			if ( property.ShouldShow() ) return true;
		}

		return false;
	}

	bool HiddenByEveryRule( InspectorVisibilityAttribute[] rules )
	{
		var target = Header?.ToggleProperty?.Parent;

		foreach ( var rule in rules )
		{
			if ( !rule.TestCondition( target ) ) return false;
		}

		return true;
	}
}
