using System;
using System.Collections.Generic;

namespace LoMSaveManager.Tests
{
    internal static class Program
    {
        private static int _count;
        private static int _failures;

        private static int Main(string[] args)
        {
            SlotKeyTests.Run(Test);
            SaveIndexStoreTests.Run(Test);
            SaveCatalogTests.Run(Test);
            StringsTests.Run(Test);
            HookTargetsTests.Run(Test);
            SlotFilesTests.Run(Test);
            OwnWriteTests.Run(Test);
            SaveSessionTests.Run(Test);
            SlotListPlanTests.Run(Test);
            CategoryControllerTests.Run(Test);
            PendingCategoryTests.Run(Test);
            FocusRulesTests.Run(Test);
            LocalStampCacheTests.Run(Test);
            CloudSyncTests.Run(Test);
            MetadataMigrationTests.Run(Test);
            HardeningTests.Run(Test);

            Console.WriteLine(_count - _failures + " passed, " + _failures + " failed");
            return _failures == 0 ? 0 : 1;
        }

        private static void Test(string name, Action action)
        {
            _count++;
            try
            {
                action();
                Console.WriteLine("PASS " + name);
            }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine("FAIL " + name + ": " + ex.Message);
            }
        }
    }

    internal static class Check
    {
        public static void True(bool value)
        {
            if (!value) throw new Exception("expected true");
        }

        public static void False(bool value)
        {
            if (value) throw new Exception("expected false");
        }

        public static void Null(object value)
        {
            if (value != null) throw new Exception("expected null, got " + value);
        }

        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception("expected " + expected + ", got " + actual);
        }

        public static void SetEqual(IEnumerable<string> expected, IEnumerable<string> actual)
        {
            var want = new HashSet<string>(expected);
            var got = new HashSet<string>(actual);
            if (!want.SetEquals(got))
                throw new Exception("expected {" + string.Join(",", want) + "}, got {" + string.Join(",", got) + "}");
        }

        public static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("expected " + typeof(T).Name);
        }
    }
}
