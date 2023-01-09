using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PocoToCsla;

public class SourceGenerationContext
{
    public class PropertyInfo
    {
        public string? Type { get; set; }

        public string? Name { get; set; }

        public static IEnumerable<PropertyInfo> CreateFromSyntaxNodes(IEnumerable<PropertyDeclarationSyntax> propertyNodes)
        {
            return propertyNodes
                .Where(p => !p.Modifiers.Any(p => p.IsKind(SyntaxKind.VirtualKeyword)))
                .Select(p => new PropertyInfo
                {
                    Type = p.Type.ToString(),
                    Name = p.Identifier.ToString()
                });
        }
    }

    public SourceGenerationContext(
        NamespaceDeclarationSyntax namespaceNode,
        ClassDeclarationSyntax classNode,
        IEnumerable<PropertyDeclarationSyntax> propertyNodes)
    {
        NamespaceName = namespaceNode.Name.ToString();
        ClassName = classNode.Identifier.ToString();
        Properties = PropertyInfo.CreateFromSyntaxNodes(propertyNodes);
    }

    public string NamespaceName { get; set; }

    public string ClassName { get; set; }

    public IEnumerable<PropertyInfo> Properties { get; set; }
}