using Elmah.Io.Client;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace Elmah.Io.WinUI
{
    /// <summary>
    /// Main class used to interact with the elmah.io API from WinUI.
    /// </summary>
    public static class ElmahIoWinUI
    {
        internal static readonly string assemblyVersion = typeof(ElmahIoWinUI).Assembly.GetName().Version?.ToString() ?? "Unknown";
        internal static readonly string elmahIoClientAssemblyVersion = typeof(IElmahioAPI).Assembly.GetName().Version?.ToString() ?? "Unknown";
        internal static readonly string winUiAssemblyVersion = typeof(Application).Assembly.GetName().Version?.ToString() ?? "Unknown";

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private static ElmahIoWinUIOptions options;
        private static IElmahioAPI logger;
        private static List<Breadcrumb> breadcrumbs;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        /// <summary>
        /// Initialize logging of all uncaught errors to elmah.io.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2208:Instantiate argument exceptions correctly", Justification = "<Pending>")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3928:Parameter names used into ArgumentException constructors should match an existing one ", Justification = "<Pending>")]
        public static void Init(ElmahIoWinUIOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (string.IsNullOrWhiteSpace(options.ApiKey)) throw new ArgumentNullException(nameof(options.ApiKey));
            if (options.LogId == Guid.Empty) throw new ArgumentException(nameof(options.LogId));

            ElmahIoWinUI.options = options;
            breadcrumbs = new List<Breadcrumb>(1 + options.MaximumBreadcrumbs);
            logger = ElmahioAPI.Create(options.ApiKey, new ElmahIoOptions
            {
                Timeout = new TimeSpan(0, 0, 5),
                UserAgent = UserAgent(),
            });

            logger.Messages.OnMessageFail += (sender, args) =>
            {
                options.OnError?.Invoke(args.Message, args.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                Log(args.ExceptionObject as Exception);

            TaskScheduler.UnobservedTaskException += (sender, args) =>
                Log(args.Exception);

            Application.Current.UnhandledException += (sender, args) =>
                Log(args.Exception);

            CreateInstallation();
        }

        /// <summary>
        /// Log an exception to elmah.io manually.
        /// </summary>
        public static void Log(Exception? exception)
        {
            var baseException = exception?.GetBaseException();
            var createMessage = new CreateMessage
            {
                DateTime = DateTime.UtcNow,
                Detail = exception?.ToString(),
                Type = baseException?.GetType().FullName,
                Title = baseException?.Message ?? "An error occurred",
                Data = exception?.ToDataList(),
                Severity = "Error",
                Source = baseException?.Source,
                User = WindowsIdentity.GetCurrent().Name,
                Hostname = Hostname(),
                Breadcrumbs = Breadcrumbs(),
                Application = options.Application,
                ServerVariables =
                [
                    new("User-Agent", $"X-ELMAHIO-APPLICATION; OS=Windows; OSVERSION={Environment.OSVersion.Version}; ENGINE=WinUI"),
                    new("Client-IP", ClientIp())
                ]
            };

            if (options.OnFilter != null && options.OnFilter(createMessage))
            {
                return;
            }

            options.OnMessage?.Invoke(createMessage);

            try
            {
                logger.Messages.Create(options.LogId.ToString(), createMessage);
            }
            catch (Exception ex)
            {
                options.OnError?.Invoke(createMessage, ex);
            }
        }

        /// <summary>
        /// Add a breadcrumb in-memory. Breadcrumbs will be added to errors when logged
        /// either automatically or manually.
        /// </summary>
        public static void AddBreadcrumb(Breadcrumb breadcrumb)
        {
            breadcrumbs.Add(breadcrumb);

            if (breadcrumbs.Count >= options.MaximumBreadcrumbs)
            {
                var oldest = breadcrumbs.OrderBy(b => b.DateTime).First();
                breadcrumbs.Remove(oldest);
            }
        }

        private static string? ClientIp()
        {
            try
            {
                // Find the NIC that has an IPv4 default gateway
                var nic = NetworkInterface
                    .GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork));

                if (nic == null) return null;

                // Get IPv4 address from that NIC
                var ip = nic
                    .GetIPProperties()
                    .UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

                return ip?.Address.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string? Hostname()
        {
            var machineName = Environment.MachineName;
            if (!string.IsNullOrWhiteSpace(machineName)) return machineName;

            return Environment.GetEnvironmentVariable("COMPUTERNAME");
        }

        private static List<Breadcrumb> Breadcrumbs()
        {
            if (ElmahIoWinUI.breadcrumbs == null || ElmahIoWinUI.breadcrumbs.Count == 0) return [];

            var utcNow = DateTime.UtcNow;

            // Set default values on properties not set
            foreach (var breadcrumb in ElmahIoWinUI.breadcrumbs)
            {
                if (!breadcrumb.DateTime.HasValue) breadcrumb.DateTime = utcNow;
                if (string.IsNullOrWhiteSpace(breadcrumb.Severity)) breadcrumb.Severity = "Information";
                if (string.IsNullOrWhiteSpace(breadcrumb.Action)) breadcrumb.Action = "Log";
            }

            var ordered = breadcrumbs.OrderByDescending(l => l.DateTime).ToList();
            breadcrumbs.Clear();
            return ordered;
        }

        private static string UserAgent()
        {
            return new StringBuilder()
                .Append(new ProductInfoHeaderValue(new ProductHeaderValue("Elmah.Io.WinUI", assemblyVersion)).ToString())
                .Append(' ')
                .Append(new ProductInfoHeaderValue(new ProductHeaderValue("Microsoft.WinUI", winUiAssemblyVersion)).ToString())
                .ToString();
        }

        private static void CreateInstallation()
        {
            try
            {
                var loggerInfo = new LoggerInfo
                {
                    Type = "Elmah.Io.WinUI",
                    Properties = [],
                    ConfigFiles = [],
                    Assemblies =
                    [
                        new AssemblyInfo { Name = "Elmah.Io.WinUI", Version = assemblyVersion },
                        new AssemblyInfo { Name = "Elmah.Io.Client", Version = elmahIoClientAssemblyVersion },
                        new AssemblyInfo { Name = "Microsoft.WindowsAppSDK", Version = winUiAssemblyVersion }
                    ],
                    EnvironmentVariables = [],
                };

                var installation = new CreateInstallation
                {
                    Type = "windowsapp",
                    Name = options.Application,
                    Loggers = [loggerInfo]
                };

                EnvironmentVariablesHelper.GetElmahIoAppSettingsEnvironmentVariables().ForEach(v => loggerInfo.EnvironmentVariables.Add(v));
                EnvironmentVariablesHelper.GetDotNetEnvironmentVariables().ForEach(v => loggerInfo.EnvironmentVariables.Add(v));

                options.OnInstallation?.Invoke(installation);

                logger.Installations.CreateAndNotify(options.LogId, installation);
            }
            catch
            {
                // We don't want to crash the entire application if the installation fails. Carry on.
            }
        }
    }
}
