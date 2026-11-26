using System;
using System.Collections.Generic;

namespace Varcom;

/// <summary>Registers a generated owner or module atomically; rolls back if one registration fails.</summary>
public static class RegistrationGroup
{
	public static IDisposable Create(params Func<IDisposable>[] registrations)
	{
		ArgumentNullException.ThrowIfNull(registrations);
		var added = new List<IDisposable>();
		try
		{
			foreach (var register in registrations)
			{
				ArgumentNullException.ThrowIfNull(register);
				added.Add(register() ?? throw new InvalidOperationException("Registration factory returned null."));
			}

			return new Group(added);
		}
		catch
		{
			for (var i = added.Count - 1; i >= 0; i--) added[i].Dispose();
			throw;
		}
	}

	private sealed class Group(List<IDisposable> registrations) : IDisposable
	{
		private List<IDisposable>? _registrations = registrations;

		public void Dispose()
		{
			var items = _registrations;
			_registrations = null;
			if (items == null) return;
			for (var i = items.Count - 1; i >= 0; i--) items[i].Dispose();
		}
	}
}
