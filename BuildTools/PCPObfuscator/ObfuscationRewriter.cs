using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PCPObfuscator;

/// <summary>
/// Two-pass Roslyn syntax rewriter that:
///   Pass 1 (CollectPass): Walks all trees to build a global rename map.
///   Pass 2 (RewritePass): Applies the rename map and strips comments.
/// </summary>
public sealed class ObfuscationRewriter
{
    private readonly NameGenerator _nameGen = new();
    private readonly Dictionary<string, string> _globalRenameMap = new(StringComparer.Ordinal);
    private readonly HashSet<string> _preservedNames = new(StringComparer.Ordinal);

    public int IdentifiersRenamed => _globalRenameMap.Count;
    public int IdentifiersPreserved => _preservedNames.Count;

    /// <summary>
    /// Pass 1: Collect all renameable identifiers across all syntax trees.
    /// Must be called once with ALL trees before calling Rewrite().
    /// </summary>
    public void Collect(IEnumerable<SyntaxTree> trees)
    {
        foreach (var tree in trees)
        {
            var root = tree.GetCompilationUnitRoot();
            CollectFromNode(root);
        }
    }

    /// <summary>
    /// Pass 2: Rewrite a single syntax tree, applying renames and stripping comments.
    /// </summary>
    public SyntaxTree Rewrite(SyntaxTree tree)
    {
        var root = tree.GetCompilationUnitRoot();

        // Strip comments first
        root = (CompilationUnitSyntax)new CommentStripper().Visit(root)!;

        // Apply renames
        root = (CompilationUnitSyntax)new RenameApplier(_globalRenameMap, _nameGen).Visit(root)!;

        return tree.WithRootAndOptions(root, tree.Options);
    }

    // ── Pass 1: Collection ──────────────────────────────────────

    private void CollectFromNode(SyntaxNode node)
    {
        foreach (var child in node.DescendantNodes(n => ShouldDescend(n)))
        {
            switch (child)
            {
                case ClassDeclarationSyntax cls:
                    CollectType(cls);
                    break;
                case StructDeclarationSyntax str:
                    CollectType(str);
                    break;
                case RecordDeclarationSyntax rec:
                    CollectType(rec);
                    break;
                case MethodDeclarationSyntax method:
                    CollectMethod(method);
                    break;
                case FieldDeclarationSyntax field:
                    CollectField(field);
                    break;
                case PropertyDeclarationSyntax prop:
                    CollectProperty(prop);
                    break;
                case EventFieldDeclarationSyntax evt:
                    CollectEvent(evt);
                    break;
            }
        }
    }

    private bool ShouldDescend(SyntaxNode node)
    {
        // Always descend into type declarations, namespace declarations, etc.
        return true;
    }

    private void CollectType(TypeDeclarationSyntax typeDecl)
    {
        string name = typeDecl.Identifier.Text;
        if (PreservationRules.ShouldPreserveType(typeDecl))
        {
            _preservedNames.Add(name);
        }
        else
        {
            // Only rename private nested types
            bool isNested = typeDecl.Parent is TypeDeclarationSyntax;
            if (isNested && !PreservationRules.IsPublicOrProtectedOrInternal(typeDecl.Modifiers))
            {
                string obfuscated = _nameGen.GetOrCreate(name, "type");
                _globalRenameMap[name] = obfuscated;
            }
            else
            {
                _preservedNames.Add(name);
            }
        }
    }

    private void CollectMethod(MethodDeclarationSyntax method)
    {
        string name = method.Identifier.Text;
        if (PreservationRules.ShouldPreserveMethod(method))
        {
            _preservedNames.Add(name);
        }
        else
        {
            string obfuscated = _nameGen.GetOrCreate(name, "method");
            _globalRenameMap[name] = obfuscated;
        }
    }

    private void CollectField(FieldDeclarationSyntax field)
    {
        bool preserve = PreservationRules.ShouldPreserveField(field);
        foreach (var variable in field.Declaration.Variables)
        {
            string name = variable.Identifier.Text;
            if (preserve)
            {
                _preservedNames.Add(name);
            }
            else
            {
                string obfuscated = _nameGen.GetOrCreate(name, "field");
                _globalRenameMap[name] = obfuscated;
            }
        }
    }

    private void CollectProperty(PropertyDeclarationSyntax prop)
    {
        string name = prop.Identifier.Text;
        if (PreservationRules.ShouldPreserveProperty(prop))
        {
            _preservedNames.Add(name);
        }
        else
        {
            string obfuscated = _nameGen.GetOrCreate(name, "property");
            _globalRenameMap[name] = obfuscated;
        }
    }

    private void CollectEvent(EventFieldDeclarationSyntax evt)
    {
        bool preserve = PreservationRules.ShouldPreserveEvent(evt);
        foreach (var variable in evt.Declaration.Variables)
        {
            string name = variable.Identifier.Text;
            if (preserve)
                _preservedNames.Add(name);
            else
            {
                string obfuscated = _nameGen.GetOrCreate(name, "field");
                _globalRenameMap[name] = obfuscated;
            }
        }
    }

    // ── Pass 2 Helper: Comment Stripper ─────────────────────────

