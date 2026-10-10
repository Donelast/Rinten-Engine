namespace Rinten;

public partial class Surface
{
	/// <summary>Play a collision sound based on this shape's surface. Can return null if sound is invalid, or too quiet to play.</summary>
	public SoundHandle PlayCollisionSound( Vector3 position, float speed = 320.0f )
	{
		float volume = speed / 1000.0f;
		if ( volume > 1.0f ) volume = 1.0f;

		//I have an inkling that this would be aweomse Scale volume by the mass of the object
		//float mass = self.Body.Mass;
		//if ( mass == 0 ) mass = 100; // static objects don't have a mass
		//float massScale = mass.Remap( 0, 1500, 0, 1 );
		//volume *= massScale;

		if ( volume < 0.001f ) return default;

		var sound = SoundCollection.ImpactHard;

		if ( speed < 130f || sound == null )
		{
			sound = SoundCollection.ImpactSoft;
		}

		if ( sound == null )
			return default;

		var s = Sound.Play( sound, position );
		if ( s is not null )
		{
			s.Volume *= volume;
		}
		return s;
	}
}
