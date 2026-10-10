namespace Rinten.UI;

//The factory is any type with a public parameterless constructor that implements IDesignDataFactory. Nothing in a game reads this; it is for the editor, and it is on the property so the sample lives next to the thing it stands in for.
/// <summary>Names a factory that makes sample values for a property while it is looked at in the UI editor, where nothing in a game is there to fill it - a list of items to lay a grid out with, a player to show a name for.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class DesignDataAttribute : Attribute
{
	/// <summary>The type that makes the samples.</summary>
	public Type Factory { get; }

	public DesignDataAttribute( Type factory )
	{
		Factory = factory;
	}
}

/// <summary>Makes the sample values a <see cref="DesignDataAttribute"/> asks for.</summary>
public interface IDesignDataFactory
{
	/// <summary>One sample, the n-th of however many the editor wants; for a value that is not a list, only the first is asked for.</summary>
	object Make( int index );
}
