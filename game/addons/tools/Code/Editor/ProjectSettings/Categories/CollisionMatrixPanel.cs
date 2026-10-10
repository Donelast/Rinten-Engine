using Rinten.Physics;

namespace Editor.ProjectSettingPages.Categories;

[StyleSheet( "CollisionMatrixPanel.cs.scss" )]
sealed class CollisionMatrixPanel : Panel
{
	public Action ValueChanged { get; set; }

	internal CollisionRules Rules { get; private set; }

	readonly Panel grid;
	readonly Rinten.UI.Label tooltip;
	string[] tags = [];
	internal string rowHovered;
	internal string colHovered;

	public CollisionMatrixPanel()
	{
		AddClass( "collision-matrix" );
		grid = AddChild<Panel>( "grid" );
		tooltip = AddChild<Rinten.UI.Label>( "tooltip" );
		tooltip.AddClass( "matrix-tooltip" );
	}

	public void Rebuild( CollisionRules from )
	{
		Rules = from;
		grid.DeleteChildren();
		tags = [];

		var tagBuilder = new List<string> { "solid", "trigger" };
		tagBuilder.AddRange( from.Defaults.Select( x => x.Key ) );
		tags = tagBuilder.Distinct().ToArray();

		var header = grid.AddChild<Panel>( "header-row" );
		header.AddClass( "matrix-row" );
		header.AddClass( "matrix-header-row" );
		header.AddChild<Panel>( "corner" ).AddClass( "matrix-corner" );
		header.AddChild<Panel>( "spacer" ).AddClass( "matrix-spacer" );

		var defaultHeader = header.AddChild<MatrixLabelPanel>( "default-header" );
		defaultHeader.IsColumn = true;
		defaultHeader.Text = "default";
		defaultHeader.HoverColumn = "~tagdefault~";
		defaultHeader.Matrix = this;

		for ( var colIndex = tags.Length - 1; colIndex >= 0; colIndex-- )
		{
			var colHeader = header.AddChild<MatrixLabelPanel>( "col-header" );
			colHeader.IsColumn = true;
			colHeader.Text = tags[colIndex];
			colHeader.HoverColumn = tags[colIndex];
			colHeader.Matrix = this;
		}

		for ( var rowIndex = 0; rowIndex < tags.Length; rowIndex++ )
		{
			var rowTag = tags[rowIndex];
			var row = grid.AddChild<Panel>( "row" );
			row.AddClass( "matrix-row" );

			var label = row.AddChild<MatrixLabelPanel>( "label" );
			label.Text = rowTag;
			label.HoverRow = rowTag;
			label.Matrix = this;
			label.CanDelete = true;

			row.AddChild<Panel>( "spacer" ).AddClass( "matrix-spacer" );
			row.AddChild( new CollisionMatrixCellPanel( this, rowTag, null ) );

			for ( var colIndex = tags.Length - 1; colIndex >= 0; colIndex-- )
			{
				if ( rowIndex > colIndex )
				{
					row.AddChild<Panel>( "gap" ).AddClass( "matrix-gap" );
					continue;
				}

				row.AddChild( new CollisionMatrixCellPanel( this, rowTag, tags[colIndex] ) );
			}
		}

		var footer = grid.AddChild<Panel>( "footer-row" );
		footer.AddClass( "matrix-row" );
		footer.AddChild<Panel>( "corner" ).AddClass( "matrix-corner" );
		footer.AddChild<Panel>( "spacer" ).AddClass( "matrix-spacer" );
		footer.AddChild( new MatrixAddTagPanel( this ) );
	}

	internal void SetHover( string row, string col )
	{
		rowHovered = row;
		colHovered = col;
		UpdateTooltip();
	}

	void UpdateTooltip()
	{
		if ( rowHovered is null || colHovered is null )
		{
			tooltip.Text = "";
			return;
		}

		var def = Rules.Defaults.FirstOrDefault( x => x.Key == rowHovered );
		var pair = FindPair( rowHovered, colHovered );
		var text = $"{rowHovered} vs {colHovered} = {pair.ToString().ToLower()} ";

		if ( colHovered == "~tagdefault~" )
		{
			text = def.Value != CollisionRules.Result.Unset
				? $"{rowHovered} defaults to {def.Value.ToString().ToLower()}"
				: $"{rowHovered} has no default";
		}
		else if ( pair == CollisionRules.Result.Unset )
		{
			text = "";
		}

		tooltip.Text = text;
	}

	public CollisionRules.Result FindPair( string left, string right )
	{
		if ( Rules is null || left is null || right is null )
			return CollisionRules.Result.Unset;

		return Rules.Pairs.GetValueOrDefault( (left, right) );
	}

	public void SetPair( string left, string right, CollisionRules.Result rule )
	{
		if ( right == null )
			Rules.Defaults[left] = rule;
		else
			Rules.Pairs[(left, right)] = rule;

		Rebuild( Rules );
		ValueChanged?.Invoke();
	}

