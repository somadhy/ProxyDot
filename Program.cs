using ProxyDot;
using Serilog;

Console.WriteLine("ProxyDot engine starting...");

var config = new ConfigurationBuilder()
               .AddJsonFile("appsettings.json")
               .Build();

var logger = new LoggerConfiguration()
    .ReadFrom.Configuration(config)
    .CreateLogger();

logger.Information("ProxyDot log began!");

var builder = Host.CreateApplicationBuilder(args);


if (!bool.TryParse(config["ProxyDot:UseDefaultCredentials"], out bool useDefaultCredentials))
{
    useDefaultCredentials = false;
}

InternalProperties internalProperties = new();

if (!useDefaultCredentials)
{
    internalProperties.RemoteSystemPassword = ReadPassword();
}

builder.Services
    .AddSingleton(internalProperties)
    .AddHostedService<Worker>()
    .Configure<HostOptions>(hostOptions =>
    {
    hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
    });

builder.Logging.ClearProviders()
    .AddSerilog(logger);

var host = builder.Build();
host.Run();

static string ReadPassword()
{
    Console.Write("Provide password: ");
    var password = string.Empty;
    while (true)
    {
        var key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.Enter)
            break;
        password += key.KeyChar;
    }
    Console.WriteLine();
    return password;
}
