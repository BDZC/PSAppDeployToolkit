﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using PSADT.ProcessManagement;
using PSADT.UserInterface.DialogOptions;
using Windows.Win32;
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;

namespace PSADT.UserInterface.Dialogs.Fluent
{
    /// <summary>
    /// Unified dialog for PSAppDeployToolkit that consolidates all dialog types into one
    /// </summary>
    internal abstract partial class FluentDialog : Window, IDialogBase, INotifyPropertyChanged
    {
        /// <summary>
        /// Static constructor to set up the theme and resources for the dialog.
        /// </summary>
        static FluentDialog()
        {
            Application.Current.Resources.MergedDictionaries.Add(new ThemeResources());
            Application.Current.Resources.MergedDictionaries.Add(new XamlControlsResources());
        }

        /// <summary>
        /// Initializes a new instance of FluentDialog
        /// </summary>
        /// <param name="options">Mandatory options needed to construct the window.</param>
        /// <param name="customMessageText"></param>
        /// <param name="countdownDuration"></param>
        /// <param name="countdownWarningDuration"></param>
        /// <param name="countdownStopwatch"></param>
        private protected FluentDialog(BaseOptions options, string? customMessageText = null, TimeSpan? countdownDuration = null, TimeSpan? countdownWarningDuration = null, Stopwatch? countdownStopwatch = null)
        {
            // Initialize the window
            InitializeComponent();

            // If the accent color is passed through, update via ThemeManager
            if (null != options.FluentAccentColor)
            {
                ThemeManager.Current.AccentColor = IntToColor(options.FluentAccentColor.Value);
            }

            // Set basic properties
            Title = options.AppTitle;
            AppTitleTextBlock.Text = options.AppTitle;
            SubtitleTextBlock.Text = options.Subtitle;

            // Set accessibility properties
            AutomationProperties.SetName(this, options.AppTitle);

            // Set remaining properties from the options
            if (null != options.DialogPosition)
            {
                _dialogPosition = options.DialogPosition.Value;
            }
            if (null != options.DialogAllowMove)
            {
                _dialogAllowMove = options.DialogAllowMove.Value;
            }
            if (_dialogAllowMove)
            {
                MouseLeftButtonDown += (sender, e) => DragMove();
            }
            WindowStartupLocation = WindowStartupLocation.Manual;
            Topmost = options.DialogTopMost;

            // Set supplemental options also
            _customMessageText = customMessageText;
            _countdownDuration = countdownDuration;
            _countdownWarningDuration = countdownWarningDuration;
            _countdownStopwatch = null != countdownStopwatch ? countdownStopwatch : new();
            CountdownStackPanel.Visibility = _countdownDuration.HasValue ? Visibility.Visible : Visibility.Collapsed;

            // Pre-format the custom message if we have one
            if (!string.IsNullOrWhiteSpace(_customMessageText))
            {
                FormatMessageWithHyperlinks(CustomMessageTextBlock, _customMessageText!);
                CustomMessageTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                CustomMessageTextBlock.Visibility = Visibility.Collapsed;
            }

            // Set everything to not visible by default, it's up to the derived class to enable what they need.
            CloseAppsStackPanel.Visibility = Visibility.Collapsed;
            ProgressStackPanel.Visibility = Visibility.Collapsed;
            InputBoxStackPanel.Visibility = Visibility.Collapsed;
            CountdownDeferPanelSeparator.Visibility = Visibility.Collapsed;
            DeferRemainingStackPanel.Visibility = Visibility.Collapsed;
            DeferDeadlineStackPanel.Visibility = Visibility.Collapsed;
            ButtonPanel.Visibility = Visibility.Collapsed;
            ButtonLeft.Visibility = Visibility.Collapsed;
            ButtonMiddle.Visibility = Visibility.Collapsed;
            ButtonRight.Visibility = Visibility.Collapsed;

            // Set up everything related to the dialog icon.
            _dialogBitmapCache = new(new Dictionary<ApplicationTheme, BitmapSource>
            {
                { ApplicationTheme.Light, GetIcon(options.AppIconImage) },
                { ApplicationTheme.Dark, GetIcon(options.AppIconDarkImage) }
            });
            ThemeManager.AddActualThemeChangedHandler(this, (_, _) => SetDialogIcon());
            SetDialogIcon();

            // Set the expiry timer if specified.
            if (null != options.DialogExpiryDuration && options.DialogExpiryDuration.Value != TimeSpan.Zero)
            {
                _expiryTimer = new DispatcherTimer() { Interval = options.DialogExpiryDuration.Value };
                _expiryTimer.Tick += (sender, e) => CloseDialog();
            }

            // PersistPrompt timer code.
            if (null != options.DialogPersistInterval && options.DialogPersistInterval.Value != TimeSpan.Zero)
            {
                _persistTimer = new DispatcherTimer() { Interval = options.DialogPersistInterval.Value };
                _persistTimer.Tick += PersistTimer_Tick;
            }

            // Initialize countdown if specified
            if (null != _countdownDuration)
            {
                _countdownTimer = new Timer(CountdownTimer_Tick, null, Timeout.Infinite, Timeout.Infinite);
                CountdownStackPanel.Visibility = Visibility.Visible;    
                CountdownDeferPanelSeparator.Visibility = Visibility.Visible;
            }

            // Configure window events
            Loaded += FluentDialog_Loaded;
            SizeChanged += FluentDialog_SizeChanged;
        }

