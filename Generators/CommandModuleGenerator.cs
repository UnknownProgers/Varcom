using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Varcom.Generators;

internal static class CommandModuleGenerator
{
	internal static void Generate(SourceProductionContext context, Compilation compilation, INamedTypeSymbol[] modules,
		IReadOnlyList<INamedTypeSymbol> owners)
	{
		var registrationType = "global::Varcom.Generated." + CommandGenerator.Safe(compilation.AssemblyName ?? "Application") +
		                       ".CommandRegistration";
		if (modules.Length == 0)
		{
			if (owners.Count != 0)
				CommandGenerator.Report(context, owners[0],
					"Declare at least one partial class with [CmdModule] to group command registrations.");
			return;
		}

		var index = 0;
		foreach (var module in modules.OrderBy(Type, StringComparer.Ordinal))
		{
			if (!ValidModule(context, module)) continue;
			var attr = module.GetAttributes().First(a => a.AttributeClass?.ToDisplayString() == "Varcom.CmdModuleAttribute");
			if (attr.ConstructorArguments.Length != 1 || attr.ConstructorArguments[0].Kind != TypedConstantKind.Array)
			{
				CommandGenerator.Report(context, module, "CmdModule requires a list of owner types.");
				continue;
			}

			var types = attr.ConstructorArguments[0];
			if (types.IsNull)
			{
				CommandGenerator.Report(context, module, "CmdModule owner list cannot be null.");
				continue;
			}

			var selected = new List<INamedTypeSymbol>();
			var registrations = new List<string>();
			var valid = true;
			var requested = types.Values.Length == 0 ? owners.Cast<ITypeSymbol?>() : types.Values.Select(v => v.Value as ITypeSymbol);
			foreach (var requestedType in requested)
			{
				if (requestedType is not INamedTypeSymbol owner || owner.TypeKind != TypeKind.Class || owner.IsGenericType ||
				    owner.ContainingType != null || owner.DeclaredAccessibility != Accessibility.Public)
				{
					CommandGenerator.Report(context, module,
						"CmdModule members must be public non-generic top-level owners with generated registration.");
					valid = false;
					continue;
				}

				if (selected.Any(t => SymbolEqualityComparer.Default.Equals(t, owner)))
				{
					CommandGenerator.Report(context, module, $"CmdModule lists '{owner.Name}' more than once.");
					valid = false;
					continue;
				}

				var target = owners.Any(t => SymbolEqualityComparer.Default.Equals(t, owner))
					? registrationType
					: ExternalRegistration(compilation, owner);
				if (target == null)
				{
					CommandGenerator.Report(context, module, $"'{owner.Name}' has no available generated registration.");
					valid = false;
					continue;
				}

				selected.Add(owner);
				registrations.Add(target);
			}

			if (!valid) continue;
			// Explicit lists retain their declared order; automatic selection is sorted by full type name.
			var ns = module.ContainingNamespace.IsGlobalNamespace
				? ""
				: module.ContainingNamespace
					.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
			Emit(context, $"Varcom.Module.{index++}.g.cs", ns, module.Name,
				module.DeclaredAccessibility == Accessibility.Public ? "public partial" : "internal partial", selected, registrations);
		}
	}

	private static bool ValidModule(SourceProductionContext context, INamedTypeSymbol module)
	{
		if (module.TypeKind != TypeKind.Class || module.IsFileLocal || module.IsRecord || module.IsStatic || module.IsAbstract ||
		    module.IsGenericType ||
		    module.ContainingType != null || module.BaseType?.SpecialType != SpecialType.System_Object ||
		    module.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is not ClassDeclarationSyntax syntax ||
		                                              !syntax.Modifiers.Any(SyntaxKind.PartialKeyword)))
		{
			CommandGenerator.Report(context, module,
				"CmdModule must be a non-abstract, non-generic top-level partial class without a base class.");
			return false;
		}

		if (module.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Varcom.CmdHandlerAttribute") ||
		    module.GetMembers().Any(m => m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() is
			    "Varcom.CmdAttribute" or "Varcom.VarAttribute")))
		{
			CommandGenerator.Report(context, module, "CmdModule cannot also be a command owner or handler.");
			return false;
		}

		if (module.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared) || module.GetMembers("Dispose").Length != 0 ||
		    module.GetMembers().OfType<IMethodSymbol>().Any(m => m.ExplicitInterfaceImplementations.Any(i =>
			    i.Name == "Dispose" && i.ContainingType.ToDisplayString() == "System.IDisposable")) ||
		    module.GetMembers("_varcomRegistration").Length != 0)
		{
			CommandGenerator.Report(context, module,
				"CmdModule generates its constructor, Dispose and _varcomRegistration; do not declare them manually.");
			return false;
		}

		return true;
	}

	private static string? ExternalRegistration(Compilation compilation, INamedTypeSymbol owner)
	{
		if (SymbolEqualityComparer.Default.Equals(owner.ContainingAssembly, compilation.Assembly)) return null;
		var registration =
			compilation.GetTypeByMetadataName("Varcom.Generated." + CommandGenerator.Safe(owner.ContainingAssembly.Name) +
			                                  ".CommandRegistration");
		if (registration?.DeclaredAccessibility != Accessibility.Public) return null;
		return registration.GetMembers("Register")
			.OfType<IMethodSymbol>()
			.Any(m => m.IsStatic && !m.IsGenericMethod &&
			          m.DeclaredAccessibility == Accessibility.Public &&
			          m.Parameters.Length == 2 &&
			          m.Parameters[0].Type.ToDisplayString() ==
			          "Varcom.CommandRegistry" &&
			          SymbolEqualityComparer.Default.Equals(
				          m.Parameters[1].Type, owner) &&
			          m.ReturnType.ToDisplayString() == "System.IDisposable")
			? Type(registration)
			: null;
	}

	private static void Emit(SourceProductionContext context, string hint, string ns, string name, string modifiers,
		IReadOnlyList<INamedTypeSymbol> owners, IReadOnlyList<string> registrationTypes)
	{
		var namespaceDeclaration = ns.Length == 0 ? "" : $"namespace {ns};";
		var parameters = string.Join(",\n", new[] { "        global::Varcom.CommandRegistry registry" }
			.Concat(owners.Select((owner, i) => $"        {Type(owner)} owner{i}")));
		var registrations = string.Join(",\n", registrationTypes.Select((type, i) =>
			$"            () => {type}.Register(registry, owner{i})"));

		var source = $$"""
		               // <auto-generated/>
		               #nullable enable
		               {{namespaceDeclaration}}

		               {{modifiers}} class @{{name}} : global::System.IDisposable
		               {
		                   private global::System.IDisposable? _varcomRegistration;

		                   public @{{name}}(
		               {{parameters}})
		                   {
		                       _varcomRegistration = global::Varcom.RegistrationGroup.Create(
		               {{registrations}});
		                   }

		                   public void Dispose()
		                   {
		                       var registration = _varcomRegistration;
		                       _varcomRegistration = null;
		                       registration?.Dispose();
		                   }
		               }
		               """;
		context.AddSource(hint, source);
	}

	private static string Type(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
