namespace Editor;

//No sheet of its own. What a button looks like is one thing for the whole editor and it is written once, in design.scss, under .btn - green for yes, red for no.
/// <summary>A button with words on it: the brick a page or a dialog is pressed by.</summary>
public sealed class TextButtonPanel : Panel
{
	readonly Rinten.UI.Label icon;
	readonly Rinten.UI.Label label;

	/// <summary>What pressing it does.</summary>
	public Action Pressed { get; set; }

	public TextButtonPanel()
	{
		AddClass( "btn" );
		AddClass( "row" );
		AddClass( "gap-2" );

		icon = AddChild<Rinten.UI.Label>( "icon" );
		icon.AddClass( "material-icon" );
		icon.SetClass( "hidden", true );

		label = AddChild<Rinten.UI.Label>( "text" );

		AddEventListener( "onclick", _ =>
		{
			if ( Disabled ) return;

			Pressed?.Invoke();
		} );
	}

	public string Text
	{
		get => label.Text;
		set => label.Text = value;
	}

	/// <summary>A font icon before the words, by name. Nothing shows where there is none.</summary>
	public string Icon
	{
		get => icon.Text;
		set
		{
			icon.Text = value ?? "";
			icon.SetClass( "hidden", string.IsNullOrEmpty( value ) );
		}
	}

	/// <summary>Greyed out, and deaf to the pointer while it is.</summary>
	public bool Disabled
	{
		get => HasClass( "is-disabled" );
		set => SetClass( "is-disabled", value );
	}

	//A tool's Apply turns on and off as the selection changes underneath it, and nothing tells the button when that happened - it has to look.
	/// <summary>When it can be pressed, asked again each frame. Always, where nothing was said.</summary>
	public Func<bool> EnabledWhen { get; set; }

	public override void Tick()
	{
		base.Tick();

		if ( EnabledWhen is null ) return;

		Disabled = !EnabledWhen();
	}
}
