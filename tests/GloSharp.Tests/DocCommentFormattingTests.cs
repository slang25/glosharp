using GloSharp.Core;

namespace GloSharp.Tests;

/// <summary>
/// Doc comment text: langword, crefs, generic arity, nested elements and inheritdoc.
/// </summary>
public class DocCommentFormattingTests
{
    private readonly GloSharpProcessor _processor = new();

    private async Task<GloSharpDocComment> DocsFor(string declarations, string usage, int caret)
    {
        var source = "// ---cut-before---\n" + declarations + "\n" + usage + "\n//" + new string(' ', caret - 2) + "^?";
        var result = await _processor.ProcessAsync(source);
        var hover = result.Hovers.Single(h => h.Persistent);
        await Assert.That(hover.Docs).IsNotNull();
        return hover.Docs!;
    }

    [Test]
    public async Task Langword_IsRendered()
    {
        var docs = await DocsFor("""
            static class C
            {
                /// <summary>Returns <see langword="null"/> when empty.</summary>
                /// <param name="value">Must not be <see langword="null"/>.</param>
                public static string? F(string value) => null;
            }
            """, "C.F(\"\");", 2);

        await Assert.That(docs.Summary).IsEqualTo("Returns null when empty.");
        await Assert.That(docs.Params[0].Text).IsEqualTo("Must not be null.");
    }

    [Test]
    public async Task MethodCref_KeepsTypeAndMember()
    {
        var docs = await DocsFor("""
            static class C
            {
                /// <summary>Like <see cref="string.Join(string, string[])"/> but lazy.</summary>
                public static void F() { }
            }
            """, "C.F();", 2);

        await Assert.That(docs.Summary).IsEqualTo("Like String.Join but lazy.");
    }

    [Test]
    public async Task GenericCref_UsesAngleBrackets()
    {
        var docs = await DocsFor("""
            static class C
            {
                /// <summary>See <see cref="System.Collections.Generic.List{T}"/> and <see cref="System.Collections.Generic.Dictionary{TKey, TValue}.TryGetValue"/>.</summary>
                public static void F() { }
            }
            """, "C.F();", 2);

        await Assert.That(docs.Summary).IsEqualTo("See List<T> and Dictionary<TKey, TValue>.TryGetValue.");
    }

    [Test]
    public async Task NestedElements_KeepTheirContent()
    {
        var docs = await DocsFor("""
            static class C
            {
                /// <summary>
                /// <para>Uses <see cref="int"/> values.</para>
                /// <para>Also <c>x</c> and <paramref name="y"/>.</para>
                /// </summary>
                /// <param name="y">The y.</param>
                public static void F(int y) { }
            }
            """, "C.F(1);", 2);

        await Assert.That(docs.Summary).IsEqualTo("Uses Int32 values. Also x and y.");
    }

    [Test]
    public async Task InheritDoc_FromInterface()
    {
        var docs = await DocsFor("""
            interface IShape
            {
                /// <summary>Computes the area.</summary>
                /// <returns>The area.</returns>
                double Area();
            }
            class Square : IShape
            {
                /// <inheritdoc/>
                public double Area() => 1;
            }
            """, "new Square().Area();", 13);

        await Assert.That(docs.Summary).IsEqualTo("Computes the area.");
        await Assert.That(docs.Returns).IsEqualTo("The area.");
    }

    [Test]
    public async Task InheritDoc_FromBaseOverride_LocalElementsWin()
    {
        var docs = await DocsFor("""
            class Base
            {
                /// <summary>Base summary.</summary>
                /// <remarks>Base remarks.</remarks>
                public virtual void Run() { }
            }
            class Derived : Base
            {
                /// <inheritdoc/>
                /// <remarks>Derived remarks.</remarks>
                public override void Run() { }
            }
            """, "new Derived().Run();", 14);

        await Assert.That(docs.Summary).IsEqualTo("Base summary.");
        await Assert.That(docs.Remarks).IsEqualTo("Derived remarks.");
    }

    [Test]
    public async Task InheritDoc_WithCref()
    {
        var docs = await DocsFor("""
            static class C
            {
                /// <summary>The original.</summary>
                public static void Original() { }

                /// <inheritdoc cref="Original"/>
                public static void Copy() { }
            }
            """, "C.Copy();", 2);

        await Assert.That(docs.Summary).IsEqualTo("The original.");
    }

    [Test]
    public async Task BclDocs_KeepLangwordAndCrefText()
    {
        var source = "string.IsNullOrEmpty(\"\");\n//     ^?";
        var result = await _processor.ProcessAsync(source);
        var docs = result.Hovers.Single(h => h.Persistent).Docs;

        await Assert.That(docs).IsNotNull();
        await Assert.That(docs!.Summary).Contains("is null or");
        await Assert.That(docs.Returns!).Contains("true");
    }

    [Test]
    [Arguments("T:System.Collections.Generic.List`1", "List")]
    [Arguments("M:System.String.Join(System.String,System.String[])", "String.Join")]
    [Arguments("M:System.Linq.Enumerable.Select``2(System.Collections.Generic.IEnumerable{``0},System.Func{``0,``1})", "Enumerable.Select")]
    [Arguments("F:System.Decimal.MaxValue", "Decimal.MaxValue")]
    [Arguments("T:System.OverflowException", "OverflowException")]
    [Arguments("M:Foo.Bar.#ctor(System.Int32)", "Bar.Bar")]
    public async Task FormatCrefText_Fallback(string cref, string expected)
    {
        var text = DocCommentFormatter.FormatCrefText(cref);
        await Assert.That(text).IsEqualTo(expected);
    }
}
