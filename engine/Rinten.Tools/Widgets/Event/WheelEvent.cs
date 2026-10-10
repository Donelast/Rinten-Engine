namespace Editor;

/// <summary>Information about a mouse wheel scroll event of a <see cref="Widget"/>.</summary>
public ref struct WheelEvent
{
	private readonly float delta;
	private readonly Vector2 position;

	private bool accepted;

	//One raised by a panel. The delta is in the notches a wheel clicks through, as angle delta is.
	internal WheelEvent( float delta, Vector2 position, KeyboardModifiers modifiers )
	{
		this.delta = delta;
		this.position = position;

		KeyboardModifiers = modifiers;
	}

	public readonly float Delta
	{
		get
		{
			return delta;
		}
	}

	public readonly Vector2 Position => position;
	public readonly Vector2 GlobalPosition => position;

	public bool Accepted
	{
		readonly get => accepted;
		set
		{
			accepted = value;
		}
	}

	public void Accept()
	{
		accepted = true;
	}

	public void Ignore()
	{
		accepted = false;
	}

	public KeyboardModifiers KeyboardModifiers { get; set; }

	public readonly bool HasShift => KeyboardModifiers.Contains( KeyboardModifiers.Shift );

	public readonly bool HasCtrl => KeyboardModifiers.Contains( KeyboardModifiers.Ctrl );

	public readonly bool HasAlt => KeyboardModifiers.Contains( KeyboardModifiers.Alt );
}

