namespace Rinten.Mounting;

public ref struct InitializeContext
{
	internal InitializeContext( MountHost system )
	{
	}

	//progress output, error recording etc
	public void AddError( string v )
	{
		//TODO
	}

	/// <summary>Return true if this app is installed. Always false: which games are installed was Steam's to say, and nothing here asks Steam anything.</summary>
	public bool IsAppInstalled( long appid ) => false;

	/// <summary>Return true if this DLC is installed. Always false, for the same reason.</summary>
	public bool IsDlcInstalled( long appid ) => false;

	/// <summary>The folder an installed app is in. Nothing is known to be installed, so nowhere.</summary>
	public string GetAppDirectory( long appid ) => null;
}
