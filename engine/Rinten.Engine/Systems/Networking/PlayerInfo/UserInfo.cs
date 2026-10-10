namespace Rinten;

[Expose]
internal struct UserInfo
{
	public SteamId SteamId { get; set; }
	public string Name { get; set; }
	public DateTimeOffset ConnectionTime { get; set; }
	public Dictionary<string, string> UserData { get; set; }
	public int EngineVersion { get; internal set; }
	public Guid HandshakeId { get; set; }
	public bool IsVr { get; set; }

	//Build info for the local user, which will then get sent to the server and possibly shared between all clients
	internal static UserInfo Local
	{
		get
		{
			var ui = new UserInfo
			{
				ConnectionTime = DateTime.UtcNow,
				SteamId = Utility.LocalUser.SteamId,
				Name = Utility.LocalUser.PersonaName,
				EngineVersion = Engine.Protocol.Network,
				UserData = new(),
				IsVr = VR.VRSystem.IsActive
			};

			//Put all the userinfo vars in
			foreach ( var convar in ConVarSystem.Members.Values.Where( x => x.IsUserInfo ) )
			{
				ui.UserData[convar.Name] = convar.Value;
			}

			return ui;
		}
	}
}
