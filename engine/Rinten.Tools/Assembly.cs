using System.Runtime.CompilerServices;

[assembly: TasksPersistOnContextReset]

[assembly: InternalsVisibleTo( "Rinten.Test.Unit" )]
[assembly: InternalsVisibleTo( "Rinten.Test.Engine" )]
[assembly: InternalsVisibleTo( "Rinten.Test.Integration" )]
[assembly: InternalsVisibleTo( "Rinten.AppSystem" )]

//The launcher is part of this executable now rather than one of its own, and it still reaches for the editor's cookie and backend to set itself up
[assembly: InternalsVisibleTo( "rinten" )]
[assembly: InternalsVisibleTo( "Base Editor Library" )]

//An agent's execute_code snippet - compiled in the editor to answer one question about the live
//engine, which is usually a question about something internal. See Editor.Mcp.ExecuteCode.
[assembly: InternalsVisibleTo( "McpSnippet" )]