	internal void DeleteTag( string layer )
	{
		var toRemove = Rules.Pairs.Keys.Where( x => x.Contains( layer ) ).ToArray();
		foreach ( var pair in toRemove )
			Rules.Pairs.Remove( pair );

		Rules.Defaults.Remove( layer );
		Rebuild( Rules );
		ValueChanged?.Invoke();
	}

	void AddTag( string layer )
	{
		if ( string.IsNullOrWhiteSpace( layer ) ) return;

		layer = layer.ToLower();
		if ( !Rules.Defaults.TryAdd( layer, CollisionRules.Result.Unset ) ) return;

		Rebuild( Rules );
		ValueChanged?.Invoke();
	}

	internal void PromptAddTag()
	{
		Dialog.AskString( AddTag, "New tag name:", okay: "Add", title: "Add Collision Tag" );
	}
}

sealed class MatrixLabelPanel : Panel
{
	public CollisionMatrixPanel Matrix;
	public string HoverRow;
	public string HoverColumn;
	public bool CanDelete;
	public bool IsColumn;

	Rinten.UI.Label label;

	public string Text
	{
		get => label?.Text ?? "";
		set
		{
			label ??= AddChild<Rinten.UI.Label>();
			label.Text = value;
		}
	}

	public MatrixLabelPanel()
	{
		AddClass( "matrix-label" );
	}

	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );
		SetClass( "matrix-label", !IsColumn );
		SetClass( "matrix-col-label", IsColumn );
	}

	public override void Tick()
	{
		base.Tick();
		var highlight = HoverRow != null && Matrix?.rowHovered == HoverRow
			|| HoverColumn != null && Matrix?.colHovered == HoverColumn;
		SetClass( "highlighted", highlight );
	}

	protected override void OnMouseOver( MousePanelEvent e )
	{
		base.OnMouseOver( e );
		Matrix?.SetHover( HoverRow, HoverColumn );
	}

	protected override void OnMouseOut( MousePanelEvent e )
	{
		base.OnMouseOut( e );
		Matrix?.SetHover( null, null );
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );
		if ( !CanDelete || HoverRow is null ) return;
		Matrix?.DeleteTag( HoverRow );
	}
}

sealed class MatrixAddTagPanel : Panel
{
	readonly CollisionMatrixPanel matrix;

	public MatrixAddTagPanel( CollisionMatrixPanel matrix )
	{
		this.matrix = matrix;
		AddClass( "matrix-add" );
		Tooltip = "Add Tag";
		var label = AddChild<Rinten.UI.Label>();
		label.Text = "+";
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );
		matrix?.PromptAddTag();
	}
}

sealed class CollisionMatrixCellPanel : Panel
{
	readonly CollisionMatrixPanel matrix;
	readonly string left;
	readonly string right;
	readonly Rinten.UI.Label icon;

	public CollisionMatrixCellPanel( CollisionMatrixPanel matrix, string left, string right )
	{
		this.matrix = matrix;
		this.left = left;
		this.right = right;
		AddClass( "matrix-cell" );
		icon = AddChild<Rinten.UI.Label>( "icon" );
		icon.AddClass( "material-icon" );
		icon.AddClass( "matrix-cell-icon" );
	}

	CollisionRules.Result CurrentValue
	{
		get
		{
			if ( right != null )
				return matrix.FindPair( left, right );

			return matrix.Rules.Defaults.TryGetValue( left, out var rule ) ? rule : CollisionRules.Result.Unset;
		}
	}

	public override void Tick()
	{
		base.Tick();
		SetClass( "unset", CurrentValue == CollisionRules.Result.Unset );
		SetClass( "ignore", CurrentValue == CollisionRules.Result.Ignore );
		SetClass( "trigger", CurrentValue == CollisionRules.Result.Trigger );
		SetClass( "collide", CurrentValue == CollisionRules.Result.Collide );

		icon.Text = CurrentValue switch
		{
			CollisionRules.Result.Collide => "check",
			CollisionRules.Result.Trigger => "remove",
			CollisionRules.Result.Ignore => "clear",
			_ => ""
		};
	}

	protected override void OnMouseOver( MousePanelEvent e )
	{
		base.OnMouseOver( e );
		matrix?.SetHover( left, right ?? "~tagdefault~" );
	}

	protected override void OnMouseOut( MousePanelEvent e )
	{
		base.OnMouseOut( e );
		matrix?.SetHover( null, null );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );
		ApplyClick( e, false );
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );
		ApplyClick( e, true );
	}

	void ApplyClick( MousePanelEvent e, bool rightClick )
	{
		var rule = CurrentValue;

		if ( rightClick )
		{
			rule = 0;
		}
		else
		{
			if ( rule >= CollisionRules.Result.Ignore ) rule = 0;
			else rule++;
		}

		if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) ) rule = CollisionRules.Result.Collide;
		if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) ) rule = CollisionRules.Result.Ignore;
		if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Alt ) ) rule = CollisionRules.Result.Trigger;
		if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Alt ) && Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) ) rule = CollisionRules.Result.Unset;

		matrix?.SetPair( left, right, rule );
	}
}
