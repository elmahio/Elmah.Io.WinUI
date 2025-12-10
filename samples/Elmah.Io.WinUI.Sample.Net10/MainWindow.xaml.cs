#pragma warning disable S125 // Sections of code should not be commented out
using Microsoft.UI.Xaml;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Elmah.Io.WinUI.Sample.Net10
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void MyButton_Click(object sender, RoutedEventArgs e)
        {
            // Comment out to add a breadcrumb before the error
            //ElmahIoWinUI.AddBreadcrumb(new Client.Breadcrumb(DateTimeOffset.Now, "Information", "Click", "User clicking error button"));

            myButton.Content = "Clicked";

            throw new InvalidOperationException("Oh no");
        }
    }
}
#pragma warning restore S125 // Sections of code should not be commented out