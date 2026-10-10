namespace Editor.MdlEditor;

public readonly record struct ValueDebugInfo(
	AnimGraphNodeDebugValue Value,
	string ID,
	string TargetBoneID );

public static class ValueDebug
{
	private static readonly Color TrueColor = new( 50.0f / 255.0f, 205.0f / 255.0f, 50.0f / 255.0f );
	private static readonly Color FalseColor = Color.Parse( "#cd5c5c" ) ?? default;

	public static bool IsSupported( AnimGraphNodeValueType type ) => type is
		AnimGraphNodeValueType.Bool
		or AnimGraphNodeValueType.ID
		or AnimGraphNodeValueType.Float
		or AnimGraphNodeValueType.Vector
		or AnimGraphNodeValueType.Target;

	/// <summary>Number of text lines a value readout of this type occupies. Targets can show a transform over three lines; everything else is a single line.</summary>
	public static int LineCount( AnimGraphNodeValueType type ) =>
		type == AnimGraphNodeValueType.Target ? 3 : 1;

	/// <summary>A compact single-line form of the live value for showing inline (e.g. on an output pin), or null for types/values with no concise form.</summary>
	public static string ShortValue( AnimGraphNodeValueType type, ValueDebugInfo info ) => type switch
	{
		AnimGraphNodeValueType.Bool => info.Value.Bool ? "True" : "False",
		AnimGraphNodeValueType.ID => string.IsNullOrEmpty( info.ID ) ? null : info.ID,
		AnimGraphNodeValueType.Float => $"{info.Value.Float:0.###}",
		AnimGraphNodeValueType.Vector => $"{info.Value.Vector.x:0.##}, {info.Value.Vector.y:0.##}, {info.Value.Vector.z:0.##}",
		_ => null,
	};

	/// <summary>One line of the value readout, or an "N/A" placeholder when not live.</summary>
	public static (string Text, Color Color) Line( int index, AnimGraphNodeValueType type, bool hasValue, ValueDebugInfo info )
	{
		if ( !hasValue )
			return (index == 0 ? "Value: N/A" : "", Theme.Text);

		return type switch
		{
			AnimGraphNodeValueType.Bool =>
				($"Value: {(info.Value.Bool ? "True" : "False")}", info.Value.Bool ? TrueColor : FalseColor),
			AnimGraphNodeValueType.ID => ($"Value: {info.ID}", Theme.Text),
			AnimGraphNodeValueType.Float => ($"Value: {info.Value.Float:0.000}", Theme.Text),
			AnimGraphNodeValueType.Vector =>
				($"X: {info.Value.Vector.x:0.00}, Y: {info.Value.Vector.y:0.00}, Z: {info.Value.Vector.z:0.00}", Theme.Text),
			AnimGraphNodeValueType.Target => (TargetLine( index, info ), Theme.Text),
			_ => ("", Theme.Text),
		};
	}

	private static string TargetLine( int index, ValueDebugInfo info )
	{
		if ( !info.Value.IsTargetSet )
			return index == 0 ? "Not Set" : "";

		if ( info.Value.IsBoneTarget )
			return index == 0 ? $"Value: {info.TargetBoneID}" : "";

		var transform = info.Value.TargetTransform;
		var angles = transform.Rotation.Angles();

		return index switch
		{
			0 => $"Rot: X: {angles.pitch:0.000}, Y: {angles.yaw:0.000}, Z: {angles.roll:0.000}",
			1 => $"Trans: X: {transform.Position.x:0.000}, Y: {transform.Position.y:0.000}, Z: {transform.Position.z:0.000}",
			_ => $"Scl: {transform.UniformScale:0.000}",
		};
	}

	/// <summary>Height of one text readout line.</summary>
	public const float LineHeight = 20.0f;

	/// <summary>Paints every text readout line for a value, stacked top to bottom.</summary>
	public static void PaintDebugInfo( Rect rect, AnimGraphNodeValueType type, bool hasValue, ValueDebugInfo info )
	{
		Paint.ClearBrush();
		Paint.SetDefaultFont( 9.0f );

		var lineCount = LineCount( type );

		for ( var i = 0; i < lineCount; i++ )
		{
			var (text, color) = Line( i, type, hasValue, info );

			Paint.SetPen( color );
			Paint.DrawText( new Rect( rect.Left, rect.Top + LineHeight * i, rect.Width, LineHeight ), text, TextFlag.LeftCenter );
		}
	}

	/// <summary>Adds the live text readout to a node's Debug section.</summary>
	public static void BuildDebugWidgets( Rinten.UI.Panel parent, Node node )
		=> parent.AddChild( new InfoCanvas( node ) );

	private sealed class InfoCanvas( Node node )
		: LiveCanvas( LineCount( node.OutputValueType ) * LineHeight )
	{
		protected override void OnPaintContent( Rect rect )
			=> PaintDebugInfo( rect, node.OutputValueType, node.HasLiveDebugValue, node.LiveDebugValue );
	}
}
