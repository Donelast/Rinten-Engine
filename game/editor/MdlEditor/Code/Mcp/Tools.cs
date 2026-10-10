using Editor.Mcp;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Editor.MdlEditor;

[McpToolset( "animgraph",
	"Create and edit the animation graph a model carries in its .mdl, without opening the AnimGraph window. " +
	"A graph is kept in the model it animates, so every tool takes the model's path. " +
	"Discover node schemas, start a model's graph, inspect every nested graph and variation, apply transactional edits, and compile." )]
public static partial class McpTools
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters = { new JsonStringEnumConverter( null, true ) }
	};
}
