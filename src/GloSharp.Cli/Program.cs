using System.Text;
using GloSharp.Cli;

// JSON/HTML on stdout and snippets on stdin are UTF-8 regardless of the console code page
// (Windows defaults to an OEM code page, which would mangle non-ASCII snippets).
try
{
    Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
catch (IOException)
{
    // No console attached; the defaults apply.
}

return await CliApp.RunAsync(args, CliConsole.System);
