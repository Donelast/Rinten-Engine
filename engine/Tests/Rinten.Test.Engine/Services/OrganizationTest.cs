using System;

namespace ServicesTests;

[TestClass]
[TestCategory( "LiveBackend" )]
public class OrganizationTest
{
	[TestMethod]
	public async Task Basic()
	{
		var org = await Rinten.Services.Organization.Get( "facepunch" );

		Assert.IsNotNull( org );
		Assert.AreEqual( "facepunch", org.Ident );
		Assert.IsFalse( string.IsNullOrEmpty( org.Title ) );
		Assert.IsTrue( org.PackageCount > 0 );
		Assert.IsTrue( org.MemberCount > 0 );

		//Who they are went with the player profiles; how many there are is still part of what an organization says about itself.
		Console.WriteLine( $"{org.Ident} - {org.Title} - {org.PackageCount} packages, {org.MemberCount} members" );
	}

	[TestMethod]
	public async Task Cached()
	{
		var first = await Rinten.Services.Organization.Get( "facepunch" );
		var second = await Rinten.Services.Organization.Get( "facepunch" );

		Assert.IsNotNull( first );
		Assert.AreSame( first, second );
	}

	[TestMethod]
	public async Task CaseInsensitive()
	{
		var lower = await Rinten.Services.Organization.Get( "facepunch" );
		var upper = await Rinten.Services.Organization.Get( "FACEPUNCH" );

		Assert.IsNotNull( lower );
		Assert.AreSame( lower, upper );
	}

	[TestMethod]
	public async Task NotFound()
	{
		var org = await Rinten.Services.Organization.Get( "this-org-definitely-does-not-exist-xyz123" );
		Assert.IsNull( org );
	}

	[TestMethod]
	[DataRow( null )]
	[DataRow( "" )]
	public async Task EmptyIdent( string ident )
	{
		var org = await Rinten.Services.Organization.Get( ident );
		Assert.IsNull( org );
	}
}
