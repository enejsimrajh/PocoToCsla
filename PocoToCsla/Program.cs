using System.CommandLine;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using PocoToCsla;

var versionString = Assembly.GetEntryAssembly()?
                            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                            .InformationalVersion
                            .ToString();

var inputFileArg = new Argument<FileInfo?>(
    name: null,
    description: "The file that contains the POCO class.",
    parse: result =>
    {
        string? filePath = result.Tokens.Single().Value;
        if (!File.Exists(filePath))
        {
            result.ErrorMessage = "File does not exist";
            return null;
        }
        else
        {
            return new FileInfo(filePath);
        }
    });

var destinationOpt = new Option<DirectoryInfo?>(
    name: "--dest",
    description: "The directory that will contain the CSLA class(es).");
destinationOpt.AddAlias("--d");

var objectTypesOpt = new Option<CslaObjectType[]>(
    name: "--type",
    description: "The type(s) of CSLA object(s) to generate.",
    getDefaultValue: () => new[] { CslaObjectType.All })
{
    AllowMultipleArgumentsPerToken = true,
};
objectTypesOpt.AddAlias("--t");

var rootCommand = new RootCommand($"poco2csla v{versionString}");
rootCommand.AddArgument(inputFileArg);
rootCommand.AddOption(destinationOpt);
rootCommand.AddOption(objectTypesOpt);

rootCommand.SetHandler((file, destination, objectTypes) =>
{
    if (file is null) return;
    string? destinationDirectory = destination?.FullName;
    string? namespaceName = null;
    if (destinationDirectory is null)
    {
        (destinationDirectory, namespaceName) = GetDestinationDirectory(file);
    }
    GenerateOutput(file, destinationDirectory, objectTypes, namespaceName);
}, inputFileArg, destinationOpt, objectTypesOpt);

return rootCommand.InvokeAsync(args).Result;

static void GenerateOutput(FileInfo file, string destination, CslaObjectType[] objectTypes, string? namespaceName)
{
    var fileStream = file.OpenRead();
    SyntaxTree tree = CSharpSyntaxTree.ParseText(SourceText.From(fileStream));
    var root = tree.GetCompilationUnitRoot();

    var namespaceNode = root.DescendantNodes()
                            .OfType<NamespaceDeclarationSyntax>()
                            .Single();
    var classNode = namespaceNode.DescendantNodes()
                                 .OfType<ClassDeclarationSyntax>()
                                 .Single();
    var propertyNodes = classNode.DescendantNodes()
                                 .OfType<PropertyDeclarationSyntax>();

    string sourceCode;
    var context = new SourceGenerationContext(namespaceNode, classNode, propertyNodes);

    if (objectTypes.Any(p => p is CslaObjectType.All || p is CslaObjectType.BO))
    {
        sourceCode = GetObjectSourceCode(context, "CslaBusinessBase", "SetProperty", "BO", namespaceName);
        File.WriteAllText(Path.Combine(destination, $"{context.ClassName}BO.cs"), sourceCode);
    }
    if (objectTypes.Any(p => p is CslaObjectType.All || p is CslaObjectType.Info))
    {
        sourceCode = GetObjectSourceCode(context, "CslaReadOnlyBase", "LoadProperty", "Info", namespaceName);
        File.WriteAllText(Path.Combine(destination, $"{context.ClassName}Info.cs"), sourceCode);
    }
    if (objectTypes.Any(p => p is CslaObjectType.All || p is CslaObjectType.EL))
    {
        sourceCode = GetListSourceCode(context, "CslaBusinessListBase", "EL", "BO", namespaceName);
        File.WriteAllText(Path.Combine(destination, $"{context.ClassName}EL.cs"), sourceCode);
    }
    if (objectTypes.Any(p => p is CslaObjectType.All || p is CslaObjectType.RL))
    {
        sourceCode = GetListSourceCode(context, "CslaReadOnlyListBase", "RL", "Info", namespaceName);
        File.WriteAllText(Path.Combine(destination, $"{context.ClassName}RL.cs"), sourceCode);
    }
}

static string GetObjectSourceCode(
    SourceGenerationContext context,
    string baseClass,
    string propertySetterMethod,
    string classNameSuffix,
    string? namespaceName)
{
    namespaceName ??= context.NamespaceName;
    string className = context.ClassName + classNameSuffix;
    StringBuilder outProperties = new StringBuilder();

    foreach (var property in context.Properties)
    {
        outProperties.AppendLine($@"
        public static readonly PropertyInfo<{property.Type}> {property.Name}Property = RegisterProperty<{property.Type}>(p => p.{property.Name});
        public {property.Type} {property.Name}
        {{
            get => GetProperty({property.Name}Property);
            set => {propertySetterMethod}({property.Name}Property, value);
        }}");
    }

    string sourceCode = $@"
using System;
using Core.Library.Base;
using Csla;
using {context.NamespaceName};

namespace {namespaceName}
{{
    [Serializable]
    public class {className} : {baseClass}<{className}, {context.ClassName}>
    {{
{outProperties.ToString().TrimNewLine()}
    }}
}}
".TrimNewLine();

    return sourceCode;
}

static string GetListSourceCode(
    SourceGenerationContext context,
    string baseClass,
    string classNameSuffix,
    string objectClassNameSuffix,
    string? namespaceName)
{
    namespaceName ??= context.NamespaceName;
    string className = context.ClassName + classNameSuffix;
    string objectClassName = context.ClassName + objectClassNameSuffix;

    string sourceCode = $@"
using System;
using Core.Library.Base;
using {context.NamespaceName};

namespace {namespaceName}
{{
    [Serializable]
    public class {className} : {baseClass}<{className}, {objectClassName}, {context.ClassName}>
    {{
        public {className}()
        {{ }}
    }}
}}
".TrimNewLine();

    return sourceCode;
}

static (string directory, string? namespaceName) GetDestinationDirectory(FileInfo file)
{
    DirectoryInfo? solutionDirectory = file.Directory?.Parent?.Parent;
    if (solutionDirectory is not null)
    {
        string targetDirectoryName = Path.GetFileNameWithoutExtension(file.Name);
        if (targetDirectoryName.Length > 2)
        {
            if (char.IsUpper(targetDirectoryName, 0) && char.IsUpper(targetDirectoryName, 1))
            {
                targetDirectoryName = targetDirectoryName.Remove(0, 2);
            }
        }

        string subdirectoryPath = Path.Combine($"{solutionDirectory.Name}.BusinessLibrary", "BO", targetDirectoryName);
        DirectoryInfo? businessObjectsDirectory = solutionDirectory.CreateSubdirectory(subdirectoryPath);

        if (businessObjectsDirectory is not null)
        {
            string namespaceName = subdirectoryPath.Replace(Path.DirectorySeparatorChar, '.');
            return (businessObjectsDirectory.FullName, namespaceName);
        }
    }

    if (file.DirectoryName is not null)
    {
        return (file.DirectoryName, null);
    }

    return (Environment.CurrentDirectory, null);
}