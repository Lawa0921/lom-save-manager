using System;
using System.IO;
using System.Linq;

namespace LoMSaveManager.Tests
{
    internal static class SlotFilesTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("occupied_keys_in_directory_reads_bare_file_names_not_full_paths", () => WithSaveFolder(folder =>
            {
                // Directory.GetFiles returns full paths; SlotFiles.OccupiedKeys only understands bare names.
                var keys = SlotFiles.OccupiedKeysInDirectory(folder);
                Check.SetEqual(new[] { "001", "002", "003", "1000" }, keys);
            }));

            test("occupied_keys_in_missing_directory_is_empty", () =>
            {
                var missing = Path.Combine(Path.GetTempPath(), "LoMSaveManagerTests-missing-" + Guid.NewGuid().ToString("N"));
                Check.Equal(0, SlotFiles.OccupiedKeysInDirectory(missing).Count);
                Check.Equal(0, SlotFiles.ScanManualSaves(missing).Count);
            });

            test("scan_manual_saves_lists_only_dat_files_with_their_stamps", () => WithSaveFolder(folder =>
            {
                var scan = SlotFiles.ScanManualSaves(folder);

                // .dat.tmp / .dat.bak occupy a key (see above) but are not saves; auto/universe/invalid keys are not manual.
                Check.SetEqual(new[] { "001", "1000" }, scan.Keys);
                var info = new FileInfo(Path.Combine(folder, "Save_001.dat"));
                Check.Equal(info.Length, scan["001"].Length);
                Check.Equal(info.LastWriteTimeUtc.Ticks, scan["001"].WriteTicks);
                Check.Equal("6a7dc6f4267242f01f6636a45c31da51c036da1e9879abce7e1d0aaa76aad876", scan["001"].Sha256);
                Check.Equal(4L, scan["1000"].Length);
            }));

            test("scan_manual_saves_sees_a_rewritten_file_as_a_new_stamp", () => WithSaveFolder(folder =>
            {
                var file = Path.Combine(folder, "Save_001.dat");
                var before = SlotFiles.ScanManualSaves(folder)["001"];
                File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 5, 6 });
                File.SetLastWriteTimeUtc(file, new DateTime(before.WriteTicks, DateTimeKind.Utc).AddSeconds(30));

                var after = SlotFiles.ScanManualSaves(folder)["001"];
                Check.Equal(6L, after.Length);
                Check.False(before.Length == after.Length && before.WriteTicks == after.WriteTicks);
                Check.False(before.Sha256 == after.Sha256);
            }));

            test("scan_manual_saves_matches_names_case_insensitively", () =>
            {
                var root = NewTempDir();
                try
                {
                    File.WriteAllBytes(Path.Combine(root, "SAVE_006.DAT"), new byte[] { 1 });
                    Check.SetEqual(new[] { "006" }, SlotFiles.ScanManualSaves(root).Keys);
                    Check.SetEqual(new[] { "006" }, SlotFiles.OccupiedKeysInDirectory(root));
                }
                finally { DeleteQuietly(root); }
            });
        }

        /// <summary>
        /// A stand-in for the game's per-account save folder: manual saves in several states, auto/universe files,
        /// unrelated files, and the mod's own LoMSaveManager subdirectory (which even holds a Save_ look-alike).
        /// </summary>
        private static void WithSaveFolder(Action<string> body)
        {
            var root = NewTempDir();
            try
            {
                var folder = Path.Combine(root, "76561190000000000");
                Directory.CreateDirectory(folder);
                Put(folder, "Save_001.dat", 3);
                Put(folder, "Save_002.dat.tmp", 1);
                Put(folder, "Save_003.dat.bak", 1);
                Put(folder, "Save_1000.dat", 4);
                Put(folder, "Save_0100.dat", 1); // not a canonical key
                Put(folder, "Save_auto.dat", 1);
                Put(folder, "Save_auto_free.dat", 1);
                Put(folder, "Save_universe.dat", 1);
                Put(folder, "Save_universe.json", 1);
                Put(folder, "Legend_001.dat", 1);
                Put(folder, "steam_autocloud.vdf", 1);
                var mod = Path.Combine(folder, "LoMSaveManager");
                Directory.CreateDirectory(mod);
                Put(mod, "index.json", 1);
                Put(mod, "Save_004.dat", 1);
                Directory.CreateDirectory(Path.Combine(folder, "Save_005.dat")); // a directory is not a save
                body(folder);
            }
            finally { DeleteQuietly(root); }
        }

        private static void Put(string dir, string name, int length)
        {
            File.WriteAllBytes(Path.Combine(dir, name), Enumerable.Repeat((byte)7, length).ToArray());
        }

        private static string NewTempDir()
        {
            var root = Path.Combine(Path.GetTempPath(), "LoMSaveManagerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void DeleteQuietly(string root)
        {
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
