//If set on a type, it (and its descendants) can be created and manipulated via the TypeLibrary system, and therefore also in action
//graphs. Note that this is only useful for our internal libraries because everything in compiled (addons) assemblies is accessible anyway.
internal sealed class ExposeAttribute : System.Attribute
{
}
