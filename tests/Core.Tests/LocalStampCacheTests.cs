using System;
using System.IO;

namespace LoMSaveManager.Tests
{
    /// <summary>
    /// The per-machine cache (LoMSaveManager/local-cache.json, never synced): key -> (length, writeTicks, sha256).
    /// A hash is reused only while the file's length and write ticks are unchanged.
    /// </summary>
    internal static class LocalStampCacheTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("cache_resolve_computes_once_and_reuses_while_length_and_ticks_match", () => TestStamps.WithFolder((folder, _) =>
            {
                var file = TestStamps.PutSave(folder, "001", 300);
                var cache = new LocalStampCache(SaveSession.LocalCachePathFor(folder));
                Check.Null(cache.LoadProblem);

                var first = cache.Resolve("001", new FileInfo(file));
                Check.Equal(TestStamps.Sha256OfFile(file), first.Sha256);
                Check.Equal(300L, first.Length);
                Check.Equal(File.GetLastWriteTimeUtc(file).Ticks, first.WriteTicks);
                Check.Equal(1, cache.Computed);

                var second = cache.Resolve("001", new FileInfo(file));
                Check.Equal(first.Sha256, second.Sha256);
                Check.Equal(1, cache.Computed);
            }));

            test("cache_recomputes_when_the_write_ticks_change", () => TestStamps.WithFolder((folder, _) =>
            {
                var file = TestStamps.PutSave(folder, "001", 300);
                var cache = new LocalStampCache(SaveSession.LocalCachePathFor(folder));
                cache.Resolve("001", new FileInfo(file));

                // Same length, new content, new time: must not reuse the old hash.
                var ticks = File.GetLastWriteTimeUtc(file);
                TestStamps.PutSave(folder, "001", 300, seed: 2);
                File.SetLastWriteTimeUtc(file, ticks.AddSeconds(3));
                var after = cache.Resolve("001", new FileInfo(file));
                Check.Equal(TestStamps.Sha256OfFile(file), after.Sha256);
                Check.Equal(2, cache.Computed);
            }));

            test("cache_save_and_reload_reuses_hashes", () => TestStamps.WithFolder((folder, _) =>
            {
                var a = TestStamps.PutSave(folder, "001", 300);
                var b = TestStamps.PutSave(folder, "002", 400, seed: 3);
                var path = SaveSession.LocalCachePathFor(folder);
                var cache = new LocalStampCache(path);
                cache.Resolve("001", new FileInfo(a));
                cache.Resolve("002", new FileInfo(b));
                cache.Save();
                Check.True(File.Exists(path));

                var reloaded = new LocalStampCache(path);
                Check.Null(reloaded.LoadProblem);
                Check.Equal(TestStamps.Sha256OfFile(b), reloaded.Resolve("002", new FileInfo(b)).Sha256);
                Check.Equal(TestStamps.Sha256OfFile(a), reloaded.Resolve("001", new FileInfo(a)).Sha256);
                Check.Equal(0, reloaded.Computed);
                Check.False(File.Exists(path + ".tmp"));
            }));

            test("cache_save_without_changes_does_not_rewrite", () => TestStamps.WithFolder((folder, _) =>
            {
                var a = TestStamps.PutSave(folder, "001", 300);
                var path = SaveSession.LocalCachePathFor(folder);
                var cache = new LocalStampCache(path);
                cache.Resolve("001", new FileInfo(a));
                cache.Save();
                var marker = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(path, marker);

                var reloaded = new LocalStampCache(path);
                reloaded.Resolve("001", new FileInfo(a));
                reloaded.Save();
                Check.Equal(marker, File.GetLastWriteTimeUtc(path));
            }));

            test("corrupt_cache_file_is_ignored_and_every_hash_recomputed", () => TestStamps.WithFolder((folder, _) =>
            {
                var a = TestStamps.PutSave(folder, "001", 300);
                var b = TestStamps.PutSave(folder, "002", 400, seed: 3);
                var path = SaveSession.LocalCachePathFor(folder);
                var cache = new LocalStampCache(path);
                cache.Resolve("001", new FileInfo(a));
                cache.Resolve("002", new FileInfo(b));
                cache.Save();

                foreach (var garbage in new[] { "{garbage", "", "[]", "null" })
                {
                    File.WriteAllText(path, garbage);
                    var broken = new LocalStampCache(path);
                    Check.True(broken.LoadProblem != null);
                    Check.Equal(TestStamps.Sha256OfFile(a), broken.Resolve("001", new FileInfo(a)).Sha256);
                    Check.Equal(TestStamps.Sha256OfFile(b), broken.Resolve("002", new FileInfo(b)).Sha256);
                    Check.Equal(2, broken.Computed);
                    broken.Save();
                    Check.Null(new LocalStampCache(path).LoadProblem);
                }
            }));

