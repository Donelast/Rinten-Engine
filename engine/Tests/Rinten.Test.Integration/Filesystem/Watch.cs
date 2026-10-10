using System;

namespace FilesystemTests;

public partial class FileSystemTest
{
	//Polls Tick for up to timeoutSeconds seconds until changeDetected returns true. FileWatch debounces changes for a short period
	//after the last filesystem event, so a single Tick straight after a file operation is not guaranteed to deliver callbacks.
	private static async Task<bool> TickUntilChangeDetected( Func<bool> changeDetected, float timeoutSeconds = 5f )
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();

		while ( sw.Elapsed.TotalSeconds < timeoutSeconds )
		{
			Rinten.FileWatch.Tick();

			if ( changeDetected() )
				return true;

			await Task.Delay( 50 );
		}

		return false;
	}

	[TestMethod]
	public async Task WatchPhysical()
	{
		Rinten.EngineFileSystem.Root.CreateDirectory( "/WatchFolder" );

		using ( var watcher = Rinten.EngineFileSystem.Root.Watch( "/WatchFolder/*" ) )
		{
			bool detectedChanges = false;

			watcher.OnChanges += w => detectedChanges = true;

			await Task.Delay( 100 );

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Rinten.FileWatch.Tick();

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Rinten.EngineFileSystem.Root.WriteAllText( "/WatchFolder/hello.txt", "test" );

			Assert.IsFalse( detectedChanges, "Changes must not be delivered before a Tick" );

			Assert.IsTrue( await TickUntilChangeDetected( () => detectedChanges ), "Callbacks should have been triggered now" );
		}
	}

	[TestMethod]
	public async Task WatchMemory()
	{
		Rinten.EngineFileSystem.Temporary.CreateDirectory( "/WatchFolder" );

		using ( var watcher = Rinten.EngineFileSystem.Temporary.Watch( "/WatchFolder/*" ) )
		{
			bool detectedChanges = false;

			watcher.OnChanges += w => detectedChanges = true;

			await Task.Delay( 200 );

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Rinten.FileWatch.Tick();

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Rinten.EngineFileSystem.Temporary.WriteAllText( "/WatchFolder/hello.txt", "test" );

			Assert.IsFalse( detectedChanges, "Changes must not be delivered before a Tick" );

			Assert.IsTrue( await TickUntilChangeDetected( () => detectedChanges ), "Callbacks should have been triggered now" );
		}

		Rinten.EngineFileSystem.Temporary.DeleteDirectory( "/WatchFolder", true );
	}


	[TestMethod]
	public async Task WatchSingleFile()
	{
		Rinten.EngineFileSystem.Temporary.CreateDirectory( "/WatchFolder" );

		using ( var watch = Rinten.EngineFileSystem.Temporary.Watch( "/WatchFolder/hello.txt" ) )
		{
			bool detectedChanges = false;
			watch.OnChanges += w => detectedChanges = true;

			Rinten.EngineFileSystem.Temporary.WriteAllText( "/WatchFolder/not watching.txt", "test" );
			Rinten.EngineFileSystem.Temporary.WriteAllText( "/WatchFolder/not watching.txt", "test" );

			await Task.Delay( 200 );

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Rinten.FileWatch.Tick();

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Rinten.EngineFileSystem.Temporary.WriteAllText( "/WatchFolder/hello.txt", "test" );

			Assert.IsFalse( detectedChanges, "Changes must not be delivered before a Tick" );

			Assert.IsTrue( await TickUntilChangeDetected( () => detectedChanges ), "Callbacks should have been triggered now" );
		}

		Rinten.EngineFileSystem.Temporary.DeleteDirectory( "/WatchFolder", true );
	}

	[TestMethod]
	public async Task WatchMounted()
	{
		var fs = new Rinten.AggregateFileSystem();
		fs.UnMountAll();

		var blue = fs.CreateAndMount( Rinten.EngineFileSystem.Root, "Addons/Blue" );
		var red = fs.CreateAndMount( Rinten.EngineFileSystem.Root, "Addons/Red" );
		var green = fs.CreateAndMount( Rinten.EngineFileSystem.Root, "Addons/Green" );

		Rinten.EngineFileSystem.Root.CreateDirectory( "Addons/Blue/UI" );
		System.IO.File.WriteAllText( Rinten.EngineFileSystem.Root.GetFullPath( "Addons/Blue/UI/hello.txt" ), "efwfwefweweffwe" );

		using ( var watcher = fs.Watch( "/UI/hello.txt" ) )
		{
			bool detectedChanges = false;

			watcher.OnChanges += w =>
			{
				Console.WriteLine( $"{string.Join( ",", w.Changes )} has changes" );
				detectedChanges = true;
			};

			await Task.Delay( 200 );

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Rinten.FileWatch.Tick();

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			System.IO.File.WriteAllText( Rinten.EngineFileSystem.Root.GetFullPath( "Addons/Blue/UI/hello.txt" ), "ergergregerg" );

			Assert.IsFalse( detectedChanges, "Changes must not be delivered before a Tick" );

			Assert.IsTrue( await TickUntilChangeDetected( () => detectedChanges ), "Callbacks should have been triggered now" );
		}
	}

	[TestMethod]
	public async Task WatchMountedCaseInsensitivePath()
	{
		var fs = new Rinten.AggregateFileSystem();
		fs.UnMountAll();

		//Wrong-case mount paths: SubFileSystem(isCaseSensitive:false) must strip the base prefix case-insensitively when
		//routing physical change events back to virtual paths, otherwise the watcher silently drops events on Linux.
		fs.CreateAndMount( Rinten.EngineFileSystem.Root, "addons/blue" );

		Rinten.EngineFileSystem.Root.CreateDirectory( "Addons/Blue/UI" );
		System.IO.File.WriteAllText( Rinten.EngineFileSystem.Root.GetFullPath( "Addons/Blue/UI/hello.txt" ), "initial" );

		using ( var watcher = fs.Watch( "/UI/hello.txt" ) )
		{
			bool detectedChanges = false;

			watcher.OnChanges += w =>
			{
				Console.WriteLine( $"{string.Join( ",", w.Changes )} has changes" );
				detectedChanges = true;
			};

			await Task.Delay( 200 );

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Rinten.FileWatch.Tick();

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			System.IO.File.WriteAllText( Rinten.EngineFileSystem.Root.GetFullPath( "Addons/Blue/UI/hello.txt" ), "changed" );

			Assert.IsFalse( detectedChanges, "Changes must not be delivered before a Tick" );

			Assert.IsTrue( await TickUntilChangeDetected( () => detectedChanges ), "Callbacks should have been triggered now" );
		}
	}

	//A write that went around the mount - File.WriteAllText - can still be handed to
	//watchers by path, so a tool that saves that way does not have to wait on inotify.
	[TestMethod]
	public void NotifyDiskWriteReachesWatchers()
	{
		var folder = "NotifyDiskWriteTest";
		Rinten.EngineFileSystem.Root.CreateDirectory( folder );
		var relative = folder + "/panel.razor";
		var full = Rinten.EngineFileSystem.Root.GetFullPath( relative );
		System.IO.File.WriteAllText( full, "before" );

		using ( var watcher = Rinten.EngineFileSystem.Root.Watch() )
		{
			var detected = false;
			watcher.OnChangedFile += file =>
			{
				if ( file.EndsWith( "panel.razor", StringComparison.OrdinalIgnoreCase ) )
					detected = true;
			};

			Rinten.FileWatch.NotifyDiskWrite( full );
			Rinten.FileWatch.TimeSinceLastChange = 1;
			Rinten.FileWatch.Tick();

			Assert.IsTrue( detected, "NotifyDiskWrite should deliver to watchers on that filesystem" );
		}
	}
}
