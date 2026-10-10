using System.Collections.Generic;

namespace Editor.MdlEditor;

/// <summary>What the preview needs from whichever editor it is in.</summary>
public interface IPreviewHost
{
	/// <summary>The graph being previewed, or null in an editor that has none.</summary>
	Data Graph { get; }

	/// <summary>The definition of the model in the picture, where there is one to read: its clips, body groups, material groups, morphs and attachments.</summary>
	ModelDefinition ModelDefinition { get; }

	/// <summary>The model's own asset, which is where its clips are compiled beside.</summary>
	Asset ModelAsset { get; }

	/// <summary>The compiled model the host holds itself, or null where the graph's target model is what should be shown.</summary>
	Model OpenModel { get; }

	/// <summary>Whether the picture is of a model as it is, rather than of a graph playing on one.</summary>
	bool IsModelPreview { get; }

	/// <summary>The scene model in the picture, for the recorder.</summary>
	SceneModel PreviewModel { get; }

	bool IsPreviewPaused { get; }

	/// <summary>The graph's preview settings were edited - the focus bone, say.</summary>
	void MarkDirty( bool updatePreview = true );

	/// <summary>A graph property was edited from the preview's own sheet.</summary>
	void CommitChange( string name );

	/// <summary>Something on the model was moved in the picture.</summary>
	void NotifyModelDocChanged();

	/// <summary>Drive the graph's parameters into the preview, where there is a graph.</summary>
	void ApplyPreviewParameters();

	void ConsumeAutoResetPreviews();

	void CollectActiveAnimations( SceneModel model, List<(string Name, float Cycle, float Duration)> animations );

	void SetRecordingReviewMode( bool reviewing, bool paused );

	bool ApplyRecordedUpdate( int updateIndex );

	/// <summary>A bone was clicked in the picture - or the picture was clicked on no bone, which is null. Nothing, in an editor with nowhere to pick one.</summary>
	void PickBone( string bone ) { }
}
