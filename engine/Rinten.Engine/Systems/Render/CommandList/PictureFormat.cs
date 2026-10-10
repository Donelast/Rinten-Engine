namespace Rinten.Rendering;

/// <summary>What one of the frame's pictures holds - see <see cref="CommandList.GetPicture(string, PictureFormat, int, int)"/>.</summary>
public enum PictureFormat
{
	/// <summary>Light past white: four half floats.</summary>
	Hdr = 0,

	/// <summary>Light past white in thirty-two bits, where the device can store into that - no alpha. Half the traffic of <see cref="Hdr"/>, which is what a blur of the frame at a smaller size wants. A compute pass declares an output of it as <c>Format( "compact" )</c>.</summary>
	HdrCompact = 1,

	/// <summary>Eight bits a channel: a mask, a colour between nought and one.</summary>
	Colour = 2,

	/// <summary>One half float.</summary>
	Single = 3,

	/// <summary>Two half floats: a direction, a movement.</summary>
	Pair = 4,

	/// <summary>Four whole floats.</summary>
	Full = 5,

	/// <summary>One whole float: a distance.</summary>
	SingleFull = 6,

	/// <summary>Ten bits a colour channel, two of alpha.</summary>
	Ten = 7,
}
