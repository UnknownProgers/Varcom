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

	[EditorBrowsable(EditorBrowsableState.Never)]
	public IDisposable Register<TCommand>(CommandInfo info,
		Func<IReadOnlyList<object?>, TCommand> factory,
		ICommandHandler<TCommand> handler) where TCommand : struct, ICommand
	{
		ArgumentNullException.ThrowIfNull(factory);
		ArgumentNullException.ThrowIfNull(handler);
		return Add(new TypedCommandBinding<TCommand>(PrepareInfo(info), factory, handler), typeof(TCommand));
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	public IDisposable RegisterMethod(CommandInfo info, Func<IReadOnlyList<object?>, CommandResult> invoke)
	{
		ArgumentNullException.ThrowIfNull(invoke);
		return Add(new MethodCommandBinding(PrepareInfo(info), invoke));
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	public IDisposable RegisterVariable<T>(VariableInfo info, Func<T> getter, Func<T, CommandResult>? setter = null)
	{
		ArgumentNullException.ThrowIfNull(info);
		ArgumentNullException.ThrowIfNull(getter);
		if (info.ValueType != typeof(T) || info.IsReadOnly != (setter == null))
			throw new ArgumentException("Variable type and read-only metadata must match its binding.", nameof(info));

		var parameters = new[] { new CommandParameter("value", typeof(T), true) };
		var commandInfo = PrepareInfo(new CommandInfo(info.Name, info.Description, parameters), true);
		return Add(new VariableCommandBinding<T>(commandInfo, info, getter, setter));
	}

	public IReadOnlyList<CommandEntry> GetEntries() => Array.AsReadOnly(_names.Values
		.Select(binding => new CommandEntry(binding.Info, (binding as IVariableBinding)?.VariableInfo))
		.ToArray());

	public CommandResult ReadVariable(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (!_names.TryGetValue(name, out var binding) || binding is not IVariableBinding variable)
			return CommandResult.Fail(new CommandError(CommandErrorCode.Unavailable, $"Variable '{name}' is unavailable."));
		try
		{
			return variable.Read();
		}
		catch (Exception ex)
		{
			return CommandResult.Fail(ex.Message);
		}
	}

	internal bool TryGet(string name, [NotNullWhen(true)] out CommandBinding? binding) => _names.TryGetValue(name, out binding);

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
		if (commandType != null)
			_types.Add(commandType, binding);

		return new Registration(() =>
		{
			_names.Remove(name);
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
		if (!ValidName(info.Name)) throw new ArgumentException("Invalid command name.", nameof(info));
		var optional = false;
		foreach (var parameter in info.Parameters)
		{
			if (parameter == null || string.IsNullOrWhiteSpace(parameter.Name) || !ArgumentParser.Supports(parameter.ValueType))
				throw new ArgumentException("Unsupported command parameter.", nameof(info));
			if (optional && !parameter.IsOptional)
				throw new ArgumentException("Required arguments must precede optional arguments.", nameof(info));
			optional |= parameter.IsOptional;
			if (!variable && parameter.IsOptional &&
			    (parameter.DefaultValue == null
				    ? parameter.ValueType.IsValueType
				    : !parameter.ValueType.IsInstanceOfType(parameter.DefaultValue)))
				throw new ArgumentException("Optional default does not match parameter type.", nameof(info));
		}

		if (info.Parameters.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != info.Parameters.Count)
			throw new ArgumentException("Duplicate parameter names.", nameof(info));
	}

	private static bool ValidName(string? name)
	{
		if (string.IsNullOrEmpty(name)) return false;
		var start = name[0] is '+' or '-' ? 1 : 0;
		if (start == name.Length || !char.IsAsciiLetter(name[start])) return false;
		return name.Skip(start + 1).All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.');
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
