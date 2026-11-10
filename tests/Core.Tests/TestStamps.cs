using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace LoMSaveManager.Tests
{
    /// <summary>Stamp helpers for in-memory catalog tests and file-based session tests.</summary>
    internal static class TestStamps
    {
        public const string ShaA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public const string ShaB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        /// <summary>
        /// A stamp whose content hash is derived from (length, ticks), so two stamps that differ in either
        /// describe different files, as the pre-hash tests assumed.
        /// </summary>
        public static SaveStamp S(long length, long writeTicks)
        {
            return new SaveStamp(length, writeTicks, "fake-" + length + "-" + writeTicks);
        }

        public static string Sha256Of(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        public static string Sha256OfFile(string path)
        {
            return Sha256Of(File.ReadAllBytes(path));
        }

        public static string NewTempFolder()
        {
            var root = Path.Combine(Path.GetTempPath(), "LoMSaveManagerTests-" + Guid.NewGuid().ToString("N"));
            var folder = Path.Combine(root, "76561190000000000");
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static void DeleteTempFolder(string folder)
        {
            try { Directory.Delete(Path.GetDirectoryName(folder), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        public static void WithFolder(Action<string, List<string>> body)
        {
            var folder = NewTempFolder();
            try { body(folder, new List<string>()); }
            finally { DeleteTempFolder(folder); }
        }

        /// <summary>Writes Save_&lt;key&gt;.dat with bytes derived from <paramref name="seed"/> and returns its path.</summary>
        public static string PutSave(string folder, string key, int length, int seed = 9)
        {
            var path = Path.Combine(folder, "Save_" + key + ".dat");
            var bytes = new byte[length];
            for (var i = 0; i < length; i++) bytes[i] = (byte)((seed * 31 + i * 7) & 0xFF);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public static string[] RootFileNames(string folder)
        {
            return Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }
    }
}
