namespace Rinten;

partial class Model
{
	//Sequence names, asked of CoreSkeletons each time so they stay current after recompiles
	internal IReadOnlyList<string> SequenceNames => CoreSkeletons.SequenceNames( this );
}
