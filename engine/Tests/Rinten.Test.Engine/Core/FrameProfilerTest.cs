using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinten;
using Rinten.Diagnostics;
using System;
using System.Linq;

namespace EngineTest.Core;

//The frame profiler is diagnostics, but wrong diagnostics are worse than none: a tree that misattributes a span sends somebody
//optimising the wrong system. These pin the shape of the tree, not the times in it - times here would flake on a busy build agent.
[TestClass]
[DoNotParallelize] //One recording, one tree - and other suites mark their own threads "main".
public class FrameProfilerTest
{
	//Scope names are unique per test: the profiler's tree persists across frames on purpose, so two tests sharing a name would see each other.
	private static string Unique( string name ) => $"{name}-{Guid.NewGuid():N}";

	[TestMethod]
	public void RecordsATreeWithDepthsAndCounts()
	{
		ThreadSafe.MarkMainThread();
		FrameProfiler.Enabled = true;

		try
		{
			var outer = Unique( "outer" );
			var inner = Unique( "inner" );

			using ( FrameProfiler.Push( outer ) )
			{
				using ( FrameProfiler.Push( inner ) ) { }
				using ( FrameProfiler.Push( inner ) ) { }
			}

			FrameProfiler.Flip();

			var entries = FrameProfiler.Frame;

			var parent = entries.FirstOrDefault( e => e.Name == outer );
			var child = entries.FirstOrDefault( e => e.Name == inner );

			Assert.IsNotNull( parent, "the outer span should be in the tree" );
			Assert.IsNotNull( child, "the inner span should be in the tree" );

			//Two entries of the same name inside one parent are one line that counts, not two lines.
			Assert.AreEqual( 2, child.Calls );
			Assert.AreEqual( parent.Depth + 1, child.Depth, "the child sits one level under its parent" );

			//Pre-order: a parent is published before what is inside it.
			var entryList = entries.ToList();
			Assert.IsTrue( entryList.IndexOf( parent ) < entryList.IndexOf( child ) );
		}
		finally
		{
			FrameProfiler.Enabled = false;
			FrameProfiler.Flip();
		}
	}

	[TestMethod]
	public void AttributesAllocationsToTheSpanThatMadeThem()
	{
		ThreadSafe.MarkMainThread();
		FrameProfiler.Enabled = true;

		try
		{
			var name = Unique( "allocator" );

			using ( FrameProfiler.Push( name ) )
			{
				//A megabyte on purpose. GC.KeepAlive so it cannot be elided.
				var bytes = new byte[1024 * 1024];
				GC.KeepAlive( bytes );
			}

			FrameProfiler.Flip();

			var entry = FrameProfiler.Frame.FirstOrDefault( e => e.Name == name );

			Assert.IsNotNull( entry );
			Assert.IsTrue( entry.AllocBytes >= 1024 * 1024,
				$"a span that allocated a megabyte says it allocated {entry.AllocBytes} bytes" );
		}
		finally
		{
			FrameProfiler.Enabled = false;
			FrameProfiler.Flip();
		}
	}

	[TestMethod]
	public void DisabledRecordsNothing()
	{
		ThreadSafe.MarkMainThread();
		FrameProfiler.Enabled = false;

		var name = Unique( "ghost" );

		using ( FrameProfiler.Push( name ) ) { }

		FrameProfiler.Flip();

		Assert.IsFalse( FrameProfiler.Frame.Any( e => e.Name == name ),
			"a span opened while disabled should not exist" );
	}

	[TestMethod]
	public void SurvivesAScopeNeverDisposed()
	{
		ThreadSafe.MarkMainThread();
		FrameProfiler.Enabled = true;

		try
		{
			var name = Unique( "leaky" );

			//Opened and never closed - an exception path, in real life. The flip is the outermost scope and closes it.
			FrameProfiler.Push( name );

			FrameProfiler.Flip();

			var entry = FrameProfiler.Frame.FirstOrDefault( e => e.Name == name );

			Assert.IsNotNull( entry, "an unclosed span still lands in the tree, closed at the frame boundary" );

			//And the next frame must be clean rather than nested inside a ghost.
			var after = Unique( "after" );

			using ( FrameProfiler.Push( after ) ) { }

			FrameProfiler.Flip();

			var top = FrameProfiler.Frame.FirstOrDefault( e => e.Name == after );

			Assert.IsNotNull( top );
			Assert.AreEqual( 0, top.Depth, "a span of the next frame is not a child of the leak" );
		}
		finally
		{
			FrameProfiler.Enabled = false;
			FrameProfiler.Flip();
		}
	}
}
