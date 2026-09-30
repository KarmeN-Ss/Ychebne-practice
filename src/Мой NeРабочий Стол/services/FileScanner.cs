using DiskAnalyzer67.models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DiskAnalyzer67.services
{
    public class ScanProgress
    {
        public int ProcessedFiles { get; set; }
        public int ProcessedFolders { get; set; }
        public long TotalSizeBytes { get; set; }
        public string CurrentPath { get; set; }
    }

    public class ScanResult
    {
        public List<FileSystemItem> Items { get; private set; }

        public List<string> InaccessiblePaths { get; private set; }

        public int SkippedEntries { get; set; }

        public long TotalSizeBytes { get; set; }

        public int FileCount { get; set; }

        public int FolderCount { get; set; }

        public ScanResult()
        {
            Items = new List<FileSystemItem>();
            InaccessiblePaths = new List<string>();
        }
    }

    public class FileScanner
    {
        private const int MaxInaccessibleDetails = 5000;

        private int processedFiles;
        private int processedFolders;
        private long totalSize;
        private int skippedEntries;

        private Stopwatch progressTimer;

        public ScanResult Scan(
            string rootPath,
            bool recursive,
            CancellationToken cancellationToken,
            IProgress<ScanProgress> progress)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException(
                    "Не указана папка для анализа.");
            }

            if (!Directory.Exists(rootPath))
            {
                throw new DirectoryNotFoundException(
                    "Указанная папка не существует.");
            }

            processedFiles = 0;
            processedFolders = 0;
            totalSize = 0;
            skippedEntries = 0;

            progressTimer = Stopwatch.StartNew();

            ScanResult result = new ScanResult();

            bool rootSizeComplete;

            ScanDirectory(
                rootPath,
                result,
                recursive,
                cancellationToken,
                progress,
                recursive,
                out rootSizeComplete);

            result.SkippedEntries = skippedEntries;
            result.TotalSizeBytes = totalSize;

            // В статистике показываем именно объекты,
            // которые попали в таблицу.
            int visibleFiles = 0;
            int visibleFolders = 0;

            foreach (FileSystemItem item in result.Items)
            {
                if (item.IsDirectory)
                    visibleFolders++;
                else
                    visibleFiles++;
            }

            result.FileCount = visibleFiles;
            result.FolderCount = visibleFolders;

            progress?.Report(
                    new ScanProgress
                    {
                        ProcessedFiles = processedFiles,
                        ProcessedFolders = processedFolders,
                        TotalSizeBytes = totalSize,
                        CurrentPath = rootPath
                    });

            return result;
        }

        private long ScanDirectory(
            string directoryPath,
            ScanResult result,
            bool recursive,
            CancellationToken cancellationToken,
            IProgress<ScanProgress> progress,
            bool addDirectoryItem,
            out bool sizeComplete)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long directorySize = 0;
            sizeComplete = true;

            processedFolders++;

            ReportProgress(progress, directoryPath);

            // =====================================================
            // ФАЙЛЫ ТЕКУЩЕЙ ПАПКИ
            // =====================================================

            try
            {
                foreach (string filePath
                    in Directory.EnumerateFiles(directoryPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        FileInfo info = new FileInfo(filePath);

                        long fileSize = info.Length;

                        result.Items.Add(
                            new FileSystemItem
                            {
                                Name = info.Name,
                                FullPath = info.FullName,
                                IsDirectory = false,
                                SizeBytes = fileSize,
                                SizeCalculated = true,
                                SizeComplete = true
                            });

                        directorySize += fileSize;
                        totalSize += fileSize;

                        processedFiles++;

                        ReportProgress(progress, filePath);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        sizeComplete = false;

                        AddInaccessible(
                            result,
                            filePath,
                            ex.Message);
                    }
                    catch (IOException ex)
                    {
                        sizeComplete = false;

                        AddInaccessible(
                            result,
                            filePath,
                            ex.Message);
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                sizeComplete = false;

                AddInaccessible(
                    result,
                    directoryPath,
                    ex.Message);
            }
            catch (IOException ex)
            {
                sizeComplete = false;

                AddInaccessible(
                    result,
                    directoryPath,
                    ex.Message);
            }

            // =====================================================
            // ПОДПАПКИ
            // =====================================================

            try
            {
                foreach (string childDirectory
                    in Directory.EnumerateDirectories(directoryPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        FileAttributes attributes =
                            File.GetAttributes(childDirectory);

                        // Не заходим в junction / symbolic link.
                        if ((attributes &
                            FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        if (recursive)
                        {
                            // =====================================
                            // ПОЛНЫЙ РЕКУРСИВНЫЙ РЕЖИМ
                            // =====================================

                            bool childSizeComplete;

                            long childSize =
                                ScanDirectory(
                                    childDirectory,
                                    result,
                                    true,
                                    cancellationToken,
                                    progress,
                                    true,
                                    out childSizeComplete);

                            directorySize += childSize;

                            if (!childSizeComplete)
                                sizeComplete = false;
                        }
                        else
                        {
                            // =====================================
                            // НЕПОЛНЫЙ РЕЖИМ ОТОБРАЖЕНИЯ
                            // =====================================
                            //
                            // В таблицу добавляем ТОЛЬКО папку.
                            //
                            // Но её содержимое просматриваем,
                            // чтобы узнать настоящий размер.
                            //

                            bool childSizeComplete;

                            long childSize =
                                CalculateDirectorySizeOnly(
                                    childDirectory,
                                    result,
                                    cancellationToken,
                                    progress,
                                    out childSizeComplete);

                            directorySize += childSize;

                            result.Items.Add(
                                new FileSystemItem
                                {
                                    Name =
                                        new DirectoryInfo(
                                            childDirectory).Name,

                                    FullPath =
                                        childDirectory,

                                    IsDirectory = true,

                                    SizeBytes = childSize,

                                    SizeCalculated = true,

                                    SizeComplete = childSizeComplete
                                });

                            if (!childSizeComplete)
                                sizeComplete = false;
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        sizeComplete = false;

                        AddInaccessible(
                            result,
                            childDirectory,
                            ex.Message);
                    }
                    catch (IOException ex)
                    {
                        sizeComplete = false;

                        AddInaccessible(
                            result,
                            childDirectory,
                            ex.Message);
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                sizeComplete = false;

                AddInaccessible(
                    result,
                    directoryPath,
                    ex.Message);
            }
            catch (IOException ex)
            {
                sizeComplete = false;

                AddInaccessible(
                    result,
                    directoryPath,
                    ex.Message);
            }

            // В полном рекурсивном режиме
            // текущую папку тоже показываем.
            if (addDirectoryItem)
            {
                result.Items.Add(
                    new FileSystemItem
                    {
                         Name =
                            new DirectoryInfo(
                                directoryPath).Name,

                         FullPath = directoryPath,

                         IsDirectory = true,

                         SizeBytes = directorySize,

                         SizeCalculated = true,

                         SizeComplete = sizeComplete
                });
            }

            return directorySize;
        }

        // =========================================================
        // РАСЧЁТ РАЗМЕРА ПАПКИ БЕЗ ДОБАВЛЕНИЯ ВЛОЖЕННЫХ ОБЪЕКТОВ
        // =========================================================

        private long CalculateDirectorySizeOnly(
            string rootDirectory,
            ScanResult result,
            CancellationToken cancellationToken,
            IProgress<ScanProgress> progress,
            out bool sizeComplete)
        {
            long totalDirectorySize = 0;

            sizeComplete = true;

            Stack<string> directories =
                new Stack<string>();

            directories.Push(rootDirectory);

            while (directories.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string currentDirectory =
                    directories.Pop();

                processedFolders++;

                ReportProgress(
                    progress,
                    currentDirectory);

                // -------------------------------------------------
                // ФАЙЛЫ
                // -------------------------------------------------

                try
                {
                    foreach (string filePath
                        in Directory.EnumerateFiles(
                            currentDirectory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            FileInfo info =
                                new FileInfo(filePath);

                            long fileSize =
                                info.Length;

                            totalDirectorySize +=
                                fileSize;

                            totalSize +=
                                fileSize;

                            processedFiles++;

                            ReportProgress(
                                progress,
                                filePath);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            sizeComplete = false;

                            AddInaccessible(
                                result,
                                filePath,
                                ex.Message);
                        }
                        catch (IOException ex)
                        {
                            sizeComplete = false;

                            AddInaccessible(
                                result,
                                filePath,
                                ex.Message);
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    sizeComplete = false;

                    AddInaccessible(
                        result,
                        currentDirectory,
                        ex.Message);
                }
                catch (IOException ex)
                {
                    sizeComplete = false;

                    AddInaccessible(
                        result,
                        currentDirectory,
                        ex.Message);
                }

                // -------------------------------------------------
                // ДОЧЕРНИЕ ПАПКИ
                // -------------------------------------------------

                try
                {
                    foreach (string childDirectory
                        in Directory.EnumerateDirectories(
                            currentDirectory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            FileAttributes attributes =
                                File.GetAttributes(
                                    childDirectory);

                            if ((attributes &
                                FileAttributes.ReparsePoint) != 0)
                            {
                                continue;
                            }

                            directories.Push(
                                childDirectory);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            sizeComplete = false;

                            AddInaccessible(
                                result,
                                childDirectory,
                                ex.Message);
                        }
                        catch (IOException ex)
                        {
                            sizeComplete = false;

                            AddInaccessible(
                                result,
                                childDirectory,
                                ex.Message);
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    sizeComplete = false;

                    AddInaccessible(
                        result,
                        currentDirectory,
                        ex.Message);
                }
                catch (IOException ex)
                {
                    sizeComplete = false;

                    AddInaccessible(
                        result,
                        currentDirectory,
                        ex.Message);
                }
            }

            return totalDirectorySize;
        }

        private void AddInaccessible(
            ScanResult result,
            string path,
            string reason)
        {
            skippedEntries++;

            if (result.InaccessiblePaths.Count
                >= MaxInaccessibleDetails)
            {
                return;
            }

            result.InaccessiblePaths.Add(
                path + " — " + reason);
        }

        private void ReportProgress(
            IProgress<ScanProgress> progress,
            string currentPath)
        {
            if (progress == null)
                return;

            if (processedFiles % 250 != 0 &&
                progressTimer.ElapsedMilliseconds < 200)
            {
                return;
            }

            progressTimer.Restart();

            progress.Report(
                new ScanProgress
                {
                    ProcessedFiles = processedFiles,

                    ProcessedFolders =
                        processedFolders,

                    TotalSizeBytes = totalSize,

                    CurrentPath = currentPath
                });
        }
    }
}