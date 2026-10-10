namespace Rinten;

//A User Command that will be sent to the current host every tick.
internal struct UserCommand( uint commandNumber )
{
	private static uint s_nextAvailableCommandNumber = 1;

	/// <summary>The command number of this <see cref="UserCommand"/>.</summary>
	public uint CommandNumber { get; private set; } = commandNumber;

	/// <summary>Which actions are currently being held down.</summary>
	public ulong Actions;

	//Serialize this UserCommand to the specified ByteStream.
	internal void Serialize( ref ByteStream bs )
	{
		bs.Write( CommandNumber );
		bs.Write( Actions );
	}

	//Deserialize this UserCommand from specified ByteStream.
	internal void Deserialize( ref ByteStream bs )
	{
		CommandNumber = bs.Read<uint>();
		Actions = bs.Read<ulong>();
	}

	//Reset the next available command number back to zero.
	internal static void Reset()
	{
		s_nextAvailableCommandNumber = 1;
	}

	/// <summary>Create a new <see cref="UserCommand"/> with the next available command number.</summary>
	public static UserCommand Create()
	{
		return new UserCommand( s_nextAvailableCommandNumber++ );
	}
}
