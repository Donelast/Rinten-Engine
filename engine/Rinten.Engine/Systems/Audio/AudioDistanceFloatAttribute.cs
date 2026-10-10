namespace Rinten.Audio;

[AttributeUsage( AttributeTargets.Property )]
public class AudioDistanceFloatAttribute : EditorAttribute
{
	public AudioDistanceFloatAttribute()
	{
		Value = "audiodistance";
	}
}
