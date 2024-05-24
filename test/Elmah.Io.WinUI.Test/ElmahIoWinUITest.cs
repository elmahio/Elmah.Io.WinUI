using Elmah.Io.Client;
using NSubstitute;
using NUnit.Framework;
using System.Reflection;

namespace Elmah.Io.WinUI.Test
{
    public class ElmahIoWinUITest
    {
        [Test]
        public void Test()
        {
            // Arrange
            var options = new ElmahIoWinUIOptions("hello", Guid.NewGuid())
            {
                Application = "MyApp"
            };

            var optionsField = typeof(ElmahIoWinUI).GetField("_options", BindingFlags.Static | BindingFlags.NonPublic);
            optionsField?.SetValue(null, options);

            var messagesClient = Substitute.For<IMessagesClient>();
            var elmahIoClient = Substitute.For<IElmahioAPI>();
            elmahIoClient.Messages.Returns(messagesClient);

            var loggerField = typeof(ElmahIoWinUI).GetField("_logger", BindingFlags.Static | BindingFlags.NonPublic);
            loggerField?.SetValue(null, elmahIoClient);

            var breadcrumbs = new List<Breadcrumb>();
            var breadcrumbsField = typeof(ElmahIoWinUI).GetField("_breadcrumbs", BindingFlags.Static | BindingFlags.NonPublic);
            breadcrumbsField?.SetValue(null, breadcrumbs);

            ElmahIoWinUI.AddBreadcrumb(new Breadcrumb
            {
                DateTime = DateTime.UtcNow,
                Action = "Navigation",
                Message = "Opening app",
                Severity = "Information",
            });

            var ex = new ApplicationException("Oh no");

            // Act
            ElmahIoWinUI.Log(ex);

            // Assert
            messagesClient.Received().Create(Arg.Is<string>(s => s == options.LogId.ToString()), Arg.Is<CreateMessage>(msg => AssertMessage(msg, ex)));
        }

        private static bool AssertMessage(CreateMessage msg, ApplicationException ex)
        {
            if (msg.Title != "Oh no") return false;
            if (msg.Breadcrumbs.Count != 1) return false;
            var openingBreadcrumb = msg.Breadcrumbs.First();
            if (openingBreadcrumb.Action != "Navigation" || openingBreadcrumb.Severity != "Information" || openingBreadcrumb.Message != "Opening app") return false;

            if (string.IsNullOrWhiteSpace(msg.Detail)) return false;
            if (msg.Type != ex.GetType().FullName) return false;
            if (msg.Severity != "Error") return false;
            if (msg.Source != ex.Source) return false;
            if (msg.Application != "MyApp") return false;

            return true;
        }
    }
}
