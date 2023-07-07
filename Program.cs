using ProxyDot;
using Serilog;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;

Console.WriteLine("ProxyDot engine starting...");

var config = new ConfigurationBuilder()
               .AddJsonFile("appsettings.json")
               .Build();

var logger = new LoggerConfiguration()
    .ReadFrom.Configuration(config)
    .CreateLogger();

try
{
    RequireAdministrator();
}
catch (Exception ex)
{
    logger.Error(ex.Message);
    return;
}

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

static SecureString ReadPassword()
{
    Console.Write("Provide password: ");
    var password = new SecureString();
    while (true)
    {
        var key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.Enter)
            break;
        password.AppendChar(key.KeyChar);
    }
    Console.WriteLine();
    return password;
}

[DllImport("libc")]
static extern uint getuid();

/// <summary>
/// Asks for administrator privileges upgrade if the platform supports it, otherwise does nothing
/// </summary>
static void RequireAdministrator()
{
    string name = AppDomain.CurrentDomain.FriendlyName;
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new InvalidOperationException($"Application must be run as administrator. Right click the \"{name}\" file and select 'run as administrator' or run \"{name}\" in elevated terminal.");
        }
    }
    else if (getuid() != 0)
    {
        throw new InvalidOperationException($"Application must be run as root/sudo. From terminal, run the executable as 'sudo {name}'");
    }
}