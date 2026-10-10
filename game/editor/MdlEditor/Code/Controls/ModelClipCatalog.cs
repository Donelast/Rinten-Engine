using System;
using System.Collections.Generic;
using System.IO;

namespace Editor.MdlEditor;

//Sequence names a graph node can play, from the target, a clip source, and open model tabs.
static class ModelClipCatalog
{
	public static string Key( string pathOrName )
	{
		if ( string.IsNullOrWhiteSpace( pathOrName ) )
			return "";

		var name = Path.GetFileNameWithoutExtension( pathOrName.Replace( '\\', '/' ) );
		return string.IsNullOrWhiteSpace( name ) ? pathOrName : name;
	}

	public static IEnumerable<string> Names( Data graph )
	{
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var name in Collect( graph ) )
		{
			var key = Key( name );
			if ( string.IsNullOrWhiteSpace( key ) || !seen.Add( key ) )
				continue;

			yield return key;
		}
	}

	//The graph's own clip source first, then every model open in a model editor - see OpenModels
	//- because a clip being added to the model is one the graph will want a moment later.
	static IEnumerable<string> Collect( Data graph )
	{
		var variation = graph?.SelectedVariation;
		var source = variation?.ClipSource ?? variation?.TargetModel;
		if ( source is not null )
		{
			foreach ( var name in source.AnimationNames )
				yield return name;
		}

		foreach ( var editor in OpenModels.All )
		{
			if ( editor.ModelDefinition is { } definition )
			{
				foreach ( var clip in definition.AllClips() )
				{
					if ( clip is null || !clip.Enabled || string.IsNullOrWhiteSpace( clip.SequenceName ) )
						continue;

					yield return clip.SequenceName;
				}
			}

			if ( editor.OpenModel is { } compiled )
			{
				foreach ( var name in compiled.AnimationNames )
					yield return name;
			}
		}
	}
}
