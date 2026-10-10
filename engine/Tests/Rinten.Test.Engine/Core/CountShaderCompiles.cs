using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinten;

namespace EngineTests.Core;

//The compute passes the cull is made of compile and bake, and say why when they do not - a
//pass that silently comes out nought turns the whole cull into the slow path without a word.
[TestClass]
public class CountShaderCompilesTests
{
	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void TheCullPassesCompile()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "no core library here" ); return; }
		if ( !CoreRenderDevice.Initialize( validation: false ) ) { Assert.Inconclusive( CoreRenderDevice.Description ); return; }

		foreach ( var name in new[] { EngineShaders.MeshCull, EngineShaders.MeshSkin } )
		{
			var spirv = CoreShaderCache.Pass( name, "main", CoreShaderCompiler.Stage.Compute );
			Assert.IsNotNull( spirv, $"{name} did not compile: {CoreRenderDevice.ShaderError}" );
			var pipeline = CoreRenderDevice.CreateCompute( spirv );
			Assert.AreNotEqual( 0ul, pipeline, $"{name} did not bake: {CoreRenderDevice.Description}" );
			CoreRenderDevice.DestroyPipeline( pipeline );
		}
	}
}
