using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Varcom.Contracts;
using Varcom.Generators;
using Xunit;

namespace Varcom.Tests.Generators;

public sealed class CommandMetadataGeneratorTests
{
	private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
		.Split(Path.PathSeparator).Append(typeof(ICommand).Assembly.Location).Distinct()
		.Select(path => MetadataReference.CreateFromFile(path)).ToArray();

	[Fact]
	public void CatalogDescribesDeclarationsWithoutCreatingOwnersOrHandlers()
	{
		var assembly = Load("""
		                    using System;
		                    using Varcom;
		                    using Varcom.Contracts;
		                    [Cmd("unbound", "No handler needed")]
		                    public readonly record struct Unbound(int Count = 3) : ICommand;
		                    public class Settings
		                    {
		                        public Settings() => throw new Exception("Must not construct owners");
		                        [Var("gravity", "World gravity")] public float Gravity => throw new Exception("Must not read values");
		                        [Cmd("reset", "Reset settings")] public void Reset() { }
		                    }
		                    [CmdModule] public partial class Commands { }
		                    """);
		var registry = new CommandRegistry();
		Assert.Empty(registry.GetMetadata());
		Assert.Empty(registry.GetEntries());
		GenerateMetadata(assembly, registry);
		var catalog = registry.GetMetadata();
		Assert.Equal(3, catalog.Count);
		var command = Assert.Single(catalog, entry => entry.Info.Name == "unbound");
		Assert.Equal("No handler needed", command.Info.Description);
		var parameter = Assert.Single(command.Info.Parameters);
		Assert.Equal(typeof(int), parameter.ValueType);
		Assert.True(parameter.IsOptional);
		Assert.Equal(3, parameter.DefaultValue);
		var variable = Assert.Single(catalog, entry => entry.Variable != null);
		Assert.Equal("World gravity", variable.Variable!.Description);
		Assert.Equal(typeof(float), variable.Variable.ValueType);
		Assert.True(variable.Variable.IsReadOnly);
		Assert.Throws<NotSupportedException>(() => ((IList<CommandEntry>)catalog).Clear());
		Assert.Throws<NotSupportedException>(() => ((IList<CommandParameter>)command.Info.Parameters).Clear());
		GenerateMetadata(assembly, registry);
		Assert.Same(catalog, registry.GetMetadata());
		Assert.Empty(registry.GetEntries());
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ExplicitMetadataDoesNotChangeRegistrationOrExecution(bool generateMetadata)
	{
		var source = """
		             using Varcom;
		             using Varcom.Contracts;
		             [Cmd("add", "Add a value")] public readonly record struct Add(int Value) : ICommand;
		             [CmdHandler(typeof(Add))] public class Handler : ICommandHandler<Add>
		             {
		                 public CommandResult Handle(in Add command) => CommandResult.Ok(command.Value);
		             }
		             public class Settings
		             {
		                 [Var("number", "A number")] public int Number { get; set; }
		                 [Cmd("ping", "Ping command")] public void Ping() { }
		             }
		             [CmdModule] public partial class Commands { }
		             """;
		var assembly = Load(source);
		var registry = new CommandRegistry();
		if (generateMetadata) GenerateMetadata(assembly, registry);
		Assert.Empty(registry.GetEntries());
		Assert.False(registry.IsAvailable("add"));
		var module = (IDisposable)Activator.CreateInstance(assembly.GetType("Commands")!, registry,
			Activator.CreateInstance(assembly.GetType("Handler")!), Activator.CreateInstance(assembly.GetType("Settings")!))!;
		Assert.True(registry.IsAvailable("ADD"));
		Assert.Equal(3, registry.GetEntries().Count);
		Assert.Equal(generateMetadata ? 3 : 0, registry.GetMetadata().Count);
		var processor = new CommandProcessor(registry);
		Assert.True(processor.Enqueue("add 7; number 9; ping; number").IsSuccess);
		var results = new List<CommandResult>();
		processor.Update(results.Add);
		Assert.All(results, result => Assert.True(result.IsSuccess));
		Assert.Equal(7, results[0].Value.Get<int>());
		Assert.Equal(9, results[3].Value.Get<int>());
		module.Dispose();
		Assert.False(registry.IsAvailable("add"));
		Assert.Empty(registry.GetEntries());
		Assert.Equal(generateMetadata ? 3 : 0, registry.GetMetadata().Count);
	}

	[Fact]
	public void MultipleHandlersDoNotDuplicateCommandMetadata()
	{
		var assembly = Load("""
		                    using Varcom;
		                    using Varcom.Contracts;
		                    [Cmd("run")] public readonly record struct Run() : ICommand;
		                    [CmdHandler(typeof(Run))] public class First : ICommandHandler<Run>
		                    { public CommandResult Handle(in Run command) => CommandResult.Ok(); }
		                    [CmdHandler(typeof(Run))] public class Second : ICommandHandler<Run>
		                    { public CommandResult Handle(in Run command) => CommandResult.Ok(); }
		                    [CmdModule(typeof(First))] public partial class Commands { }
		                    """);
		var registry = new CommandRegistry();
		GenerateMetadata(assembly, registry);
		Assert.Single(registry.GetMetadata());
	}

	[Theory]
	[InlineData("[Cmd(\"bad\")] public readonly record struct Bad(string Text) : ICommand;")]
	[InlineData("[Cmd(\"bad\")] public class Bad : ICommand { }")]
	public void UnhandledCommandDeclarationsAreValidated(string declaration)
	{
		var (_, diagnostics) = Generate(Create("using Varcom; using Varcom.Contracts; " + declaration));
		Assert.Contains(diagnostics, d => d.Id == "VARCOM001");
	}

	[Fact]
	public void RegistriesOwnIndependentMetadataAndCanBeCollected()
	{
		var assembly = Load("""
		                    using Varcom;
		                    using Varcom.Contracts;
		                    [Cmd("run")] public readonly record struct Run() : ICommand;
		                    """);
		var first = new CommandRegistry();
		var second = new CommandRegistry();
		GenerateMetadata(assembly, first);
		Assert.Single(first.GetMetadata());
		Assert.Empty(second.GetMetadata());
		GenerateMetadata(assembly, second);
		Assert.NotSame(first.GetMetadata()[0], second.GetMetadata()[0]);
		var weak = CreateWeakRegistry(assembly);
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		Assert.False(weak.IsAlive);
		Assert.DoesNotContain(assembly.GetTypes(), t => t.Name == "CommandCatalog");
		Assert.Empty(assembly.GetTypes().Single(t => t.Name == "CommandMetadata").GetFields(
			BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
	}

	[Fact]
	public void DifferentDeclarationsWithTheSameNameAreNotMerged()
	{
		var assembly = Load("""
		                    using Varcom;
		                    public class Owner
		                    {
		                        [Cmd("same")] public void Execute() { }
		                        [Cmd("same")] public void Execute(int count) { }
		                        [Var("same")] public int Number { get; set; }
		                    }
		                    [CmdModule] public partial class Commands { }
		                    """);
		var registry = new CommandRegistry();
		GenerateMetadata(assembly, registry);
		Assert.Equal(3, registry.GetMetadata().Count);
		GenerateMetadata(assembly, registry);
		Assert.Equal(3, registry.GetMetadata().Count);
	}

	[Fact]
	public void MultipleAssembliesMergeDeclarationsWithoutDuplicatingReferencedCommands()
	{
		var feature = Generate(Create("""
		                              using Varcom;
		                              using Varcom.Contracts;
		                              namespace Feature;
		                              [Cmd("run")] public readonly record struct Run() : ICommand;
		                              public class Settings { [Var("value")] public int Value { get; set; } }
		                              [CmdModule] public partial class Commands { }
		                              """, "Feature_" + Guid.NewGuid().ToString("N"))).Compilation;
		using var featureStream = new MemoryStream();
		Assert.True(feature.Emit(featureStream).Success);
		var reference = MetadataReference.CreateFromImage(featureStream.ToArray());
		featureStream.Position = 0;
		var featureAssembly = AssemblyLoadContext.Default.LoadFromStream(featureStream);
		var consumer = Generate(Create("""
		                               using Varcom;
		                               using Varcom.Contracts;
		                               [CmdHandler(typeof(Feature.Run))] public class Handler : ICommandHandler<Feature.Run>
		                               { public CommandResult Handle(in Feature.Run command) => CommandResult.Ok(); }
		                               public class Settings { [Var("value")] public int Value { get; set; } }
		                               [CmdModule] public partial class Commands { }
		                               """, "Consumer_" + Guid.NewGuid().ToString("N")).AddReferences(reference));
		AssertNoErrors(consumer.Compilation, consumer.Diagnostics);
		using var consumerStream = new MemoryStream();
		Assert.True(consumer.Compilation.Emit(consumerStream).Success);
		consumerStream.Position = 0;
		var consumerAssembly = AssemblyLoadContext.Default.LoadFromStream(consumerStream);
		var registry = new CommandRegistry();
		GenerateMetadata(featureAssembly, registry);
		Assert.Equal(2, registry.GetMetadata().Count);
		GenerateMetadata(consumerAssembly, registry);
		Assert.Equal(3, registry.GetMetadata().Count);
		Assert.Equal(2, registry.GetMetadata().Count(entry => entry.Info.Name == "value"));
		GenerateMetadata(featureAssembly, registry);
		GenerateMetadata(consumerAssembly, registry);
		Assert.Equal(3, registry.GetMetadata().Count);
	}

	[Fact]
	public void IdenticallyNamedTypesInDifferentAssembliesRemainDistinct()
	{
		const string source = """
		                      using Varcom;
		                      using Varcom.Contracts;
		                      [Cmd("run")] public readonly record struct Run() : ICommand;
		                      public class Owner { [Var("value")] public int Value { get; set; } }
		                      [CmdModule] public partial class Commands { }
		                      """;
		var first = Load(source);
		var second = Load(source);
		var registry = new CommandRegistry();
		GenerateMetadata(first, registry);
		GenerateMetadata(second, registry);
		Assert.Equal(4, registry.GetMetadata().Count);
		GenerateMetadata(first, registry);
		GenerateMetadata(second, registry);
		Assert.Equal(4, registry.GetMetadata().Count);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference CreateWeakRegistry(Assembly assembly)
	{
		var registry = new CommandRegistry();
		GenerateMetadata(assembly, registry);
		return new WeakReference(registry);
	}

	private static void GenerateMetadata(Assembly assembly, CommandRegistry registry) =>
		assembly.GetTypes().Single(t => t.Name == "CommandMetadata").GetMethod("GenerateMetadata")!
			.Invoke(null, new object[] { registry });

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
