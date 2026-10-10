using System.Threading;

namespace Rinten.Services;

public static class Auth
{
	/// <summary>Get an auth token, which can be passed to the backend</summary>
	public static async Task<string> GetToken( string serviceName, CancellationToken token = default )
	{
		var package = Application.GameIdent;
		if ( package is null ) return default;
		if ( Backend.Account is null ) return default;

		try
		{
			var tt = await Backend.Account.GetAuthToken( Api.SessionId.ToString(), package, serviceName );
			return tt?.Trim( '"', '\'', ' ' );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error getting token for {serviceName}" );
			return default;
		}
	}
}
