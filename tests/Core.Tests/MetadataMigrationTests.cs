using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LoMSaveManager.Tests
{
    /// <summary>v1 LoMSaveManager/index.json (length + write ticks) -> v2 LoMSaveManager.json (length + SHA-256).</summary>
    internal static class MetadataMigrationTests
    {
        // The user's real v1 index (category names replaced by placeholders, GUIDs and stamps as found).
        private const string RealV1 = "{\"Categories\":[{\"Id\":\"45d6ee6275e444d89c6d3ba96f18f010\",\"Name\":\"某某線\",\"Order\":0},{\"Id\":\"02f38a39de1a434fa48d81b56406b433\",\"Name\":\"某某線二\",\"Order\":1}],\"NewestFirst\":true,\"Slots\":{\"004\":{\"CategoryId\":\"45d6ee6275e444d89c6d3ba96f18f010\",\"Length\":92437,\"Name\":null,\"WriteTicks\":639287949933067456},\"005\":{\"CategoryId\":\"45d6ee6275e444d89c6d3ba96f18f010\",\"Length\":77766,\"Name\":null,\"WriteTicks\":639284402393105668},\"006\":{\"CategoryId\":\"45d6ee6275e444d89c6d3ba96f18f010\",\"Length\":86783,\"Name\":null,\"WriteTicks\":639284520878988162},\"008\":{\"CategoryId\":\"02f38a39de1a434fa48d81b56406b433\",\"Length\":77356,\"Name\":null,\"WriteTicks\":639286549490496615},\"009\":{\"CategoryId\":\"02f38a39de1a434fa48d81b56406b433\",\"Length\":87165,\"Name\":null,\"WriteTicks\":639286898192533006},\"021\":{\"CategoryId\":null,\"Length\":58711,\"Name\":null,\"WriteTicks\":639298332782572350}},\"Version\":1}";

        private const string CatA = "45d6ee6275e444d89c6d3ba96f18f010";
        private const string CatB = "02f38a39de1a434fa48d81b56406b433";

        public static void Run(Action<string, Action> test)
        {
            MoreTests(test);
            SummaryTests(test);

            test("migrates_real_v1_fixture", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var expected = new Dictionary<string, Tuple<long, long, string>>
                {
                    { "004", Tuple.Create(92437L, 639287949933067456L, CatA) },
                    { "005", Tuple.Create(77766L, 639284402393105668L, CatA) },
                    { "006", Tuple.Create(86783L, 639284520878988162L, CatA) },
                    { "008", Tuple.Create(77356L, 639286549490496615L, CatB) },
                    { "009", Tuple.Create(87165L, 639286898192533006L, CatB) },
                    { "021", Tuple.Create(58711L, 639298332782572350L, (string)null) },
                };
                var seed = 1;
                foreach (var pair in expected)
                {
                    var file = TestStamps.PutSave(folder, pair.Key, (int)pair.Value.Item1, seed++);
                    // v1 FileStamp used LastWriteTimeUtc.Ticks.
                    File.SetLastWriteTimeUtc(file, new DateTime(pair.Value.Item2, DateTimeKind.Utc));
                }
                WriteLegacy(folder, RealV1);
                var rootBefore = TestStamps.RootFileNames(folder);

                var result = MetadataMigration.TryMigrate(folder);

                Check.Null(result.Problem);
                Check.True(result.Migrated);
                Check.Equal(6, result.Kept);
                Check.Equal(0, result.Dropped);

                var path = SaveSession.IndexPathFor(folder);
                Check.True(File.Exists(path));
                Check.Equal(string.Join(",", rootBefore.Concat(new[] { "LoMSaveManager.json" }).OrderBy(n => n, StringComparer.Ordinal)),
                    string.Join(",", TestStamps.RootFileNames(folder)));

                var store = new SaveIndexStore(path, SaveSession.LocalFolderFor(folder));
                var index = store.Load();
                Check.Null(store.LastLoadProblem);
                Check.True(File.ReadAllText(path).Contains("\"Version\":2"));
                Check.False(File.ReadAllText(path).Contains("WriteTicks"));
                Check.Equal(2, index.Version);
                Check.True(index.NewestFirst);
                Check.Equal(2, index.Categories.Count);
                Check.Equal(CatA, index.Categories[0].Id);
                Check.Equal("某某線", index.Categories[0].Name);
                Check.Equal(0, index.Categories[0].Order);
                Check.Equal(CatB, index.Categories[1].Id);
                Check.Equal("某某線二", index.Categories[1].Name);
                Check.Equal(1, index.Categories[1].Order);
                Check.SetEqual(expected.Keys, index.Slots.Keys);
                foreach (var pair in expected)
                {
                    var record = index.Slots[pair.Key];
                    Check.Null(record.Name);
                    Check.Equal(pair.Value.Item3, record.CategoryId);
                    Check.Equal(pair.Value.Item1, record.Length);
                    Check.Equal(TestStamps.Sha256OfFile(Path.Combine(folder, "Save_" + pair.Key + ".dat")), record.Sha256);
                }

                var local = SaveSession.LocalFolderFor(folder);
                Check.False(File.Exists(SaveSession.LegacyIndexPathFor(folder)));
                var migrated = Directory.GetFiles(local, "index.json.migrated-*");
                Check.Equal(1, migrated.Length);
                Check.Equal(RealV1, File.ReadAllText(migrated[0]));

                // The session opened afterwards sees the same metadata and has nothing to rewrite.
                var bytes = File.ReadAllBytes(path);
                var session = SaveSession.Open(folder, warnings.Add);
                Check.Equal(CatA, session.Catalog.CategoryOf("004"));
                Check.Equal(CatB, session.Catalog.CategoryOf("009"));
                Check.Null(session.Catalog.CategoryOf("021"));
                Check.Equal(3, session.Catalog.UserCategories()[0].Count);
                Check.Equal(2, session.Catalog.UserCategories()[1].Count);
                Check.True(bytes.SequenceEqual(File.ReadAllBytes(path)));
                Check.Equal(0, warnings.Count);

                // Running it again is a no-op.
                var second = MetadataMigration.TryMigrate(folder);
                Check.False(second.Migrated);
                Check.Null(second.Problem);
                Check.True(bytes.SequenceEqual(File.ReadAllBytes(path)));
            }));

            test("migration_drops_records_whose_file_no_longer_matches", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var good = TestStamps.PutSave(folder, "001", 100, 1);
                var lengthChanged = TestStamps.PutSave(folder, "002", 120, 2);
                var timeChanged = TestStamps.PutSave(folder, "003", 100, 3);
                var t = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
                foreach (var f in new[] { good, lengthChanged, timeChanged }) File.SetLastWriteTimeUtc(f, new DateTime(t, DateTimeKind.Utc));
                WriteLegacy(folder, "{\"Version\":1,\"Categories\":[],\"NewestFirst\":false,\"Slots\":{"
                    + "\"001\":{\"Name\":\"good\",\"CategoryId\":null,\"Length\":100,\"WriteTicks\":" + t + "},"
                    + "\"002\":{\"Name\":\"len\",\"CategoryId\":null,\"Length\":100,\"WriteTicks\":" + t + "},"
                    + "\"003\":{\"Name\":\"time\",\"CategoryId\":null,\"Length\":100,\"WriteTicks\":" + (t + 1) + "},"
                    + "\"007\":{\"Name\":\"gone\",\"CategoryId\":null,\"Length\":100,\"WriteTicks\":" + t + "},"
                    + "\"auto\":{\"Name\":\"not manual\",\"CategoryId\":null,\"Length\":100,\"WriteTicks\":" + t + "}}}");

                var result = MetadataMigration.TryMigrate(folder);

                Check.True(result.Migrated);
                Check.Null(result.Problem);
                Check.Equal(1, result.Kept);
                Check.Equal(4, result.Dropped);
                var index = new SaveIndexStore(SaveSession.IndexPathFor(folder), SaveSession.LocalFolderFor(folder)).Load();
                Check.False(index.NewestFirst);
                Check.SetEqual(new[] { "001" }, index.Slots.Keys);
                Check.Equal("good", index.Slots["001"].Name);
            }));

            test("both_files_present_new_wins_old_untouched", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var a = TestStamps.PutSave(folder, "001", 100, 1);
                var t = File.GetLastWriteTimeUtc(a).Ticks;
                var legacy = "{\"Version\":1,\"Categories\":[],\"Slots\":{\"001\":{\"Name\":\"old\",\"Length\":100,\"WriteTicks\":" + t + "}}}";
                WriteLegacy(folder, legacy);
                var synced = "{\"Version\":2,\"Categories\":[],\"NewestFirst\":true,\"Slots\":{\"001\":{\"Name\":\"new\",\"CategoryId\":null,\"Length\":100,\"Sha256\":\""
                    + TestStamps.Sha256OfFile(a) + "\"}}}";
                File.WriteAllText(SaveSession.IndexPathFor(folder), synced);

                var result = MetadataMigration.TryMigrate(folder);

                Check.False(result.Migrated);
                Check.Null(result.Problem);
                Check.Null(result.Summary);
                // Synced wins, but the user is told that the old names on this PC were not carried over.
                Check.True(result.Note != null && result.Note.Contains("index.json") && result.Note.Contains("LoMSaveManager.json")
                    && result.Note.Contains("not migrated"));
                Check.Equal(legacy, File.ReadAllText(SaveSession.LegacyIndexPathFor(folder)));
                Check.Equal(synced, File.ReadAllText(SaveSession.IndexPathFor(folder)));
                Check.Equal(0, Directory.GetFiles(SaveSession.LocalFolderFor(folder), "index.json.migrated-*").Length);
                var session = SaveSession.Open(folder, warnings.Add);
                Check.Equal("new", session.Catalog.NameOf("001"));
                Check.Equal(result.Note, session.Migration.Note);
                Check.Equal(1, warnings.Count(w => w == result.Note));
                Check.Equal(legacy, File.ReadAllText(SaveSession.LegacyIndexPathFor(folder)));
                Check.Equal("index.json", string.Join(",", Directory.GetFiles(SaveSession.LocalFolderFor(folder), "index.json*").Select(Path.GetFileName)));
            }));

            test("nothing_to_migrate_creates_nothing", () => TestStamps.WithFolder((folder, warnings) =>
            {
                TestStamps.PutSave(folder, "001", 100, 1);
                var result = MetadataMigration.TryMigrate(folder);
                Check.False(result.Migrated);
                Check.Null(result.Problem);
                Check.Equal("Save_001.dat", string.Join(",", TestStamps.RootFileNames(folder)));
                Check.False(Directory.Exists(SaveSession.LocalFolderFor(folder)));
            }));

            test("migrating_an_empty_v1_index_creates_no_synced_file", () => TestStamps.WithFolder((folder, warnings) =>
            {
                // v1 wrote index.json even when nothing was ever named; an empty LoMSaveManager.json must not appear from it.
                TestStamps.PutSave(folder, "001", 100, 1);
                WriteLegacy(folder, "{\"Version\":1,\"Categories\":[],\"NewestFirst\":true,\"Slots\":{\"002\":{\"Name\":\"gone\",\"Length\":5,\"WriteTicks\":1}}}");

                var result = MetadataMigration.TryMigrate(folder);

                Check.True(result.Migrated);
                Check.Null(result.Problem);
                Check.Equal(1, result.Dropped);
                Check.False(File.Exists(SaveSession.IndexPathFor(folder)));
                Check.False(File.Exists(SaveSession.LegacyIndexPathFor(folder)));
                Check.Equal(1, Directory.GetFiles(SaveSession.LocalFolderFor(folder), "index.json.migrated-*").Length);
                Check.True(result.Summary.Contains("not created"));

                var session = SaveSession.Open(folder, warnings.Add);
                Check.True(session.CanPersist);
                Check.False(File.Exists(SaveSession.IndexPathFor(folder)));
                Check.Equal(0, warnings.Count);
            }));

            test("migrating_a_v1_index_of_unnamed_unfiled_saves_creates_no_synced_file", () => TestStamps.WithFolder((folder, warnings) =>
            {
                // v1 stamped every save the game wrote; a still-matching record with no name or category carries nothing.
                var a = TestStamps.PutSave(folder, "001", 100, 1);
                var t = File.GetLastWriteTimeUtc(a).Ticks;
                WriteLegacy(folder, "{\"Version\":1,\"Categories\":[],\"NewestFirst\":true,\"Slots\":{\"001\":{\"Name\":null,\"CategoryId\":null,\"Length\":100,\"WriteTicks\":" + t + "}}}");

                var result = MetadataMigration.TryMigrate(folder);

                Check.True(result.Migrated);
                Check.Null(result.Problem);
                Check.False(result.WroteSyncedFile);
                Check.False(File.Exists(SaveSession.IndexPathFor(folder)));
                Check.Equal(1, Directory.GetFiles(SaveSession.LocalFolderFor(folder), "index.json.migrated-*").Length);
            }));

            test("a_content_free_v1_index_left_beside_the_synced_file_is_not_reported", () => TestStamps.WithFolder((folder, warnings) =>
            {
                // Its rename failed when it was migrated (nothing to carry over); once the first edit creates
                // LoMSaveManager.json, saying "not migrated" at every start would only be noise.
                var a = TestStamps.PutSave(folder, "001", 100, 1);
                var t = File.GetLastWriteTimeUtc(a).Ticks;
                var legacy = "{\"Version\":1,\"Categories\":[],\"NewestFirst\":true,\"Slots\":{\"001\":{\"Name\":null,\"CategoryId\":null,\"Length\":100,\"WriteTicks\":" + t + "}}}";
                WriteLegacy(folder, legacy);
                var seed = new SaveIndex();
                seed.Categories.Add(new CategoryRecord { Id = "c", Name = "Main" });
                new SaveIndexStore(SaveSession.IndexPathFor(folder), SaveSession.LocalFolderFor(folder)).Save(seed);

                var result = MetadataMigration.TryMigrate(folder);
                Check.False(result.Migrated);
                Check.Null(result.Problem);
                Check.Null(result.Note);
                var session = SaveSession.Open(folder, warnings.Add);
                Check.True(session.CanPersist);
                Check.Equal(0, warnings.Count);
                Check.Equal(legacy, File.ReadAllText(SaveSession.LegacyIndexPathFor(folder))); // still never touched

                // An unreadable old file still gets the note: whether it holds names cannot be told.
                File.WriteAllText(SaveSession.LegacyIndexPathFor(folder), "{not json");
                Check.True(MetadataMigration.TryMigrate(folder).Note != null);
                // So does one holding only a sort preference.
                WriteLegacy(folder, "{\"Version\":1,\"Categories\":[],\"NewestFirst\":false,\"Slots\":{}}");
                Check.True(MetadataMigration.TryMigrate(folder).Note != null);
            }));

            test("migration_failure_writes_nothing", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var a = TestStamps.PutSave(folder, "001", 100, 1);
                var t = File.GetLastWriteTimeUtc(a).Ticks;
                var legacy = "{\"Version\":1,\"Categories\":[],\"Slots\":{\"001\":{\"Name\":\"keep\",\"Length\":100,\"WriteTicks\":" + t + "}}}";
                WriteLegacy(folder, legacy);
                var legacyPath = SaveSession.LegacyIndexPathFor(folder);
                var local = SaveSession.LocalFolderFor(folder);

                // 1. The old index cannot be read (locked).
                using (new FileStream(legacyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var locked = MetadataMigration.TryMigrate(folder);
                    Check.False(locked.Migrated);
                    Check.True(locked.Problem != null);
                }
                AssertNothingWritten(folder, legacy);

                // 2. A save it needs to hash cannot be read.
                using (new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var busy = MetadataMigration.TryMigrate(folder);
                    Check.False(busy.Migrated);
                    Check.True(busy.Problem != null);
                }
                AssertNothingWritten(folder, legacy);

                // 3. The new file cannot be written (a directory squats on the temp name).
                var tmp = Path.Combine(local, "LoMSaveManager.json.tmp");
                Directory.CreateDirectory(tmp);
                var blocked = MetadataMigration.TryMigrate(folder);
                Check.False(blocked.Migrated);
                Check.True(blocked.Problem != null);
                Directory.Delete(tmp);
                AssertNothingWritten(folder, legacy);

                // 4. An old index that can never be parsed is reported and moved aside (kept), so it cannot block forever.
                File.WriteAllText(legacyPath, "{not json");
                var corrupt = MetadataMigration.TryMigrate(folder);
                Check.False(corrupt.Migrated);
                Check.True(corrupt.Problem != null);
                Check.False(File.Exists(SaveSession.IndexPathFor(folder)));
                Check.False(File.Exists(legacyPath));
                var aside = Directory.GetFiles(local, "index.json.corrupt-*");
                Check.Equal(1, aside.Length);
                Check.Equal("{not json", File.ReadAllText(aside[0]));
                File.Delete(aside[0]);

                // Next time it works.
                File.WriteAllText(legacyPath, legacy);
                File.SetLastWriteTimeUtc(a, new DateTime(t, DateTimeKind.Utc));
                var ok = MetadataMigration.TryMigrate(folder);
                Check.True(ok.Migrated);
                Check.Null(ok.Problem);
                Check.Equal("keep", SaveSession.Open(folder, warnings.Add).Catalog.NameOf("001"));
            }));
        }

        private static void MoreTests(Action<string, Action> test)
        {
            test("open_after_failed_migration_does_not_create_synced_file", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var a = TestStamps.PutSave(folder, "001", 100, 1);
                var t = File.GetLastWriteTimeUtc(a).Ticks;
                WriteLegacy(folder, "{\"Version\":1,\"Categories\":[{\"Id\":\"c\",\"Name\":\"主線\",\"Order\":0}],\"Slots\":{\"001\":{\"Name\":\"keep\",\"CategoryId\":\"c\",\"Length\":100,\"WriteTicks\":" + t + "}}}");
                var path = SaveSession.IndexPathFor(folder);

                // A save is locked while the game starts: the migration cannot hash it and fails.
                using (new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var session = SaveSession.Open(folder, warnings.Add);
                    Check.True(session.Migration.Problem != null);
                    Check.False(session.Migration.Migrated);
                    Check.False(session.CanPersist);
                    Check.False(File.Exists(path));
                    session.Catalog.CreateCategory("x");
                    Check.False(session.Persist());
                    Check.False(session.Persist());
                    Check.Equal(1, warnings.Count(w => w.Contains("not migrated yet") && w.Contains("will not be saved")));
                }
                Check.False(File.Exists(path));
                Check.True(File.Exists(SaveSession.LegacyIndexPathFor(folder)));
                Check.Equal("Save_001.dat", string.Join(",", TestStamps.RootFileNames(folder)));

                // Unlocked: the next migration succeeds and the session persists normally.
                var result = MetadataMigration.TryMigrate(folder);
                Check.True(result.Migrated);
                Check.Null(result.Problem);
                var reopened = SaveSession.Open(folder, warnings.Add);
                Check.True(reopened.CanPersist);
                Check.Equal("keep", reopened.Catalog.NameOf("001"));
                Check.Equal("c", reopened.Catalog.CategoryOf("001"));
                reopened.Catalog.SetName("001", "renamed");
                Check.True(reopened.Persist());
                Check.Equal("renamed", new SaveIndexStore(path, SaveSession.LocalFolderFor(folder)).Load().Slots["001"].Name);
            }));

            test("open_migrates_a_v1_index_by_itself", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var a = TestStamps.PutSave(folder, "001", 100, 1);
                var t = File.GetLastWriteTimeUtc(a).Ticks;
                WriteLegacy(folder, "{\"Version\":1,\"Categories\":[],\"Slots\":{\"001\":{\"Name\":\"keep\",\"Length\":100,\"WriteTicks\":" + t + "}}}");

                var session = SaveSession.Open(folder, warnings.Add);

                Check.True(session.Migration.Migrated);
                Check.Equal(1, session.Migration.Kept);
                Check.True(session.CanPersist);
                Check.Equal("keep", session.Catalog.NameOf("001"));
                Check.True(File.Exists(SaveSession.IndexPathFor(folder)));
                Check.False(File.Exists(SaveSession.LegacyIndexPathFor(folder)));
                Check.Equal(0, warnings.Count);
            }));

            test("open_with_a_corrupt_v1_index_moves_it_aside_and_works_normally", () => TestStamps.WithFolder((folder, warnings) =>
            {
                TestStamps.PutSave(folder, "001", 100, 1);
                WriteLegacy(folder, "{not json");

                var session = SaveSession.Open(folder, warnings.Add);

                Check.True(session.CanPersist);
                Check.False(File.Exists(SaveSession.IndexPathFor(folder))); // nothing to hold yet: created at the first edit
                Check.Equal(1, Directory.GetFiles(SaveSession.LocalFolderFor(folder), "index.json.corrupt-*").Length);
                Check.True(warnings.Any(w => w.Contains("corrupt")));
                session.Catalog.CreateCategory("Main");
                Check.True(session.Persist());
                Check.True(File.Exists(SaveSession.IndexPathFor(folder)));
            }));
        }

        private static void SummaryTests(Action<string, Action> test)
        {
            test("migration_summary_describes_a_completed_migration_only", () =>
            {
                TestStamps.WithFolder((folder, warnings) =>
                {
                    var a = TestStamps.PutSave(folder, "001", 100, 1);
                    TestStamps.PutSave(folder, "002", 120, 2);
                    var t = File.GetLastWriteTimeUtc(a).Ticks;
                    // 001 matches its record; 002's recorded write time is wrong, so v1 itself would have dropped it.
                    WriteLegacy(folder, "{\"Version\":1,\"Categories\":[],\"Slots\":{\"001\":{\"Name\":\"keep\",\"Length\":100,\"WriteTicks\":" + t + "},\"002\":{\"Name\":\"stale\",\"Length\":120,\"WriteTicks\":1}}}");

                    var first = SaveSession.Open(folder, warnings.Add);
                    var summary = first.Migration.Summary;
                    Check.True(summary != null);
                    Check.True(summary.Contains("1 kept"));
                    Check.True(summary.Contains("1 dropped"));
                    Check.True(summary.Contains("LoMSaveManager.json"));
                    Check.True(summary.Contains("index.json.migrated-"));

                    // Nothing left to migrate on the next start: no line.
                    Check.True(SaveSession.Open(folder, warnings.Add).Migration.Summary == null);
                });

                TestStamps.WithFolder((folder, warnings) =>
                {
                    Check.True(SaveSession.Open(folder, warnings.Add).Migration.Summary == null);
                });

                // The synced file was written but the old one could not be renamed: Migrated, with a Problem. The line must
                // not claim the old file was kept under a .migrated- name.
                TestStamps.WithFolder((folder, warnings) =>
                {
                    var a = TestStamps.PutSave(folder, "001", 100, 1);
                    var t = File.GetLastWriteTimeUtc(a).Ticks;
                    WriteLegacy(folder, "{\"Version\":1,\"Categories\":[],\"Slots\":{\"001\":{\"Name\":\"keep\",\"Length\":100,\"WriteTicks\":" + t + "}}}");
                    // A reader that shares reads but not deletes: ReadAllBytes still works, the rename does not.
                    using (new FileStream(SaveSession.LegacyIndexPathFor(folder), FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var result = MetadataMigration.TryMigrate(folder);
                        Check.True(result.Migrated);
                        Check.True(result.Problem != null);
                        Check.True(File.Exists(SaveSession.IndexPathFor(folder)));
                        Check.True(File.Exists(SaveSession.LegacyIndexPathFor(folder)));
                        var summary = result.Summary;
                        Check.True(summary != null);
                        Check.True(summary.Contains("1 kept"));
                        Check.True(summary.Contains("could not be renamed"));
                        Check.False(summary.Contains("migrated-"));
                    }
                });

                // A failed migration is reported through Problem (and the warn callback), not as a success line.
                TestStamps.WithFolder((folder, warnings) =>
                {
                    TestStamps.PutSave(folder, "001", 100, 1);
                    WriteLegacy(folder, "{not json");
                    var session = SaveSession.Open(folder, warnings.Add);
                    Check.True(session.Migration.Problem != null);
                    Check.True(session.Migration.Summary == null);
                });
            });
        }

        private static void WriteLegacy(string folder, string json)
        {
            var path = SaveSession.LegacyIndexPathFor(folder);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }

        private static void AssertNothingWritten(string folder, string legacy)
        {
            Check.False(File.Exists(SaveSession.IndexPathFor(folder)));
            Check.Equal(legacy, File.ReadAllText(SaveSession.LegacyIndexPathFor(folder)));
            Check.Equal("index.json", string.Join(",", Directory.GetFiles(SaveSession.LocalFolderFor(folder)).Select(Path.GetFileName)));
            Check.Equal("Save_001.dat", string.Join(",", TestStamps.RootFileNames(folder)));
        }
    }
}
