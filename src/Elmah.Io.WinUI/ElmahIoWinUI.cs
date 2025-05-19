using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Elmah.Io.Client;
using System.Linq;
using System.Security.Principal;
using System.Net.Http.Headers;
using Microsoft.UI.Xaml;

namespace Elmah.Io.WinUI
{
    /// <summary>
    /// Main class used to interact with the elmah.io API from WinUI.
    /// </summary>
    public static class ElmahIoWinUI
    {
        internal static readonly string _assemblyVersion = typeof(ElmahIoWinUI).Assembly.GetName().Version?.ToString() ?? "Unknown";
        internal static readonly string _elmahIoClientAssemblyVersion = typeof(IElmahioAPI).Assembly.GetName().Version?.ToString() ?? "Unknown";
        internal static readonly string _winUiAssemblyVersion = typeof(Application).Assembly.GetName().Version?.ToString() ?? "Unknown";

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private static ElmahIoWinUIOptions _options;
        private static IElmahioAPI _logger;
        private static List<Breadcrumb> _breadcrumbs;
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

            _options = options;
            _breadcrumbs = new List<Breadcrumb>(1 + options.MaximumBreadcrumbs);
            _logger = ElmahioAPI.Create(options.ApiKey, new ElmahIoOptions
            {
                Timeout = new TimeSpan(0, 0, 5),
                UserAgent = UserAgent(),
            });

            _logger.Messages.OnMessageFail += (sender, args) =>
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
                Application = _options.Application,
                ServerVariables =
                [
                    new("User-Agent", $"X-ELMAHIO-APPLICATION; OS=Windows; OSVERSION={Environment.OSVersion.Version}; ENGINE=WinUI"),
            ]
            };

            if (_options.OnFilter != null && _options.OnFilter(createMessage))
            {
                return;
            }

            _options.OnMessage?.Invoke(createMessage);

            try
            {
                _logger.Messages.Create(_options.LogId.ToString(), createMessage);
            }
            catch (Exception ex)
            {
                _options.OnError?.Invoke(createMessage, ex);
            }
        }

        /// <summary>
        /// Add a breadcrumb in-memory. Breadcrumbs will be added to errors when logged
        /// either automatically or manually.
        /// </summary>
        public static void AddBreadcrumb(Breadcrumb breadcrumb)
        {
            _breadcrumbs.Add(breadcrumb);

            if (_breadcrumbs.Count >= _options.MaximumBreadcrumbs)
            {
                var oldest = _breadcrumbs.OrderBy(b => b.DateTime).First();
                _breadcrumbs.Remove(oldest);
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
            if (_breadcrumbs == null || _breadcrumbs.Count == 0) return [];

            var utcNow = DateTime.UtcNow;

            // Set default values on properties not set
            foreach (var breadcrumb in _breadcrumbs)
            {
                if (!breadcrumb.DateTime.HasValue) breadcrumb.DateTime = utcNow;
                if (string.IsNullOrWhiteSpace(breadcrumb.Severity)) breadcrumb.Severity = "Information";
                if (string.IsNullOrWhiteSpace(breadcrumb.Action)) breadcrumb.Action = "Log";
            }

            var breadcrumbs = _breadcrumbs.OrderByDescending(l => l.DateTime).ToList();
            _breadcrumbs.Clear();
            return breadcrumbs;
        }

        private static string UserAgent()
        {
            return new StringBuilder()
                .Append(new ProductInfoHeaderValue(new ProductHeaderValue("Elmah.Io.WinUI", _assemblyVersion)).ToString())
                .Append(' ')
                .Append(new ProductInfoHeaderValue(new ProductHeaderValue("Microsoft.WinUI", _winUiAssemblyVersion)).ToString())
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
                        new AssemblyInfo { Name = "Elmah.Io.WinUI", Version = _assemblyVersion },
                        new AssemblyInfo { Name = "Elmah.Io.Client", Version = _elmahIoClientAssemblyVersion },
                        new AssemblyInfo { Name = "Microsoft.WindowsAppSDK", Version = _winUiAssemblyVersion }
                    ],
                    EnvironmentVariables = [],
                };

                var installation = new CreateInstallation
                {
                    Type = "windowsapp",
                    Name = _options.Application,
                    Loggers = [loggerInfo]
                };

                EnvironmentVariablesHelper.GetElmahIoAppSettingsEnvironmentVariables().ForEach(v => loggerInfo.EnvironmentVariables.Add(v));
                EnvironmentVariablesHelper.GetDotNetEnvironmentVariables().ForEach(v => loggerInfo.EnvironmentVariables.Add(v));

                _options.OnInstallation?.Invoke(installation);

                _logger.Installations.CreateAndNotify(_options.LogId, installation);
            }
            catch
            {
                // We don't want to crash the entire application if the installation fails. Carry on.
            }
        }
    }
}
