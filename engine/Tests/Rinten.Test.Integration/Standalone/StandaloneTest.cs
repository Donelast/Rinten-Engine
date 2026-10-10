using Editor;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace StandaloneTests;

//Tests for the Standalone export pipeline
[TestClass]
public class StandaloneTest
{
	[TestInitialize]
	public void TestInitialize()
	{
		Project.Clear();
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Project.Clear();
	}

	[TestMethod]
	public async Task ExportAndRun()
	{
		var config = new ExportConfig();
		var tempFolderName = Guid.NewGuid().ToString();
		config.TargetDir = Path.Combine( Path.GetTempPath(), tempFolderName, "sboxexportest" );

		try
		{
			var project = Project.AddFromFile( "unittest/addons/spacewars/.rinproj" );
			config.Project = project;
			config.ExecutableName = project.Package.Ident;

			CleanOutput( config );

			//Build the project
			{
				var exporter = await StandaloneExporter.FromConfig( config );
				await exporter.Run();
			}

			//Run the exported project, make sure it launches and exits cleanly
			{
				using var process = new Process
				{
					StartInfo = new ProcessStartInfo
					{
						//Whatever a program is called here. An export is for a platform now, and this one
						//is for the machine running the test - which is the only one it could then start.
					FileName = Path.Combine( config.TargetDir,
						ExportPlatform.Host.Executable( config.ExecutableName ) ),
						WorkingDirectory = config.TargetDir,
						Arguments = "-headless -test-standalone",
						RedirectStandardError = true,
						UseShellExecute = false,
						CreateNoWindow = true
					}
				};

				//forward error output so we know why it's failed
				process.ErrorDataReceived += ( sender, e ) =>
				{
					if ( e.Data != null )
						Console.Error.WriteLine( e.Data );
				};

				bool success = process.Start();
				Assert.IsTrue( success, "Failed to start standalone exe" );

				process.BeginErrorReadLine();

				//A hung exe would otherwise stall the whole serial suite - kill it after a generous timeout
				using var timeout = new CancellationTokenSource( TimeSpan.FromMinutes( 5 ) );

				try
				{
					await process.WaitForExitAsync( timeout.Token );
				}
				catch ( OperationCanceledException )
				{
					process.Kill( entireProcessTree: true );
					Assert.Fail( "Standalone exe didn't exit within 5 minutes - killed the process" );
				}

				int exitCode = process.ExitCode;
				Assert.AreEqual( 0, exitCode, $"Process exited with code {exitCode}" );
			}
		}
		finally
		{
			CleanOutput( config );
		}
	}

	private void CleanOutput( ExportConfig config )
	{
		if ( config?.TargetDir is null || !Directory.Exists( config.TargetDir ) )
			return;

		try
		{
			Directory.Delete( config.TargetDir, true );
		}
		catch ( IOException )
		{
			Thread.Sleep( 500 );
			Directory.Delete( config.TargetDir, true );
		}
		catch ( UnauthorizedAccessException )
		{
			Thread.Sleep( 500 );
			Directory.Delete( config.TargetDir, true );
		}
	}

}
