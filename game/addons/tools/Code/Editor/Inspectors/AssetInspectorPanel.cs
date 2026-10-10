using Rinten.UI;
using System.Linq;

namespace Editor.Inspectors;

/// <summary>The inspector dock's page for an asset picked in the browser.</summary>
[Inspector( typeof( Asset ) )]
[StyleSheet( "AssetInspectorPanel.cs.scss" )]
public sealed class AssetInspectorPanel : InspectorPanel
{
	readonly InspectorPanel _page;

	public AssetInspectorPanel( SerializedObject so ) : base( so )
	{
		AddClass( "asset-inspector column" );

		var asset = so.IsMultipleTargets ? null : so.Targets.OfType<Asset>().FirstOrDefault();

		//Whatever page follows, it opens on which file it is.
		var header = asset is null ? null : AddChild( new AssetHeaderPanel( asset ) );

		//Each cast up: ?? binds to the right, and two sibling panel types have no common type between them but the one they both derive from.
		_page = PageFor( so )
			?? (InspectorPanel)ResourceEditorInspectorPanel.For( so )
			?? (InspectorPanel)FileInspectorPanel.For( so )
			?? GameResourceInspectorPanel.For( so );

		if ( _page is not null )
		{
			_page.AddClass( "grow-shrink" );
			AddChild( _page );

			if ( header is not null && _page is IInspectorHead head )
				head.FillHead( header );

			//And under it, the preview block the old asset inspector had - for the kinds whose preview is something to
			//use, see AssetPreview.CreatePanel - as a card of its own, folded away for the project when it is not wanted.
			if ( PreviewFor( asset ) is { } preview )
			{
				var card = AddChild( new InspectorCard( "Preview", "play_circle", null, "asset.preview" ) );
				card.AddClass( "preview" );
				card.Body.AddChild( preview );
				card.Refold();
			}

			return;
		}

		//Nothing edits this kind of file. The asset's own fields used to stand here - flags and paths about the file, a dozen of them read-only
		//- which was the dock showing its plumbing rather than the file. The header says what the file is; several picked at once say how many.
		if ( so.IsMultipleTargets )
		{
			var count = AddChild<Rinten.UI.Label>( "asset-count" );
			count.Text = $"{so.Targets.Count()} assets selected";
			count.Tokenize = false;
		}
	}

	//The page for the resource behind one asset, or null where there is none.
	static InspectorPanel PageFor( SerializedObject so )
	{
		if ( so.IsMultipleTargets ) return null;
		if ( so.Targets.OfType<Asset>().FirstOrDefault() is not { } asset ) return null;
		if ( !asset.TryLoadResource<GameResource>( out var resource ) ) return null;

		return InspectorPanel.Create( resource.GetSerialized() );
	}

	//The panel an asset's preview offers the inspector, or null where it offers none.
	static Rinten.UI.Panel PreviewFor( Asset asset )
	{
		if ( asset is null ) return null;

		using var preview = Editor.Assets.AssetPreview.CreateForAsset( asset );

		return preview?.CreatePanel();
	}

	//The resource's page is what has edits to see off.
	protected override bool OnInspectorClose( object newObj = null ) => _page?.CloseInspector( newObj ) ?? true;
}
