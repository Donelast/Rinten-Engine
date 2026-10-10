using Rinten.Utility;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Rinten;

/// <summary>Lets your game make async HTTP requests.</summary>
public static partial class Http
{
	internal const string UserAgent = "rinten"; //todo: add version?
	internal const string Referrer = ""; //no backend to refer to

	private static readonly HttpClient Client;

	[System.Diagnostics.CodeAnalysis.SuppressMessage( "Reliability", "CA2000:Dispose objects before losing scope", Justification = "HttpClient will dispose handlers." )]
	static Http()
	{
		var socketHttpHandler = new SocketsHttpHandler
		{
			PooledConnectionLifetime = TimeSpan.FromMinutes( 2 ),
			//Must be false — SocketsHttpHandler bypasses DelegatingHandler on redirects, allowing SSRF.
			AllowAutoRedirect = false,
			//Opens another connection if the server's stream limit is below what we're asking for.
			EnableMultipleHttp2Connections = true,
		};

		//Gives us 1 http client per game, so cookies don't persist etc.
		Client = new HttpClient( new RintenHttpHandler( socketHttpHandler ) );
		Client.Timeout = TimeSpan.FromMinutes( 120 );

		//h2 so parallel requests share connections, ALPN falls back to 1.1. Without this HttpRequestMessage defaults to 1.1,
		//which never offers h2 in ALPN, so something fetching many resources at once opens a TCP+TLS connection per request.
		Client.DefaultRequestVersion = HttpVersion.Version20;
		Client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
	}

	/// <summary>Allows any http/https/ws/wss URI; local and private addresses are no longer blocked</summary>
	public static bool IsAllowed( Uri uri ) =>
		uri.Scheme is "http" or "https" or "wss" or "ws";

	internal static Task<bool> IsAllowedAsync( Uri uri ) => Task.FromResult( IsAllowed( uri ) );

	//https://developer.mozilla.org/en-US/docs/Glossary/Forbidden_header_name
	private static readonly HashSet<string> ForbiddenHeaders = new( StringComparer.InvariantCultureIgnoreCase )
	{
		"Accept-Charset",
		"Accept-Encoding",
		"Access-Control-Request-Headers",
		"Access-Control-Request-Method",
		"Connection",
		"Content-Length",
		//"Cookie", // cookies are necessary for us "Cookie2",
		"Date",
		"DNT",
		"Expect",
		"Feature-Policy",
		"Host",
		"Keep-Alive",
		"Origin", //we should set this (preferably with a way to identify the gamemode)
		"Referer",
		"TE",
		"Trailer",
		"Transfer-Encoding",
		"Upgrade",
		"Via",
		"User-Agent", //not forbidden officially but we'll be setting this to something Rinten specific
	};

	/// <summary>Checks if a given header is allowed to be set.</summary>
	public static bool IsHeaderAllowed( string header )
	{
		return !string.IsNullOrWhiteSpace( header ) &&
			   !ForbiddenHeaders.Contains( header ) &&
			   !header.StartsWith( "Proxy-", StringComparison.InvariantCultureIgnoreCase ) &&
			   !header.StartsWith( "Sec-", StringComparison.InvariantCultureIgnoreCase );
	}
}

internal sealed class RintenHttpHandler : DelegatingHandler
{
	//Match .NET's default redirect limit
	private const int MaxRedirects = 50;

	public RintenHttpHandler( HttpMessageHandler innerHandler ) : base( innerHandler ) { }

	private static async Task HandleRequestAsync( HttpRequestMessage request )
	{
		if ( !await Http.IsAllowedAsync( request.RequestUri ) )
			throw new InvalidOperationException( $"Access to '{request.RequestUri}' is not allowed." );

		request.Headers.Remove( "User-Agent" );
		request.Headers.TryAddWithoutValidation( "User-Agent", Http.UserAgent );

		request.Headers.Remove( "Referer" );
		request.Headers.TryAddWithoutValidation( "Referer", Http.Referrer );
	}

	private static bool IsRedirectStatus( HttpStatusCode status ) => status is
		HttpStatusCode.MovedPermanently or
		HttpStatusCode.Found or
		HttpStatusCode.SeeOther or
		HttpStatusCode.TemporaryRedirect or
		HttpStatusCode.PermanentRedirect;

	//Mirrors dotnet/runtime RedirectHandler.RequestRequiresForceGet.
	private static HttpMethod RedirectMethod( HttpStatusCode status, HttpMethod original )
	{
		return status switch
		{
			HttpStatusCode.MovedPermanently or HttpStatusCode.Found
				=> original == HttpMethod.Post ? HttpMethod.Get : original,
			HttpStatusCode.SeeOther
				=> (original == HttpMethod.Get || original == HttpMethod.Head) ? original : HttpMethod.Get,
			_ => original, //307/308: preserve
		};
	}

	protected override HttpResponseMessage Send( HttpRequestMessage request, CancellationToken cancellationToken )
	{
		throw new NotSupportedException( "Synchronous HTTP requests are not supported. Use async methods instead." );
	}

	protected override async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
	{
		await HandleRequestAsync( request );
		var response = await base.SendAsync( request, cancellationToken );

		for ( int i = 0; i < MaxRedirects && IsRedirectStatus( response.StatusCode ); i++ )
		{
			var location = ResolveRedirectLocation( response, request.RequestUri );
			if ( location is null ) break;
			var status = response.StatusCode;
			response.Dispose();

			ApplyRedirect( request, status, location );
			await HandleRequestAsync( request );
			response = await base.SendAsync( request, cancellationToken );
		}

		return response;
	}

	//Mutates request in-place for the redirect, matching dotnet/runtime RedirectHandler: - Always clears Authorization (credentials must
	//not follow redirects) - Coerces method to GET and clears Content for 301/302/303 POST requests - 307/308 preserve method and content
	private static void ApplyRedirect( HttpRequestMessage request, HttpStatusCode status, Uri location )
	{
		request.RequestUri = location;
		request.Headers.Authorization = null;

		var method = RedirectMethod( status, request.Method );
		if ( method != request.Method )
		{
			request.Method = method;
			request.Content = null;
			request.Headers.TransferEncodingChunked = false;
		}
	}

	//Returns null to silently stop: missing Location or https→http downgrade.
	private static Uri ResolveRedirectLocation( HttpResponseMessage response, Uri requestUri )
	{
		var location = response.Headers.Location;
		if ( location is null ) return null;

		if ( !location.IsAbsoluteUri )
			location = new Uri( requestUri, location );

		//Block https→http downgrade
		if ( requestUri.Scheme == Uri.UriSchemeHttps && location.Scheme == Uri.UriSchemeHttp )
			return null;

		//RFC 7231 §7.1.2: inherit fragment if redirect has none
		if ( !string.IsNullOrEmpty( requestUri.Fragment ) && string.IsNullOrEmpty( location.Fragment ) )
			location = new UriBuilder( location ) { Fragment = requestUri.Fragment }.Uri;

		return location;
	}
}
