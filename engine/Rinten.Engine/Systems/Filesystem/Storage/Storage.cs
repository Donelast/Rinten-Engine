namespace Rinten;

public static partial class Storage
{
	/// <summary>Create a new storage entry of the given type.</summary>
	public static Entry CreateEntry( string type )
	{
		return new Entry( type );
	}

	//This is what is saved to the .meta file
	internal class StorageMeta
	{
		public string Id { get; set; }
		public string Type { get; set; }
		public DateTimeOffset Timestamp { get; set; }
		public Dictionary<string, string> Meta { get; set; }
	}
}

