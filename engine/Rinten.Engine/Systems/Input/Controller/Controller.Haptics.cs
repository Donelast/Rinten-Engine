namespace Rinten;

partial class Controller
{
	internal HapticEffect ActiveHapticEffect = new();

	/// <summary>Stop all vibration events on this controller.</summary>
	public void StopAllVibrations()
	{
		StopAllHaptics();
	}

	internal void UpdateHaptics()
	{
		ActiveHapticEffect?.Update( this );
	}

	/// <summary>Trigger a vibration based on a predefined <see cref="HapticPattern"/>. All <see cref="HapticPattern"/>s are normalized (start at 0, peak at 1).</summary>
	public void TriggerHapticEffect( HapticEffect effect, float lengthScale = 1.0f, float frequencyScale = 1.0f, float amplitudeScale = 1.0f )
	{
		ActiveHapticEffect = effect;
		ActiveHapticEffect.Reset();

		ActiveHapticEffect.FrequencyScale = frequencyScale;
		ActiveHapticEffect.AmplitudeScale = amplitudeScale;
		ActiveHapticEffect.LengthScale = lengthScale;
	}
}