    private sealed class CommentStripper : CSharpSyntaxRewriter
    {
        public CommentStripper() : base(visitIntoStructuredTrivia: true) { }

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
        {
            switch (trivia.Kind())
            {
                case SyntaxKind.SingleLineCommentTrivia:
                case SyntaxKind.MultiLineCommentTrivia:
                case SyntaxKind.SingleLineDocumentationCommentTrivia:
                case SyntaxKind.MultiLineDocumentationCommentTrivia:
                case SyntaxKind.DocumentationCommentExteriorTrivia:
                case SyntaxKind.RegionDirectiveTrivia:
                case SyntaxKind.EndRegionDirectiveTrivia:
                    // Remove comments and regions, keep the whitespace/newline
                    return default;

                default:
                    // Preserve everything else (preprocessor directives, whitespace, etc.)
                    return base.VisitTrivia(trivia);
            }
        }
    }

    // ── Pass 2 Helper: Rename Applier ───────────────────────────

    private sealed class RenameApplier : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, string> _map;
        private readonly NameGenerator _nameGen;

        // Per-method local variable scoping
        private readonly Stack<Dictionary<string, string>> _localScopes = new();

        public RenameApplier(Dictionary<string, string> map, NameGenerator nameGen)
        {
            _map = map;
            _nameGen = nameGen;
        }

        // ── Type declarations ──
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (_map.TryGetValue(node.Identifier.Text, out string? newName))
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            return base.VisitClassDeclaration(node);
        }

        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
        {
            if (_map.TryGetValue(node.Identifier.Text, out string? newName))
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            return base.VisitStructDeclaration(node);
        }

        // ── Method declarations ──
        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            // Push local scope for this method's locals/params
            _localScopes.Push(new Dictionary<string, string>(StringComparer.Ordinal));

            if (_map.TryGetValue(node.Identifier.Text, out string? newName))
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));

            var result = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            _localScopes.Pop();
            return result;
        }

        // ── Constructor declarations ──
        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            _localScopes.Push(new Dictionary<string, string>(StringComparer.Ordinal));
            var result = base.VisitConstructorDeclaration(node);
            _localScopes.Pop();
            return result;
        }

        // ── Property declarations ──
        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (_map.TryGetValue(node.Identifier.Text, out string? newName))
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            return base.VisitPropertyDeclaration(node);
        }

        // ── Field variable declarations ──
        public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            string name = node.Identifier.Text;

            // Check if this is a local variable inside a method
            if (_localScopes.Count > 0 && node.Parent?.Parent is LocalDeclarationStatementSyntax)
            {
                // Create a scoped local rename
                var scope = _localScopes.Peek();
                if (!scope.ContainsKey(name))
                {
                    string localName = _nameGen.GetOrCreate(
                        $"{_localScopes.Count}_{name}", "local");
                    scope[name] = localName;
                }
                string newLocal = scope[name];
                node = node.WithIdentifier(SyntaxFactory.Identifier(newLocal)
                    .WithTriviaFrom(node.Identifier));
            }
            else if (_map.TryGetValue(name, out string? newName))
            {
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }

            return base.VisitVariableDeclarator(node);
        }

        // ── Parameters ──
        public override SyntaxNode? VisitParameter(ParameterSyntax node)
        {
            if (!PreservationRules.ShouldPreserveParameter(node) && _localScopes.Count > 0)
            {
                string name = node.Identifier.Text;
                var scope = _localScopes.Peek();
                if (!scope.ContainsKey(name))
                {
                    string paramName = _nameGen.GetOrCreate(
                        $"{_localScopes.Count}_{name}", "param");
                    scope[name] = paramName;
                }
                string newName = scope[name];
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return base.VisitParameter(node);
        }

        // ── Identifier references (the core rename site) ──
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            string name = node.Identifier.Text;

            // Skip identifiers that are part of attribute names
            if (node.Parent is AttributeSyntax)
                return base.VisitIdentifierName(node);

            // Skip identifiers in qualified names (namespace.Type patterns)
            if (node.Parent is QualifiedNameSyntax qualified && node == qualified.Left)
                return base.VisitIdentifierName(node);

            // Skip type references in declarations (return types, parameter types, etc.)
            if (IsTypeReference(node))
                return base.VisitIdentifierName(node);

            // Check local scope first
            if (_localScopes.Count > 0)
            {
                var scope = _localScopes.Peek();
                if (scope.TryGetValue(name, out string? localName))
                {
                    return node.WithIdentifier(SyntaxFactory.Identifier(localName)
                        .WithTriviaFrom(node.Identifier));
                }
            }

            // Check global rename map
            if (_map.TryGetValue(name, out string? newName))
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }

            return base.VisitIdentifierName(node);
        }

        // ── ForEach variable ──
        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            if (_localScopes.Count > 0)
            {
                string name = node.Identifier.Text;
                var scope = _localScopes.Peek();
                if (!scope.ContainsKey(name))
                {
                    string localName = _nameGen.GetOrCreate(
                        $"{_localScopes.Count}_{name}", "local");
                    scope[name] = localName;
                }
                string newName = scope[name];
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return base.VisitForEachStatement(node);
        }

        private static bool IsTypeReference(IdentifierNameSyntax node)
        {
            // If the parent is a type syntax context, this is a type name, not a variable
            return node.Parent is TypeArgumentListSyntax
                || node.Parent is BaseTypeSyntax
                || node.Parent is TypeConstraintSyntax
                || (node.Parent is MemberAccessExpressionSyntax ma && ma.Name == node
                    && ma.Expression is IdentifierNameSyntax);
        }
    }
}
