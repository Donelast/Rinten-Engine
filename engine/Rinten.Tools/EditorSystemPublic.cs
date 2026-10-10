
using System;
using System.Threading;

namespace Rinten;

public class EditorSystemPublic : EditorSystem
{
	public override Scene Scene => SceneEditorSession.Active?.Scene;

	/// <summary>Run a process on multiple items, show progress bar</summary>
	public override async Task ForEachAsync<T>( IEnumerable<T> list, string title, Func<T, CancellationToken, Task> worker, CancellationToken cancel = default, bool modal = false )
	{
		int count = list.Count();
		if ( count <= 0 ) return;

		using ( var p = ProgressSection( modal ) )
		{
			using var linkedCt = CancellationTokenSource.CreateLinkedTokenSource( cancel, p.GetCancel() );

			p.Title = title;
			p.TotalCount = count;
			await Task.Delay( 1 );

			int current = 0;
			FastTimer ft = FastTimer.StartNew();
			foreach ( var item in list )
			{
				current++;
				await worker( item, linkedCt.Token );

				p.Current = current;
				p.Subtitle = $"Processing {current} / {count}";

				if ( ft.ElapsedMilliSeconds > 50 )
				{
					await Task.Delay( 1, linkedCt.Token );
					ft = FastTimer.StartNew();
				}

				if ( linkedCt.IsCancellationRequested ) return;
			}
		}
	}


	/// <summary>Start a progress section</summary>
	public override IProgressSection ProgressSection( bool modal = false )
	{
		return new ProgressJobSection();
	}

	//Walks every component of the scene - don't call per frame, use Scene.EditorCamera instead
	public override CameraComponent Camera
	{
		get
		{
			if ( SceneEditorSession.Active?.Scene is not { } scene ) return null;

			foreach ( var camera in scene.GetAll<CameraComponent>() )
			{
				if ( camera.IsSceneEditorCamera ) return camera;
			}

			return null;
		}
	}
}
