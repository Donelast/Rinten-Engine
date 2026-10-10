using System;
using Editor.Inspectors;

namespace Editor;

public interface IResourceEditor
{
	/// <summary>The asset we're editing</summary>
	Asset Asset { get; }

	/// <summary>The resource contained within the asset</summary>
	Resource Resource { get; }

	event Action<SerializedProperty> Changed;

	void Initialize( Asset asset, Resource resource );
	void SavedToDisk();
	void SelectMember( string memberName );
}

/// <summary>Implement this with your target type to create a special inspector for the resource type</summary>
public abstract class BaseResourceEditor<T> : Widget, IResourceEditor
	where T : Resource
{
	public Asset Asset { get; private set; }

	public T Resource { get; private set; }

	public event Action<SerializedProperty> Changed;

	Resource IResourceEditor.Resource => Resource;

	/// <summary>Default constructor does nothing</summary>
	public BaseResourceEditor() : base( null ) { }

	void IResourceEditor.Initialize( Asset asset, Resource resource )
	{
		Asset = asset;
		Resource = (T)resource;

		Initialize( Asset, Resource );
	}

	//Override this to build your UI or whatever for this. Default behaviour is to create a property sheet and add it to y
	protected abstract void Initialize( Asset asset, T resource );

	void IResourceEditor.SavedToDisk() => SavedToDisk();

	protected virtual void SavedToDisk() { }

	protected void NoteChanged( SerializedProperty property = null )
	{
		Changed?.Invoke( property );
	}

	public virtual void SelectMember( string name )
	{

	}
}
