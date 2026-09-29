namespace DiskAnalyzer.Models
{
    public class FileSystemItem
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public long SizeBytes { get; set; }

        public string Type => IsDirectory ? "Папка" : "Файл";

        public string SizeText
        {
            get
            {
                const double KB = 1024;
                const double MB = KB * 1024;
                const double GB = MB * 1024;

                if (SizeBytes >= GB) return $"{SizeBytes / GB:F2} ГБ";
                if (SizeBytes >= MB) return $"{SizeBytes / MB:F2} МБ";
                if (SizeBytes >= KB) return $"{SizeBytes / KB:F2} КБ";
                return $"{SizeBytes} Б";
            }
        }
    }
}
