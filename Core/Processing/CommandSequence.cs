#nullable enable

namespace Varcom.Processing;

// Reused after completion; generation distinguishes external signals from earlier uses.
internal sealed class CommandSequence
{
	internal CommandSequence? Previous;
	internal CommandSequence? Next;
	internal int Head = -1;
	internal int Tail = -1;
	internal int InsertionTail = -1;
	internal ulong WaitUntilFrame;
	internal bool Suspended;
	internal ulong Generation;
	internal ulong ResumeFrame;
}
