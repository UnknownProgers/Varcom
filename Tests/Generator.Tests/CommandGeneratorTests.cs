using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Varcom.Contracts;
using Varcom.Generators;
using Xunit;

namespace Varcom.Tests.Generators;

public sealed class CommandGeneratorTests
{
	private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
		.Split(Path.PathSeparator).Append(typeof(ICommand).Assembly.Location).Distinct()
		.Select(path => MetadataReference.CreateFromFile(path)).ToArray();

	[Theory]
	[MemberData(nameof(Declarations))]
	public void Generate_ValidatesDeclaration(string scenario, string source, bool invalid)
	{
		var compilation = CreateCompilation(source);
		GeneratorDriver driver = CSharpGeneratorDriver.Create(new CommandGenerator().AsSourceGenerator());
		driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
		if (invalid)
		{
			Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "VARCOM001" && diagnostic.Severity == DiagnosticSeverity.Error);
			return;
		}

		var errors = output.GetDiagnostics().Concat(diagnostics).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
			.ToArray();
		Assert.True(errors.Length == 0, $"{scenario}: {string.Join(Environment.NewLine, errors.Select(error => error.ToString()))}");
		using var stream = new MemoryStream();
		Assert.True(output.Emit(stream).Success, $"{scenario}: generated consumer must compile");
	}

	[Theory]
	[MemberData(nameof(InvalidCommandUses))]
	public void Compile_RejectsNonStructCommands(string scenario, string source)
	{
		var diagnostics = CreateCompilation(source).GetDiagnostics();
		Assert.True(diagnostics.Any(diagnostic => diagnostic.Id == "CS0453" && diagnostic.Severity == DiagnosticSeverity.Error),
			$"{scenario}: expected a struct constraint error");
	}

	private static CSharpCompilation CreateCompilation(string source) => CSharpCompilation.Create("Consumer",
		new[] { CSharpSyntaxTree.ParseText(source) }, References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

	public static TheoryData<string, string, bool> Declarations()
	{
		var cases = new (string Name, string Source, bool Invalid)[]
		{
			("Mixed owner: command method, writable and readonly variables", """
			                                                                 using Varcom;
			                                                                 using Varcom.Contracts;
			                                                                 public sealed class Settings
			                                                                 {
			                                                                     [Var("gravity")] public float Gravity { get; set; } = 800;
			                                                                     [Var("profile")] public string Profile => "default";
			                                                                     [Cmd("reset")] public CommandResult Reset() { Gravity = 800; return CommandResult.Ok(); }
			                                                                     [Cmd("noop")] public void Noop() { }
			                                                                 }
			                                                                 """, false),
			("Command structs and enum defaults", """
			                                      using Varcom;
			                                      using Varcom.Contracts;
			                                      public enum Mode { One, Two }
			                                      [Cmd("set")] public readonly record struct SetCmd(Mode Mode = Mode.One, double Value = 2.5) : ICommand;
			                                      [CmdHandler(typeof(SetCmd))] public sealed class SetHandler : ICommandHandler<SetCmd>
			                                      { public CommandResult Handle(in SetCmd c) => CommandResult.Ok(); }
			                                      """, false),
			("Invalid parameter type", """
			                           using Varcom;
			                           using Varcom.Contracts;
			                           public class Owner { [Cmd("bad")] public void Bad(System.DateTime x) {} }
			                           """, true),
			("Private variable getter", """
			                            using Varcom;
			                            using Varcom.Contracts;
			                            public class Owner { [Var("bad")] private int Bad { get; set; } }
			                            """, true),
			("Mismatched handler", """
			                       using Varcom;
			                       using Varcom.Contracts;
			                       [Cmd("test")] public readonly record struct TestCmd(int X) : ICommand;
			                       [CmdHandler(typeof(TestCmd))] public class Handler { }
			                       """, true),
			("Async command", """
			                  using Varcom;
			                  using Varcom.Contracts;
			                  public class Owner { [Cmd("bad")] public async System.Threading.Tasks.Task Bad() { await System.Threading.Tasks.Task.Yield(); } }
			                  """, true),
			("Unsupported owner", """
			                      using Varcom;
			                      using Varcom.Contracts;
			                      public class Owner<T> { [Var("bad")] public int Bad { get; set; } }
			                      """, true),
			("Struct variable owner is rejected to avoid editing a copy", """
			                                                              using Varcom;
			                                                              using Varcom.Contracts;
			                                                              public struct Owner { [Var("bad")] public int Bad { get; set; } }
			                                                              """, true),
			("Invalid name", """
			                 using Varcom;
			                 using Varcom.Contracts;
			                 public class Owner { [Cmd("123")] public void Bad() {} }
			                 """, true)
		};
		var data = new TheoryData<string, string, bool>();
		foreach (var test in cases)
			data.Add(test.Name, test.Source, test.Invalid);
		return data;
	}

	public static TheoryData<string, string> InvalidCommandUses()
	{
		var invalidCommandUses = new (string Name, string Source)[]
		{
			("Class commands cannot be enqueued", """
			                                      using Varcom;
			                                      using Varcom.Contracts;
			                                      public class Bad : ICommand { }
			                                      public static class Usage { public static void Run(CommandProcessor p) => p.Enqueue(new Bad()); }
			                                      """),
			("Interface-typed commands cannot be enqueued", """
			                                                using Varcom;
			                                                using Varcom.Contracts;
			                                                public static class Usage { public static void Run(CommandProcessor p, ICommand c) => p.Enqueue(c); }
			                                                """),
			("Class commands cannot have typed handlers or registrations", """
			                                                               using Varcom;
			                                                               using Varcom.Contracts;
			                                                               public class Bad : ICommand { }
			                                                               public abstract class Handler : ICommandHandler<Bad> { public abstract CommandResult Handle(in Bad command); }
			                                                               public static class Usage
			                                                               {
			                                                                   public static void Run(CommandRegistry r, Handler h) => r.Register<Bad>(
			                                                                       new CommandInfo("bad", "", System.Array.Empty<CommandParameter>()), _ => new Bad(), h);
			                                                               }
			                                                               """)
		};
		var data = new TheoryData<string, string>();
		foreach (var test in invalidCommandUses)
			data.Add(test.Name, test.Source);
		return data;
	}
}
