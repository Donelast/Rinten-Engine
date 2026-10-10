using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Rinten;

public static partial class Http
{
	/// <summary>Send a HTTP request to the specified URI and return the response body as a string in an asynchronous operation.</summary>
	public static async Task<string> RequestStringAsync( string requestUri, string method = "GET", HttpContent content = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default )
	{
		using var response = await RequestAsync( requestUri, method, content, headers: headers, cancellationToken: cancellationToken );
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadAsStringAsync( cancellationToken );
	}

	/// <summary>Send a HTTP request to the specified URI and return the response body as a byte array in an asynchronous operation.</summary>
	public static async Task<byte[]> RequestBytesAsync( string requestUri, string method = "GET", HttpContent content = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default )
	{
		using var response = await RequestAsync( requestUri, method, content, headers: headers, cancellationToken: cancellationToken );
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadAsByteArrayAsync( cancellationToken );
	}

	/// <summary>Returns the response body stream once headers arrive; dispose it, and use the token as a timeout</summary>
	public static async Task<System.IO.Stream> RequestStreamAsync( string requestUri, string method = "GET", HttpContent content = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default )
	{
		if ( string.IsNullOrWhiteSpace( method ) )
		{
			throw new ArgumentNullException( nameof( method ) );
		}

		using var request = CreateRequest( new HttpMethod( method ), requestUri, headers );
		request.Content = content;

		//ResponseHeadersRead so the task resolves on headers; validation still runs in RintenHttpHandler
		var response = await Client.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancellationToken );

		try
		{
			response.EnsureSuccessStatusCode();
			var stream = await response.Content.ReadAsStreamAsync( cancellationToken );

			//The response owns the connection the stream reads from, so it has to outlive this method.
			//(The old code disposed it here and only got away with it because the body was buffered.)
			return new ResponseStream( stream, response );
		}
		catch
		{
			response.Dispose();
			throw;
		}
	}

	//A response body stream that disposes its HttpResponseMessage with itself, so disposing the stream releases the connection. Everything else delegates.
	private sealed class ResponseStream : System.IO.Stream
	{
		private readonly System.IO.Stream inner;
		private readonly HttpResponseMessage response;

		public ResponseStream( System.IO.Stream inner, HttpResponseMessage response )
		{
			this.inner = inner;
			this.response = response;
		}

		public override bool CanRead => inner.CanRead;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override int Read( byte[] buffer, int offset, int count ) => inner.Read( buffer, offset, count );
		public override int Read( Span<byte> buffer ) => inner.Read( buffer );
		public override int ReadByte() => inner.ReadByte();

		public override Task<int> ReadAsync( byte[] buffer, int offset, int count, CancellationToken cancellationToken )
			=> inner.ReadAsync( buffer, offset, count, cancellationToken );
		public override ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default )
			=> inner.ReadAsync( buffer, cancellationToken );

		public override void CopyTo( System.IO.Stream destination, int bufferSize ) => inner.CopyTo( destination, bufferSize );
		public override Task CopyToAsync( System.IO.Stream destination, int bufferSize, CancellationToken cancellationToken )
			=> inner.CopyToAsync( destination, bufferSize, cancellationToken );

		public override void Flush() => inner.Flush();
		public override Task FlushAsync( CancellationToken cancellationToken ) => inner.FlushAsync( cancellationToken );

		public override long Seek( long offset, System.IO.SeekOrigin origin ) => throw new NotSupportedException();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();

		protected override void Dispose( bool disposing )
		{
			if ( disposing )
			{
				inner.Dispose();
				response.Dispose();
			}

			base.Dispose( disposing );
		}

		public override async ValueTask DisposeAsync()
		{
			await inner.DisposeAsync();
			response.Dispose();
			GC.SuppressFinalize( this );
		}
	}

	/// <summary>Sends a HTTP request to the specified URI and return the response body as a JSON deserialized object in an asynchronous operation.</summary>
	public static async Task<T> RequestJsonAsync<T>( string requestUri, string method = "GET", HttpContent content = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default )
	{
		using var response = await RequestAsync( requestUri, method, content, headers: headers, cancellationToken: cancellationToken );
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadFromJsonAsync<T>( cancellationToken: cancellationToken );
	}

	/// <summary>Sends a HTTP request to the specified URI and returns the response in an asynchronous operation.</summary>
	public static async Task<HttpResponseMessage> RequestAsync( string requestUri, string method = "GET", HttpContent content = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default )
	{
		if ( string.IsNullOrWhiteSpace( method ) )
		{
			throw new ArgumentNullException( nameof( method ) );
		}

		using var request = CreateRequest( new HttpMethod( method ), requestUri, headers );
		request.Content = content;
		return await Client.SendAsync( request, cancellationToken );
	}

	/// <summary>Creates a new <see cref="HttpContent"/> instance containing the specified object serialized to JSON.</summary>
	public static HttpContent CreateJsonContent<T>( T target )
	{
		return JsonContent.Create( target, new MediaTypeHeaderValue( "application/json" ) );
	}

	internal static HttpRequestMessage CreateRequest( HttpMethod method, string requestUri, Dictionary<string, string> headers )
	{
		var uri = new Uri( requestUri, UriKind.Absolute );

		//Note: IsAllowed is enforced by RintenHttpHandler.HandleRequestAsync before every async send
		//(including redirects). Synchronous sends are explicitly unsupported and throw NotSupportedException.

		var request = new HttpRequestMessage( method, uri );
		if ( headers != null )
		{
			foreach ( var (key, value) in headers )
			{
				if ( !IsHeaderAllowed( key ) )
				{
					throw new InvalidOperationException( $"Not allowed to set header '{key}'." );
				}

				request.Headers.TryAddWithoutValidation( key, value );
			}
		}

		return request;
	}
}
