using BenchmarkDotNet.Attributes;
using Rinten.Internal;

[MemoryDiagnoser]
[ThreadingDiagnoser]
public class TypelibraryMark
{
	[Benchmark]
	public void AddAssembly()
	{
		var tl = new TypeLibrary();

		tl.AddAssembly( typeof( Rinten.CookieContainer ).Assembly, true );
	}

}
