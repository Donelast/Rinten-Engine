namespace Editor.Inspectors;

//What a resource field should show right now.
readonly record struct ResourceDisplayState(
	ResourceFieldMode Mode,
	string Title,
	string Path,
	string Placeholder,
	AssetType AssetType,
	Asset Asset,
	Resource Resource,
	Pixmap Thumb = null );

enum ResourceFieldMode
{
	Placeholder,
	Set,
	Multiple,
	Missing
}
