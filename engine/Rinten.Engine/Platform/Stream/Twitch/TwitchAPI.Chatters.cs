using System.Text.Json.Serialization;

namespace Rinten.Twitch;

internal partial class TwitchAPI
{
	internal class ChatterResponse
	{
		[JsonPropertyName( "user_id" )]
		public string UserId { get; set; }

		[JsonPropertyName( "user_login" )]
		public string UserLogin { get; set; }

		[JsonPropertyName( "user_name" )]
		public string UserName { get; set; }
	}

	internal class ChattersResponse
	{
		internal class Page
		{
			[JsonPropertyName( "cursor" )]
			public string Cursor { get; set; }
		}

		[JsonPropertyName( "data" )]
		public ChatterResponse[] Chatters { get; set; }

		[JsonPropertyName( "pagination" )]
		public Page Pagination { get; set; }

		[JsonPropertyName( "total" )]
		public int Total { get; set; }
	}

	/// <summary>All chatters by login -&gt; (display name, user id), paged; null if any page fails (e.g. missing scope)</summary>
	public async Task<Dictionary<string, (string DisplayName, string UserId)>> GetChatters( string userId )
	{
		var present = new Dictionary<string, (string, string)>( StringComparer.OrdinalIgnoreCase );

		string cursor = null;
		do
		{
			var request = $"/chat/chatters?broadcaster_id={userId}&moderator_id={userId}&first=1000";
			if ( !string.IsNullOrEmpty( cursor ) )
				request += $"&after={cursor}";

			var response = await Get<ChattersResponse>( request );
			if ( response?.Chatters is null )
				return null; //request failed (or a page dropped) - abandon the whole snapshot

			foreach ( var chatter in response.Chatters )
			{
				if ( string.IsNullOrEmpty( chatter.UserLogin ) )
					continue;

				present[chatter.UserLogin] = (chatter.UserName, chatter.UserId);
			}

			cursor = response.Pagination?.Cursor;
		}
		while ( !string.IsNullOrEmpty( cursor ) );

		return present;
	}
}