        /// <summary>
        /// Redefined ShowDialog method to allow for custom behavior.
        /// </summary>
        public new void ShowDialog()
        {
            base.ShowDialog();
        }

        /// <summary>
        /// Closes the dialog window and cancels associated operations. Can be called by timers or button clicks.
        /// </summary>
        public void CloseDialog()
        {
            // If we're already processing, just return.
            if (_disposed)
            {
                return;
            }
            _canClose = true;
            Dispatcher.Invoke(Close);
        }

        /// <summary>
        /// Raises the PropertyChanged event for the specified property.
        /// </summary>
        /// <param name="propertyName"></param>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Prevent window movement by handling WM_SYSCOMMAND
        /// </summary>
        /// <param name="hwnd"></param>
        /// <param name="msg"></param>
        /// <param name="wParam"></param>
        /// <param name="lParam"></param>
        /// <param name="handled"></param>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == PInvoke.WM_SYSCOMMAND)
            {
                int command = wParam.ToInt32() & 0xfff0;
                if (command == PInvoke.SC_MOVE && !_dialogAllowMove)
                {
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Handles the click event of the left button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void ButtonLeft_Click(object sender, RoutedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }
            CloseDialog();
        }

        /// <summary>
        /// Handles the click event of the middle button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void ButtonMiddle_Click(object sender, RoutedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }
            CloseDialog();
        }

        /// <summary>
        /// Handles the click event of the right button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void ButtonRight_Click(object sender, RoutedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }
            CloseDialog();
        }

        /// <summary>
        /// Prevents the user from closing the app via the taskbar
        /// </summary>
        /// <param name="e"></param>
        protected override void OnClosing(CancelEventArgs e)
        {
            // Prevent the window from closing unless explicitly allowed in code
            // This is to prevent the user from closing the dialog via taskbar
            e.Cancel = !_canClose;
        }

        /// <summary>
        /// Clean up resources when the window is closed
        /// </summary>
        /// <param name="e"></param>
        protected override void OnClosed(EventArgs e)
        {
            _persistTimer?.Stop();
            _expiryTimer?.Stop();
            base.OnClosed(e);
            Dispose();
        }

        /// <summary>
        /// Handles the loaded event of the window.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void FluentDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // Update dialog layout
            UpdateButtonLayout();
            UpdateLayout();

            // Initialize countdown display if needed
            InitializeCountdown();

            // Update row definitions based on current content
            UpdateRowDefinition();

            // Position the window
            PositionWindow();

            // Record the starting point for the window.
            _startingLeft = Left;
            _startingTop = Top;

