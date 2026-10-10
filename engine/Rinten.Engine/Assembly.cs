global using Rinten;
global using Rinten.Diagnostics;
global using Rinten.Physics;
global using Rinten.Tasks;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using static Rinten.Internal.GlobalSystemNamespace;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo( "Rinten.Test.Unit" )]
[assembly: InternalsVisibleTo( "Rinten.Test.Engine" )]
[assembly: InternalsVisibleTo( "Rinten.Test.Integration" )]
[assembly: InternalsVisibleTo( "Benchmark" )]
[assembly: InternalsVisibleTo( "Rinten.GameInstance" )]
[assembly: InternalsVisibleTo( "Rinten.Tools" )]
[assembly: InternalsVisibleTo( "Rinten.Code" )]

//The porters read legacy content through the engine's own asset pipeline - CoreMeshImport - which is internal and stays that way.
[assembly: InternalsVisibleTo( "Rinten.Porting.SBox" )]
[assembly: InternalsVisibleTo( "Rinten.AppSystem" )]
[assembly: InternalsVisibleTo( "MenuBuild" )]
[assembly: InternalsVisibleTo( "Rinten.Mounting.Test" )]
[assembly: InternalsVisibleTo( "Rinten-Server" )]
[assembly: InternalsVisibleTo( "Rinten" )]
[assembly: InternalsVisibleTo( "Rinten-Standalone" )]
[assembly: InternalsVisibleTo( "Rinten-Web" )]
[assembly: InternalsVisibleTo( "Rinten-Android" )]
[assembly: InternalsVisibleTo( "CreateGameCache" )]

//An agent's execute_code snippet - compiled in the editor to answer one question about the live
//engine, which is usually a question about something internal. See Editor.Mcp.ExecuteCode.
[assembly: InternalsVisibleTo( "McpSnippet" )]

[assembly: TasksPersistOnContextReset]

//Moved filesystem to its own assembly
[assembly: TypeForwardedTo( typeof( BaseFileSystem ) )]
[assembly: TypeForwardedTo( typeof( FileWatch ) )]

