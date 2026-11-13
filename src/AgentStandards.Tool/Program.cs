using AgentStandards.Core;
using AgentStandards.Tool;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

return await new Cli(ToolEnvironment.FromProcess(), Console.Out, Console.Error, Console.In).RunAsync(args, cts.Token);
