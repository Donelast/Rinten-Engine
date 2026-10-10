

//If applied to an attribute on a class, it won't be inherited from its base classes.
internal interface IUninheritable
{
	//TODO: I'm pretty sure this is what [(AttributeUsage( Inherited = false )] is for
}
