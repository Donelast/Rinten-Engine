using Rinten;
using System.Threading;

namespace Editor;

[DropObject( "terrain", "terrain" )]
partial class TerrainDropObject : BaseDropObject
{
	Asset Asset;

	protected override async Task Initialize( string dragData, CancellationToken token )
	{
		Asset = await InstallAsset( dragData, token );

		if ( token.IsCancellationRequested )
			return;
	}

	public override async Task OnDrop()
	{
		await WaitForLoad();

		if ( Asset is null ) return;

		var storage = Asset.LoadResource<TerrainStorage>();
		if ( storage is null ) return;

		using var scene = SceneEditorSession.Scope();

		using ( SceneEditorSession.Active.UndoScope( "Drop Terrain" ).WithGameObjectCreations().Push() )
		{
			GameObject = new GameObject( true, storage.ResourceName );
			var terrain = GameObject.Components.Create<Terrain>( false );
			terrain.Storage = storage;
			terrain.Enabled = true;

			EditorScene.Selection.Clear();
			EditorScene.Selection.Add( GameObject );
		}

		await Task.CompletedTask;
	}
}
