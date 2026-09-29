using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using DiskAnalyzer.Models;
using DiskAnalyzer.Services;

namespace DiskAnalyzer
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<FileSystemItem> _items =
            new ObservableCollection<FileSystemItem>();

        private readonly FileScanner _scanner = new FileScanner();
        private ICollectionView _view;
        private CancellationTokenSource _cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();

            PathTextBox.Text =
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            ItemsDataGrid.ItemsSource = _items;
            _view = CollectionViewSource.GetDefaultView(_items);
            _view.Filter = SearchFilter;

            ApplySorting();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку для анализа";
                dialog.SelectedPath = Directory.Exists(PathTextBox.Text)
                    ? PathTextBox.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    PathTextBox.Text = dialog.SelectedPath;
            }
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            string rootPath = PathTextBox.Text.Trim();

            if (!Directory.Exists(rootPath))
            {
                MessageBox.Show("Указанная папка не существует.", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetScanningState(true);
            _items.Clear();
            _cancellationTokenSource = new CancellationTokenSource();

            var progress = new Progress<ScanProgress>(p =>
            {
                ProgressTextBlock.Text =
                    $"Файлов: {p.ProcessedFiles}, папок: {p.ProcessedFolders}";
                StatusTextBlock.Text = $"Сканируется: {p.CurrentPath}";
            });

            try
            {
                ScanResult result = await Task.Run(
                    () => _scanner.Scan(rootPath, _cancellationTokenSource.Token, progress),
                    _cancellationTokenSource.Token);

                foreach (var item in result.Items)
                    _items.Add(item);

                FilesCountTextBlock.Text = $"Файлов: {result.FileCount}";
                FoldersCountTextBlock.Text = $"Папок: {result.FolderCount}";
                TotalSizeTextBlock.Text = $"Размер: {FormatSize(result.TotalSizeBytes)}";

                StatusTextBlock.Text = result.SkippedEntries > 0
                    ? $"Сканирование завершено. Недоступных объектов: {result.SkippedEntries}."
                    : "Сканирование завершено без ошибок доступа.";

                ApplySorting();
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Сканирование отменено.";
                ProgressTextBlock.Text = "Остановлено";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Произошла ошибка.";
                MessageBox.Show(ex.Message, "Ошибка сканирования",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                SetScanningState(false);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
        }

        private void SearchTextBox_TextChanged(
            object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            _view?.Refresh();
        }

        private void SortComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_view == null) return;
            ApplySorting();
        }

        private void ApplySorting()
        {
            if (_view == null) return;

            _view.SortDescriptions.Clear();

            switch (SortComboBox.SelectedIndex)
            {
                case 1:
                    _view.SortDescriptions.Add(
                        new SortDescription("SizeBytes", ListSortDirection.Ascending));
                    break;
                case 2:
                    _view.SortDescriptions.Add(
                        new SortDescription("Name", ListSortDirection.Ascending));
                    break;
                case 3:
                    _view.SortDescriptions.Add(
                        new SortDescription("Name", ListSortDirection.Descending));
                    break;
                default:
                    _view.SortDescriptions.Add(
                        new SortDescription("SizeBytes", ListSortDirection.Descending));
                    break;
            }
        }

        private void ItemsDataGrid_MouseDoubleClick(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            var selected = ItemsDataGrid.SelectedItem as FileSystemItem;
            if (selected == null) return;

            string targetPath = selected.IsDirectory
                ? selected.FullPath
                : Path.GetDirectoryName(selected.FullPath);

            if (!string.IsNullOrWhiteSpace(targetPath) &&
                Directory.Exists(targetPath))
            {
                Process.Start("explorer.exe", $""{targetPath}"");
            }
        }

        private bool SearchFilter(object item)
        {
            var data = item as FileSystemItem;
            if (data == null) return false;

            string query = SearchTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query)) return true;

            return data.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || data.FullPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetScanningState(bool isScanning)
        {
            ScanButton.IsEnabled = !isScanning;
            CancelButton.IsEnabled = isScanning;
            BrowseButton.IsEnabled = !isScanning;
            PathTextBox.IsEnabled = !isScanning;

            if (isScanning)
            {
                ProgressTextBlock.Text = "Сканирование...";
                StatusTextBlock.Text = "Начинаем анализ файловой системы...";
            }
        }

        private static string FormatSize(long bytes)
        {
            const double KB = 1024;
            const double MB = KB * 1024;
            const double GB = MB * 1024;

            if (bytes >= GB) return $"{bytes / GB:F2} ГБ";
            if (bytes >= MB) return $"{bytes / MB:F2} МБ";
            if (bytes >= KB) return $"{bytes / KB:F2} КБ";
            return $"{bytes} Б";
        }
    }
}
