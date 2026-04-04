using GloSharp.Core;

namespace GloSharp.Tests;

public class DocCommentExtractionTests
{
    private readonly GloSharpProcessor _processor = new();

    [Test]
    public async Task Process_FrameworkMethod_ExtractsStructuredDocs()
    {
        var source = """
            Console.WriteLine("test");
            //        ^?
            """;
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Docs).IsNotNull();
        await Assert.That(persistent.Docs!.Summary).IsNotNull();
    }

    [Test]
    public async Task Process_LocalVariable_DocsIsNull()
    {
        var source = "var x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Docs).IsNull();
    }

    [Test]
    public async Task Process_ClassMethodWithAllDocTags_ExtractsAllSections()
    {
        var source = """
            // ---cut-before---
            class Calc
            {
                /// <summary>Adds two numbers together.</summary>
                /// <param name="a">The first number.</param>
                /// <param name="b">The second number.</param>
                /// <returns>The sum of a and b.</returns>
                /// <remarks>This method does not check for overflow.</remarks>
                /// <example>var result = Add(1, 2);</example>
                /// <exception cref="System.OverflowException">Thrown when the result overflows.</exception>
                public static int Add(int a, int b) => a + b;
            }
            Calc.Add(1, 2);
            //    ^?
            """;
        var result = await _processor.ProcessAsync(source);

        var docs = result.Hovers.First(h => h.Persistent).Docs;
        await Assert.That(docs).IsNotNull();
        await Assert.That(docs!.Summary).IsEqualTo("Adds two numbers together.");
        await Assert.That(docs.Params.Count).IsEqualTo(2);
        await Assert.That(docs.Params[0].Name).IsEqualTo("a");
        await Assert.That(docs.Params[0].Text).IsEqualTo("The first number.");
        await Assert.That(docs.Params[1].Name).IsEqualTo("b");
        await Assert.That(docs.Params[1].Text).IsEqualTo("The second number.");
        await Assert.That(docs.Returns).IsEqualTo("The sum of a and b.");
        await Assert.That(docs.Remarks).IsEqualTo("This method does not check for overflow.");
        await Assert.That(docs.Examples.Count).IsEqualTo(1);
        await Assert.That(docs.Examples[0]).IsEqualTo("var result = Add(1, 2);");
        await Assert.That(docs.Exceptions.Count).IsEqualTo(1);
        await Assert.That(docs.Exceptions[0].Type).IsEqualTo("OverflowException");
        await Assert.That(docs.Exceptions[0].Text).IsEqualTo("Thrown when the result overflows.");
    }

    [Test]
    public async Task Process_MethodWithSeeRef_ResolvesInlineXml()
    {
        var source = """
            // ---cut-before---
            class Converter
            {
                /// <summary>Converts to <see cref="System.String"/>.</summary>
                public static string Convert() => "";
            }
            Converter.Convert();
            //          ^?
            """;
        var result = await _processor.ProcessAsync(source);

        var docs = result.Hovers.First(h => h.Persistent).Docs;
        await Assert.That(docs).IsNotNull();
        await Assert.That(docs!.Summary).IsEqualTo("Converts to String.");
    }

    [Test]
    public async Task Process_MethodWithParamref_ResolvesInlineXml()
    {
        var source = """
            // ---cut-before---
            class Utils
            {
                /// <summary>Returns <paramref name="value"/> as-is.</summary>
                /// <param name="value">The input.</param>
                public static int Identity(int value) => value;
            }
            Utils.Identity(1);
            //      ^?
            """;
        var result = await _processor.ProcessAsync(source);

        var docs = result.Hovers.First(h => h.Persistent).Docs;
        await Assert.That(docs).IsNotNull();
        await Assert.That(docs!.Summary).IsEqualTo("Returns value as-is.");
    }

    [Test]
    public async Task Process_SummaryOnlyDocs_HasEmptyCollections()
    {
        var source = """
            // ---cut-before---
            class Worker
            {
                /// <summary>A simple method.</summary>
                public static void DoWork() { }
            }
            Worker.DoWork();
            //       ^?
            """;
        var result = await _processor.ProcessAsync(source);

        var docs = result.Hovers.First(h => h.Persistent).Docs;
        await Assert.That(docs).IsNotNull();
        await Assert.That(docs!.Summary).IsEqualTo("A simple method.");
        await Assert.That(docs.Params.Count).IsEqualTo(0);
        await Assert.That(docs.Returns).IsNull();
        await Assert.That(docs.Remarks).IsNull();
        await Assert.That(docs.Examples.Count).IsEqualTo(0);
        await Assert.That(docs.Exceptions.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Process_DocsJsonSerialization_StructuredFormat()
    {
        var source = """
            // ---cut-before---
            class Funcs
            {
                /// <summary>Does stuff.</summary>
                /// <param name="x">The x.</param>
                /// <returns>A value.</returns>
                public static int Foo(int x) => x;
            }
            Funcs.Foo(1);
            //     ^?
            """;
        var result = await _processor.ProcessAsync(source);
        var json = JsonOutput.Serialize(result);

        await Assert.That(json).Contains("\"summary\":");
        await Assert.That(json).Contains("\"Does stuff.\"");
        await Assert.That(json).Contains("\"params\":");
        await Assert.That(json).Contains("\"returns\":");
        await Assert.That(json).Contains("\"A value.\"");
        // Should not contain flat docs string
        await Assert.That(json).DoesNotContain("\"docs\": \"");
    }
}
