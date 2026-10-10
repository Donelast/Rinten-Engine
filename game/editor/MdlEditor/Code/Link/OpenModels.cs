using System.Collections.Generic;
using System.Linq;

namespace Editor.MdlEditor;

//An editor with a model open: its definition and compiled model, for controls that need one
internal interface IOpenModel : IValid
{
	Asset ModelAsset { get; }
	ModelDefinition ModelDefinition { get; }
	Model OpenModel { get; }
}

//The models open anywhere in the editor, most recently opened first.
internal static class OpenModels
{
	private static readonly List<IOpenModel> _all = new();

	public static IEnumerable<IOpenModel> All
	{
		get
		{
			_all.RemoveAll( x => !x.IsValid );

			//Newest first: with two models open, the one somebody just opened is the one they mean.
			for ( var i = _all.Count - 1; i >= 0; i-- )
				yield return _all[i];
		}
	}

	public static void Register( IOpenModel editor )
	{
		_all.Remove( editor );
		_all.Add( editor );
	}

	public static void Unregister( IOpenModel editor ) => _all.Remove( editor );

	/// <summary>The most recently opened model that has a definition, or null.</summary>
	public static IOpenModel First() => All.FirstOrDefault( x => x.ModelDefinition is not null );

	/// <summary>The open editor holding the model at this path, or null.</summary>
	public static IOpenModel Holding( string modelPath )
	{
		if ( string.IsNullOrWhiteSpace( modelPath ) ) return null;

		return All.FirstOrDefault( x => ModelGraphResolver.SamePath( x.ModelAsset?.Path, modelPath ) );
	}
}
