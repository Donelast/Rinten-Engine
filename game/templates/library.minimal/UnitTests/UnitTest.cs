global using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class TestInit
{
	public static Rinten.TestAppSystem AppSystem;

	[AssemblyInitialize]
	public static void AssemblyInitialize( TestContext context )
	{
		AppSystem = new Rinten.TestAppSystem();
		AppSystem.Init();
	}

	[AssemblyCleanup]
	public static void AssemblyCleanup()
	{
		AppSystem.Shutdown();
	}
}
