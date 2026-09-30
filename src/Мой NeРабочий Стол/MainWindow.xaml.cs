using DiskAnalyzer67.services;
using DiskAnalyzer67.models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DiskAnalyzer67
{
    public partial class MainWindow : Window
    {
        private const int PageSize = 1000;

        private readonly List<FileSystemItem> allItems =
            new List<FileSystemItem>();

        private readonly ObservableCollection<FileSystemItem> visibleItems =
            new ObservableCollection<FileSystemItem>();

        private readonly FileScanner scanner =
            new FileScanner();

        private CancellationTokenSource cancellationTokenSource;

        private string lastScanRoot;

        private int currentPage = 0;


        public MainWindow()
        {
            InitializeComponent();


            PathTextBox.Text =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments);


            ItemsDataGrid.ItemsSource =
                visibleItems;


            ShowEmptyChart();

            ShowNoInaccessible();
        }


        // =========================================================
        // ВЫБОР ПАПКИ
        // =========================================================

        private void BrowseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            using (
                var dialog =
                    new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description =
                    "Выберите папку для анализа";


                if (Directory.Exists(PathTextBox.Text))
                {
                    dialog.SelectedPath =
                        PathTextBox.Text;
                }


                if (dialog.ShowDialog()
                    == System.Windows.Forms.DialogResult.OK)
                {
                    PathTextBox.Text =
                        dialog.SelectedPath;
                }
            }
        }


        // =========================================================
        // СКАНИРОВАНИЕ
        // =========================================================

        private async void ScanButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string rootPath =
                PathTextBox.Text.Trim();


            if (!Directory.Exists(rootPath))
            {
                MessageBox.Show(
                    "Указанная папка не существует.",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            bool recursive =
                RecursiveCheckBox.IsChecked == true;


            SetScanningState(true);


            allItems.Clear();
            visibleItems.Clear();

            currentPage = 0;


            cancellationTokenSource =
                new CancellationTokenSource();


            var progress =
                new Progress<ScanProgress>(
                    p =>
                    {
                        ProgressTextBlock.Text =
                            string.Format(
                                "Файлов: {0}, папок: {1}",
                                p.ProcessedFiles,
                                p.ProcessedFolders);


                        StatusTextBlock.Text =
                            "Сканируется: "
                            + p.CurrentPath;
                    });


            try
            {
                ScanResult result =
                    await Task.Run(
                        () =>
                            scanner.Scan(
                                rootPath,
                                recursive,
                                cancellationTokenSource.Token,
                                progress),

                        cancellationTokenSource.Token);


                allItems.AddRange(
                    result.Items);


                lastScanRoot =
                    rootPath;


                FilesCountTextBlock.Text =
                    "Файлов: "
                    + result.FileCount;


                FoldersCountTextBlock.Text =
                    "Папок: "
                    + result.FolderCount;


                TotalSizeTextBlock.Text =
                    "Размер: "
                    + FormatSize(
                        result.TotalSizeBytes);


                UpdateInaccessibleList(
                    result);


                RefreshView();


                UpdateUsageChart();


                if (result.SkippedEntries > 0)
                {
                    if (recursive)
                    {
                        StatusTextBlock.Text =
                            string.Format(
                                "Сканирование завершено. " +
                                "Недоступных объектов: {0}.",
                                result.SkippedEntries);
                    }
                    else
                    {
                        StatusTextBlock.Text =
                            string.Format(
                                "Готово. Показаны только объекты " +
                                "выбранной папки, размеры подпапок рассчитаны. " +
                                "Недоступных объектов: {0}.",
                                result.SkippedEntries);
                    }
                }
                else
                {
                    if (recursive)
                    {
                        StatusTextBlock.Text =
                            "Сканирование завершено без ошибок доступа.";
                    }
                    else
                    {
                        StatusTextBlock.Text =
                            "Готово. Показаны только объекты выбранной папки, " +
                            "размеры подпапок рассчитаны.";
                    }
                }

                ProgressTextBlock.Text =
                    "Готово";
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text =
                    "Сканирование отменено.";

                ProgressTextBlock.Text =
                    "Остановлено";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text =
                    "Произошла ошибка.";


                MessageBox.Show(
                    ex.Message,
                    "Ошибка сканирования",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                cancellationTokenSource?.Dispose();

                cancellationTokenSource =
                    null;


                SetScanningState(false);
            }
        }


        // =========================================================
        // ОТМЕНА
        // =========================================================

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            cancellationTokenSource?.Cancel();
        }


        // =========================================================
        // ПОИСК
        // =========================================================

        private void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (allItems.Count == 0)
                return;


            currentPage = 0;

            RefreshView();
        }


        // =========================================================
        // ФИЛЬТР ТИПА
        // =========================================================

        private void TypeFilterComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (allItems.Count == 0)
                return;


            currentPage = 0;

            RefreshView();
        }


        // =========================================================
        // СОРТИРОВКА
        // =========================================================

        private void SortComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (allItems.Count == 0)
                return;


            currentPage = 0;

            RefreshView();
        }
        private void SortDirectionComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (allItems.Count == 0)
                return;

            currentPage = 0;

            RefreshView();
        }

        // =========================================================
        // ПЕРЕСТРОЕНИЕ ОТОБРАЖЕНИЯ
        // =========================================================

        private void RefreshView()
        {
            IEnumerable<FileSystemItem> query =
                allItems;

            // =========================================================
            // ФИЛЬТР ТИПА
            // =========================================================

            switch (TypeFilterComboBox.SelectedIndex)
            {
                case 1:
                    query =
                        query.Where(
                            x => !x.IsDirectory);
                    break;

                case 2:
                    query =
                        query.Where(
                            x => x.IsDirectory);
                    break;
            }

            // =========================================================
            // ПОИСК
            // =========================================================

            string search =
                SearchTextBox.Text.Trim();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query =
                    query.Where(
                        x =>
                            x.Name.IndexOf(
                                search,
                                StringComparison.OrdinalIgnoreCase)
                            >= 0

                            ||

                            x.FullPath.IndexOf(
                                search,
                                StringComparison.OrdinalIgnoreCase)
                            >= 0);
            }

            // =========================================================
            // СОРТИРОВКА
            // =========================================================

            int sortField =
                SortComboBox.SelectedIndex;

            bool descending =
                SortDirectionComboBox.SelectedIndex == 1;

            switch (sortField)
            {
                // -----------------------------------------------------
                // ИМЯ
                // -----------------------------------------------------

                case 0:

                    if (descending)
                    {
                        query =
                            query.OrderByDescending(
                                x => x.Name,
                                StringComparer.OrdinalIgnoreCase)
                                 .ThenBy(
                                     x => x.FullPath,
                                     StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        query =
                            query.OrderBy(
                                x => x.Name,
                                StringComparer.OrdinalIgnoreCase)
                                 .ThenBy(
                                     x => x.FullPath,
                                     StringComparer.OrdinalIgnoreCase);
                    }

                    break;

                // -----------------------------------------------------
                // РАЗМЕР
                // -----------------------------------------------------

                case 1:

                    if (descending)
                    {
                        query =
                            query.OrderByDescending(
                                x => x.SizeCalculated
                                    ? x.SizeBytes
                                    : long.MinValue)
                                 .ThenBy(
                                     x => x.Name,
                                     StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        query =
                            query.OrderBy(
                                x => x.SizeCalculated
                                    ? x.SizeBytes
                                    : long.MaxValue)
                                 .ThenBy(
                                     x => x.Name,
                                     StringComparer.OrdinalIgnoreCase);
                    }

                    break;

                // -----------------------------------------------------
                // ПУТЬ
                // -----------------------------------------------------

                case 2:

                    if (descending)
                    {
                        query =
                            query.OrderByDescending(
                                x => x.FullPath,
                                StringComparer.OrdinalIgnoreCase)
                                 .ThenBy(
                                     x => x.Name,
                                     StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        query =
                            query.OrderBy(
                                x => x.FullPath,
                                StringComparer.OrdinalIgnoreCase)
                                 .ThenBy(
                                     x => x.Name,
                                     StringComparer.OrdinalIgnoreCase);
                    }

                    break;

                // -----------------------------------------------------
                // ТИП
                // -----------------------------------------------------

                case 3:

                    if (descending)
                    {
                        // Папки -> файлы
                        query =
                            query.OrderBy(
                                x => x.IsDirectory ? 0 : 1)
                                 .ThenByDescending(
                                     x => x.SizeCalculated
                                         ? x.SizeBytes
                                         : long.MinValue)
                                 .ThenBy(
                                     x => x.Name,
                                     StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        // Файлы -> папки
                        query =
                            query.OrderBy(
                                x => x.IsDirectory ? 1 : 0)
                                 .ThenByDescending(
                                     x => x.SizeCalculated
                                         ? x.SizeBytes
                                         : long.MinValue)
                                 .ThenBy(
                                     x => x.Name,
                                     StringComparer.OrdinalIgnoreCase);
                    }

                    break;

                default:

                    query =
                        query.OrderByDescending(
                            x => x.SizeCalculated
                                ? x.SizeBytes
                                : long.MinValue);

                    break;
            }

            // =========================================================
            // ПАГИНАЦИЯ
            // =========================================================

            List<FileSystemItem> preparedItems =
                query.ToList();

            int totalCount =
                preparedItems.Count;

            int totalPages =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        totalCount /
                        (double)PageSize));

            if (currentPage >= totalPages)
            {
                currentPage =
                    totalPages - 1;
            }

            List<FileSystemItem> pageItems =
                preparedItems
                    .Skip(
                        currentPage *
                        PageSize)
                    .Take(PageSize)
                    .ToList();

            visibleItems.Clear();

            foreach (FileSystemItem item in pageItems)
            {
                visibleItems.Add(item);
            }

            PageTextBlock.Text =
                string.Format(
                    "Страница {0} из {1}",
                    currentPage + 1,
                    totalPages);

            PreviousPageButton.IsEnabled =
                currentPage > 0;

            NextPageButton.IsEnabled =
                currentPage < totalPages - 1;

            StatusTextBlock.Text =
                string.Format(
                    "Показано {0} из {1} объектов.",
                    pageItems.Count,
                    totalCount);
        }


        // =========================================================
        // СТРАНИЦА НАЗАД
        // =========================================================

        private void PreviousPageButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (currentPage <= 0)
                return;


            currentPage--;

            RefreshView();
        }


        // =========================================================
        // СТРАНИЦА ВПЕРЁД
        // =========================================================

        private void NextPageButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            currentPage++;

            RefreshView();
        }


        // =========================================================
        // ОТКРЫТИЕ В ПРОВОДНИКЕ
        // =========================================================

        private void ItemsDataGrid_MouseDoubleClick(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            FileSystemItem selected =
                ItemsDataGrid.SelectedItem as FileSystemItem;

            if (selected == null)
                return;

            try
            {
                if (selected.IsDirectory)
                {
                    // Для папки просто открываем саму папку.
                    if (Directory.Exists(selected.FullPath))
                    {
                        Process.Start(
                            new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = "\"" + selected.FullPath + "\"",
                                UseShellExecute = true
                            });
                    }
                }
                else
                {
                    // Для файла открываем его расположение
                    // и выделяем сам файл.
                    if (File.Exists(selected.FullPath))
                    {
                        Process.Start(
                            new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments =
                                    "/select,\""
                                    + selected.FullPath
                                    + "\"",
                                UseShellExecute = true
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось открыть расположение объекта:\n\n"
                    + ex.Message,
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // =========================================================
        // СПИСОК НЕДОСТУПНЫХ
        // =========================================================

        private void UpdateInaccessibleList(
            ScanResult result)
        {
            InaccessibleListBox.Items.Clear();


            if (result.SkippedEntries == 0)
            {
                InaccessibleListBox.Items.Add(
                    "Недоступных объектов не обнаружено.");

                return;
            }


            foreach (string path
                in result.InaccessiblePaths)
            {
                InaccessibleListBox.Items.Add(path);
            }


            if (result.SkippedEntries
                > result.InaccessiblePaths.Count)
            {
                InaccessibleListBox.Items.Add(
                    string.Format(
                        "Показаны первые {0} записей "
                        + "из {1}.",
                        result.InaccessiblePaths.Count,
                        result.SkippedEntries));
            }
        }


        private void ShowNoInaccessible()
        {
            InaccessibleListBox.Items.Clear();

            InaccessibleListBox.Items.Add(
                "Сканирование ещё не выполнялось.");
        }


        // =========================================================
        // ДИАГРАММА ЗАПОЛНЕНИЯ
        // =========================================================

        private void UpdateUsageChart()
        {
            UsageChartPanel.Children.Clear();

            if (string.IsNullOrWhiteSpace(lastScanRoot))
            {
                ShowEmptyChart();
                return;
            }

            // Берём только непосредственные объекты
            // выбранной пользователем папки.
            //
            // В рекурсивном режиме allItems содержит всё дерево,
            // поэтому IsDirectChild отбрасывает вложенные уровни.
            //
            // В нерекурсивном режиме allItems уже содержит только
            // непосредственные объекты.

            List<FileSystemItem> directChildren =
                allItems
                    .Where(
                        x =>
                            x.SizeCalculated
                            && IsDirectChild(
                                lastScanRoot,
                                x.FullPath))
                    .OrderByDescending(
                        x => x.SizeBytes)
                    .ToList();

            double totalSize =
                directChildren.Sum(
                    x => (double)x.SizeBytes);

            if (totalSize <= 0)
            {
                ShowEmptyChart();

                return;
            }

            UsageChartPanel.Children.Add(
                new TextBlock
                {
                    Text =
                        "Крупнейшие объекты " +
                        "в выбранном каталоге",

                    FontSize = 20,

                    FontWeight =
                        FontWeights.SemiBold,

                    Margin =
                        new Thickness(
                            0, 0, 0, 16)
                });

            UsageChartPanel.Children.Add(
                new TextBlock
                {
                    Text =
                        "Размеры папок рассчитаны с учётом " +
                        "их содержимого. В таблице при этом " +
                        "могут отображаться только объекты " +
                        "непосредственно выбранной папки.",

                    TextWrapping =
                        TextWrapping.Wrap,

                    Foreground =
                        new SolidColorBrush(
                            Colors.Gray),

                    Margin =
                        new Thickness(
                            0, 0, 0, 16)
                });

            List<FileSystemItem> topItems =
                directChildren
                    .Take(8)
                    .ToList();

            foreach (FileSystemItem item
                in topItems)
            {
                AddUsageBar(
                    item.Name,
                    item.Type,
                    item.SizeBytes,
                    totalSize);
            }

            double otherSize =
                directChildren
                    .Skip(8)
                    .Sum(
                        x => (double)x.SizeBytes);

            if (otherSize > 0)
            {
                AddUsageBar(
                    "Остальное",
                    "Остальные объекты",
                    (long)otherSize,
                    totalSize);
            }
        }

        private void AddUsageBar(
            string name,
            string type,
            long size,
            double totalSize)
        {
            double percentage =
                size /
                totalSize *
                100.0;


            Grid row =
                new Grid
                {
                    Margin =
                        new Thickness(
                            0, 0, 0, 12)
                };


            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            260)
                });


            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(1,
                            GridUnitType.Star)
                });


            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            180)
                });


            TextBlock title =
                new TextBlock
                {
                    Text =
                        name
                        + " ("
                        + type
                        + ")",

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    TextTrimming =
                        TextTrimming.CharacterEllipsis
                };


            ProgressBar bar =
                new ProgressBar
                {
                    Minimum = 0,

                    Maximum = 100,

                    Value =
                        percentage,

                    Height = 20,

                    Margin =
                        new Thickness(
                            8, 0, 8, 0)
                };


            TextBlock value =
                new TextBlock
                {
                    Text =
                        string.Format(
                            "{0:F1}% — {1}",
                            percentage,
                            FormatSize(size)),

                    VerticalAlignment =
                        VerticalAlignment.Center
                };


            Grid.SetColumn(
                title,
                0);


            Grid.SetColumn(
                bar,
                1);


            Grid.SetColumn(
                value,
                2);


            row.Children.Add(title);
            row.Children.Add(bar);
            row.Children.Add(value);


            UsageChartPanel.Children.Add(row);
        }


        private void ShowEmptyChart()
        {
            UsageChartPanel.Children.Clear();


            UsageChartPanel.Children.Add(
                new TextBlock
                {
                    Text =
                        "Здесь будет отображаться "
                        + "диаграмма заполнения "
                        + "выбранного каталога.",

                    FontSize =
                        18,

                    Foreground =
                        new SolidColorBrush(
                            Colors.Gray),

                    TextWrapping =
                        TextWrapping.Wrap
                });
        }


        // =========================================================
        // ОПРЕДЕЛЕНИЕ НЕПОСРЕДСТВЕННОГО ДЕТСКОГО ОБЪЕКТА
        // =========================================================

        private static bool IsDirectChild(
            string rootPath,
            string itemPath)
        {
            try
            {
                string normalizedRoot =
                    Path.GetFullPath(
                        rootPath)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);


                string normalizedItem =
                    Path.GetFullPath(
                        itemPath);


                string parentPath;


                if (Directory.Exists(
                    normalizedItem))
                {
                    DirectoryInfo parent =
                        Directory.GetParent(
                            normalizedItem);

                    if (parent == null)
                        return false;

                    parentPath =
                        parent.FullName;
                }
                else
                {
                    parentPath =
                        Path.GetDirectoryName(
                            normalizedItem);
                }


                if (string.IsNullOrWhiteSpace(
                    parentPath))
                {
                    return false;
                }


                parentPath =
                    parentPath.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);


                return string.Equals(
                    normalizedRoot,
                    parentPath,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }


        // =========================================================
        // СОСТОЯНИЕ UI
        // =========================================================

        private void SetScanningState(
            bool isScanning)
        {
            ScanButton.IsEnabled =
                !isScanning;

            CancelButton.IsEnabled =
                isScanning;

            BrowseButton.IsEnabled =
                !isScanning;

            PathTextBox.IsEnabled =
                !isScanning;

            RecursiveCheckBox.IsEnabled =
                !isScanning;

            SearchTextBox.IsEnabled =
                !isScanning;

            TypeFilterComboBox.IsEnabled =
                !isScanning;

            SortComboBox.IsEnabled =
                !isScanning;

            SortDirectionComboBox.IsEnabled =
                !isScanning;

            PreviousPageButton.IsEnabled =
                false;

            NextPageButton.IsEnabled =
                false;


            if (!isScanning)
            {
                RefreshView();
            }
        }


        // =========================================================
        // РАЗМЕР
        // =========================================================

        private static string FormatSize(
            long bytes)
        {
            const double KB = 1024.0;

            const double MB =
                KB * 1024.0;

            const double GB =
                MB * 1024.0;


            if (bytes >= GB)
            {
                return string.Format(
                    "{0:F2} ГБ",
                    bytes / GB);
            }


            if (bytes >= MB)
            {
                return string.Format(
                    "{0:F2} МБ",
                    bytes / MB);
            }


            if (bytes >= KB)
            {
                return string.Format(
                    "{0:F2} КБ",
                    bytes / KB);
            }


            return bytes + " Б";
        }

        private void BrowseButton_Click_1(object sender, RoutedEventArgs e)
        {

        }
    }
}