using System;
using System.IO;
using System.Text;

namespace PotatoOptimization.Utilities
{
    public static class ExportFileWriter
    {
        public static void WriteNew(string path, string content)
        {
            // A concurrent export must never overwrite a user's existing file.
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(true))) writer.Write(content);
                File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
