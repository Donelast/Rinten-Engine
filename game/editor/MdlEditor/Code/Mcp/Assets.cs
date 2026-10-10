using Editor.TreeBrowser;
using Editor.Mcp;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor.MdlEditor;

public static partial class McpTools
{
	/// <summary>Start the graph of a model that has none, with a Graph Output node, ready for headless editing. A graph is kept in the model it animates, so it is written into that model's .mdl. Returns the revision required by animgraph_edit.</summary>
	/// <param name="path">The model (.mdl) the graph is for, relative to the project's assets folder.</param>
	/// <param name="skeleton">Optional target skeleton path. A graph can't compile without one.</param>
	[McpTool( "animgraph_create" )]
	public static AssetResult Create( string path, string skeleton = "" )
	{
		var session = LoadSession( path );
		EnsureWritableProjectAsset( session );

		if ( session.Carried )
			throw new Exception( $"'{session.Model.Path}' already carries a graph - inspect and edit it instead" );

		if ( !string.IsNullOrWhiteSpace( skeleton ) )
			session.Graph.TargetSkeleton = skeleton;

		var result = session.Save();

		return CreateAssetResult( session, result );
	}

	/// <summary>Inspect an AnimGraph without opening its editor. Returns stable graph locators, the variation tree, parameters, nodes, properties, plugs and connections. Leave graph empty to return the root and every nested graph; pass a locator from a previous result to narrow it. Node data that varies per variation is resolved for the given variation.</summary>
	/// <param name="path">The model (.mdl) whose graph it is.</param>
	/// <param name="graph">Optional graph locator from this tool, such as root, virtual:id, or root/node:id/state:0.</param>
	/// <param name="variation">Optional variation to resolve node data for. Defaults to Default.</param>
	[McpTool.ReadOnly( "animgraph_inspect" )]
	public static InspectionResult Inspect( string path, string graph = "", string variation = "" )
	{
		var session = LoadSession( path );
		session.Normalize();
		SelectVariation( session.Graph, variation );
		var allReferences = Graphs( session.Graph );
		var references = allReferences;

		if ( !string.IsNullOrWhiteSpace( graph ) )
		{
			references = references
				.Where( reference => string.Equals( reference.Id, graph, StringComparison.OrdinalIgnoreCase ) )
				.ToArray();

			if ( references.Count == 0 )
				throw new Exception( $"Unknown graph '{graph}'" );
		}

		var graphLocators = allReferences.ToDictionary( reference => reference.Graph, reference => reference.Id, ReferenceEqualityComparer.Instance );

		return new InspectionResult
		{
			Path = session.Model.Path,
			Revision = session.Revision,
			Model = session.Graph.TargetModel?.Name,
			Skeleton = session.Graph.TargetSkeleton,
			Variation = session.Graph.SelectedVariationID,
			Variations = DescribeVariations( session.Graph ),
			Parameters = session.Graph.AllParameters.Select( parameter => new ParameterInfo
			{
				Id = parameter.Identifier,
				Name = parameter.Name,
				Group = parameter.Group,
				Type = parameter.Type.ToString(),
				Virtual = parameter.IsVirtual,
				Graph = parameter is VirtualParameter virtualParameter ? graphLocators.GetValueOrDefault( virtualParameter.Graph ) : null,
				Values = ParameterValues( parameter )
			} ).ToArray(),
			Graphs = references.Select( reference => InspectGraph( reference, session.Graph ) ).ToArray()
		};
	}

	/// <summary>Compile a model's graph headlessly. By default this validates without writing; set save to write the graph into its model and the compiled graph beside it. Every variation is always compiled - variation picks which one's warnings come back. Returns warnings tied to editor node ids and runtime node indices.</summary>
	/// <param name="path">The model (.mdl) whose graph it is.</param>
	/// <param name="save">Write the graph into the model and the compiled graph beside it.</param>
	/// <param name="expectedRevision">Required when saving; get it from animgraph_inspect.</param>
	/// <param name="variation">Optional variation to report results for. Defaults to Default.</param>
	[McpTool( "animgraph_compile" )]
	public static AssetResult Compile( string path, bool save = false, string expectedRevision = "", string variation = "" )
	{
		var session = LoadSession( path );
		SelectVariation( session.Graph, variation );

		if ( save )
		{
			EnsureWritableProjectAsset( session );
			CheckRevision( session, expectedRevision );
		}

		var result = save ? session.Save( expectedRevision: expectedRevision ) : session.Compile();
		return CreateAssetResult( session, result );
	}

