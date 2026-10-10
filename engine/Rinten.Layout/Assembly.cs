using System.Runtime.CompilerServices;

//Small methods are AggressiveInlining as Mono's web interpreter barely inlines; mark new ones too

//The engine drives the layout through the seam in Rinten.Engine/Systems/UI; the tests
//and the benchmark build trees on the nodes directly. Nothing else needs to see inside.
[assembly: InternalsVisibleTo( "Rinten.Engine" )]
[assembly: InternalsVisibleTo( "Rinten.Test.Unit" )]
[assembly: InternalsVisibleTo( "Rinten.Test.Engine" )]
[assembly: InternalsVisibleTo( "Rinten.Test.Integration" )]
[assembly: InternalsVisibleTo( "Benchmark" )]
