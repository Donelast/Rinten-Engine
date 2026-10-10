namespace Editor.MeshEditor;

using Editor.Inspectors;
using Rinten.UI;

//Which faces of a box get built, laid out as the box unfolded. A flags dropdown would list six names that mean nothing without the axis
//they point down. Unfolded, the cross says which face is which by where it is, and the axis is on the tooltip for when it still doesn't.
[StyleSheet( "BoxSidesControl.cs.scss" )]
[CustomEditor( typeof( BlockPrimitive.Side ), WithAllAttributes = [typeof( BoxSidesAttribute )] )]
internal sealed class BoxSidesControl : BaseControl
{
	static readonly (BlockPrimitive.Side Side, int Column, int Row, string Axis)[] Layout =
	[
		//By the letters, which moved with everything else - see BlockPrimitive.Side. Up is
		//+Y and forward is +Z now, and these read +Y on the top button and -Z on a side one.
		(BlockPrimitive.Side.Top, 2, 0, "+Y"),
		(BlockPrimitive.Side.Back, 0, 1, "-Z"),
		(BlockPrimitive.Side.Left, 1, 1, "-X"),
		(BlockPrimitive.Side.Front, 2, 1, "+Z"),
		(BlockPrimitive.Side.Right, 3, 1, "+X"),
		(BlockPrimitive.Side.Bottom, 2, 2, "-Y")
	];

	readonly List<(Panel Face, BlockPrimitive.Side Side)> _faces = new();

	public override bool IsWideMode => true;

	public BoxSidesControl()
	{
		ShowBackground = false;

		//A control is one row tall unless it says otherwise, and this cross is three. Held to that row and
		//centred in it, the top face sat on the group's title and the bottom face on the row under the cross.
		MultipleRows = true;

		var grid = AddChild<Panel>( "grid" );

		for ( var row = 0; row < 3; row++ )
		{
			var line = grid.AddChild<Panel>( "row" );

			for ( var column = 0; column < 4; column++ )
			{
				var cell = Layout.FirstOrDefault( x => x.Column == column && x.Row == row );

				if ( cell.Axis is null )
				{
					line.AddChild<Panel>( "gap" );
					continue;
				}

				var side = cell.Side;
				var face = line.AddChild<Panel>( "face" );

				face.AddChild<Rinten.UI.Label>( "name" ).Text = side.ToString();
				face.Tooltip = $"{side} ({cell.Axis})";
				face.AddEventListener( "onclick", () => Toggle( side ) );

				_faces.Add( (face, side) );
			}
		}
	}

	public override void Rebuild() => SyncFaces();

	public override void Tick()
	{
		base.Tick();

		SyncFaces();
	}

	protected override void OnReadOnlyChanged() => SyncFaces();

	void Toggle( BlockPrimitive.Side side )
	{
		if ( IsReadOnly || Property is null ) return;

		var value = Property.GetValue<BlockPrimitive.Side>() ^ side;

		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue( value );
		Property.Parent?.NoteFinishEdit( Property );

		SyncFaces();
	}

	void SyncFaces()
	{
		if ( Property is null ) return;

		var value = Property.GetValue<BlockPrimitive.Side>();

		foreach ( var (face, side) in _faces )
		{
			face.SetClass( "selected", (value & side) != 0 );
			face.SetClass( "readonly", IsReadOnly );
		}
	}
}