	private static AssetResult CreateAssetResult( Session session, Compiler.Result result ) => new()
	{
		Path = session.Model.Path,
		SourceFile = session.Model.GetSourceFile( true ),
		Revision = session.Revision,
		Variation = session.Graph?.SelectedVariationID,
		AssetCompiled = CompiledBeside( session ),
		Compile = ToResult( result )
	};

	//Whether the compiled graph is there beside the model, for a result that says what was written.
	private static bool? CompiledBeside( Session session )
		=> session.Model?.GetSourceFile( true ) is { } source ? System.IO.File.Exists( AnimGraph.ModelGraphFile( source ) ) : null;

	private static GraphInfo InspectGraph( GraphReference reference, Data root )
	{
		return new GraphInfo
		{
			Id = reference.Id,
			Owner = reference.Owner,
			Kind = reference.Graph.Kind.ToString(),
			Nodes = reference.Graph.Nodes.OfType<Node>().Select( node =>
			{
				return new NodeInfo
				{
					Id = node.Identifier,
					Type = node.GetType().Name,
					Title = node.DisplayInfo.Name,
					Position = node.Position,
					Properties = PropertyValues( node, nameof( Node.Identifier ), nameof( Node.Position ),
						DefaultVariationDataProperty, VariationOverridesProperty ),
					Variation = DescribeNodeVariation( node as VariationDataNode, root ),
					Inputs = node.Inputs.Select( input => new PlugInfo
					{
						Name = input.Identifier,
						Title = input.DisplayInfo.Name,
						Type = LinkType( input.Type ),
						Connection = input.ConnectedOutput is { } output
							? new ConnectionInfo { Node = output.Node.Identifier, Output = output.Identifier }
							: null
					} ).ToArray(),
					Outputs = node.Outputs.Select( output => new PlugInfo
					{
						Name = output.Identifier,
						Title = output.DisplayInfo.Name,
						Type = LinkType( output.Type )
					} ).ToArray()
				};
			} ).ToArray()
		};
	}

	private static JsonObject PropertyValues( object target, params string[] excluded )
	{
		var result = new JsonObject();

		foreach ( var property in target.GetType().GetProperties( BindingFlags.Instance | BindingFlags.Public ) )
		{
			if ( !property.CanRead
				|| property.GetCustomAttribute<JsonIgnoreAttribute>() is not null
				|| excluded.Contains( property.Name, StringComparer.OrdinalIgnoreCase )
				|| typeof( Data ).IsAssignableFrom( property.PropertyType )
				|| property.PropertyType == typeof( NodeInput ) )
			{
				continue;
			}

			if ( typeof( IList ).IsAssignableFrom( property.PropertyType )
				&& CollectionElementType( property ) == typeof( NodeInput ) )
			{
				continue;
			}

			if ( property.GetValue( target ) is not { } value )
				continue;

			var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
			var serialized = JsonSerializer.SerializeToNode( value, property.PropertyType, JsonOptions );
			RemoveManagedProperties( serialized, property.PropertyType );
			result[name] = serialized;
		}

		return result;
	}

	private static void RemoveManagedProperties( JsonNode node, Type type )
	{
		if ( node is JsonArray items && typeof( IList ).IsAssignableFrom( type ) )
		{
			var itemType = type.GetGenericArguments().FirstOrDefault();
			if ( itemType is null )
				return;

			foreach ( var item in items )
				RemoveManagedProperties( item, itemType );

			return;
		}

		if ( node is not JsonObject document )
			return;

		foreach ( var property in type.GetProperties( BindingFlags.Instance | BindingFlags.Public ) )
		{
			var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;

			if ( typeof( Data ).IsAssignableFrom( property.PropertyType ) || property.PropertyType == typeof( NodeInput ) )
			{
				document.Remove( name );
				continue;
			}

			if ( document[name] is { } value )
				RemoveManagedProperties( value, property.PropertyType );
		}
	}

	private static JsonObject ParameterValues( IParameter parameter )
		=> PropertyValues( parameter, nameof( IParameter.Identifier ), nameof( IParameter.Name ), nameof( IParameter.Group ), nameof( IParameter.Type ), nameof( Parameter.Graph ) );
}
