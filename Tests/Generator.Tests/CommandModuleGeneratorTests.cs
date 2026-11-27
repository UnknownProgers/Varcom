using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Varcom.Contracts;
using Varcom.Generators;
using Xunit;

namespace Varcom.Tests.Generators;

public sealed class CommandModuleGeneratorTests
{
	private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
		.Split(Path.PathSeparator).Append(typeof(ICommand).Assembly.Location).Distinct()
		.Select(path => MetadataReference.CreateFromFile(path)).ToArray();

	private const string Owners = """
	                              using System;
	                              using Varcom;
	                              public sealed class First : IDisposable
	                              {
	                                  public int Calls { get; private set; }
	                                  public bool IsDisposed { get; private set; }
	                                  public void Dispose() => IsDisposed = true;
	                                  [Cmd("first")] public void Execute() => Calls++;
	                              }
	                              public sealed class Second
	                              {
	                                  [Var("second")] public int Number { get; set; }
	                              }
	                              """;

	[Fact]
	public void OwnersWithoutModuleRequireAnExplicitDeclaration()
	{
		var (output, diagnostics) = Generate(Create(Owners));
		Assert.Contains(diagnostics, d => d.Id == "VARCOM001" && d.GetMessage().Contains("[CmdModule]"));
		Assert.Null(output.GetTypeByMetadataName("Varcom.Generated.Assembly_ModuleConsumer.CommandBindings"));
	}

	[Fact]
	public void AssemblyWithoutOwnersDoesNotRequireAModule()
	{
		var (output, diagnostics) = Generate(Create("public class PlainService { }"));
		AssertNoErrors(output, diagnostics);
	}

	[Fact]
	public void ExplicitModulesKeepTheirRegistrationsIndependent()
	{
		var assembly = Load(Owners + """
		                             [CmdModule(typeof(First))] public sealed partial class FirstModule { }
		                             [CmdModule(typeof(Second))] public sealed partial class SecondModule { }
		                             """);
		Assert.DoesNotContain(assembly.GetTypes(), type => type.Name == "CommandBindings");
		var registry = new CommandRegistry();
		var first = Activator.CreateInstance(assembly.GetType("First")!)!;
		var second = Activator.CreateInstance(assembly.GetType("Second")!)!;
		var firstModule = (IDisposable)Activator.CreateInstance(assembly.GetType("FirstModule")!, registry, first)!;
		var processor = new CommandProcessor(registry);
		Assert.True(processor.Enqueue("first").IsSuccess);
		Assert.True(processor.Enqueue("second 7").IsSuccess);
		CommandResult unavailable = default;
		processor.Update(r => unavailable = r);
		Assert.Equal(CommandErrorCode.UnknownCommand, unavailable.Error.Code);
		using var secondModule = (IDisposable)Activator.CreateInstance(assembly.GetType("SecondModule")!, registry, second)!;
		Assert.True(processor.Enqueue("second 7").IsSuccess);
		processor.Update();
		Assert.Equal(1, first.GetType().GetProperty("Calls")!.GetValue(first));
		Assert.Equal(7, second.GetType().GetProperty("Number")!.GetValue(second));

		firstModule.Dispose();
		firstModule.Dispose();
		Assert.True(processor.Enqueue("first").IsSuccess);
		Assert.True(processor.Enqueue("second 8").IsSuccess);
		processor.Update();
		Assert.Equal(8, second.GetType().GetProperty("Number")!.GetValue(second));
		Assert.Equal(false, first.GetType().GetProperty("IsDisposed")!.GetValue(first));
	}

	[Fact]
	public void EmptySelectionDiscoversLocalOwnersAndUsesTypedConstructor()
	{
		var assembly = Load(Owners + """
		                             namespace Game { [CmdModule] internal partial class AllCommands { } }
		                             """);
		var module = assembly.GetType("Game.AllCommands")!;
		var parameters = module.GetConstructors().Single().GetParameters();
		Assert.Equal(new[] { typeof(CommandRegistry), assembly.GetType("First"), assembly.GetType("Second") },
			parameters.Select(p => p.ParameterType).ToArray());
	}

