namespace DiskAnalyzer67.models
{
    public class FileSystemItem
    {
        public string Name { get; set; }

        public string FullPath { get; set; }

        public bool IsDirectory { get; set; }

        public long SizeBytes { get; set; }

        // Может быть false для папки, если включён режим
        // без рекурсивного захода внутрь неё.
        public bool SizeCalculated { get; set; }

        public bool SizeComplete { get; set; }

        public string Type
        {
            get
            {
                return IsDirectory ? "Папка" : "Файл";
            }
        }

        public string SizeText
        {
            get
            {
                if (!SizeCalculated)
                    return "—";

                const double KB = 1024.0;
                const double MB = KB * 1024.0;
                const double GB = MB * 1024.0;

                string result;

                if (SizeBytes >= GB)
                {
                    result = string.Format(
                        "{0:F2} ГБ",
                        SizeBytes / GB);
                }
                else if (SizeBytes >= MB)
                {
                    result = string.Format(
                        "{0:F2} МБ",
                        SizeBytes / MB);
                }
                else if (SizeBytes >= KB)
                {
                    result = string.Format(
                        "{0:F2} КБ",
                        SizeBytes / KB);
                }
                else
                {
                    result = SizeBytes + " Б";
                }

                if (!SizeComplete)
                    return "≈ " + result;

                return result;
            }
        }
    }
}