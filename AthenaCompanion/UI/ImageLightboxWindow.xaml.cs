using System.Diagnostics;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AthenaCompanion.UI;

public partial class ImageLightboxWindow : Window
{
    private readonly string _imagePath;

    public ImageLightboxWindow(string imagePath)
    {
        _imagePath = imagePath;
        InitializeComponent();
        LoadImage();
    }

    private void LoadImage()
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(_imagePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        GeneratedImage.Source = bitmap;
        PathText.Text = _imagePath;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_imagePath))
        {
            return;
        }

        var data = new System.Windows.DataObject();
        var files = new StringCollection { _imagePath };
        data.SetFileDropList(files);
        System.Windows.Clipboard.SetDataObject(data, true);
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_imagePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{_imagePath}\"",
            UseShellExecute = true
        });
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
