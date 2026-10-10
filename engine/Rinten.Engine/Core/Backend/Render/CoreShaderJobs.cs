using System.Diagnostics;

namespace Rinten;

//Async shader compiles on CoreShaderThreads; callers draw a fallback until the result is cached
internal static class CoreShaderJobs
{
	private static readonly Diagnostics.Logger log = new( "Shaders" );

	//What a compile is handed.
	internal readonly record struct Request( string Source, string Directory, string Entry,
		CoreShaderCompiler.Stage Stage, CoreShaderCompiler.Format Format, string Name, string Instead = null );

	private sealed class Job
	{
		public string Key;
		public Request Asked;
		public volatile bool Done;
		public byte[] Words;
	}

	//Every compile asked for, by what the asker calls it.
	private static readonly Dictionary<string, Job> jobs = new( StringComparer.Ordinal );

	//The words for one entry of a text, where they exist; otherwise the compile is started - once -
	//and waiting says so. A compile that failed is null and not waiting, and has said why in the log.
	internal static byte[] Words( string id, Func<Request?> ask, out bool waiting )
	{
		waiting = false;

		lock ( jobs )
		{
			if ( jobs.TryGetValue( id, out var had ) )
			{
				waiting = !had.Done;
				return had.Done ? had.Words : null;
			}
		}

		if ( ask() is not { } request ) return null;

		if ( CoreShaderCache.Cached( request.Source, request.Directory, request.Entry, request.Stage, request.Format,
			out var key, out var words ) )
		{
			lock ( jobs ) jobs[id] = new Job { Done = true, Words = words };
			return words;
		}

		if ( key is null ) return null;

		//No compiler here - a shipped game: nothing is coming, and the cache says so once.
		if ( !CoreShaderCompiler.Available )
		{
			var none = CoreShaderCache.Words( request.Source, request.Directory, request.Entry, request.Stage, request.Format,
				request.Name, request.Instead );

			lock ( jobs ) jobs[id] = new Job { Done = true, Words = none };
			return none;
		}

		var job = new Job { Key = key, Asked = request };

		lock ( jobs ) jobs[id] = job;

		_ = CoreShaderThreads.Run( () => Compile( job ) );
		waiting = true;

		return null;
	}

	//One compile, on a compile thread. Whatever it throws stays here: the words are null, the
	//job is done, and the log says why - a shader that would not compile is not worth the thread.
	private static bool Compile( Job job )
	{
		var clock = Stopwatch.StartNew();
		var asked = job.Asked;
		string error = null;

		try
		{
			job.Words = CoreShaderCache.CompileAndKeep( job.Key, asked.Source, asked.Directory, asked.Entry, asked.Stage,
				asked.Format, asked.Name, asked.Instead );

			if ( job.Words is null ) error = CoreShaderCompiler.LastError;
		}
		catch ( Exception e )
		{
			error = e.Message;
		}

		var took = clock.Elapsed.TotalMilliseconds;

		job.Asked = default;
		job.Done = true;

		//How long the frame would have stood still for it, which is the point of doing it here.
		if ( error is null ) log.Info( $"{asked.Name} {asked.Entry} compiled beside the frame in {took:0} ms" );
		else log.Warning( $"{asked.Name} {asked.Entry} did not compile ({took:0} ms): {error}" );

		return job.Words is not null;
	}
}
