Console.WriteLine("Bedrock Console Client - Milestone 0 (no networking yet)");
Console.WriteLine("Press Ctrl+C to exit.");

var exitSignal = new ManualResetEventSlim(initialState: false);

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    exitSignal.Set();
};

exitSignal.Wait();

Console.WriteLine("Shutting down.");
