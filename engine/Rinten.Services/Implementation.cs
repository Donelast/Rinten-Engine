using Refit;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using static Rinten.Services.ServiceApi;

namespace Rinten;

public static class Backend
{
	public static IPackageApi Package { get; private set; }
	public static IVersionApi Version { get; private set; }
	public static IStatsApi Stats { get; private set; }
	public static ILeaderboardApi Leaderboards { get; private set; }
	public static IAccountApi Account { get; private set; }
	public static IBenchmarkApi Benchmarks { get; private set; }
	public static IAchievementApi Achievements { get; private set; }
	public static IPlayerApi Players { get; private set; }
	public static INewsApi News { get; private set; }
	public static INotificationApi Notifications { get; private set; }
	public static IStorageApi Storage { get; private set; }
	public static IUtilityApi Utility { get; private set; }

	private static HttpClient httpClient = null;

	/// <summary>Where the backend is, or null when there is none. Anything building a link to a package or an organisation asks here rather than naming a host - there is no host to name until somebody configures one.</summary>
	public static string Url { get; private set; }

	/// <summary>True once <see cref="Initialize"/> has been pointed at a backend. There is no backend by default - this engine doesn't ship with a service host - so callers have to cope with the APIs being null, the same way they already cope with being offline.</summary>
	public static bool IsConfigured => httpClient is not null;

	/// <summary>Point the service APIs at a backend. Nothing is contacted until a URL is supplied - set RINTEN_BACKEND_URL, or pass one in.</summary>
	public static void Initialize( DelegatingHandler httpHandler, string url = null )
	{
		url ??= Environment.GetEnvironmentVariable( "RINTEN_BACKEND_URL" );

		//No backend configured: leave every API null and stay offline
		if ( string.IsNullOrWhiteSpace( url ) )
			return;

		Url = url.TrimEnd( '/' );

		var refitSettings = BuildSettings( url );

		httpClient = new HttpClient( httpHandler )
		{
			BaseAddress = new Uri( url ),
			Timeout = TimeSpan.FromMinutes( 15 )
		};

		Package = RestService.For<IPackageApi>( httpClient, refitSettings );
		Version = RestService.For<IVersionApi>( httpClient, refitSettings );
		Stats = RestService.For<IStatsApi>( httpClient, refitSettings );
		Leaderboards = RestService.For<ILeaderboardApi>( httpClient, refitSettings );
		Account = RestService.For<IAccountApi>( httpClient, refitSettings );
		Benchmarks = RestService.For<IBenchmarkApi>( httpClient, refitSettings );
		Achievements = RestService.For<IAchievementApi>( httpClient, refitSettings );
		Players = RestService.For<IPlayerApi>( httpClient, refitSettings );
		News = RestService.For<INewsApi>( httpClient, refitSettings );
		Notifications = RestService.For<INotificationApi>( httpClient, refitSettings );
		Storage = RestService.For<IStorageApi>( httpClient, refitSettings );
		Utility = RestService.For<IUtilityApi>( httpClient, refitSettings );
	}

	static RefitSettings BuildSettings( string url )
	{
		return new RefitSettings
		{
			UrlParameterFormatter = new Rinten.Services.ParameterFormatter(),
			ContentSerializer = new CustomJsonContentSerializer(),
			Buffered = true
		};
	}

}

class CustomJsonContentSerializer : IHttpContentSerializer
{
	public async Task<T> FromHttpContentAsync<T>( HttpContent content, CancellationToken cancellationToken = default )
	{
		var str = await content.ReadAsStringAsync();
		if ( string.IsNullOrWhiteSpace( str ) ) return default;

		return System.Text.Json.JsonSerializer.Deserialize<T>( str, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true } );
	}

	public string GetFieldNameForProperty( PropertyInfo propertyInfo )
	{
		if ( propertyInfo is null )
			throw new ArgumentNullException( nameof( propertyInfo ) );

		return propertyInfo.GetCustomAttributes<JsonPropertyNameAttribute>( true )
				   .Select( a => a.Name )
				   .FirstOrDefault();
	}

	public HttpContent ToHttpContent<T>( T values )
	{
		var json = System.Text.Json.JsonSerializer.Serialize( values );
		var jsonBytes = Encoding.UTF8.GetBytes( json );
		var jsonStream = new MemoryStream( jsonBytes );

		var content = new ByteArrayContent( jsonBytes );
		content.Headers.ContentType = new MediaTypeHeaderValue( "application/json" );
		return content;

	}
}
