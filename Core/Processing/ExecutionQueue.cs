#nullable enable

using System;

namespace Varcom.Processing;

// Sequences share array-backed command nodes. Insertion never moves pending invocations.
internal sealed class ExecutionQueue(int capacity)
{
	private struct Node
	{
		internal QueuedInvocation Invocation;
		internal int Next;
	}

	private Node[] _nodes = new Node[capacity];
	private int _allocated;
	private int _free = -1;
	private CommandSequence? _freeSequences;
	private CommandSequence? _insertionTail;
	internal CommandSequence? First { get; private set; }
	internal CommandSequence? Last { get; private set; }

	internal CommandSequence CreateSequence(bool insert = false)
	{
		var sequence = _freeSequences;
		if (sequence == null)
			sequence = new CommandSequence();
		else
		{
			_freeSequences = sequence.Next;
			sequence.Next = null;
		}

		var previous = insert ? _insertionTail : Last;
		var next = previous == null ? First : previous.Next;
		sequence.Previous = previous;
		sequence.Next = next;

		if (previous == null)
			First = sequence;
		else previous.Next =
			sequence;

		if (next == null)
			Last = sequence;
		else
			next.Previous = sequence;

		if (insert)
			_insertionTail = sequence;

		return sequence;
	}

	internal void Enqueue(CommandSequence sequence, QueuedInvocation invocation)
	{
		var index = Allocate(invocation);
		if (sequence.Tail < 0)
			sequence.Head = index;
		else
			_nodes[sequence.Tail].Next = index;
		sequence.Tail = index;
	}

	internal void Insert(CommandSequence sequence, QueuedInvocation invocation)
	{
		var index = Allocate(invocation);
		if (sequence.InsertionTail < 0)
		{
			_nodes[index].Next = sequence.Head;
			sequence.Head = index;
		}
		else
		{
			_nodes[index].Next = _nodes[sequence.InsertionTail].Next;
			_nodes[sequence.InsertionTail].Next = index;
		}

		if (_nodes[index].Next < 0)
			sequence.Tail = index;

		sequence.InsertionTail = index;
	}

	internal void ResetInsertion() => _insertionTail = null;

	internal QueuedInvocation Dequeue(CommandSequence sequence)
	{
		var index = sequence.Head;
		var invocation = _nodes[index].Invocation;
		sequence.Head = _nodes[index].Next;

		if (sequence.Head < 0)
			sequence.Tail = -1;

		_nodes[index] = new Node { Next = _free };
		_free = index;

		return invocation;
	}

	internal void Remove(CommandSequence sequence)
	{
		if (sequence.Previous == null)
			First = sequence.Next;
		else
			sequence.Previous.Next = sequence.Next;

		if (sequence.Next == null)
			Last = sequence.Previous;
		else
			sequence.Next.Previous = sequence.Previous;

		sequence.Previous = null;
		sequence.Next = _freeSequences;
		sequence.InsertionTail = -1;
		sequence.WaitUntilFrame = 0;
		sequence.Suspended = false;
		sequence.Generation = 0;
		sequence.ResumeFrame = 0;
		_freeSequences = sequence;
	}

	private int Allocate(QueuedInvocation invocation)
	{
		int index;
		if (_free >= 0)
		{
			index = _free;
			_free = _nodes[index].Next;
		}
		else
		{
			if (_allocated == _nodes.Length)
				Array.Resize(ref _nodes, Math.Max(4, _nodes.Length * 2));

			index = _allocated++;
		}

		_nodes[index] = new Node { Invocation = invocation, Next = -1 };
		return index;
	}
}
