using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using DiskAnalyzer.Models;

namespace DiskAnalyzer.Services
{
    public sealed class ScanProgress
    {
        public int ProcessedFiles { get; set; }
        public int ProcessedFolders { get; set; }
        public long TotalSizeBytes { get; set; }
        public string CurrentPath { get; set; }
    }

    public sealed class ScanResult
    {
        public List<FileSystemItem> Items { get; } = new List<FileSystemItem>();
        public int SkippedEntries { get; set; }
        public long TotalSizeBytes { get; set; }
        public int FileCount { get; set; }
        public int FolderCount { get; set; }
    }

    public class FileScanner
    {
        private int _processedFiles;
        private int _processedFolders;
        private long _totalSize;
        private int _skippedEntries;

        public ScanResult Scan(
            string rootPath,
            CancellationToken cancellationToken,
            IProgress<ScanProgress> progress = null)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("Не указана папка для анализа.");

            if (!Directory.Exists(rootPath))
                throw new DirectoryNotFoundException("Указанная папка не существует.");

            _processedFiles = 0;
            _processedFolders = 0;
            _totalSize = 0;
            _skippedEntries = 0;

            var result = new ScanResult();
            ScanDirectory(rootPath, result.Items, cancellationToken, progress);

            result.SkippedEntries = _skippedEntries;
            result.TotalSizeBytes = _totalSize;
            result.FileCount = _processedFiles;
            result.FolderCount = _processedFolders;
            return result;
        }

        private long ScanDirectory(
            string directoryPath,
            List<FileSystemItem> items,
            CancellationToken cancellationToken,
            IProgress<ScanProgress> progress)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directoryItem = new FileSystemItem
            {
                Name = new DirectoryInfo(directoryPath).Name,
                FullPath = directoryPath,
                IsDirectory = true
            };

            long directorySize = 0;
            _processedFolders++;

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(directoryPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var info = new FileInfo(filePath);
                        long fileSize = info.Length;

                        items.Add(new FileSystemItem
                        {
                            Name = info.Name,
                            FullPath = info.FullName,
                            IsDirectory = false,
                            SizeBytes = fileSize
                        });

                        directorySize += fileSize;
                        _totalSize += fileSize;
                        _processedFiles++;

                        ReportProgress(progress, filePath);
                    }
                    catch (UnauthorizedAccessException) { _skippedEntries++; }
                    catch (IOException) { _skippedEntries++; }
                }
            }
            catch (UnauthorizedAccessException) { _skippedEntries++; }
            catch (IOException) { _skippedEntries++; }

            try
            {
                foreach (var childDirectory in Directory.EnumerateDirectories(directoryPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var attributes = File.GetAttributes(childDirectory);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            continue;

                        directorySize += ScanDirectory(
                            childDirectory, items, cancellationToken, progress);
                    }
                    catch (UnauthorizedAccessException) { _skippedEntries++; }
                    catch (IOException) { _skippedEntries++; }
                }
            }
            catch (UnauthorizedAccessException) { _skippedEntries++; }
            catch (IOException) { _skippedEntries++; }

            directoryItem.SizeBytes = directorySize;
            items.Add(directoryItem);
            return directorySize;
        }

        private void ReportProgress(IProgress<ScanProgress> progress, string currentPath)
        {
            progress?.Report(new ScanProgress
            {
                ProcessedFiles = _processedFiles,
                ProcessedFolders = _processedFolders,
                TotalSizeBytes = _totalSize,
                CurrentPath = currentPath
            });
        }
    }
}
