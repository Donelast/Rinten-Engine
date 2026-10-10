global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Rinten;
global using System.Linq;
global using System.Threading.Tasks;
global using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

//No native engine here, so tests parallelize; Compiling/ and Hotload/ share state and run serially
[assembly: Parallelize( Workers = 0, Scope = ExecutionScope.MethodLevel )]

[TestClass]
public class TestInit
{
	[AssemblyInitialize]
	public static void AssemblyInitialize( TestContext context )
	{
		//Managed-only setup - keep anything native out of here.
		Application.IsUnitTest = true;
	}
}
