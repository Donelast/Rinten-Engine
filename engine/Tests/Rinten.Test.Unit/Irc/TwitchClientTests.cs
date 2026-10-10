using Rinten.Twitch;

namespace IrcTests;

//Tests for the small parsing helpers on TwitchClient that turn raw Twitch tag values into engine types.
[TestClass]
[TestCategory( "Irc" )]
public class TwitchClientTest
{
	//Sub-plan tag strings map to the right tier, and anything unexpected (or null) falls back to Tier1.
	[TestMethod]
	public void ParseSubTier_MapsPlanStrings()
	{
		Assert.AreEqual( Streamer.SubscriptionTier.Prime, TwitchClient.ParseSubTier( "Prime" ) );
		Assert.AreEqual( Streamer.SubscriptionTier.Tier1, TwitchClient.ParseSubTier( "1000" ) );
		Assert.AreEqual( Streamer.SubscriptionTier.Tier2, TwitchClient.ParseSubTier( "2000" ) );
		Assert.AreEqual( Streamer.SubscriptionTier.Tier3, TwitchClient.ParseSubTier( "3000" ) );
		Assert.AreEqual( Streamer.SubscriptionTier.Tier1, TwitchClient.ParseSubTier( "something-else" ) );
		Assert.AreEqual( Streamer.SubscriptionTier.Tier1, TwitchClient.ParseSubTier( null ) );
	}
}
