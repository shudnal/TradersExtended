using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TradersExtended
{
    internal static class ConfigPersistence
    {
        internal const int MaximumFileBytes = 8 * 1024 * 1024;

        // YamlDotNet must receive plain CLR data, not Newtonsoft's JToken implementation objects.
        internal static object ToPlainData(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;
            if (token is JObject map)
                return map.Properties().ToDictionary(property => property.Name, property => ToPlainData(property.Value));
            if (token is JArray list)
                return list.Select(ToPlainData).ToList();
            if (token is JValue value)
                return value.Value;
            throw new InvalidDataException("Unsupported configuration value.");
        }

        internal static string ReadBounded(string path)
        {
            using (FileStream source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (MemoryStream content = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (content.Length + count > MaximumFileBytes)
                        throw new InvalidDataException("The configuration exceeds the 8 MiB editor file limit.");
                    content.Write(buffer, 0, count);
                }
                content.Position = 0;
                using (StreamReader reader = new StreamReader(content, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
        }

        internal static void WriteAtomically(string path, string content, bool create)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(content ?? string.Empty);
            if (bytes.Length > MaximumFileBytes)
                throw new InvalidDataException("The configuration exceeds the 8 MiB editor file limit.");
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (create)
                    File.Move(temporaryPath, path); // Fails rather than overwriting a concurrently created file.
                else
                    File.Replace(temporaryPath, path, null); // Never truncate or recreate a deleted original.
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
