using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PCPObfuscator;

/// <summary>
/// Determines which identifiers must be preserved (not renamed) during obfuscation.
/// Uses syntax-only analysis — no SemanticModel or Unity DLL references required.
/// </summary>
public static class PreservationRules
{
    /// <summary>Unity magic callback method names that must never be renamed.</summary>
    private static readonly HashSet<string> s_UnityCallbacks = new(StringComparer.Ordinal)
    {
        "Awake", "Start", "Update", "LateUpdate", "FixedUpdate",
        "OnEnable", "OnDisable", "OnDestroy", "OnGUI",
        "OnValidate", "Reset", "OnApplicationQuit",
        "CreateGUI", "OnInspectorGUI", "OnSceneGUI",
        "OnPreprocessBuild", "OnPostprocessBuild",
        "OnWillSaveAssets", "OnPostprocessAllAssets",
    };

    /// <summary>Common override method names from System.Object and interfaces.</summary>
    private static readonly HashSet<string> s_CommonOverrides = new(StringComparer.Ordinal)
    {
        "ToString", "Equals", "GetHashCode", "CompareTo",
        "Dispose", "GetEnumerator", "MoveNext",
    };

    /// <summary>Attributes that mark a type or member as requiring name preservation.</summary>
    private static readonly HashSet<string> s_PreservingAttributes = new(StringComparer.Ordinal)
    {
        "MenuItem", "MenuItemAttribute",
        "InitializeOnLoad", "InitializeOnLoadAttribute",
        "InitializeOnLoadMethod", "InitializeOnLoadMethodAttribute",
        "Callback", "CallbackAttribute",
        "DidReloadScripts", "DidReloadScriptsAttribute",
        "RuntimeInitializeOnLoadMethod", "RuntimeInitializeOnLoadMethodAttribute",
        "CustomEditor", "CustomEditorAttribute",
        "CustomPropertyDrawer", "CustomPropertyDrawerAttribute",
        "Serializable", "SerializableAttribute",
        "CreateAssetMenu", "CreateAssetMenuAttribute",
        "FilePath", "FilePathAttribute",
        "UxmlElement", "UxmlElementAttribute",
    };

    /// <summary>
    /// Check if a type declaration should have its name preserved.
    /// </summary>
    public static bool ShouldPreserveType(TypeDeclarationSyntax typeDecl)
    {
        // Public, protected, or internal types — preserve
        if (IsPublicOrProtectedOrInternal(typeDecl.Modifiers))
            return true;

        // Types with preserving attributes ([Serializable], [InitializeOnLoad], etc.)
        if (HasPreservingAttribute(typeDecl.AttributeLists))
            return true;

        // Enums are always preserved (members are part of API contract)
        if (typeDecl.IsKind(SyntaxKind.EnumDeclaration))
            return true;

        // Interface declarations — always preserved
        if (typeDecl.IsKind(SyntaxKind.InterfaceDeclaration))
            return true;

        // Nested in a [Serializable] type — preserve
        if (IsInsideSerializableType(typeDecl))
            return true;

        return false;
    }

    /// <summary>
    /// Check if a method declaration should have its name preserved.
    /// </summary>
    public static bool ShouldPreserveMethod(MethodDeclarationSyntax method)
    {
        string name = method.Identifier.Text;

        // Unity callbacks
        if (s_UnityCallbacks.Contains(name))
            return true;

        // Common overrides
        if (s_CommonOverrides.Contains(name))
            return true;

        // Override modifier — must match base class
        if (method.Modifiers.Any(SyntaxKind.OverrideKeyword))
            return true;

        // Public, protected, or internal
        if (IsPublicOrProtectedOrInternal(method.Modifiers))
            return true;

        // Has preserving attribute
        if (HasPreservingAttribute(method.AttributeLists))
            return true;

        // Abstract or virtual
        if (method.Modifiers.Any(SyntaxKind.AbstractKeyword) ||
            method.Modifiers.Any(SyntaxKind.VirtualKeyword))
            return true;

        // Part of an interface
        if (method.Parent is InterfaceDeclarationSyntax)
            return true;

        // Explicit interface implementation (has dotted name)
        if (method.ExplicitInterfaceSpecifier != null)
            return true;

        // Static constructor
        if (name == ".cctor")
            return true;

        return false;
    }

