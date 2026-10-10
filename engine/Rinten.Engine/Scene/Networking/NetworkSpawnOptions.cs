namespace Rinten;

/// <summary>Configurable options when spawning a networked object.</summary>
public struct NetworkSpawnOptions()
{
	/// <summary>The default network spawn options.</summary>
	public static readonly NetworkSpawnOptions Default = new();

	public NetworkOrphaned? OrphanedMode { get; set; }

	public OwnerTransfer? OwnerTransfer { get; set; }

	public NetworkFlags? Flags { get; set; }

	public bool? AlwaysTransmit { get; set; }

	/// <summary>Should this networked object start enabled?</summary>
	public bool StartEnabled { get; set; } = true;

	/// <summary>Who should be the owner of this networked object?</summary>
	public Connection Owner { get; set; }
}
