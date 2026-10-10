namespace Editor;

//This class's frame hooks only tick after Widget.RequestFrame instead of every frame; not inherited
[System.AttributeUsage( System.AttributeTargets.Class, Inherited = false )]
internal sealed class FramesOnDemandAttribute : System.Attribute
{
}
