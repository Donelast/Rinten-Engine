namespace Rinten.CodeUpgrader;

//A global list of rules, so we can avoid doubling up on diagnostic descriptors
static class Diagnostics
{
	public static DiagnosticDescriptor BroadcastAttribute = Warning( "RINTEN001", "Replace [Broadcast] with [Rpc.Broadcast]", "The [Broadcast] attribute has been moved to [Rpc.Broadcast] to make it more discoverable." );
	public static DiagnosticDescriptor AuthorityAttribute = Warning( "RINTEN002", "Replace [Authority] with [Rpc.Owner]", "The [Authority] attribute has been moved to [Rpc.Owner] and [Rpc.Host]." );
	public static DiagnosticDescriptor GpuBuffer = Warning( "RINTEN003", "Replace ComputeBuffer with GpuBuffer", "The ComputeBuffer class has been moved to GpuBuffer." );
	public static DiagnosticDescriptor HostSyncAttribute = Warning( "RINTEN004", "Replace [HostSync] with [Sync( SyncFlags.FromHost )]", "The [HostSync] attribute has been merged with [Sync] to make functionality expandable with SyncFlags." );
	public static DiagnosticDescriptor SyncQuery = Warning( "RINTEN005", "Replace Query with SyncFlags.Query in [Sync]", "[Sync] attributes should have SyncFlags.Query set instead of the Query property." );
	public static DiagnosticDescriptor ConCmdAttribute = Warning( "RINTEN006", "Make ConCmd Method Static", "[ConCmd] methods need to be static to function." );
	public static DiagnosticDescriptor ConVarAttribute = Warning( "RINTEN007", "Make ConVar Property Static", "[ConVar] properties need to be static to function." );
	public static DiagnosticDescriptor GenericStaticMembersUnsupported = Warning( "RINTEN008", "Add [SkipHotload] to Static Member in Generic Type", "Static members in generic types won't be processed during hotloads, so should be explicitly marked with [SkipHotload]" );

	static DiagnosticDescriptor Warning( string id, string title, string message )
	{
		return new DiagnosticDescriptor(
			id: id,
			title: title,
			messageFormat: message,
			category: "Refactoring",
			defaultSeverity: DiagnosticSeverity.Warning,
			isEnabledByDefault: true );
	}
}
