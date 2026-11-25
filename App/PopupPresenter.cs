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
    public event Action? Closed;
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
            ((TextBlock)window.FindName("CountText")).Text = preview ? "Preview" : _count == 1 ? "1 new email" : $"{_count} new emails";
            ((TextBlock)window.FindName("TimeText")).Text = at.ToLocalTime().ToString("HH:mm");
        }
    }

    public void CloseAll()
    {
        _allowClose = true;
        foreach (var window in _windows.ToArray()) window.Close();
        _windows.Clear();
        _count = 0;
        _preview = false;
        Closed?.Invoke();
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
            <Setter Property="FontSize" Value="21"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="ButtonSurface" Background="{TemplateBinding Background}"
                                CornerRadius="16" BorderThickness="3" BorderBrush="Transparent">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="ButtonSurface" Property="Background" Value="#0068DB"/>
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="ButtonSurface" Property="Background" Value="#005CC2"/>
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
            <Border Margin="16" Padding="36" CornerRadius="28"
                    BorderBrush="#DCE5F1" BorderThickness="1">
                <Border.Background>
                    <LinearGradientBrush StartPoint="0,0" EndPoint="0.8,1">
                        <GradientStop Color="#F5F9FF" Offset="0"/>
                        <GradientStop Color="#FFFFFF" Offset="0.48"/>
                        <GradientStop Color="#FFFFFF" Offset="1"/>
                    </LinearGradientBrush>
                </Border.Background>
                <Border.Effect>
                    <DropShadowEffect Color="#172B4D" BlurRadius="22" ShadowDepth="4" Opacity="0.13"/>
                </Border.Effect>
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="64"/>
                        <RowDefinition Height="24"/>
                        <RowDefinition Height="1"/>
                        <RowDefinition Height="36"/>
                        <RowDefinition Height="60"/>
                        <RowDefinition Height="42"/>
                        <RowDefinition Height="24"/>
                        <RowDefinition Height="42"/>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="68"/>
                        <RowDefinition Height="24"/>
                        <RowDefinition Height="20"/>
                    </Grid.RowDefinitions>
                    <Grid Grid.Row="0" x:Name="DragHeader" Background="Transparent">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="56"/>
                            <ColumnDefinition Width="18"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="110"/>
                        </Grid.ColumnDefinitions>
                        <Border CornerRadius="16" Background="#007AFF" Height="56" VerticalAlignment="Center">
                            <Viewbox Width="32" Height="32">
                                <Canvas Width="40" Height="40">
                                    <Path Data="M 5,10 L 35,10 L 35,30 L 5,30 Z M 5,11 L 20,22 L 35,11"
                                          Stroke="White" StrokeThickness="2.3" StrokeLineJoin="Round"/>
                                </Canvas>
                            </Viewbox>
                        </Border>
                        <StackPanel Grid.Column="2" VerticalAlignment="Center">
                            <TextBlock Text="Outlook Alert" FontSize="22" FontWeight="SemiBold" Foreground="#182230"/>
                            <TextBlock Text="Email notification" FontSize="14" Foreground="#667085" Margin="0,3,0,0"/>
                        </StackPanel>
                        <Border Grid.Column="3" Background="#EDF3FB" CornerRadius="12"
                                Padding="14,9" HorizontalAlignment="Right" VerticalAlignment="Center">
                            <TextBlock x:Name="TimeText" Text="--:--" FontSize="19" FontWeight="Medium"
                                       Foreground="#526176"/>
                        </Border>
                    </Grid>
                    <Border Grid.Row="2" Background="#E3EAF3"/>
                    <TextBlock Grid.Row="4" Text="New email received" FontSize="42" FontWeight="SemiBold"
                               Foreground="#182230" VerticalAlignment="Center"/>
                    <TextBlock Grid.Row="5" Text="Please review your Outlook inbox."
                               FontSize="20" Foreground="#667085" VerticalAlignment="Center"/>
                    <Border Grid.Row="7" HorizontalAlignment="Left" Background="#EAF3FF"
                            BorderBrush="#DCEBFF" BorderThickness="1" CornerRadius="12" Padding="15,0">
                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                            <Ellipse Width="7" Height="7" Fill="#007AFF" Margin="0,0,9,0"/>
                            <TextBlock x:Name="CountText" Text="1 new email" Foreground="#075EBF"
                                       FontSize="15" FontWeight="SemiBold"/>
                        </StackPanel>
                    </Border>
                    <Button Grid.Row="9" x:Name="AcknowledgeButton" Style="{StaticResource AcknowledgeStyle}"
                            AutomationProperties.Name="Acknowledge and close alerts on all screens">
                        <StackPanel Orientation="Horizontal">
                            <Viewbox Width="21" Height="21" Margin="0,0,12,0" VerticalAlignment="Center">
                                <Path Data="M 3,12 L 9,18 L 21,5" Stroke="White" StrokeThickness="2.6"
                                      StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"/>
                            </Viewbox>
                            <TextBlock Text="Acknowledge" Foreground="White" FontSize="21" FontWeight="SemiBold"/>
                        </StackPanel>
                    </Button>
                    <Grid Grid.Row="11">
                        <TextBlock Text="Built for KYCDD" Foreground="#7B8798" FontSize="12"
                                   HorizontalAlignment="Left" VerticalAlignment="Center"/>
                        <TextBlock Text="facoding" Foreground="#7B8798" FontSize="12"
                                   HorizontalAlignment="Right" VerticalAlignment="Center"
                                   AutomationProperties.Name="Made by facoding"/>
                    </Grid>
                </Grid>
            </Border>
        </Border>
    </Viewbox>
</Window>
""";
}
