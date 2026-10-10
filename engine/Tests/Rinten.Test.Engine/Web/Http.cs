using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;

namespace WebTests;

[TestClass]
public class HttpTest
{
	//The scheme, and nothing else. Loopback, private addresses and bare IPs are a game's own
	//network to reach - a dev server on localhost is the ordinary case, not something to be refused.
	[TestMethod]
	[DataRow( "http://google.com", true )]
	[DataRow( "https://google.com", true )]
	[DataRow( "wss://google.com", true )]
	[DataRow( "http://127.0.0.1", true )]
	[DataRow( "http://127.0.0.1:1337", true )]
	[DataRow( "https://localhost:8080/", true )]
	[DataRow( "https://8.8.8.8/", true )]
	[DataRow( "https://192.168.1.1/", true )]
	[DataRow( "file://blah", false )]
	[DataRow( "ftp://blah/", false )]
	public async Task IsUriAllowed( string uri, bool expected )
	{
		var u = new Uri( uri, UriKind.Absolute );
		Assert.AreEqual( expected, Http.IsAllowed( u ) );
		Assert.AreEqual( expected, await Http.IsAllowedAsync( u ) );
	}

	[TestMethod]
	[DataRow( "Authorization", true )]
	[DataRow( "Host", false )]
	[DataRow( "X-Test", true )]
	[DataRow( "Proxy-Blah", false )]
	[DataRow( "Sec-Blah", false )]
	public void IsHeaderAllowed( string header, bool expected )
	{
		Assert.AreEqual( expected, Http.IsHeaderAllowed( header ) );
		Assert.AreEqual( expected, Http.IsHeaderAllowed( header.ToUpperInvariant() ) );
		Assert.AreEqual( expected, Http.IsHeaderAllowed( header.ToLowerInvariant() ) );
	}

	[TestMethod]
	public void CreateRequest_Valid_Succeeds()
	{
		var headers = new Dictionary<string, string> { { "X-Test", "1" } };
		using var request = Http.CreateRequest( HttpMethod.Get, "https://google.com/", headers );
		Assert.AreEqual( HttpMethod.Get, request.Method );
		Assert.AreEqual( "https://google.com/", request.RequestUri?.ToString() );
		Assert.AreEqual( 1, request.Headers.Count() );
		Assert.IsTrue( request.Headers.Contains( "X-Test" ) );
		CollectionAssert.AreEqual( new[] { "1" }, request.Headers.GetValues( "X-Test" ).ToArray() );
	}

	[TestMethod]
	public void CreateRequest_BadHeader_Throws()
	{
		var headers = new Dictionary<string, string> { { "Host", "blah" } };
		Assert.ThrowsException<InvalidOperationException>( () => Http.CreateRequest( HttpMethod.Get, "http://google.com", headers ) );
	}

	[TestMethod]
	[TestCategory( "LiveBackend" )]
	public async Task GetString()
	{
		var str = await Http.RequestStringAsync( "https://google.com" );

		Assert.IsFalse( string.IsNullOrEmpty( str ) );
	}


	[TestMethod]
	[TestCategory( "LiveBackend" )]
	public async Task GetBytes()
	{
		var bytes = await Http.RequestBytesAsync( "https://www.google.com/favicon.ico" );

		Assert.IsNotNull( bytes );
		Assert.IsTrue( bytes.Length > 0 );
	}
}
