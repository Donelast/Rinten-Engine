namespace Editor.Gallery;

/// <summary>An object with one of everything on it, for a sheet to show every row it knows how to make.</summary>
public sealed class SampleModel
{
	public string StringValue { get; set; } = "Hello There";
	public uint UIntValue { get; set; } = 477;
	public int IntValue { get; set; } = -765;
	public ulong UlongValue { get; set; } = 546234623;
	public long LongValue { get; set; } = -46223512;
	public float FloatValue { get; set; } = 109.9f;
	[Range( 0, 10 )] public float FloatValueWithRange { get; set; } = 8.0f;
	public RangedFloat RangedFloat { get; set; } = 16.0f;
	public double DoubleValue { get; set; } = 78.9;
	public Transform TransformValue { get; set; } = new Transform { Position = new Vector3( 600, 400, 20 ), Scale = 1.0f, Rotation = Rotation.From( 90, 0, 0 ) };
	public Vector2 Vector2Value { get; set; }
	public Material MaterialValue { get; set; }
	public Vector3 Vector3Value { get; set; }
	public Color ColorValue { get; set; } = "#f00";
	public Vector4 Vector4Value { get; set; }
	public Rotation RotationValue { get; set; }
	public Angles AnglesValue { get; set; }
	public Margin MarginValue { get; set; } = new Margin( 10, 10, 10, 20 );
	public Rect RectValue { get; set; } = new Rect( 20, 20, 200, 100 );
	public bool BoolValue { get; set; } = true;
	public SampleEnum EnumValue { get; set; } = SampleEnum.Ireland;
	public SampleDecoratedEnum DecoratedEnumValue { get; set; } = SampleDecoratedEnum.Pose;
	public SampleFlags FlagsValue { get; set; } = SampleFlags.Bottom | SampleFlags.Top;
	public Model ModelValue { get; set; }
	public Curve CurveValue { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );
	public Gradient GradientValue { get; set; } = new Gradient( new Gradient.ColorFrame( 0.0f, Color.Cyan ), new Gradient.ColorFrame( 0.2f, Color.Red ), new Gradient.ColorFrame( 1.0f, Color.Yellow ) );
	public List<Vector3> ValueList { get; set; } = new() { Vector3.Up, Vector3.Down };
	public Dictionary<string, float> DictionaryValues { get; set; } = new() { { "garry", 6.3f }, { "helk", 4.6f } };
	public Guid Guid { get; set; } = Guid.NewGuid();
	public DateTime DateTimeValue { get; set; } = new DateTime( 2006, 11, 29, 14, 30, 0 );

	[ReadOnly] public float ReadOnlyFloatValue { get; set; } = 12.5f;
	[ReadOnly] public Vector3 ReadOnlyVectorValue { get; set; } = new Vector3( 0, 1, 2 );
	[ReadOnly] public Vector4 ReadOnlyVector4Value { get; set; } = new Vector4( 3, 2, 1, 0 );

	public Dictionary<SampleEnum, Vector3> OtherDictionaryValues { get; set; } = new() { { SampleEnum.Scotland, Vector3.Up }, { SampleEnum.Wales, Vector3.Down } };
}

/// <summary>Properties under [Group] headers, and one under a [Feature] tab.</summary>
public sealed class SampleGroupedModel
{
	[Group( "Body" )] public float Mass { get; set; } = 80;
	[Group( "Body" )] public float Height { get; set; } = 1.8f;
	[Group( "Body" )] public Color Skin { get; set; } = Color.Orange;

	[Group( "Movement" )] public float WalkSpeed { get; set; } = 150;
	[Group( "Movement" )] public float RunSpeed { get; set; } = 320;
	[Group( "Movement" )] public bool CanJump { get; set; } = true;

	public string Ungrouped { get; set; } = "Stands outside every group";

	[Feature( "Physics" )] public float Friction { get; set; } = 0.6f;
	[Feature( "Physics" )] public float Bounce { get; set; } = 0.1f;
}

/// <summary>The attributes a row reads: range, read-only, title, description, text area.</summary>
public sealed class SampleAttributesModel
{
	[Title( "A titled float" ), Description( "The description is the row's tooltip." )]
	public float Titled { get; set; } = 1.5f;

	[Range( 0, 100 )] public int Percent { get; set; } = 40;
	[Range( -1, 1 )] public float Signed { get; set; } = 0.25f;

	[ReadOnly] public string ReadOnlyText { get; set; } = "Cannot be edited";
	[ReadOnly] public Vector3 ReadOnlyVector { get; set; } = new Vector3( 1, 2, 3 );

	[TextArea] public string Paragraph { get; set; } = "A text area takes several lines,\nand grows with them.";

	public float? NullableFloat { get; set; } = 2.5f;
	public Color? NullableColor { get; set; }
}

/// <summary>Lists, dictionaries, sets and arrays, with something in each.</summary>
public sealed class SampleCollectionsModel
{
	public List<string> Names { get; set; } = new() { "garry", "helk", "layla" };
	public List<Vector3> Points { get; set; } = new() { Vector3.Up, Vector3.Forward, Vector3.Right };
	public Dictionary<string, Color> Colors { get; set; } = new() { { "sky", Color.Cyan }, { "grass", Color.Green } };
	public HashSet<SampleEnum> Countries { get; set; } = new() { SampleEnum.Ireland, SampleEnum.Wales };
	public int[] Numbers { get; set; } = [1, 1, 2, 3, 5, 8];
	public List<SampleAttributesModel> Objects { get; set; } = new() { new SampleAttributesModel(), new SampleAttributesModel { Percent = 90 } };
}

/// <summary>Static properties, for rows made from an expression rather than an object.</summary>
public static class SampleSettings
{
	[Title( "Static float" )] public static float Amount { get; set; } = 0.5f;
	[Title( "Static switch" )] public static bool Enabled { get; set; } = true;
	[Title( "Static color" )] public static Color Tint { get; set; } = Color.Magenta;
}

public enum SampleDecoratedEnum
{
	//A car you can drive
	[Icon( "🚗" )] Vehicle,

	//A robot that eats you
	[Icon( "🤖" )] Robot,

	//A car that flies
	[Icon( "✈️" )] Plane,

	//A standing person doing a weird pose or something
	[Icon( "🕺🏼" )] Pose,
}

public enum SampleEnum
{
	NotApplicable, England, Ireland, Wales, Scotland, USA, Canada, Mexico, France, Germany, Italy, Spain, Portugal,
	Netherlands, Belgium, Switzerland, Austria, Poland, Sweden, Norway, Denmark, Finland, Greece, Turkey, Russia,
	Japan, China, India, Australia, NewZealand, Brazil, Argentina, Chile, SouthAfrica, Egypt,
}

[Flags]
public enum SampleFlags
{
	None = 0,
	Top = 1 << 0,
	Bottom = 1 << 1,
	Left = 1 << 2,
	Right = 1 << 3,
}
