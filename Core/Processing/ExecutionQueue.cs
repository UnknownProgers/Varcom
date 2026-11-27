using System;

namespace Varcom.Processing;

// Array-backed linked nodes: insertion never moves pending invocations.
internal sealed class ExecutionQueue(int capacity)
{
	private struct Node
	{
		internal QueuedInvocation Invocation;
		internal int Next;
	}

	private Node[] _nodes = CreateNodes(capacity);
	private int _allocated;
	private int _free = -1;
	private int _head = -1;
	private int _tail = -1;
	private int _insertionTail = -1;
	internal int First => _head;

	internal int Enqueue(QueuedInvocation invocation)
	{
		var index = Allocate(invocation);
		if (_tail < 0)
			_head = index;
		else
			_nodes[_tail].Next = index;
		_tail = index;
		return index;
	}

	internal void Insert(QueuedInvocation invocation)
	{
		var index = Allocate(invocation);
		if (_insertionTail < 0)
		{
			_nodes[index].Next = _head;
			_head = index;
		}
		else
		{
			_nodes[index].Next = _nodes[_insertionTail].Next;
			_nodes[_insertionTail].Next = index;
		}

		if (_nodes[index].Next < 0) _tail = index;
		_insertionTail = index;
	}

	// Enqueue from handlers lands after this marker and waits for the next Update.
	internal int BeginBatch() => Enqueue(default);

	internal void ResetInsertion() => _insertionTail = -1;

	internal QueuedInvocation Dequeue()
	{
		var index = _head;
		var invocation = _nodes[index].Invocation;
		_head = _nodes[index].Next;

		if (_head < 0)
			_tail = -1;

		Release(index);
		return invocation;
	}

	// Callback failure can leave the batch boundary further into the queue.
	internal void EndBatch(int boundary)
	{
		if (_head == boundary)
		{
			Dequeue();
			return;
		}

		var previous = _head;
		while (_nodes[previous].Next != boundary)
			previous = _nodes[previous].Next;

		_nodes[previous].Next = _nodes[boundary].Next;
		if (_tail == boundary)
			_tail = previous;

		Release(boundary);
	}

	private static Node[] CreateNodes(int capacity)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(capacity);
		return new Node[capacity];
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

	private void Release(int index)
	{
		_nodes[index] = new Node { Next = _free };
		_free = index;
	}
}