    /// <summary>
    /// Check if a field declaration should have its name preserved.
    /// </summary>
    public static bool ShouldPreserveField(FieldDeclarationSyntax field)
    {
        // Public, protected, or internal
        if (IsPublicOrProtectedOrInternal(field.Modifiers))
            return true;

        // Has [SerializeField] attribute
        if (HasAttribute(field.AttributeLists, "SerializeField", "SerializeFieldAttribute"))
            return true;

        // Has [NonSerialized] — can be renamed even in serializable types
        if (HasAttribute(field.AttributeLists, "NonSerialized", "NonSerializedAttribute"))
            return false;

        // Inside a [Serializable] type — Unity serializes public and
        // non-attributed fields by name
        if (IsInsideSerializableType(field))
            return true;

        // Const or static readonly with public-like visibility
        if (field.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            if (IsPublicOrProtectedOrInternal(field.Modifiers))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Check if a property declaration should have its name preserved.
    /// </summary>
    public static bool ShouldPreserveProperty(PropertyDeclarationSyntax property)
    {
        // Public, protected, or internal
        if (IsPublicOrProtectedOrInternal(property.Modifiers))
            return true;

        // Override
        if (property.Modifiers.Any(SyntaxKind.OverrideKeyword))
            return true;

        // Abstract or virtual
        if (property.Modifiers.Any(SyntaxKind.AbstractKeyword) ||
            property.Modifiers.Any(SyntaxKind.VirtualKeyword))
            return true;

        // Interface member
        if (property.Parent is InterfaceDeclarationSyntax)
            return true;

        // Explicit interface implementation
        if (property.ExplicitInterfaceSpecifier != null)
            return true;

        return false;
    }

    /// <summary>
    /// Check if an event declaration should have its name preserved.
    /// </summary>
    public static bool ShouldPreserveEvent(EventFieldDeclarationSyntax eventField)
    {
        if (IsPublicOrProtectedOrInternal(eventField.Modifiers))
            return true;

        if (eventField.Parent is InterfaceDeclarationSyntax)
            return true;

        return false;
    }

    /// <summary>
    /// Check if a parameter should have its name preserved.
    /// Parameters of public/protected methods are part of the API.
    /// </summary>
    public static bool ShouldPreserveParameter(ParameterSyntax parameter)
    {
        // Walk up to find the containing method
        var method = parameter.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method != null)
            return ShouldPreserveMethod(method);

        // Constructor parameters in public types
        var ctor = parameter.FirstAncestorOrSelf<ConstructorDeclarationSyntax>();
        if (ctor != null)
            return IsPublicOrProtectedOrInternal(ctor.Modifiers);

        // Lambda / delegate parameters — safe to rename
        return false;
    }

    /// <summary>
    /// Check if a constructor should have its name preserved.
    /// (Constructors use the type name, which is handled separately.)
    /// </summary>
    public static bool ShouldPreserveConstructor(ConstructorDeclarationSyntax ctor)
    {
        // Static constructors (class initializers) — preserve
        if (ctor.Modifiers.Any(SyntaxKind.StaticKeyword))
            return true;

        // Public/protected constructors
        if (IsPublicOrProtectedOrInternal(ctor.Modifiers))
            return true;

        return false;
    }

    // ── Helpers ──────────────────────────────────────────────

    public static bool IsPublicOrProtectedOrInternal(SyntaxTokenList modifiers)
    {
        return modifiers.Any(SyntaxKind.PublicKeyword) ||
               modifiers.Any(SyntaxKind.ProtectedKeyword) ||
               modifiers.Any(SyntaxKind.InternalKeyword);
    }

    public static bool HasPreservingAttribute(SyntaxList<AttributeListSyntax> attrLists)
    {
        foreach (var attrList in attrLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                string name = GetAttributeName(attr);
                if (s_PreservingAttributes.Contains(name))
                    return true;
            }
        }
        return false;
    }

    public static bool HasAttribute(SyntaxList<AttributeListSyntax> attrLists,
                                     params string[] names)
    {
        var nameSet = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var attrList in attrLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                if (nameSet.Contains(GetAttributeName(attr)))
                    return true;
            }
        }
        return false;
    }

    public static bool IsInsideSerializableType(SyntaxNode node)
    {
        var parent = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        // If the node IS a TypeDeclaration, check its own parent type
        if (parent == node)
            parent = node.Parent?.FirstAncestorOrSelf<TypeDeclarationSyntax>();

        while (parent != null)
        {
            if (HasAttribute(parent.AttributeLists, "Serializable", "SerializableAttribute"))
                return true;
            parent = parent.Parent?.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        }
        return false;
    }

    private static string GetAttributeName(AttributeSyntax attr)
    {
        return attr.Name switch
        {
            SimpleNameSyntax simple => simple.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => attr.Name.ToString(),
        };
    }
}
