using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using Forms = System.Windows.Forms;

namespace Faraz.OutlookAlert;

internal sealed class PopupPresenter
{
    private readonly List<Window> _windows = new();
    private int _count;
    private bool _allowClose, _preview;

    public void Show(int added, DateTimeOffset at, bool preview)
    {
        if (preview || (_preview && !preview)) CloseAll();
        _preview = preview;
        _count += added;
        if (_windows.Count == 0)
        {
            _allowClose = false;
            foreach (var screen in Forms.Screen.AllScreens)
            {
                var window = (Window)XamlReader.Parse(AlertXaml);
                ((Button)window.FindName("AcknowledgeButton")).Click += (_, _) => CloseAll();
                ((Grid)window.FindName("DragHeader")).MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount == 1) { try { window.DragMove(); } catch (InvalidOperationException) { } }
                };
                window.Closing += (_, e) => { if (!_allowClose) e.Cancel = true; };
                _windows.Add(window);
                window.Show();
                var area = screen.WorkingArea;
                double scale = Math.Min(1, Math.Min(area.Width * 0.94 / 960, area.Height * 0.94 / 640));
                int width = (int)Math.Floor(960 * scale), height = (int)Math.Floor(640 * scale);
                int x = area.Left + (area.Width - width) / 2, y = area.Top + (area.Height - height) / 2;
                var handle = new WindowInteropHelper(window).Handle;
                if (!SetWindowPos(handle, new IntPtr(-1), x, y, width, height, 0x0010))
                {
                    CloseAll();
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }
        foreach (var window in _windows)
        {
            ((TextBlock)window.FindName("CountText")).Text = preview ? "Appearance test" : _count == 1 ? "1 new email" : $"{_count} new emails";
            ((TextBlock)window.FindName("TimeText")).Text = at.ToLocalTime().ToString("HH:mm");
            ((TextBlock)window.FindName("FooterText")).Text = preview
                ? "This is a preview. Send a real email to test detection."
                : "This alert stays open until you acknowledge it.";
        }
    }

