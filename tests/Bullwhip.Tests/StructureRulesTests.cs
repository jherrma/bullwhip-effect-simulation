using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Bullwhip.Tests;

// Enforces the structure rules in CLAUDE.md on every C# file under src/, tests/ and experiments/:
//   - a method, constructor, local function or accessor body has at most 50 lines (braces included);
//   - a control-flow block is nested at most 4 levels deep inside its member.
// Lambdas do not start a new count; they inherit the nesting of the member that contains them.
public class StructureRulesTests
{
    private const int MaxBodyLines = 50;
    private const int MaxNestingLevel = 4;

    // "path:line" entries that are exempt. Empty on purpose. An entry needs a reason in the commit message.
    private static readonly HashSet<string> Exempt = [];

    [Fact]
    public void Methods_HaveAtMost50Lines()
    {
        var violations = SourceFiles().SelectMany(f => CheckBodyLength(f.Path, f.Root)).ToList();
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void Methods_NestAtMost4Blocks()
    {
        var violations = SourceFiles().SelectMany(f => CheckNesting(f.Path, f.Root)).ToList();
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void Checker_FlagsSyntheticViolations()
    {
        var longBody = "class C { void M() {\n" + string.Concat(Enumerable.Repeat("int x = 0;\n", 51)) + "} }";
        var deep = "class C { void M(bool a) { if (a) { if (a) { if (a) { if (a) { if (a) { } } } } } } }";
        var flatElseIf = "class C { void M(bool a) { if (a) { } else if (a) { } else if (a) { } else if (a) { } else if (a) { } } }";
        // A braced else is one level deeper than its if: five ifs in a row, the first one in the else block, reach level 5.
        var deepElse = "class C { void M(bool a) { if (a) { } else { if (a) { if (a) { if (a) { if (a) { } } } } } } }";
        // Four levels, the innermost reached through a braced else: allowed.
        var shallowElse = "class C { void M(bool a) { if (a) { } else { if (a) { if (a) { if (a) { } } } } } }";
        // A deconstructing foreach is a control block: foreach plus four ifs is level 5.
        var deconstructing = "class C { void M(int[][] xs) { foreach (var (a, b) in xs) { if (a > 0) { if (a > 0) { if (a > 0) { if (a > 0) { } } } } } } }";

        Assert.Single(CheckBodyLength("synthetic.cs", Parse(longBody)));
        Assert.Single(CheckNesting("synthetic.cs", Parse(deep)));
        Assert.Empty(CheckNesting("synthetic.cs", Parse(flatElseIf)));
        Assert.Single(CheckNesting("synthetic.cs", Parse(deepElse)));
        Assert.Empty(CheckNesting("synthetic.cs", Parse(shallowElse)));
        Assert.Single(CheckNesting("synthetic.cs", Parse(deconstructing)));
    }

    [Fact]
    public void Checker_BodyLimitIsExactlyFiftyLines()
    {
        // The body runs from the opening brace on its own line to the closing brace, both included.
        string Body(int statements) =>
            "class C { void M()\n{\n" + string.Concat(Enumerable.Repeat("int x = 0;\n", statements)) + "}\n}";

        Assert.Empty(CheckBodyLength("synthetic.cs", Parse(Body(48))));
        Assert.Single(CheckBodyLength("synthetic.cs", Parse(Body(49))));
    }

    [Fact]
    public void Checker_ScansAtLeastOneFileOfEachFolder()
    {
        var files = SourceFiles().ToList();
        Assert.Contains(files, f => f.Path.StartsWith("src" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.Contains(files, f => f.Path.StartsWith("tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static IEnumerable<(string Path, SyntaxNode Root)> SourceFiles()
    {
        var repo = FindRepoRoot();
        foreach (var folder in new[] { "src", "tests", "experiments" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, folder), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(repo, file);
                if (relative.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
                {
                    continue;
                }

                yield return (relative, Parse(File.ReadAllText(file)));
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bullwhip.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Bullwhip.sln not found above " + AppContext.BaseDirectory);
    }

    private static SyntaxNode Parse(string text) => CSharpSyntaxTree.ParseText(text).GetRoot();

    private static IEnumerable<string> CheckBodyLength(string path, SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            var body = BodyOf(node);
            if (body is null)
            {
                continue;
            }

            var span = body.GetLocation().GetLineSpan();
            var lines = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
            var line = span.StartLinePosition.Line + 1;
            if (lines > MaxBodyLines && !Exempt.Contains($"{path}:{line}"))
            {
                yield return $"{path}:{line}: body has {lines} lines (limit {MaxBodyLines})";
            }
        }
    }

    private static IEnumerable<string> CheckNesting(string path, SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes().Where(IsControl))
        {
            var level = NestingLevel(node);
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (level > MaxNestingLevel && !Exempt.Contains($"{path}:{line}"))
            {
                yield return $"{path}:{line}: block nested {level} levels deep (limit {MaxNestingLevel})";
            }
        }
    }

    // Counts the control blocks from the node up to its enclosing member, the node itself included.
    // An else-if does not add a level: it sits at the same indentation as its if. A braced else does (see IsElseBranch).
    private static int NestingLevel(SyntaxNode control)
    {
        var level = 1;
        SyntaxNode child = control;
        for (var parent = control.Parent; parent != null && !IsMember(parent); parent = parent.Parent)
        {
            if (IsControl(parent) && !IsElseBranch(parent, child))
            {
                level++;
            }

            child = parent;
        }

        return level;
    }

    private static SyntaxNode? BodyOf(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
        LocalFunctionStatementSyntax localFunction => (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody,
        AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody,
        _ => null,
    };

    private static bool IsMember(SyntaxNode node) =>
        node is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax;

    private static bool IsControl(SyntaxNode node) => node is IfStatementSyntax or ForStatementSyntax
        or ForEachStatementSyntax or ForEachVariableStatementSyntax or WhileStatementSyntax or DoStatementSyntax
        or SwitchStatementSyntax or TryStatementSyntax or UsingStatementSyntax or LockStatementSyntax;

    // Only "else if" shares the level of its if. A braced else block is indented one level deeper.
    private static bool IsElseBranch(SyntaxNode parent, SyntaxNode child) =>
        parent is IfStatementSyntax && child is ElseClauseSyntax { Statement: IfStatementSyntax };

    private static string Describe(IReadOnlyCollection<string> violations) =>
        violations.Count == 0 ? string.Empty : string.Join(Environment.NewLine, violations);
}