	[Fact]
	public void ExplicitModuleRollsBackWhenALaterOwnerConflicts()
	{
		var assembly = Load(Owners + """
		                             [CmdModule(typeof(First), typeof(Second))] public partial class Pair { }
		                             """);
		var registry = new CommandRegistry();
		using var existing = registry.RegisterMethod(new("second", "", Array.Empty<CommandParameter>()), _ => CommandResult.Ok());
		var exception = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(assembly.GetType("Pair")!,
			registry, Activator.CreateInstance(assembly.GetType("First")!), Activator.CreateInstance(assembly.GetType("Second")!)));
		Assert.IsType<ArgumentException>(exception.InnerException);
		Assert.Single(registry.GetEntries());
		Assert.Equal("second", registry.GetEntries()[0].Info.Name);
	}

	[Theory]
	[InlineData("using Varcom; [CmdModule] public partial class Empty { }")]
	[InlineData("using Varcom; namespace @namespace { [CmdModule] public partial class @event { } }")]
	public void EmptyAndEscapedModuleDeclarationsCompile(string source)
	{
		var (output, diagnostics) = Generate(Create(source));
		AssertNoErrors(output, diagnostics);
		using var stream = new MemoryStream();
		Assert.True(output.Emit(stream).Success);
	}

	[Theory]
	[InlineData("[CmdModule(typeof(First))] public class Broken { }")]
	[InlineData("[CmdModule(typeof(First))] public abstract partial class Broken { }")]
	[InlineData("[CmdModule(typeof(First))] public partial class Broken<T> { }")]
	[InlineData("[CmdModule(typeof(First))] public partial record Broken;")]
	[InlineData("public class Outer { [CmdModule] public partial class Broken { } }")]
	[InlineData("[CmdModule] file partial class Broken { }")]
	[InlineData("[CmdModule(typeof(First), typeof(First))] public partial class Broken { }")]
	[InlineData("[CmdModule(typeof(string))] public partial class Broken { }")]
	[InlineData("[CmdModule(null)] public partial class Broken { }")]
	[InlineData("[CmdModule(typeof(First), null)] public partial class Broken { }")]
	[InlineData("[CmdModule] public partial class Broken { public Broken() {} }")]
	[InlineData("[CmdModule] public partial class Broken { public void Dispose() {} }")]
	[InlineData("[CmdModule] public partial class Broken : IDisposable { void IDisposable.Dispose() {} }")]
	[InlineData("[CmdModule] public partial class Broken { private object _varcomRegistration; }")]
	[InlineData("[CmdModule] public partial class Broken { [Cmd(\"bad\")] public void Execute() {} }")]
	[InlineData("public class Empty { } [CmdModule(typeof(Empty))] public partial class Broken { }")]
	public void InvalidModulesProduceDiagnosticsWithoutGeneratorCrashes(string declaration)
	{
		var (_, diagnostics) = Generate(Create(Owners + declaration));
		Assert.Contains(diagnostics, d => d.Id == "VARCOM001" && d.Severity == DiagnosticSeverity.Error);
		Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ReferencedOwnersRequireAvailableGeneratedRegistration(bool generateRegistration)
	{
		const string feature = "using Varcom; public class External { [Cmd(\"external\")] public void Execute() {} }";
		var compilation = Create(feature + " [CmdModule] public partial class FeatureCommands { }", "Feature");
		if (generateRegistration) compilation = Generate(compilation).Compilation;
		using var stream = new MemoryStream();
		Assert.True(compilation.Emit(stream).Success);
		var reference = MetadataReference.CreateFromImage(stream.ToArray());
		var source = "using Varcom; [CmdModule(typeof(External))] public partial class Imports { }";
		var (output, diagnostics) = Generate(Create(source).AddReferences(reference));
		if (!generateRegistration)
		{
			Assert.Contains(diagnostics, d => d.Id == "VARCOM001");
			return;
		}

		AssertNoErrors(output, diagnostics);
		using var emitted = new MemoryStream();
		Assert.True(output.Emit(emitted).Success);
	}

	private static CSharpCompilation Create(string source, string name = "ModuleConsumer") => CSharpCompilation.Create(name,
		new[] { CSharpSyntaxTree.ParseText(source) }, References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

	private static (CSharpCompilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Generate(
		CSharpCompilation input)
	{
		GeneratorDriver driver = CSharpGeneratorDriver.Create(new CommandGenerator().AsSourceGenerator());
		driver.RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
		return ((CSharpCompilation)output, diagnostics);
	}

	private static Assembly Load(string source)
	{
		var (output, diagnostics) = Generate(Create(source, "ModuleConsumer_" + Guid.NewGuid().ToString("N")));
		AssertNoErrors(output, diagnostics);
		using var stream = new MemoryStream();
		var result = output.Emit(stream);
		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
		return Assembly.Load(stream.ToArray());
	}

	private static void AssertNoErrors(CSharpCompilation output, ImmutableArray<Diagnostic> diagnostics)
	{
		var errors = output.GetDiagnostics().Concat(diagnostics).Where(d => d.Severity == DiagnosticSeverity.Error);
		Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
	}
}
