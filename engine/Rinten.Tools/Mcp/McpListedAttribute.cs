using System;

namespace Editor.Mcp;

//Puts a tool in the client's tool list alongside the other built in entry points. Internal on purpose - addon tools stay
//out of that list and get found through search_tools, so a list the client fetched once never goes stale as code hotloads.
[AttributeUsage( AttributeTargets.Method )]
internal class McpListedAttribute : Attribute
{
}
