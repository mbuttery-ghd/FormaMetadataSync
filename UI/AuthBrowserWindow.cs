using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

// This assembly already only ships for net8.0-windows/AutoCAD (win-x64); WebView2's
// Windows-10-minimum requirement is always satisfied at runtime.
[module: System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]

namespace AccC3DMetadata.UI
{
    /// <summary>
    /// Thrown when the WebView2 Runtime is not installed on the machine, signalling the
    /// caller to fall back to the system-browser + loopback-listener auth flow.
    /// </summary>
    public class WebView2RuntimeNotFoundException : Exception
    {
        public WebView2RuntimeNotFoundException(string message, Exception inner)
            : base(message, inner) { }
    }

    /// <summary>
    /// Modal, in-process sign-in window that hosts the Autodesk OAuth authorize page in a
    /// WebView2 control. Replaces the system-browser + loopback-HTTP-listener flow so that:
    /// <list type="bullet">
    ///   <item><description>Closing the window is an immediate, explicit cancellation — there is
    ///     no way to "lose" the browser and leave the sign-in hanging for the full timeout.</description></item>
    ///   <item><description>No local port/URL-ACL is required — the redirect is intercepted in-process
    ///     before the WebView2 control attempts to load it.</description></item>
    /// </list>
    /// </summary>
    public class AuthBrowserWindow : Window
    {
        private readonly string _authorizeUrl;
        private readonly string _redirectUri;
        private readonly TaskCompletionSource<Uri> _tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private WebView2 _webView;

        private static readonly SolidColorBrush AccBlue = Freeze(
            new SolidColorBrush(Color.FromRgb(0, 120, 212))
        );

        public AuthBrowserWindow(string authorizeUrl, string redirectUri)
        {
            _authorizeUrl = authorizeUrl;
            _redirectUri = redirectUri;

            Title = "Sign in to Autodesk";
            Width = 500;
            Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
            root.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            );

            var accent = new Border { Background = AccBlue };
            Grid.SetRow(accent, 0);

            _webView = new WebView2();
            Grid.SetRow(_webView, 1);

            root.Children.Add(accent);
            root.Children.Add(_webView);
            Content = root;

            Loaded += async (_, _) => await InitializeAsync().ConfigureAwait(true);
            // Closing the window (the "X" button, Alt+F4, or Close()) without having already
            // resolved the code is treated as an explicit user cancellation, not a hang.
            Closed += (_, _) => _tcs.TrySetCanceled();
        }

        /// <summary>
        /// Shows the window modally and returns the redirect URI captured from the OAuth
        /// callback once the user completes (or cancels) sign-in.
        /// </summary>
        /// <exception cref="OperationCanceledException">The user closed the sign-in window.</exception>
        /// <exception cref="WebView2RuntimeNotFoundException">The WebView2 Runtime is not installed.</exception>
        public async Task<Uri> WaitForRedirectAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => Dispatcher.Invoke(Close));

            // Application.ShowModalWindow blocks the caller's STA thread until Close() is
            // called, but the WebView2 navigation events still pump on the same dispatcher.
            Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(this);

            return await _tcs.Task.ConfigureAwait(false);
        }

        private async Task InitializeAsync()
        {
            try
            {
                // Isolate the sign-in session's cookies/cache per-user so switching Autodesk
                // accounts doesn't require clearing shared browser state.
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AccC3DSync",
                    "WebView2"
                );
                var env = await CoreWebView2Environment
                    .CreateAsync(userDataFolder: userDataFolder)
                    .ConfigureAwait(true);

                await _webView.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            }
            catch (Exception ex) when (IsRuntimeMissing(ex))
            {
                _tcs.TrySetException(
                    new WebView2RuntimeNotFoundException(
                        "The WebView2 Runtime is not installed on this machine.",
                        ex
                    )
                );
                Close();
                return;
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
                Close();
                return;
            }

            _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            _webView.CoreWebView2.Navigate(_authorizeUrl);
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!e.Uri.StartsWith(_redirectUri, StringComparison.OrdinalIgnoreCase))
                return;

            // Intercept the redirect before WebView2 tries to actually load it (there is no
            // real server listening on the loopback URI in this flow).
            e.Cancel = true;
            _tcs.TrySetResult(new Uri(e.Uri));
            Close();
        }

        private static bool IsRuntimeMissing(Exception ex) =>
            ex is WebView2RuntimeNotFoundException
            || (ex is COMException or FileNotFoundException or DllNotFoundException)
            || ex.Message.Contains("WebView2", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("HRESULT: 0x80070002", StringComparison.OrdinalIgnoreCase);

        private static T Freeze<T>(T freezable)
            where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