            // Start the timers if specified
            _persistTimer?.Start();
            _expiryTimer?.Start();
        }

        /// <summary>
        /// Handles the size changed event of the window.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FluentDialog_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Only reposition window - no animations
            PositionWindow();

            // Add hook to prevent window movement
            WindowInteropHelper helper = new(this);
            HwndSource? source = HwndSource.FromHwnd(helper.Handle);
            if (source != null)
            {
                source.AddHook(new HwndSourceHook(WndProc));
            }
        }

        /// <summary>
        /// Handles the timer tick event for persisting the dialog.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PersistTimer_Tick(object? sender, EventArgs e)
        {
            // Reset the window and restore its location.
            RestoreWindow();
        }

        /// <summary>
        /// Handles the request navigate event of the hyperlink.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // Use ShellExecute to open the URL in the default browser/handler
            if (_disposed)
            {
                return;
            }
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        /// <summary>
        /// Formats the message text with enhanced markdown support including hyperlinks, bold, italic, and accent colored.
        /// Supports: [text](url), **bold**, *italic*, __bold__, _italic_, and colored quotes (> warning:, > info:, > error:, > success:).
        /// </summary>
        /// <param name="textBlock"></param>
        /// <param name="message"></param>
        protected void FormatMessageWithHyperlinks(TextBlock textBlock, string message)
        {
            // Throw if our process was started with ServiceUI anywhere as a parent process.
            if (ProcessUtilities.GetParentProcesses().Any(static p => p.ProcessName.Equals("ServiceUI", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Hyperlinks are only permitted when ServiceUI is not used to start the toolkit.");
            }

            // Don't waste time on an empty string.
            textBlock.Inlines.Clear();
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var lastPos = 0;
            foreach (Match match in DialogTools.TextFormattingRegex.Matches(message))
            {
                // Add text before the current match
                if (match.Index > lastPos)
                {
                    textBlock.Inlines.Add(new Run(message.Substring(lastPos, match.Index - lastPos)));
                }

                // Process the matched element
                ProcessMatch(textBlock, match);
                lastPos = match.Index + match.Length;
            }

            // Add any remaining text after the last match
            if (lastPos < message.Length)
            {
                textBlock.Inlines.Add(new Run(message.Substring(lastPos)));
            }
        }

        /// <summary>
        /// Processes a regex match and adds the corresponding formatted text to the TextBlock.
        /// </summary>
        /// <param name="textBlock">The TextBlock to add the formatted text to.</param>
        /// <param name="match">The regex match to process.</param>
        private void ProcessMatch(TextBlock textBlock, Match match)
        {
            if (match.Groups["UrlLink"].Success)
            {
                ProcessUrlLink(textBlock, match.Groups["UrlLinkContent"].Value);
            }
            else if (match.Groups["Accent"].Success)
            {
                AddAccentText(textBlock, match.Groups["AccentText"].Value);
            }
            else if (match.Groups["Bold"].Success)
            {
                AddBoldText(textBlock, match.Groups["BoldText"].Value);
            }
            else if (match.Groups["Italic"].Success)
            {
                AddItalicText(textBlock, match.Groups["ItalicText"].Value);
            }
        }

        /// <summary>
        /// Adds accent-colored text to the TextBlock.
        /// </summary>
        /// <param name="textBlock">The TextBlock to add the text to.</param>
        /// <param name="text">The text to add.</param>
        private static void AddAccentText(TextBlock textBlock, string text)
        {
            Run accentRun = new(text) { FontWeight = FontWeights.Bold };
            accentRun.SetResourceReference(ForegroundProperty, ThemeKeys.AccentTextFillColorPrimaryBrushKey);
            textBlock.Inlines.Add(accentRun);
        }

        /// <summary>
        /// Adds bold text to the TextBlock.
        /// </summary>
        /// <param name="textBlock">The TextBlock to add the text to.</param>
        /// <param name="text">The text to add.</param>
        private static void AddBoldText(TextBlock textBlock, string text)
        {
            textBlock.Inlines.Add(new Run(text) { FontWeight = FontWeights.Bold });
        }

        /// <summary>
        /// Adds italicized text to the TextBlock.
        /// </summary>
        /// <param name="textBlock">The TextBlock to add the text to.</param>
        /// <param name="text">The text to add.</param>
        private static void AddItalicText(TextBlock textBlock, string text)
        {
            textBlock.Inlines.Add(new Run(text) { FontStyle = FontStyles.Italic });
        }

        /// <summary>
        /// Creates a hyperlink with the specified URL.
        /// </summary>
        /// <param name="textBlock"></param>
        /// <param name="url"></param>
        private void ProcessUrlLink(TextBlock textBlock, string url)
        {
            // Ensure the URL has a scheme for Process.Start
            string navigateUrl = url;
            if (!navigateUrl.Contains("://") && !navigateUrl.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                if (navigateUrl.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
                    navigateUrl.StartsWith("ftp.", StringComparison.OrdinalIgnoreCase))
                {
                    navigateUrl = "http://" + navigateUrl;
                }
            }

            // Add the URL as a proper hyperlink
            if (Uri.TryCreate(navigateUrl, UriKind.Absolute, out var uri))
            {
                Hyperlink link = new(new Run(url))
                {
                    NavigateUri = uri,
                    ToolTip = $"Open link: {url}"
                };
                link.RequestNavigate += Hyperlink_RequestNavigate;
                textBlock.Inlines.Add(link);
            }
            else
            {
                // If it's not a valid URI, just add as plain text
                textBlock.Inlines.Add(new Run(url));
            }
        }

        /// <summary>
        /// Sets the button content with an accelerator key (underscore) for accessibility.
        /// </summary>
        /// <param name="button"></param>
        /// <param name="text"></param>
        protected void SetButtonContentWithAccelerator(Button button, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            // Create AccessText to properly handle the underscore as accelerator
            button.Content = new AccessText
            {
                Text = text
            };
        }

        /// <summary>
        /// Updates the Grid RowDefinition based on the current content.
        /// </summary>
        protected void UpdateRowDefinition()
        {
            // Always use Auto sizing for all dialog types
            CenterPanelRow.Height = new GridLength(1, GridUnitType.Auto);
        }

        /// <summary>
        /// Converts a 32-bit integer representation of a color into a <see cref="Color"/> object.
        /// </summary>
        /// <remarks>The integer is interpreted as an ARGB value, where the most significant byte represents the alpha channel, followed by the red, green, and blue channels in order.</remarks>
        /// <param name="color">A 32-bit integer where each byte represents a component of the color in ARGB order.</param>
        /// <returns>A <see cref="Color"/> object corresponding to the specified integer value.</returns>
        private static Color IntToColor(int color)
        {
            var colorBytes = BitConverter.GetBytes(color);
            return Color.FromArgb(colorBytes[3], colorBytes[2], colorBytes[1], colorBytes[0]);
        }

        /// <summary>
        /// Retrieves a bitmap representation of the icon specified by the given file path.
        /// </summary>
        /// <remarks>The method caches the retrieved icons to improve performance on subsequent calls with
        /// the same file path.  If the icon or image can be frozen, it is made shareable across threads.</remarks>
        /// <param name="dialogIconPath">The absolute file path to the icon. This can be a path to an .ico file or another image format.</param>
        /// <returns>A <see cref="BitmapSource"/> representing the icon. If the icon is an .ico file, the highest resolution
        /// frame is returned.</returns>
        private static BitmapSource GetIcon(string dialogIconPath)
        {
            // Try to get from cache first.
            if (!_dialogIconCache.TryGetValue(dialogIconPath, out var bitmapSource))
            {
                // Nothing cached. If we have an icon, get the highest resolution frame.
                if (Path.GetExtension(dialogIconPath).Equals(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    // Use IconBitmapDecoder to get the icon frame.
                    var iconFrame = new IconBitmapDecoder(new Uri(dialogIconPath, UriKind.Absolute), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First();

                    // Make it shareable across threads
                    if (iconFrame.CanFreeze)
                    {
                        iconFrame.Freeze();
                    }
                    _dialogIconCache.Add(dialogIconPath, iconFrame);
                    bitmapSource = iconFrame;
                }
                else
                {
                    // Use BeginInit/EndInit pattern for better performance.
                    BitmapImage bitmapImage = new();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.UriSource = new Uri(dialogIconPath, UriKind.Absolute);
                    bitmapImage.EndInit();

                    // Make it shareable across threads
                    if (bitmapImage.CanFreeze)
                    {
                        bitmapImage.Freeze();
                    }
                    _dialogIconCache.Add(dialogIconPath, bitmapImage);
                    bitmapSource = bitmapImage;
                }
            }
            return bitmapSource;
        }

        /// <summary>
        /// Sets the icon for the dialog using the specified bitmap source.
        /// </summary>
        /// <remarks>This method updates both the dialog's window icon and any associated UI element
        /// displaying the application icon.</remarks>
        private void SetDialogIcon()
        {
            Icon = AppIconImage.Source = _dialogBitmapCache[ThemeManager.Current.ActualApplicationTheme];
        }

        /// <summary>
        /// Positions the window on the screen based on the specified dialog position.
        /// </summary>
        private void PositionWindow()
        {
            // Get the working area in DIPs.
            Rect workingArea = SystemParameters.WorkArea;

            // Calculate positions based on window position setting.
            double left, top;
            switch (_dialogPosition)
            {
                case DialogPosition.TopLeft:
                    // Align to top left corner
                    left = workingArea.Left;
                    top = workingArea.Top;
                    break;

                case DialogPosition.Top:
                    // Center horizontally, align to top
                    left = workingArea.Left + ((workingArea.Width - ActualWidth) / 2);
                    top = workingArea.Top;
                    break;

                case DialogPosition.TopRight:
                    // Align to top right corner
                    left = workingArea.Left + (workingArea.Width - ActualWidth);
                    top = workingArea.Top;
                    break;

                case DialogPosition.TopCenter:
                    // Center horizontally, align to top but not to the top of the screen
                    left = workingArea.Left + ((workingArea.Width - ActualWidth) / 2);
                    top = workingArea.Top + ((workingArea.Height - ActualHeight) * (1.0 / 6.0));
                    break;

                case DialogPosition.Center:
                    // Center horizontally and vertically
                    left = workingArea.Left + ((workingArea.Width - ActualWidth) / 2);
                    top = workingArea.Top + ((workingArea.Height - ActualHeight) / 2);
                    break;

                case DialogPosition.BottomLeft:
                    // Align to bottom left corner
                    left = workingArea.Left;
                    top = workingArea.Top + (workingArea.Height - ActualHeight);
                    break;

                case DialogPosition.Bottom:
                    // Center horizontally, align to bottom
                    left = workingArea.Left + ((workingArea.Width - ActualWidth) / 2);
                    top = workingArea.Top + (workingArea.Height - ActualHeight);
                    break;

                case DialogPosition.BottomCenter:
                    // Center horizontally, align to bottom but not to the bottom of the screen
                    left = workingArea.Left + ((workingArea.Width - ActualWidth) / 2);
                    top = workingArea.Top + ((workingArea.Height - ActualHeight) * (5.0 / 6.0));
                    break;

                case DialogPosition.BottomRight:
                default:
                    // Align to bottom right (original behavior)
                    left = workingArea.Left + (workingArea.Width - ActualWidth);
                    top = workingArea.Top + (workingArea.Height - ActualHeight);
                    break;
            }

            // Ensure the window is within the screen bounds.
            left = Math.Max(workingArea.Left, Math.Min(left, workingArea.Right - ActualWidth));
            top = Math.Max(workingArea.Top, Math.Min(top, workingArea.Bottom - ActualHeight));

            // Align positions to whole pixels.
            left = Math.Floor(left);
            top = Math.Floor(top);

            // Adjust for workArea offset.
            string dialogPosName = _dialogPosition.ToString();
            left -= dialogPosName.EndsWith("Right") ? 18 : dialogPosName.EndsWith("Left") ? -18 : 0;
            top -= dialogPosName.StartsWith("Bottom") ? 14 : dialogPosName.StartsWith("Top") ? -14 : 0;

            // Set positions in DIPs.
            Left = _startingLeft = left;
            Top = _startingTop = top;
        }

        /// <summary>
        /// Restores the window to its normal state and repositions it to its original location.
        /// </summary>
        protected void RestoreWindow()
        {
            // Reset the window and restore its location.
            WindowState = WindowState.Normal;
            Left = _startingLeft;
            Top = _startingTop;
        }

        /// <summary>
        /// Updates the layout of the action buttons based on which buttons are visible.
        /// </summary>
        private void UpdateButtonLayout()
        {
            // Build a list of visible buttons in the order they appear.
            List<UIElement> visibleButtons = [];
            if (ButtonLeft.Visibility == Visibility.Visible)
            {
                visibleButtons.Add(ButtonLeft);
            }
            if (ButtonMiddle.Visibility == Visibility.Visible)
            {
                visibleButtons.Add(ButtonMiddle);
            }
            if (ButtonRight.Visibility == Visibility.Visible)
            {
                visibleButtons.Add(ButtonRight);
            }

            // Clear any existing column definitions.
            ActionButtons.ColumnDefinitions.Clear();

            // Return early if there's no buttons.
            if (visibleButtons.Count == 0)
            {
                return;
            }

            // Special case: if there's only one visible button, limit its width to half of the grid
            if (visibleButtons.Count > 1)
            {
                // Create equally sized columns for each visible button (original behavior)
                for (int i = 0; i < visibleButtons.Count; i++)
                {
                    // Set margin based on position
                    ActionButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Grid.SetColumn(visibleButtons[i], i);
                    Button button = (Button)visibleButtons[i];
                    if (i == 0)
                    {
                        button.Margin = new Thickness(0, 0, 4, 0);
                    }
                    else if (i == visibleButtons.Count - 1)
                    {
                        button.Margin = new Thickness(4, 0, 0, 0);
                    }
                    else
                    {
                        button.Margin = new Thickness(4, 0, 4, 0);
                    }
                }
            }
            else
            {
                // Add two columns - one for the button (50% width) and one empty (50% width)
                ActionButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                ActionButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Place the single button in the second column
                Grid.SetColumn(visibleButtons[0], 1);

                // Set appropriate margin
                Button button = (Button)visibleButtons[0];
                button.Margin = new Thickness(0, 0, 0, 0);

                // Set to Primary appearance for single button
                button.Style = (Style)FindResource(ThemeKeys.AccentButtonStyleKey);
            }
        }

        /// <summary>
        /// Initializes the countdown timer and display for dialogs that support it (CloseApps, Restart).
        /// </summary>
        private void InitializeCountdown()
        {
            // Return early if there's no countdown to set.
            if (null == _countdownTimer)
            {
                return;
            }
            if (!_countdownStopwatch.IsRunning)
            {
                _countdownStopwatch.Start();
            }
            _countdownTimer.Change(0, 1000);
        }

        /// <summary>
        /// Updates the countdown text display and adjusts text color based on remaining time.
        /// Handles disabling the dismiss button for Restart dialogs based on `countdownNoMinimizeDuration`.
        /// </summary>
        private void UpdateCountdownDisplay()
        {
            // Get the current remaining time.
            if (_countdownDuration is null)
            {
                return;
            }
            var countdownRemainingTime = _countdownDuration.Value - _countdownStopwatch.Elapsed;
            if (countdownRemainingTime < TimeSpan.Zero)
            {
                countdownRemainingTime = TimeSpan.Zero;
            }

            // Format the remaining time as hh:mm:ss
            CountdownValueTextBlock.Text = $"{countdownRemainingTime.Hours}h {countdownRemainingTime.Minutes}m {countdownRemainingTime.Seconds}s";
            AutomationProperties.SetName(CountdownValueTextBlock, $"Time remaining: {countdownRemainingTime.Hours} hours, {countdownRemainingTime.Minutes} minutes, {countdownRemainingTime.Seconds} seconds");

            // Update text color based on remaining time using style application
            if (countdownRemainingTime.TotalSeconds <= 60)
            {
                CountdownValueTextBlock.Style = (Style)FindResource("CriticalTextBlockStyle");
            }
            else if (_countdownWarningDuration.HasValue && _countdownStopwatch.Elapsed >= _countdownWarningDuration.Value)
            {
                CountdownValueTextBlock.Style = (Style)FindResource("CautionTextBlockStyle");
            }
        }

        /// <summary>
        /// Callback executed by the countdown timer every second. Decrements remaining time, updates display, and handles auto-action on timeout.
        /// </summary>
        /// <param name="state">Timer state object (not used).</param>
        protected virtual void CountdownTimer_Tick(object? state)
        {
            Dispatcher.Invoke(UpdateCountdownDisplay);
        }

        /// <summary>
        /// The result of the dialog interaction.
        /// </summary>
        public new virtual object DialogResult
        {
            get => _dialogResult;
            private protected set
            {
                _dialogResult = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// An optional custom message to display.
        /// </summary>
        protected readonly string? _customMessageText;

        /// <summary>
        /// The cancellation token source for the dialog.
        /// </summary>
        private object _dialogResult = "Timeout";

        /// <summary>
        /// Whether this window has been disposed.
        /// </summary>
        private bool _disposed = false;

        /// <summary>
        /// Whether this window is able to be closed.
        /// </summary>
        private bool _canClose = false;

        /// <summary>
        /// The specified position of the dialog.
        /// </summary>
        private readonly DialogPosition _dialogPosition = DialogPosition.BottomRight;

        /// <summary>
        /// Whether the dialog is allowed to be moved.
        /// </summary>
        private readonly bool _dialogAllowMove = false;

        /// <summary>
        /// The countdown duration for the dialog.
        /// </summary>
        private readonly Timer? _countdownTimer;

        /// <summary>
        /// An optional countdown to zero to commence a preferred action.
        /// </summary>
        protected readonly TimeSpan? _countdownDuration;

        /// <summary>
        /// An optional countdown to zero for when the dialog can be no longer minimized.
        /// </summary>
        protected readonly TimeSpan? _countdownWarningDuration;

        /// <summary>
        /// The end date/time for the countdown duration, as determined during form load.
        /// </summary>
        protected readonly Stopwatch _countdownStopwatch;

        /// <summary>
        /// A timer used to close the dialog at a configured interval after no user response.
        /// </summary>
        private readonly DispatcherTimer? _expiryTimer;

        /// <summary>
        /// A timer used to restore the dialog's position on the screen at a configured interval.
        /// </summary>
        private readonly DispatcherTimer? _persistTimer;

        /// <summary>
        /// Represents the initial top position of an element or object.
        /// </summary>
        private double _startingTop;

        /// <summary>
        /// Represents the initial left position of the window.
        /// </summary>
        private double _startingLeft;

        /// <summary>
        /// A read-only dictionary that caches dialog icons for different application themes.
        /// </summary>
        /// <remarks>This dictionary maps <see cref="ApplicationTheme"/> values to their corresponding
        /// <see cref="BitmapSource"/> icons. It is intended to optimize access to preloaded icons for dialogs, ensuring
        /// consistent and efficient retrieval.</remarks>
        private readonly ReadOnlyDictionary<ApplicationTheme, BitmapSource> _dialogBitmapCache; 

        /// <summary>
        /// Dialog icon cache for improved performance
        /// </summary>
        private static readonly Dictionary<string, BitmapSource> _dialogIconCache = [];

        /// <summary>
        /// Event handler for when a window property has changed.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Dispose managed resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose managed and unmanaged resources
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                _countdownTimer?.Dispose();
            }
            _disposed = true;
        }
    }
}