            test("cache_entry_with_a_malformed_sha_is_recomputed", () => TestStamps.WithFolder((folder, _) =>
            {
                var a = TestStamps.PutSave(folder, "001", 300);
                var info = new FileInfo(a);
                var path = SaveSession.LocalCachePathFor(folder);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "{\"Version\":1,\"Entries\":{\"001\":{\"Length\":" + info.Length + ",\"WriteTicks\":"
                    + info.LastWriteTimeUtc.Ticks + ",\"Sha256\":\"not-a-sha\"}}}");

                var cache = new LocalStampCache(path);
                Check.Equal(TestStamps.Sha256OfFile(a), cache.Resolve("001", new FileInfo(a)).Sha256);
                Check.Equal(1, cache.Computed);
            }));

            test("cache_invalidate_forces_a_recompute", () => TestStamps.WithFolder((folder, _) =>
            {
                var a = TestStamps.PutSave(folder, "001", 300);
                var cache = new LocalStampCache(SaveSession.LocalCachePathFor(folder));
                cache.Resolve("001", new FileInfo(a));

                // Same length and same write time, different bytes: only an explicit invalidate notices.
                var ticks = File.GetLastWriteTimeUtc(a);
                TestStamps.PutSave(folder, "001", 300, seed: 5);
                File.SetLastWriteTimeUtc(a, ticks);
                cache.Invalidate("001");
                Check.Equal(TestStamps.Sha256OfFile(a), cache.Resolve("001", new FileInfo(a)).Sha256);
                Check.Equal(2, cache.Computed);
            }));

            test("cache_retain_drops_entries_of_vanished_saves", () => TestStamps.WithFolder((folder, _) =>
            {
                var a = TestStamps.PutSave(folder, "001", 300);
                var b = TestStamps.PutSave(folder, "002", 300, seed: 3);
                var path = SaveSession.LocalCachePathFor(folder);
                var cache = new LocalStampCache(path);
                cache.Resolve("001", new FileInfo(a));
                cache.Resolve("002", new FileInfo(b));
                cache.Retain(new[] { "001" });
                cache.Save();

                var reloaded = new LocalStampCache(path);
                reloaded.Resolve("002", new FileInfo(b));
                Check.Equal(1, reloaded.Computed);
            }));

            test("content_hash_retries_a_briefly_locked_file", () => TestStamps.WithFolder((folder, _) =>
            {
                var a = TestStamps.PutSave(folder, "001", 300);
                var expected = TestStamps.Sha256OfFile(a);
                var lockHeld = new System.Threading.ManualResetEventSlim();
                var holder = new System.Threading.Thread(() =>
                {
                    using (new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        lockHeld.Set();
                        System.Threading.Thread.Sleep(30);
                    }
                });
                holder.Start();
                lockHeld.Wait();
                Check.Equal(expected, ContentHash.Sha256Hex(a));
                holder.Join();
            }));

            test("content_hash_gives_up_on_a_file_that_stays_locked", () => TestStamps.WithFolder((folder, _) =>
            {
                var a = TestStamps.PutSave(folder, "001", 300);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                using (new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Check.Throws<IOException>(() => ContentHash.Sha256Hex(a));
                Check.True(watch.ElapsedMilliseconds < 2000);
                Check.Throws<FileNotFoundException>(() => ContentHash.Sha256Hex(Path.Combine(folder, "missing.dat")));
            }));

            test("scan_with_a_cache_hashes_only_changed_files", () => TestStamps.WithFolder((folder, _) =>
            {
                var a = TestStamps.PutSave(folder, "001", 300);
                TestStamps.PutSave(folder, "002", 300, seed: 3);
                var cache = new LocalStampCache(SaveSession.LocalCachePathFor(folder));

                var scan = SlotFiles.ScanManualSaves(folder, cache);
                Check.Equal(2, cache.Computed);
                Check.Equal(TestStamps.Sha256OfFile(a), scan["001"].Sha256);

                SlotFiles.ScanManualSaves(folder, cache);
                Check.Equal(2, cache.Computed);

                File.SetLastWriteTimeUtc(a, File.GetLastWriteTimeUtc(a).AddSeconds(1));
                SlotFiles.ScanManualSaves(folder, cache);
                Check.Equal(3, cache.Computed);
            }));
        }
    }
}
