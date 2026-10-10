using System;
using System.IO;
using System.Text.Json;

namespace Editor.MdlEditor;

/// <summary>Building the graph a model carries, from outside the graph editor.</summary>
public static class GraphBuild
{
	[BuildsAssetType( "mdl" )]
	public static bool Build( Asset model )
	{
		if ( model?.GetSourceFile( true ) is not { Length: > 0 } source || !File.Exists( source ) ) return false;

		var carried = GraphText( source );
		var compiled = AnimGraph.ModelGraphFile( source );

		//Asked because the definition is newer than the compiled graph - which an edit to the model's meshes or clips makes it just as much
		//as an edit to its graph. Where the graph is the one that was compiled, it is current: said so by its date, and not compiled again.
		if ( carried is not null && Session.StampOf( carried ) == Session.StampIn( compiled ) )
		{
			File.SetLastWriteTimeUtc( compiled, DateTime.UtcNow );
			return true;
		}

		var session = Session.ForModel( model );
		if ( !session.HasHome ) return false;

		if ( !session.Carried )
		{
			//Strips an old graph file name from the file on disk, only when it actually names one
			if ( carried is null && NamesOldGraph( source ) && ModelGraphResolver.DefinitionAt( model ) is { } definition )
				model.SaveToDiskUnbuilt( definition );

			return false;
		}

		if ( carried is null )
		{
			//In memory and not on the disk: a graph just read out of the file the model used
			//to name - see ModelDefinition's version 2. Written into the model for good.
			session.Write();
		}
		else
		{
			session.Source = Session.StampOf( carried );
		}

		session.WriteCompiled();

		return File.Exists( compiled );
	}

	//The graph document as the definition on disk has it written, or null where it carries none. Whether
	//the file still names a graph the way models used to, by path - see ModelDefinition.Upgrader_v2.
	private static bool NamesOldGraph( string definition )
	{
		try
		{
			using var document = JsonDocument.Parse( File.ReadAllText( definition ), new JsonDocumentOptions
			{
				MaxDepth = 512,
				CommentHandling = JsonCommentHandling.Skip,
				AllowTrailingCommas = true,
			} );

			foreach ( var property in document.RootElement.EnumerateObject() )
			{
				if ( string.Equals( property.Name, "AnimationGraph", StringComparison.OrdinalIgnoreCase ) ) return true;
			}

			return false;
		}
		catch ( Exception e ) when ( e is IOException or JsonException or InvalidOperationException )
		{
			return false;
		}
	}

	private static string GraphText( string definition )
	{
		try
		{
			using var document = JsonDocument.Parse( File.ReadAllText( definition ), new JsonDocumentOptions
			{
				MaxDepth = 512,
				CommentHandling = JsonCommentHandling.Skip,
				AllowTrailingCommas = true,
			} );

			foreach ( var property in document.RootElement.EnumerateObject() )
			{
				if ( !string.Equals( property.Name, nameof( ModelDefinition.Graph ), StringComparison.OrdinalIgnoreCase ) ) continue;

				return property.Value.ValueKind == JsonValueKind.Object ? property.Value.GetRawText() : null;
			}

			return null;
		}
		catch ( Exception e ) when ( e is IOException or JsonException or InvalidOperationException )
		{
			return null;
		}
	}
}
