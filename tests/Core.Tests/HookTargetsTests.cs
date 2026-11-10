using System;
using System.Linq;

namespace LoMSaveManager.Tests
{
    internal static class HookTargetsTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("validate_reports_every_missing_member_and_passes_when_complete", () =>
            {
                var complete = new[]
                {
                    new HookTarget("SaveSystem.SaveGameData(String)", true),
                    new HookTarget("SaveSystem.DeleteSaveData(String)", true),
                    new HookTarget("LoadGamePanel._saveSlots", true),
                };
                Check.Equal(0, HookTargets.Validate(complete).Count);

                var partial = new[]
                {
                    new HookTarget("SaveSystem.SaveGameData(String)", true),
                    new HookTarget("SaveSystem.GetUserFolder()", false),
                    new HookTarget("LoadGamePanel._saveSlots", true),
                    new HookTarget("LoadSlotPanel.Setup()", false),
                    new HookTarget("SaveSlotPanel.SelectButton()", false),
                };
                var missing = HookTargets.Validate(partial);
                Check.Equal("SaveSystem.GetUserFolder(),LoadSlotPanel.Setup(),SaveSlotPanel.SelectButton()", string.Join(",", missing));

                // A catalog with nothing in it has nothing missing, and a single miss is still a miss.
                Check.Equal(0, HookTargets.Validate(new HookTarget[0]).Count);
                Check.Equal(1, HookTargets.Validate(new[] { new HookTarget("X", false) }).Count);
            });

            test("validate_does_not_stop_at_the_first_missing_member_and_rejects_null", () =>
            {
                var all = Enumerable.Range(0, 10).Select(i => new HookTarget("M" + i, false));
                Check.Equal(10, HookTargets.Validate(all).Count);
                Check.Throws<ArgumentNullException>(() => HookTargets.Validate(null));
            });

            test("disabled_message_names_the_missing_members", () =>
            {
                var message = HookTargets.DisabledMessage(new[] { "SaveSystem.GetUserFolder()", "LoadSlotPanel.Setup()" });
                Check.Equal("LoM Save Manager disabled: missing SaveSystem.GetUserFolder(), LoadSlotPanel.Setup()", message);
                Check.Throws<ArgumentException>(() => HookTargets.DisabledMessage(new string[0]));
                Check.Throws<ArgumentNullException>(() => HookTargets.DisabledMessage(null));
            });
        }
    }
}
