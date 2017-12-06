﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using XForm.IO;

namespace XForm.Extensions
{
    /// <summary>
    ///  StreamProviderExtensions provides higher level functions on the IStreamProvider in terms
    ///  of IStreamProvider primitives.
    /// </summary>
    public static class StreamProviderExtensions
    {
        public const string DateTimeFolderFormat = "yyyy.MM.dd HH.mm.ssZ";

        public static string Path(this IStreamProvider streamProvider, LocationType type, string tableName, string extension)
        {
            return System.IO.Path.Combine(type.ToString(), tableName + extension);
        }

        public static string Path(this IStreamProvider streamProvider, LocationType type, string tableName, CrawlType crawlType)
        {
            return System.IO.Path.Combine(type.ToString(), tableName, crawlType.ToString());
        }

        public static string Path(this IStreamProvider streamProvider, LocationType type, string tableName, CrawlType crawlType, DateTime version)
        {
            return System.IO.Path.Combine(type.ToString(), tableName, crawlType.ToString(), version.ToUniversalTime().ToString(DateTimeFolderFormat));
        }

        public static StreamAttributes LatestBeforeCutoff(this IStreamProvider streamProvider, LocationType type, string tableName, DateTime asOfDateTime)
        {
            // Find the last Full crawl which isn't after the cutoff
            StreamAttributes latestStream = StreamAttributes.NotExists;
            DateTime latestStreamVersion = DateTime.MinValue;

            string sourceFullPath = streamProvider.Path(type, tableName, CrawlType.Full);
            foreach (StreamAttributes version in streamProvider.Enumerate(sourceFullPath, false))
            {
                DateTime versionAsOf;
                if (!DateTime.TryParseExact(System.IO.Path.GetFileName(version.Path), DateTimeFolderFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out versionAsOf))
                {
                    continue;
                }

                // Track the latest version, modifying the WhenModifiedUtc to be the folder stamp and not the actual file time
                if (versionAsOf > latestStreamVersion && versionAsOf <= asOfDateTime)
                {
                    latestStream = version;
                    latestStream.WhenModifiedUtc = versionAsOf;
                }
            }

            return latestStream;
        }

        public static IEnumerable<string> Tables(this IStreamProvider streamProvider)
        {
            HashSet<string> tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Return all Configs as tables
            foreach (StreamAttributes item in streamProvider.Enumerate("Config", true))
            {
                if (item.Path.EndsWith(".xql", StringComparison.OrdinalIgnoreCase))
                {
                    tables.Add(item.Path.RelativePath("Config\\", ".xql"));
                }
            }

            // Return raw Sources as tables
            AddFullFolderContainers(streamProvider, "Source", tables);

            return tables;
        }

        private static void AddFullFolderContainers(this IStreamProvider streamProvider, string underPath, HashSet<string> results)
        {
            foreach (StreamAttributes item in streamProvider.Enumerate(underPath, false))
            {
                if (item.Path.EndsWith("\\Full"))
                {
                    // If this has a 'Full' folder in it, add it and stop recursing
                    results.Add(underPath.RelativePath("Source\\"));
                    return;
                }

                // Otherwise look under this folder
                AddFullFolderContainers(streamProvider, item.Path, results);
            }
        }

        public static IEnumerable<string> Queries(this IStreamProvider streamProvider)
        {
            // Return all Queries
            foreach (StreamAttributes item in streamProvider.Enumerate("Query", true))
            {
                if (item.Path.EndsWith(".xql", StringComparison.OrdinalIgnoreCase))
                {
                    yield return item.Path.RelativePath("Query\\", ".xql");
                }
            }
        }

        public static string ReadAllText(this IStreamProvider streamProvider, string path)
        {
            using (StreamReader reader = new StreamReader(streamProvider.OpenRead(path)))
            {
                return reader.ReadToEnd();
            }
        }

        public static void WriteAllText(this IStreamProvider streamProvider, string path, string content)
        {
            using (StreamWriter writer = new StreamWriter(streamProvider.OpenWrite(path)))
            {
                writer.Write(content);
            }
        }

        public static void Copy(this IStreamProvider streamProvider, Stream source, string targetPath)
        {
            using (source)
            {
                using (Stream output = streamProvider.OpenWrite(targetPath))
                {
                    source.CopyTo(output);
                }
            }
        }
    }
}