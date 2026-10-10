namespace Rinten.Twitch;

//The subset of Twitch IRC commands we act on. Anything else parses to Unknown and is ignored. The numerics keep their RFC names:
//004 is the post-registration welcome (our "connected" signal), 353 is the NAMES reply listing who's already in the channel.
internal enum IrcCommand
{
	Unknown,
	Ping,
	Notice,
	UserNotice,
	Reconnect,
	PrivMsg,
	Join,
	Part,
	RPL_004,
	RPL_353,
}
