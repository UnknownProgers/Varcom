using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Varcom.Bindings;
using Varcom.Contracts;

namespace Varcom;

public sealed class CommandRegistry
{
	private readonly Dictionary<string, CommandBinding> _names = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<Type, CommandBinding> _types = new();
	private readonly Dictionary<int, List<CommandBinding>> _nameHashes = new();
	private IReadOnlyList<CommandEntry>? _entries;
	private Dictionary<(Type Owner, int Member), CommandEntry>? _metadata;
	private IReadOnlyList<CommandEntry>? _metadataEntries;

	[EditorBrowsable(EditorBrowsableState.Never)]
	public IDisposable Register<TCommand>(CommandInfo info,
		Func<CommandArguments, TCommand> factory,
		ICommandHandler<TCommand> handler) where TCommand : struct, ICommand
	{
		ArgumentNullException.ThrowIfNull(factory);
		ArgumentNullException.ThrowIfNull(handler);
		return Add(new TypedCommandBinding<TCommand>(PrepareInfo(info), factory, handler), typeof(TCommand));
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	public IDisposable RegisterMethod(CommandInfo info, Func<CommandArguments, CommandResult> invoke)
	{
		ArgumentNullException.ThrowIfNull(invoke);
		return Add(new MethodCommandBinding(PrepareInfo(info), invoke));
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	public IDisposable RegisterVariable<T>(VariableInfo info, Func<T> getter, Func<T, CommandResult>? setter = null) where T : struct
	{
		ArgumentNullException.ThrowIfNull(info);
		ArgumentNullException.ThrowIfNull(getter);
		if (info.ValueType != typeof(T) || info.IsReadOnly != (setter == null))
			throw new ArgumentException("Variable type and read-only metadata must match its binding.", nameof(info));

		var parameters = new[] { CommandParameter.Create<T>("value", true) };
		var commandInfo = PrepareInfo(new CommandInfo(info.Name, info.Description, parameters), true);
		return Add(new VariableCommandBinding<T>(commandInfo, info, getter, setter));
	}

	/// <summary>Whether a command or variable with this name is currently registered.</summary>
	public bool IsAvailable(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		return _names.ContainsKey(name);
	}

	/// <summary>Returns currently registered commands and variables.</summary>
	public IReadOnlyList<CommandEntry> GetEntries() => _entries ??= Array.AsReadOnly(_names.Values
		.Select(binding => new CommandEntry(binding.Info, (binding as IVariableBinding)?.VariableInfo))
		.ToArray());

	/// <summary>Returns explicitly generated declarations, including those without active registrations.</summary>
	public IReadOnlyList<CommandEntry> GetMetadata() => _metadata == null
		? Array.Empty<CommandEntry>()
		: _metadataEntries ??= Array.AsReadOnly(_metadata.Values.ToArray());

	[EditorBrowsable(EditorBrowsableState.Never)]
	public bool ContainsMetadata(Type declarationType, int memberId)
	{
		ArgumentNullException.ThrowIfNull(declarationType);
		return _metadata?.ContainsKey((declarationType, memberId)) == true;
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	public void AddMetadata(Type declarationType, int memberId, CommandInfo info, VariableInfo? variable = null)
	{
		ArgumentNullException.ThrowIfNull(declarationType);
		ArgumentNullException.ThrowIfNull(info);
		if (ContainsMetadata(declarationType, memberId))
			return;

		var preparedInfo = PrepareInfo(info, variable != null);
		_metadata ??= new Dictionary<(Type Owner, int Member), CommandEntry>();
		_metadata.Add((declarationType, memberId), new CommandEntry(preparedInfo, variable));
		_metadataEntries = null;
	}

	public CommandResult ReadVariable(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (!_names.TryGetValue(name, out var binding) || binding is not IVariableBinding variable)
			return CommandResult.Fail(new CommandError(CommandErrorCode.Unavailable, "Variable is unavailable."));
		try
		{
			return variable.Read();
		}
		catch (Exception ex)
		{
			return CommandResult.Fail(ex.Message);
		}
	}

	internal bool TryGet(ReadOnlySpan<char> name, [NotNullWhen(true)] out CommandBinding? binding)
	{
		if (_nameHashes.TryGetValue(string.GetHashCode(name, StringComparison.OrdinalIgnoreCase), out var bucket))
			foreach (var candidate in bucket)
				if (name.Equals(candidate.Info.Name, StringComparison.OrdinalIgnoreCase))
				{
					binding = candidate;
					return true;
				}

		binding = null;
		return false;
	}

	internal bool TryGet<TCommand>([NotNullWhen(true)] out TypedCommandBinding<TCommand>? binding) where TCommand : struct, ICommand
	{
		if (_types.TryGetValue(typeof(TCommand), out var found))
		{
			binding = (TypedCommandBinding<TCommand>)found;
			return true;
		}

		binding = null;
		return false;
	}

	private IDisposable Add(CommandBinding binding, Type? commandType = null)
	{
		var name = binding.Info.Name;
		if (_names.ContainsKey(name) || commandType != null && _types.ContainsKey(commandType))
			throw new ArgumentException($"Command '{name}' or its type is already registered.");

		_names.Add(name, binding);
		var hash = string.GetHashCode(name.AsSpan(), StringComparison.OrdinalIgnoreCase);
		if (!_nameHashes.TryGetValue(hash, out var bucket))
			_nameHashes.Add(hash, bucket = new List<CommandBinding>());

		bucket.Add(binding);
		_entries = null;
		if (commandType != null)
			_types.Add(commandType, binding);

		return new Registration(() =>
		{
			_names.Remove(name);
			bucket.Remove(binding);
			if (bucket.Count == 0)
				_nameHashes.Remove(hash);

			_entries = null;
			if (commandType != null)
				_types.Remove(commandType);

			binding.Deactivate();
		});
	}

	private static CommandInfo PrepareInfo(CommandInfo info, bool variable = false)
	{
		Validate(info, variable);
		return info with { Parameters = Array.AsReadOnly(info.Parameters.ToArray()) };
	}

	private static void Validate(CommandInfo info, bool variable)
	{
		ArgumentNullException.ThrowIfNull(info);
		ArgumentNullException.ThrowIfNull(info.Parameters);
		if (!ValidName(info.Name))
			throw new ArgumentException("Invalid command name.", nameof(info));

		var optional = false;
		foreach (var parameter in info.Parameters)
		{
			if (parameter == null || string.IsNullOrWhiteSpace(parameter.Name) || !ArgumentParser.Supports(parameter.ValueType))
				throw new ArgumentException("Unsupported command parameter.", nameof(info));

			if (optional && !parameter.IsOptional)
				throw new ArgumentException("Required arguments must precede optional arguments.", nameof(info));

			optional |= parameter.IsOptional;
			if (!variable
			    && parameter.IsOptional
			    && (parameter.DefaultValue == null
				    ? parameter.ValueType.IsValueType && parameter.ValueType != typeof(CmdString)
				    : !parameter.ValueType.IsInstanceOfType(parameter.DefaultValue)))
				throw new ArgumentException("Optional default does not match parameter type.", nameof(info));
		}

		if (info.Parameters.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != info.Parameters.Count)
			throw new ArgumentException("Duplicate parameter names.", nameof(info));
	}

	private static bool ValidName(string? name)
	{
		if (string.IsNullOrEmpty(name))
			return false;

		var start = name[0] is '+' or '-' ? 1 : 0;
		if (start == name.Length || !char.IsAsciiLetter(name[start]))
			return false;

		return name
			.Skip(start + 1)
			.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.');
	}

	private sealed class Registration(Action release) : IDisposable
	{
		private Action? _release = release;

		public void Dispose()
		{
			var action = _release;
			_release = null;
			action?.Invoke();
		}
	}
}