    public void CloseAll()
    {
        _allowClose = true;
        foreach (var window in _windows.ToArray()) window.Close();
        _windows.Clear();
        _count = 0;
        _preview = false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    private const string AlertXaml = """
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Outlook Alert" Width="960" Height="640"
        WindowStartupLocation="Manual" WindowStyle="None" ResizeMode="NoResize"
        AllowsTransparency="True" Background="Transparent" Topmost="True"
        ShowActivated="False" ShowInTaskbar="False"
        FontFamily="Segoe UI" UseLayoutRounding="True" SnapsToDevicePixels="True">
    <Window.Resources>
        <Style x:Key="AcknowledgeStyle" TargetType="Button">
            <Setter Property="Background" Value="#007AFF"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FontSize" Value="22"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="ButtonSurface" Background="{TemplateBinding Background}"
                                CornerRadius="18" BorderThickness="3" BorderBrush="Transparent">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="ButtonSurface" Property="Background" Value="#0066D6"/>
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="ButtonSurface" Property="Background" Value="#005BBE"/>
                            </Trigger>
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter TargetName="ButtonSurface" Property="BorderBrush" Value="#003D80"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    <Viewbox Stretch="Uniform" StretchDirection="Both">
        <Border Width="960" Height="640" Background="Transparent">
            <Border Margin="16" Padding="40" CornerRadius="32"
                    Background="#FFFFFF" BorderBrush="#E5E5EA" BorderThickness="1">
                <Border.Effect>
                    <DropShadowEffect Color="#101828" BlurRadius="24" ShadowDepth="4" Opacity="0.16"/>
                </Border.Effect>
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="64"/>
                        <RowDefinition Height="28"/>
                        <RowDefinition Height="112"/>
                        <RowDefinition Height="32"/>
                        <RowDefinition Height="20"/>
                        <RowDefinition Height="36"/>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="72"/>
                        <RowDefinition Height="16"/>
                        <RowDefinition Height="24"/>
                    </Grid.RowDefinitions>
                    <Grid Grid.Row="0" x:Name="DragHeader" Background="Transparent">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="64"/>
                            <ColumnDefinition Width="18"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="170"/>
                        </Grid.ColumnDefinitions>
                        <Border CornerRadius="18" Background="#007AFF">
                            <Viewbox Width="38" Height="38">
                                <Canvas Width="40" Height="40">
                                    <Path Data="M 5,10 L 35,10 L 35,30 L 5,30 Z M 5,11 L 20,22 L 35,11"
                                          Stroke="White" StrokeThickness="2.4" StrokeLineJoin="Round"/>
                                </Canvas>
                            </Viewbox>
                        </Border>
                        <StackPanel Grid.Column="2" VerticalAlignment="Center">
                            <TextBlock Text="Outlook" FontSize="24" FontWeight="SemiBold" Foreground="#1C1C1E"/>
                            <TextBlock Text="Mail notifications" FontSize="15" Margin="0,3,0,0" Foreground="#636366"/>
                        </StackPanel>
                        <StackPanel Grid.Column="3" VerticalAlignment="Center" HorizontalAlignment="Right">
                            <TextBlock Text="LAST ALERT" FontSize="12" FontWeight="SemiBold" Foreground="#636366"
                                       HorizontalAlignment="Right"/>
                            <TextBlock x:Name="TimeText" Text="--:--" FontSize="24" FontWeight="SemiBold"
                                       Foreground="#1C1C1E" HorizontalAlignment="Right" Margin="0,4,0,0"/>
                        </StackPanel>
                    </Grid>
                    <TextBlock Grid.Row="2" FontSize="46" FontWeight="SemiBold" LineHeight="54"
                               Foreground="#1C1C1E" TextWrapping="Wrap">
                        <Run Text="You have a new"/><LineBreak/><Run Text="email."/>
                    </TextBlock>
                    <TextBlock Grid.Row="3" Text="Please check your Outlook inbox."
                               FontSize="21" Foreground="#636366" VerticalAlignment="Center"/>
                    <Border Grid.Row="5" HorizontalAlignment="Left" Background="#EAF3FF"
                            CornerRadius="18" Padding="16,0">
                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                            <Ellipse Width="8" Height="8" Fill="#007AFF" Margin="0,0,9,0"/>
                            <TextBlock x:Name="CountText" Text="1 new email" Foreground="#005CBF"
                                       FontSize="16" FontWeight="SemiBold"/>
                        </StackPanel>
                    </Border>
                    <Button Grid.Row="7" x:Name="AcknowledgeButton" Style="{StaticResource AcknowledgeStyle}"
                            AutomationProperties.Name="Got it, close alerts on all screens">
                        <StackPanel Orientation="Horizontal">
                            <Viewbox Width="22" Height="22" Margin="0,0,12,0" VerticalAlignment="Center">
                                <Path Data="M 3,12 L 9,18 L 21,5" Stroke="White" StrokeThickness="2.6"
                                      StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"/>
                            </Viewbox>
                            <TextBlock Text="Got it, close on all screens" Foreground="White"
                                       FontSize="22" FontWeight="SemiBold"/>
                        </StackPanel>
                    </Button>
                    <TextBlock Grid.Row="9" x:Name="FooterText" Text="This alert stays open until you acknowledge it."
                               Foreground="#636366" FontSize="15" HorizontalAlignment="Center"/>
                    <TextBlock Grid.Row="9" Text="facoding" Foreground="#636366" FontSize="13"
                               FontWeight="SemiBold" HorizontalAlignment="Right" VerticalAlignment="Center"
                               Margin="16,0,0,0" AutomationProperties.Name="Made by facoding"/>
                </Grid>
            </Border>
        </Border>
    </Viewbox>
</Window>
""";
}
