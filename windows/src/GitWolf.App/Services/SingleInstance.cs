using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GitWolf.App.Services;

/// <summary>
/// One running copy per user and program path. A later one hands its command line to the first over a pipe
/// only this user can open, and exits, so two copies never overwrite each other's saved state.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const int MaxMessage = 32 * 1024;
    private const int AnyProcess = -1;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _closing = new();
    private readonly string _name;

    private SingleInstance(Mutex mutex, string name) => (_mutex, _name) = (mutex, name);

    private static string Name()
    {
        var identity = $"{Environment.UserName}|{Environment.ProcessPath}".ToUpperInvariant();
        return "GitWolf-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
    }

    /// <summary>
    /// Null when another copy took the arguments, so this one should exit. If that copy doesn't answer,
    /// this one runs anyway.
    /// </summary>
    public static SingleInstance? Claim(string[] arguments)
    {
        var name = Name();
        var mutex = new Mutex(initiallyOwned: false, name, out var created);
        if (created || !Send(name, arguments)) return new SingleInstance(mutex, name);
        mutex.Dispose();
        return null;
    }

    private static bool Send(string name, string[] arguments)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            // The running copy may itself be starting.
            pipe.Connect(Patience * 3);
            // Lets the running copy come to the front.
            AllowSetForegroundWindow(AnyProcess);
            var message = JsonSerializer.SerializeToUtf8Bytes(arguments);
            if (message.Length > MaxMessage) return true;
            pipe.Write(message);
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"The running copy didn't answer: {e.GetType().Name}");
            return false;
        }
    }

    /// <summary>Calls <paramref name="received"/> on a background thread for each later copy's arguments.</summary>
    public async Task ListenAsync(Action<string[]> received)
    {
        var token = _closing.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_name, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
                limit.CancelAfter(Patience);
                var buffer = new byte[MaxMessage + 1];
                var length = await pipe.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, limit.Token)
                    .ConfigureAwait(false);
                if (length is > 0 and <= MaxMessage
                    && JsonSerializer.Deserialize<string[]>(buffer.AsSpan(0, length)) is { } arguments
                    && arguments.All(a => a is not null))
                    received(arguments);
            }
            catch (OperationCanceledException)
            {
                // Closing, or a sender that stalled.
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                Trace.TraceWarning($"A request from another copy was dropped: {e.GetType().Name}");
                // A copy that is still closing may hold the name for a moment.
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException e)
            {
                // Someone else holds the name; trying again would only spin.
                Trace.TraceWarning($"Requests from other copies can't be received: {e.GetType().Name}");
                return;
            }
        }
    }

    public void Dispose()
    {
        if (_closing.IsCancellationRequested) return;
        _closing.Cancel();
        _mutex.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
