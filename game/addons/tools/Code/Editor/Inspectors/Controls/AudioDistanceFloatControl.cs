namespace Editor.Inspectors;

using Rinten.Audio;
using Rinten.UI;

/// <summary>How far a sound carries, beside the everyday loudness that reaches as far.</summary>
[StyleSheet( "AudioDistanceFloatControl.cs.scss" )]
[CustomEditor( typeof( float ), WithAllAttributes = [typeof( AudioDistanceFloatAttribute )] )]
public sealed class AudioDistanceFloatControl : BaseControl
{
	Panel _presets;
	Rinten.UI.Label _presetName;
	float _shown = float.NaN;

	public override bool SupportsMultiEdit => true;

	public AudioDistanceFloatControl()
	{
		ShowBackground = false;
	}

	public override void Rebuild()
	{
		DeleteChildren();

		if ( Property is null )
			return;

		var distance = AddChild<NumberControl>();
		distance.Icon = "radar";
		distance.Color = Theme.Green;
		distance.MinValue = AudioDistancePresets.Nearest;
		distance.MaxValue = AudioDistancePresets.Loudest;
		distance.Property = Property;

		_presets = AddChild<Panel>( "presets" );
		_presets.AddEventListener( "onclick", OpenPresets );

		_presetName = _presets.AddChild<Rinten.UI.Label>( "name" );
		_presetName.Tokenize = false;

		_shown = float.NaN;
		SyncPreset();
	}

	public override void Tick()
	{
		base.Tick();

		SyncPreset();
	}

	protected override void OnReadOnlyChanged()
	{
		_presets?.SetClass( "readonly", IsReadOnly );
	}

	void SyncPreset()
	{
		if ( _presetName is null || Property is null )
			return;

		if ( Property.IsMultipleDifferentValues )
		{
			_shown = float.NaN;
			_presetName.Text = "Multiple Values";
			_presetName.Style.FontColor = Theme.MultipleValues;
			return;
		}

		var value = Property.As.Float;
		if ( value == _shown )
			return;

		_shown = value;
		_presetName.Text = AudioDistancePresets.Describe( value );
		_presetName.Style.FontColor = Color.Lerp( Theme.Green, Theme.Red, value / AudioDistancePresets.Loudest );
	}

	void OpenPresets( PanelEvent e )
	{
		if ( IsReadOnly || Property is null )
			return;

		var value = Property.As.Float;
		var menu = new Menu();

		foreach ( var preset in AudioDistancePresets.All )
		{
			var distance = preset.Distance;
			var option = menu.AddOption( AudioDistancePresets.Label( preset ), action: () => Assign( distance ) );
			option.Checkable = true;
			option.Checked = value == distance;
		}

		menu.OpenBelowLeft( _presets, 2.0f );
		e.StopPropagation();
	}

	void Assign( float distance )
	{
		if ( IsReadOnly || Property is null )
			return;

		Property.Parent?.NoteStartEdit( Property );
		Property.As.Float = distance;
		Property.Parent?.NoteFinishEdit( Property );
	}
}
