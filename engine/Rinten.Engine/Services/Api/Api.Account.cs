using NativeEngine;
using Refit;
using Rinten.Engine;
using Rinten.Services;
using System.Diagnostics;

namespace Rinten;

internal static partial class Api
{
	internal static bool IsConnected { get; private set; } = true;

	internal static void StartOffline()
	{
		IsConnected = false;
	}

	//Get linked service credentials, ie "twitch". Under the hood, on our server, we will probably
	//be renewing the token with the service (assume the token is only good for 2-3 hours).
	internal static async Task<LoginResult> GetAccountInformation()
	{
		//There is no account to sign in to, so the front end runs signed out.
		return default;
	}

}
