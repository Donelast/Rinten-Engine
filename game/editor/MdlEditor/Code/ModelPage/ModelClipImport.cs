using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Editor.MdlEditor;

//Adds an export's animations to the definition as clips, skipping ones already listed
static class ModelClipImport
{
	/// <summary>Adds every take in this export the definition does not already list, and says how many that was.</summary>
	public static int Add( ModelDocContext ctx, string file, float scale = 1.0f, bool whole = false )
	{
		if ( ctx?.Definition is null || string.IsNullOrWhiteSpace( file ) )
			return 0;

		var takes = ModelSourceCatalog.Takes( ctx.Asset, file );

		if ( takes.Count == 0 )
		{
			if ( !whole ) return 0;

			takes = [ModelSourceCatalog.Whole( file )];
		}

		//Named, and checked against what is already listed, the same way "Create Model" on a mesh in the asset browser does it.
		return ModelTakes.AddClips( ctx.Definition, file, takes, scale );
	}

	/// <summary>Adds one clip from an export (first take not yet present) and returns its index, or -1</summary>
	public static int AddOne( ModelDocContext ctx, string file, float scale = 1.0f )
	{
		if ( ctx?.Definition is null || string.IsNullOrWhiteSpace( file ) )
			return -1;

		var takes = ModelSourceCatalog.Takes( ctx.Asset, file );

		//Nothing to be. An SMD or a compiled clip is listed as its one take already, so an empty list is an FBX or a glTF with no animation stack in it
		//at all - a mesh exported without its actions. Added as the whole file anyway, it was a clip that failed to compile every time the model did.
		if ( takes.Count == 0 )
			return -1;

		var at = takes.FindIndex( take => !ModelTakes.Has( ctx.Definition, file, take ) );

		ModelTakes.AddClip( ctx.Definition, file, takes[at < 0 ? 0 : at], scale );

		return ctx.Definition.Clips.Count - 1;
	}
}
