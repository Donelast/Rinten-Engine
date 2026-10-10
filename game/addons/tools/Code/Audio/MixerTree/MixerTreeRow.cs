using Rinten.Audio;

namespace Editor.Audio;

//The mixer's row: the editor's one tree row, with the mixer's level along the foot of its glyph and how many voices it is playing at the
//far end. Both move while sound plays, so this row looks at its mixer every frame - the name, the arrow and the rest are the shared row's.
[StyleSheet( "MixerTreeRow.cs.scss" )]
sealed class MixerTreeRow : TreeRowPanel<Mixer>
{
	readonly Panel volumeFill;
	readonly Rinten.UI.Label voices;

	int voiceCount = -1;
	int fillClass;
	bool active;
	bool isDefault;

	public MixerTreeRow( TreePanel<Mixer> tree, Mixer node, TreeItemInfo info, int depth ) : base( tree, node, info, depth, 0 )
	{
		AddClass( "mixer-row" );

		volumeFill = IconSlot.AddChild<Panel>( "volume-fill hidden" );

		voices = Trailing.AddChild<Rinten.UI.Label>( "voices hidden" );
		Trailing.SetChildIndex( voices, 0 );

		SyncMeter();
	}

	//The meter moves with the sound, not with anything the tree is told.
	protected override bool TickWhenAsked => false;

	public override void Tick()
	{
		base.Tick();

		SyncMeter();
	}

	void SyncMeter()
	{
		var mixer = Node;
		var meter = mixer.Meter.Current;

		var nowActive = meter.VoiceCount > 0;
		var nowDefault = Mixer.Default == mixer;

		if ( nowActive != active || nowDefault != isDefault )
		{
			active = nowActive;
			isDefault = nowDefault;
			SetClass( "active", active );
			SetClass( "default", isDefault );
		}

		var count = nowActive ? meter.VoiceCount : 0;
		if ( count != voiceCount )
		{
			voiceCount = count;
			voices.Text = $"{count:n0}";
			voices.SetClass( "hidden", count == 0 );
		}

		var bucket = (int)MathF.Ceiling( meter.MaxLevel.Clamp( 0, 1 ) * 20 );
		if ( bucket == fillClass ) return;

		if ( fillClass > 0 ) volumeFill.RemoveClass( $"fill-{fillClass}" );

		fillClass = bucket;

		if ( fillClass > 0 ) volumeFill.AddClass( $"fill-{fillClass}" );
		volumeFill.SetClass( "hidden", fillClass == 0 );
	}
}
