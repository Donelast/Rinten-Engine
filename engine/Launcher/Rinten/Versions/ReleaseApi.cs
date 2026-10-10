using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Rinten;

public sealed record EngineArtifact( string Platform, string Url, long SizeBytes, string Sha256, bool Available );

public sealed record EngineReleaseInfo(
	string Version,
	string Channel,
	DateTimeOffset PublishedAt,
	string Notes,
	EngineArtifact[] Artifacts );

public readonly record struct DownloadProgress( long BytesRead, long? TotalBytes )
{
	public double? Fraction => TotalBytes is > 0 ? (double)BytesRead / TotalBytes.Value : null;
}

//Engine builds from a GitHub repository's releases; asset names are rinten-<version>-<platform>.zip (package-release)
public sealed class ReleaseApi : IDisposable
{
	private readonly HttpClient http;

	private readonly string repository;

	public ReleaseApi( string repository )
	{
		this.repository = repository.Trim().Trim( '/' );

		http = new HttpClient { BaseAddress = new Uri( "https://api.github.com/" ) };
		http.DefaultRequestHeaders.UserAgent.ParseAdd( "Rinten-Launcher" );
		http.DefaultRequestHeaders.Add( "X-GitHub-Api-Version", "2022-11-28" );
	}

	//The platform id package-release names this machine's zip with.
	public static string CurrentPlatform
	{
		get
		{
			var arm = RuntimeInformation.OSArchitecture is Architecture.Arm64;

			if ( OperatingSystem.IsWindows() ) return "win64";
			if ( OperatingSystem.IsMacOS() ) return "osxarm64";

			return arm ? "linuxsteamrtarm64" : "linuxsteamrt64";
		}
	}

	//Newest first, pre-releases included and marked as such.
	public async Task<IReadOnlyList<EngineReleaseInfo>> GetReleasesAsync( CancellationToken cancel = default )
	{
		using var request = new HttpRequestMessage( HttpMethod.Get, $"repos/{repository}/releases?per_page=50" );
		request.Headers.Accept.ParseAdd( "application/vnd.github+json" );

		using var response = await http.SendAsync( request, cancel );
		await ThrowIfRefused( response );

		using var json = JsonDocument.Parse( await response.Content.ReadAsStreamAsync( cancel ) );

		var releases = new List<EngineReleaseInfo>();

		foreach ( var release in json.RootElement.EnumerateArray() )
		{
			if ( release.GetProperty( "draft" ).GetBoolean() ) continue;

			var pre = release.GetProperty( "prerelease" ).GetBoolean();

			var version = release.GetProperty( "tag_name" ).GetString()?.TrimStart( 'v' ) ?? "";
			var published = release.TryGetProperty( "published_at", out var at ) && at.ValueKind == JsonValueKind.String
				? at.GetDateTimeOffset()
				: default;

			releases.Add( new EngineReleaseInfo( version, pre ? "dev" : "stable", published, pre ? "pre-release" : null,
				Artifacts( release, version ) ) );
		}

		return releases;
	}

	private static EngineArtifact[] Artifacts( JsonElement release, string version )
	{
		var prefix = $"rinten-{version}-";
		var artifacts = new List<EngineArtifact>();

		foreach ( var asset in release.GetProperty( "assets" ).EnumerateArray() )
		{
			var name = asset.GetProperty( "name" ).GetString() ?? "";

			if ( !name.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) || !name.EndsWith( ".zip", StringComparison.OrdinalIgnoreCase ) )
				continue;

			//"sha256:<hex>" on assets uploaded since mid 2025, null on older ones
			var digest = asset.TryGetProperty( "digest", out var d ) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
			var sha = digest is not null && digest.StartsWith( "sha256:", StringComparison.Ordinal ) ? digest[7..] : null;

			artifacts.Add( new EngineArtifact(
				name[prefix.Length..^4],
				asset.GetProperty( "url" ).GetString(),
				asset.GetProperty( "size" ).GetInt64(),
				sha,
				asset.GetProperty( "state" ).GetString() == "uploaded" ) );
		}

		return artifacts.ToArray();
	}

	//Says what to do about a refusal rather than a status code
	private async Task ThrowIfRefused( HttpResponseMessage response )
	{
		if ( response.IsSuccessStatusCode ) return;

		if ( response.StatusCode == HttpStatusCode.NotFound )
			throw new HttpRequestException( $"{repository} is not on GitHub, or is not public" );

		if ( response.StatusCode == HttpStatusCode.Forbidden && response.Headers.TryGetValues( "x-ratelimit-remaining", out var left ) && string.Join( "", left ) == "0" )
			throw new HttpRequestException( "GitHub's hourly limit for this address is used up - try again later" );

		throw new HttpRequestException( $"GitHub answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}" );
	}

	//Downloads an artifact into targetDirectory and returns the file path; a sha256 mismatch leaves nothing behind
	public async Task<string> DownloadAsync(
		EngineArtifact artifact,
		string targetDirectory,
		IProgress<DownloadProgress> progress = null,
		CancellationToken cancel = default )
	{
		if ( artifact.Url is null )
			throw new InvalidOperationException( $"artifact for {artifact.Platform} has no url" );

		Directory.CreateDirectory( targetDirectory );

		var finalPath = Path.Combine( targetDirectory, $"rinten-{artifact.Platform}.zip" );
		var tempPath = finalPath + ".part";

		//The asset's API url with octet-stream is the file itself, private or not
		using var request = new HttpRequestMessage( HttpMethod.Get, artifact.Url );
		request.Headers.Accept.ParseAdd( "application/octet-stream" );

		using var response = await http.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancel );
		await ThrowIfRefused( response );

		var total = response.Content.Headers.ContentLength
			?? (artifact.SizeBytes > 0 ? artifact.SizeBytes : null);

		using var hasher = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
		var buffer = new byte[81920];
		long read = 0;

		await using ( var source = await response.Content.ReadAsStreamAsync( cancel ) )
		await using ( var target = File.Create( tempPath ) )
		{
			int chunk;

			while ( (chunk = await source.ReadAsync( buffer, cancel )) > 0 )
			{
				hasher.AppendData( buffer, 0, chunk );
				await target.WriteAsync( buffer.AsMemory( 0, chunk ), cancel );

				read += chunk;
				progress?.Report( new DownloadProgress( read, total ) );
			}
		}

		var hash = Convert.ToHexString( hasher.GetHashAndReset() ).ToLowerInvariant();

		if ( artifact.Sha256 is { Length: > 0 } expected
			&& !string.Equals( hash, expected, StringComparison.OrdinalIgnoreCase ) )
		{
			File.Delete( tempPath );
			throw new InvalidDataException( $"sha256 mismatch for {artifact.Platform}: expected {expected}, got {hash}" );
		}

		File.Move( tempPath, finalPath, overwrite: true );

		return finalPath;
	}

	public void Dispose()
	{
		http.Dispose();
	}
}
