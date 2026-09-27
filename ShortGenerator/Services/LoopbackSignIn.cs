using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ShortGenerator.Services;

/// <summary>
/// The loopback half of "Sign in with galiluna": a one-shot HTTP listener on 127.0.0.1 that waits for
/// galiluna to redirect the browser to /galiluna/callback with either a key or an error, checks the
/// anti-forgery state, and answers the browser with a small page. Never logs the key.
/// </summary>
public sealed class LoopbackSignIn : IDisposable
{
    public sealed record Result(string? Key, string? Error, bool StateMismatch);

    private readonly HttpListener _listener;

    public int Port { get; }
    public string State { get; }
    public string CallbackUrl => $"http://127.0.0.1:{Port}/galiluna/callback/";

    private LoopbackSignIn(HttpListener listener, int port, string state) { _listener = listener; Port = port; State = state; }

    /// <summary>Binds a random free port between 1024 and 65535 (retrying if taken) and creates the state.</summary>
    public static LoopbackSignIn Start()
    {
        var state = RandomState();
        for (int attempt = 0; attempt < 12; attempt++)
        {
            int port = RandomNumberGenerator.GetInt32(1024, 65536);
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/galiluna/callback/");
            try { listener.Start(); return new LoopbackSignIn(listener, port, state); }
            catch (HttpListenerException) { listener.Close(); }
        }
        throw new InvalidOperationException("No free local port could be opened for the browser callback.");
    }

    /// <summary>
    /// Waits for the callback. Requests whose state does not match are answered and ignored (the wait
    /// continues), so a stray or forged request cannot complete the sign-in. Cancellation returns null.
    /// </summary>
    public async Task<Result?> WaitAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var getContext = _listener.GetContextAsync();
                var done = await Task.WhenAny(getContext, Task.Delay(Timeout.Infinite, ct));
                if (done != getContext) return null;
                var context = await getContext;
                var q = context.Request.QueryString;
                bool stateOk = string.Equals(q["state"], State, StringComparison.Ordinal);
                if (!stateOk)
                {
                    await WriteAsync(context.Response, Page("This sign-in link does not match the request from Galiluna Shorts. Please try again from the app."));
                    continue;
                }
                var error = q["error"];
                var key = q["key"];
                if (!string.IsNullOrEmpty(error))
                {
                    await WriteAsync(context.Response, Page("Sign-in was not completed. You can close this tab and return to Galiluna Shorts."));
                    return new Result(null, error, false);
                }
                if (!string.IsNullOrEmpty(key))
                {
                    await WriteAsync(context.Response, Page("Signed in. You can close this tab and return to Galiluna Shorts."));
                    return new Result(key, null, false);
                }
                await WriteAsync(context.Response, Page("Nothing to do here. You can close this tab."));
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or OperationCanceledException) { }
        return null;
    }

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); } catch { }
    }

    public static string RandomState()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('='); // 43 URL-safe chars
    }

    private static string Page(string message) =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>Galiluna Shorts</title>" +
        "<style>body{font-family:Inter,Segoe UI,sans-serif;background:#F6F7FC;color:#0B1028;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}" +
        ".card{background:#fff;border:1px solid #E7EAF5;border-radius:16px;padding:32px 40px;max-width:420px;text-align:center;box-shadow:0 16px 50px rgba(41,91,255,.12)}" +
        "h1{font-size:20px;margin:0 0 8px;color:#2E3A6E}p{margin:0;color:#3E4668}</style></head>" +
        $"<body><div class=\"card\"><h1>Galiluna Shorts</h1><p>{WebUtility.HtmlEncode(message)}</p></div></body></html>";

    private static async Task WriteAsync(HttpListenerResponse response, string html)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
            response.OutputStream.Close();
        }
        catch { }
    }
}
