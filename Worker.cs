using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ProxyDot;

/// <summary>
/// Main worker/
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly InternalProperties _internalProperties;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="configuration">Configuration</param>
    /// <param name="internalProperties">ExtraProperties service</param>
    public Worker(ILogger<Worker> logger, IConfiguration configuration, InternalProperties internalProperties)
    {
        _configuration = configuration;
        _logger = logger;
        _internalProperties = internalProperties;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!HttpListener.IsSupported)
        {
            _logger.LogError("Windows XP SP2 or Server 2003+ is required to use the HttpListener class.");
            return;
        }

        // Get local port from config.
        if(!uint.TryParse(_configuration["ProxyDot:LocalPort"], out uint localPort))
        {
            // Default value
            localPort = 8001;
        }

        // Get upstream URI from config.
        string? upstreamUri = _configuration?["ProxyDot:UpstreamURI"];

        if (string.IsNullOrWhiteSpace(upstreamUri))
        {
            _logger.LogError("Upstream URI is empty");
        }

        using var listener = new HttpListener();

        // Bind prefixes.
        listener.Prefixes.Add($"http://127.0.0.1:{localPort}/");
        listener.Prefixes.Add($"http://localhost:{localPort}/");
        listener.Start();

        _logger.LogInformation("Listening on port {localPort}...", localPort);

        ICredentials credentials = GetCredentials();

        HttpClient httpClient = GetHttpClient(credentials);

        // Exclude with headers from client request.
        var stopList = _configuration?.GetSection("ProxyDot:IgnoredRequestHeaders")?
            .AsEnumerable()
            .Where(s => s.Value is not null)
            .Select(s => s.Value?.ToLowerInvariant())
            .ToHashSet();

        if (stopList is not null)
        {
            stopList.Add("content-length");
            stopList.Add("content-type");
        }

        uint requestCounter = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                HttpListenerContext context = await listener.GetContextAsync().WaitAsync(stoppingToken); ;
                HttpListenerRequest clientRequest = context.Request;

                requestCounter++;

                _logger.LogDebug("Received request #{requestCounter} for {localUrl}", requestCounter, clientRequest.Url);

                if (clientRequest?.RawUrl is null)
                {
                    _logger.LogWarning("Request #{requestCounter} is null", requestCounter);
                    continue;
                }

                var remoteUrl = $"{upstreamUri}{clientRequest.RawUrl}";

                var remoteRequest = new HttpRequestMessage()
                {
                    RequestUri = new Uri(remoteUrl),
                    Method = new HttpMethod(clientRequest.HttpMethod.ToUpper()),
                };


                _logger.LogInformation("Send request #{requestCounter} to {remoteUrl} method {method}", requestCounter, remoteUrl, remoteRequest.Method);

                // If request have body, forward it to upstream.
                if (clientRequest.HasEntityBody)
                {
                    var contentString = await GetRequestContent(clientRequest, stoppingToken);
                    remoteRequest.Content = new StringContent(contentString);

                    if (!string.IsNullOrEmpty(clientRequest.ContentType))
                    {
                        if (MediaTypeHeaderValue.TryParse(clientRequest.ContentType, out MediaTypeHeaderValue? requestMadiaType))
                        {
                            remoteRequest.Content.Headers.ContentType = requestMadiaType;
                            _logger.LogInformation(requestMadiaType.ToString());
                        }
                    }

                    remoteRequest.Content.Headers.ContentLength = (await remoteRequest.Content.ReadAsByteArrayAsync(stoppingToken)).LongLength;

                }

                // Forward headers to upstream.
                if (clientRequest.Headers?.Count > 0)
                {
                    CopyRequestHeaders(clientRequest, remoteRequest, stopList);

                    _logger.LogTrace("Remote request #{requestCounter} headers: {headers}",
                        requestCounter,
                        remoteRequest.Headers.Select(header =>
                            $"{header.Key}:[{string.Join(";", header.Value)}]"));
                }

                Stopwatch stopwatch = Stopwatch.StartNew();

                var remoteResponse = await httpClient.SendAsync(remoteRequest, stoppingToken);

                stopwatch.Stop();

                _logger.LogDebug(
                    "A response to the request #{requestCounter} was received. Status is {status}. Content-Length: {ContentLength}. Time taken ms: {ElapsedMilliseconds}",
                    requestCounter, remoteResponse.StatusCode,
                    remoteResponse.Content.Headers.ContentLength, stopwatch.ElapsedMilliseconds);

                if (remoteResponse is null)
                {
                    _logger.LogWarning("A response to the request #{requestCounter} is empty", requestCounter);
                    continue;
                }

                using HttpListenerResponse response = context.Response;
                response.ContentEncoding = Encoding.UTF8;

                // Forward headers to client.
                CopyResponseHeaders(remoteResponse, response);
                _logger.LogTrace("Response to request #{requestCounter} headers: {headers}",
                        requestCounter,
                        remoteResponse.Headers.Select(header =>
                            $"{header.Key}:[{string.Join(";", header.Value)}]"));

                response.StatusCode = (int)remoteResponse.StatusCode;

                byte[] buffer = await remoteResponse.Content.ReadAsByteArrayAsync(stoppingToken);
                response.ContentLength64 = buffer.Length;
                using Stream responseStream = response.OutputStream;
                await responseStream.WriteAsync(buffer, stoppingToken);
            }
            catch (OperationCanceledException ex)
            {
                // Apparently Ctrl-C pressed.
                _logger.LogInformation("Full stop");
                _logger.LogDebug("Canceled: {ex}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "We have a problem :( Last request #{requestCounter}", requestCounter);
            }

        }
        listener.Stop();
    }

    /// <summary>
    /// Get actual credentials from config values.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private ICredentials GetCredentials()
    {
        if (!bool.TryParse(_configuration["ProxyDot:UseDefaultCredentials"], out bool useDefaultCredentials))
        {
            useDefaultCredentials = false;
        }

        string upstreamURI = _configuration["ProxyDot:UpstreamURI"] ?? throw new Exception();
        string? domain = _configuration["ProxyDot:Domain"];
        string? username = _configuration["ProxyDot:UserName"];
        string authenticationMethod = _configuration["ProxyDot:AuthenticationMethod"] ?? "NTLM";

        if (useDefaultCredentials)
        {
            _logger.LogDebug("Use default credentials");
            return CredentialCache.DefaultCredentials;
        }

        _logger.LogDebug("Use new {authenticationMethod} credentials for domain '{domain}' user '{username}'", authenticationMethod, domain, username);

        CredentialCache credentialsCache = new()
        {
            {new Uri(upstreamURI), authenticationMethod, new NetworkCredential(username, _internalProperties.RemoteSystemPassword, domain)}
        };

        return credentialsCache;
    }

    private static HttpClient GetHttpClient(ICredentials credentials)
    {
        HttpClientHandler handler = new()
        {
            Credentials = credentials,
        };

        return new HttpClient(handler);
    }

    private static void CopyResponseHeaders(HttpResponseMessage? remoteResponse, HttpListenerResponse response)
    {
        if (remoteResponse?.Content?.Headers is null)
        {
            return;
        }

        foreach (var remoteHeader in remoteResponse.Content.Headers)
        {
            response.Headers.Set(remoteHeader.Key, remoteHeader.Value.FirstOrDefault());
        }
    }

    private static void CopyRequestHeaders(HttpListenerRequest clientRequest, HttpRequestMessage remoteRequest, HashSet<string?>? stopList)
    {
        foreach (string clientHeaderKey in clientRequest.Headers.Keys)
        {
            if (stopList is null || !stopList.Contains(clientHeaderKey.ToLowerInvariant()))
            {
                remoteRequest.Headers.Add(clientHeaderKey, clientRequest.Headers[clientHeaderKey]);
            }
        }
    }

    private static async Task<string> GetRequestContent(HttpListenerRequest request, CancellationToken stoppingToken)
    {
        if (!request.HasEntityBody)
        {
            return string.Empty;
        }

        using Stream body = request.InputStream;
        using var reader = new StreamReader(body, request.ContentEncoding);
        return await reader.ReadToEndAsync(stoppingToken);
    }
}
