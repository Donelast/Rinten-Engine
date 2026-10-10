using System;
using System.IO;
using System.Linq;
using System.Text;
using Editor;

namespace UnitTests.Editor;

//The edit journal keeps every completed write, and a torn write loses nothing earlier
[TestClass]
public class EditJournalTest
{
	private string Somewhere()
	{
		var directory = Path.Combine( Path.GetTempPath(), "rinten-journal-test", Guid.NewGuid().ToString( "N" ) );

		Directory.CreateDirectory( directory );

		return Path.Combine( directory, "document.journal" );
	}

	[TestMethod]
	public void WhatWasWrittenComesBack()
	{
		var path = Somewhere();

		using ( var journal = EditJournal.Open( path, baseHash: 1234 ) )
		{
			Assert.IsNotNull( journal );
			Assert.IsTrue( journal.BaseMatches );

			for ( var i = 0; i < 500; i++ )
			{
				Assert.IsTrue( journal.Append( $"{{\"edit\":{i}}}" ) );
			}

			Assert.AreEqual( 500, journal.Count );
		}

		using var reopened = EditJournal.Open( path, baseHash: 1234 );

		Assert.IsNotNull( reopened );
		Assert.IsTrue( reopened.BaseMatches, "the journal was written against this very document" );
		Assert.AreEqual( 500, reopened.Count );

		var records = reopened.Read();

		Assert.AreEqual( 500, records.Count );
		Assert.AreEqual( "{\"edit\":0}", Encoding.UTF8.GetString( records[0] ) );
		Assert.AreEqual( "{\"edit\":499}", Encoding.UTF8.GetString( records[^1] ) );
	}

	//A crash in the middle of an append leaves half a record at the end. Every record before
	//it is still readable, which is the whole reason this file is only ever added to.
	[TestMethod]
	public void ATornLastRecordCostsOnlyItself()
	{
		var path = Somewhere();

		using ( var journal = EditJournal.Open( path, baseHash: 7 ) )
		{
			for ( var i = 0; i < 20; i++ ) journal.Append( $"edit {i}" );
		}

		//Whatever was being written when the power went: the last record, cut off part way through.
		using ( var file = new FileStream( path, FileMode.Open, FileAccess.Write ) )
		{
			file.SetLength( file.Length - 3 );
		}

		using var reopened = EditJournal.Open( path, baseHash: 7 );

		var records = reopened.Read();

		Assert.AreEqual( 19, records.Count, "everything but the torn record survives" );
		Assert.AreEqual( "edit 18", Encoding.UTF8.GetString( records[^1] ) );
	}

	//A record whose contents were mangled is not applied, and neither is anything after
	//it - the journal is a sequence, and a hole in it is the end of what can be trusted.
	[TestMethod]
	public void AMangledRecordStopsTheReplay()
	{
		var path = Somewhere();

		using ( var journal = EditJournal.Open( path, baseHash: 7 ) )
		{
			for ( var i = 0; i < 10; i++ ) journal.Append( $"edit {i}" );
		}

		var bytes = File.ReadAllBytes( path );

		//The header is 16 bytes and each record here is 16 + 7. Land in the middle of the fourth record's contents.
		var at = 16 + (3 * (16 + 7)) + 16 + 2;

		bytes[at] = (byte)(bytes[at] ^ 0xFF);

		File.WriteAllBytes( path, bytes );

		using var reopened = EditJournal.Open( path, baseHash: 7 );

		var records = reopened.Read();

		Assert.AreEqual( 3, records.Count, "the three records before the damage are still good" );
	}

	//Edits belong to the version of the document they were made against. If the file has been changed by
	//something else, the journal says so rather than being replayed onto a document it was never about.
	[TestMethod]
	public void AJournalKnowsWhichDocumentItIsAbout()
	{
		var path = Somewhere();

		using ( var journal = EditJournal.Open( path, baseHash: 100 ) )
		{
			journal.Append( "edit" );
		}

		using var reopened = EditJournal.Open( path, baseHash: 200 );

		Assert.IsNotNull( reopened );
		Assert.IsFalse( reopened.BaseMatches, "the document is not the one these edits were made on" );
		Assert.AreEqual( 1, reopened.Count, "and the edits are still there to be looked at" );
	}

	//Once the document itself has been written out, the records that made it are no longer needed and the journal starts again from the new version.
	[TestMethod]
	public void RebasingStartsAgainFromTheNewDocument()
	{
		var path = Somewhere();

		//Closed before it is opened again: a journal is held exclusively, so that two
		//editors cannot write into one and make a sequence that is neither of theirs.
		using ( var journal = EditJournal.Open( path, baseHash: 1 ) )
		{
			for ( var i = 0; i < 5; i++ ) journal.Append( $"edit {i}" );

			Assert.IsTrue( journal.Rebase( baseHash: 2 ) );
			Assert.AreEqual( 0, journal.Count );
			Assert.AreEqual( 0, journal.Read().Count );

			journal.Append( "after" );

			Assert.AreEqual( 1, journal.Count );
		}

		using var reopened = EditJournal.Open( path, baseHash: 2 );

		Assert.IsNotNull( reopened );

		Assert.IsTrue( reopened.BaseMatches );
		Assert.AreEqual( "after", Encoding.UTF8.GetString( reopened.Read().Single() ) );
	}
}
