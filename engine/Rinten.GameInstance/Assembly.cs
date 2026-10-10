global using Rinten;
global using Rinten.Engine;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using static Rinten.Internal.GlobalGameNamespace;

using System.Runtime.CompilerServices;

[assembly: TasksPersistOnContextReset]
[assembly: InternalsVisibleTo( "Rinten.Tools" )]
[assembly: InternalsVisibleTo( "Rinten.AppSystem" )]
[assembly: InternalsVisibleTo( "Rinten-Web" )]
[assembly: InternalsVisibleTo( "Rinten.Test.Unit" )]
[assembly: InternalsVisibleTo( "Rinten.Test.Engine" )]
[assembly: InternalsVisibleTo( "Rinten.Test.Integration" )]
