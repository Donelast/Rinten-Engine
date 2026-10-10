using Rinten.Utility;

namespace SystemTests;

[TestClass]
[DoNotParallelize]
public class CommandLineTest
{
	//The parser should split switches on +/- prefixes, keep quoted strings intact, and tolerate switch-name lookups with or without the
	//prefix. CommandLine is process-global state, so we save and restore it around the test and don't run in parallel with anything else.
	[TestMethod]
	public void TestCommandLineParser()
	{
		var previousCommandLine = CommandLine.CommandLineString;

		try
		{
			CommandLine.CommandLineString = @"+game facepunch.walker garry.scenemap +something ""hello there im a string"" test -value 35455 +somethingelse ""a -switch inside a string""";
			CommandLine.Parse();

			Assert.AreEqual( "facepunch.walker garry.scenemap", CommandLine.GetSwitch( "game", string.Empty ) );
			Assert.AreEqual( @"""a -switch inside a string""", CommandLine.GetSwitch( "somethingelse", string.Empty ) );
			Assert.AreEqual( "35455", CommandLine.GetSwitch( "value", string.Empty ) );
			Assert.AreEqual( "35455", CommandLine.GetSwitch( "-value", string.Empty ) );
			Assert.AreEqual( @"""hello there im a string"" test", CommandLine.GetSwitch( "something", string.Empty ) );

			CommandLine.CommandLineString = @"D:\Facepunch\sbox\game\rinten.dll -project D:\Facepunch\sbox\game\addons\sbox-deathmatch\sbdm.sbproj";
			CommandLine.Parse();

			Assert.AreEqual( @"D:\Facepunch\sbox\game\addons\sbox-deathmatch\sbdm.sbproj", CommandLine.GetSwitch( "project", string.Empty ) );
		}
		finally
		{
			CommandLine.CommandLineString = previousCommandLine;
			CommandLine.Parse();
		}
	}

	/// <summary>
	/// A project opened from a file manager arrives as a bare path with no
	/// switch in front of it, and Launcher.AdoptProjectArgument answers that by
	/// writing "-project" onto the end of the command line it was given. Both
	/// halves of that have to survive the parser - the path already on the line
	/// as a positional, and the quoted copy appended after it - and a path with
	/// a space in it is the case that would not.
	/// </summary>
	[TestMethod]
	public void TestAppendedProjectSwitch()
	{
		var previousCommandLine = CommandLine.CommandLineString;

		try
		{
			const string project = "/home/someone/my games/thing.rinproj";

			CommandLine.CommandLineString =
				$"/opt/rinten/game/Rinten \"{project}\" -project \"{project}\"";
			CommandLine.Parse();

			Assert.AreEqual( project,
				CommandLine.GetSwitch( "-project", string.Empty ).TrimQuoted() );
		}
		finally
		{
			CommandLine.CommandLineString = previousCommandLine;
			CommandLine.Parse();
		}
	}
}
