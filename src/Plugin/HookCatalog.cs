using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace LoMSaveManager
{
    /// <summary>
    /// Every game member the mod relies on, looked up by name and checked against its expected signature.
    /// Types are found by name rather than with typeof(), so a game build that renamed or removed one is reported
    /// as a missing member (see <see cref="HookTargets"/>) instead of failing with a load error. Nothing is patched
    /// unless all of them are present.
    /// </summary>
    internal sealed class HookCatalog
    {
        private const string Void = "System.Void";
        private const string Text = "System.String";
        private const string Int = "System.Int32";
        private const string GameObject = "UnityEngine.GameObject";
        private const string Transform = "UnityEngine.Transform";
        private const string Button = "UnityEngine.UI.Button";
        private const string Texts = "UnityEngine.UI.Text[]";
        private const string GameSave = "Mortal.Core.GameSave";
        private const string TextType = "UnityEngine.UI.Text";
        private const string CommonPanel = "Mortal.Core.CommonPanel";
        private const string InputButtonType = "Mortal.Core.InputButton";

        private const string SaveSystemType = "Mortal.Core.SaveSystem";
        private const string LoadGamePanelType = "Mortal.Core.LoadGamePanel";
        private const string SaveGamePanelType = "Mortal.Core.SaveGamePanel";
        private const string LoadSlotPanelType = "Mortal.Core.LoadSlotPanel";
        private const string SaveSlotPanelType = "Mortal.Core.SaveSlotPanel";
        private const string AutoSaveSlotPanelType = "Mortal.Core.AutoSaveSlotPanel";
        private const string RecentSaveSlotPanelType = "Mortal.Core.RecentSaveSlotPanel";
        private const string LocalizationManagerType = "Mortal.Core.LocalizationManager";
        private const string LocaleResolverType = "Mortal.Core.ILocaleResolver";
        private const string LoadGameSlotType = "Mortal.Core.LoadGameSlot";
        private const string Bool = "System.Boolean";

        private const BindingFlags Declared =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private enum Kind { Method, Field, Property, Event }

        private sealed class Spec
        {
            public string TypeName;
            public Kind Kind;
            public string Name;
            public string ValueType;   // return type, field type, property type or event handler type (full name)
            public string[] Parameters; // methods only (full names)
            public bool Static;         // a static member (instance otherwise)
            public bool Setter;         // properties: the mod also sets it
        }

        // Every member the hooks patch, read through reflection or call. A game that lacks any of them is left untouched.
        // LoadGamePanel has the recent/auto-save members; SaveGamePanel does not (it has no sidebar of its own).
        private static readonly Spec[] Required =
        {
            Method(SaveSystemType, "SaveGameData", Void, Text),
            Method(SaveSystemType, "DeleteSaveData", Void, Text),
            Method(SaveSystemType, "NewGameData", Void),
            Method(SaveSystemType, "GetUserFolder", Text),
            Property(SaveSystemType, "CurrentSlot", Text),

            Method(LoadGamePanelType, "CreateSlot", Void),
            Method(LoadGamePanelType, "UpdateSlot", Void),
            Method(LoadGamePanelType, "SetButtonNavigation", Void),
            Method(LoadGamePanelType, "OnPanelOpen", Void),
            Method(LoadGamePanelType, "ConfirmDeleteSlot", Void),
            Field(LoadGamePanelType, "_slotPrefab", GameObject),
            Field(LoadGamePanelType, "_slotContainer", Transform),
            Field(LoadGamePanelType, "_saveSlots", LoadSlotPanelType + "[]"),
            Field(LoadGamePanelType, "_recentSavePanel", "Mortal.Core.RecentSaveSlotPanel"),
            Field(LoadGamePanelType, "_autoSaveSlot", "Mortal.Core.AutoSaveSlotPanel[]"),
            Field(LoadGamePanelType, "_deletePanel", CommonPanel),
            Field(LoadGamePanelType, "_closeButton", InputButtonType),
            Field(LoadGamePanelType, "_currentDeleteSlot", Text),

            Method(SaveGamePanelType, "CreateSlot", Void),
            Method(SaveGamePanelType, "UpdateSlot", Void),
            Method(SaveGamePanelType, "SetButtonNavigation", Void),
            Method(SaveGamePanelType, "OnPanelOpen", Void),
            Method(SaveGamePanelType, "SaveData", Void, Text),
            Method(SaveGamePanelType, "ConfirmSaveSlot", Void),
            Field(SaveGamePanelType, "_slotPrefab", GameObject),
            Field(SaveGamePanelType, "_slotContainer", Transform),
            Field(SaveGamePanelType, "_saveSlots", SaveSlotPanelType + "[]"),
            Field(SaveGamePanelType, "_confirmPanel", CommonPanel),
            Field(SaveGamePanelType, "_closeInputButton", InputButtonType),
            Field(SaveGamePanelType, "_currentConfirmSlot", Text),

            Method(LoadSlotPanelType, "SetSlot", Void, Int),
            Method(LoadSlotPanelType, "Setup", Void),
            Property(LoadSlotPanelType, "SlotButton", Button),
            Property(LoadSlotPanelType, "DeleteButton", Button),
            Field(LoadSlotPanelType, "_currentData", GameSave),
            Field(LoadSlotPanelType, "_titleText", Texts),
            Field(LoadSlotPanelType, "_timeText", Texts),
            Field(LoadSlotPanelType, "_slotText", TextType),
            Field(LoadSlotPanelType, "_focusObj", GameObject),
            Field(LoadSlotPanelType, "_newGamePlusIcon", GameObject),
            Field(LoadSlotPanelType, "_loadGameSlot", "Mortal.Core.LoadGameSlot"),

            Method(SaveSlotPanelType, "SetSlot", Void, Int),
            Method(SaveSlotPanelType, "Setup", Void),
            Method(SaveSlotPanelType, "SelectButton", Void),
            Property(SaveSlotPanelType, "SlotButton", Button),
            Field(SaveSlotPanelType, "_currentData", GameSave),
            Field(SaveSlotPanelType, "_titleText", Texts),
            Field(SaveSlotPanelType, "_timeText", Texts),
            Field(SaveSlotPanelType, "_slotText", TextType),
            Field(SaveSlotPanelType, "_focusObj", GameObject),
            Field(SaveSlotPanelType, "_newGamePlusIcon", GameObject),

            // The UI pauses the game's hotkey buttons (Esc closes the panel, Enter confirms) while a dialog is open.
            Field(InputButtonType, "_active", "System.Boolean"),

            Method(AutoSaveSlotPanelType, "Setup", Void),
            Property(AutoSaveSlotPanelType, "SlotButton", Button),
            Property(AutoSaveSlotPanelType, "DeleteButton", Button),
            Method(RecentSaveSlotPanelType, "Setup", Void),
            Property(RecentSaveSlotPanelType, "SlotButton", Button),

            // Called directly by the UI: the game's language (texts, OnLanguageChanged refresh), a broken load row
            // marked not-empty so its delete hotkey works, and the dialogs pausing the panel's hotkey buttons.
            Static(Property(LocalizationManagerType, "Instance", LocalizationManagerType)),
            Property(LocalizationManagerType, "LocaleResolver", LocaleResolverType),
            Static(Event(LocalizationManagerType, "OnLanguageChanged", "System.Action")),
            Method(LocaleResolverType, "GetCurrentLang", Text),
            Method(LocaleResolverType, "GetString", Text, Text),
            WithSetter(Property(LoadGameSlotType, "IsEmpty", Bool)),
            Method(InputButtonType, "SetActive", Void, Bool),
        };

        private readonly Dictionary<string, MemberInfo> _resolved = new Dictionary<string, MemberInfo>();

        private HookCatalog(IReadOnlyList<HookTarget> targets)
        {
            Targets = targets;
        }

        /// <summary>One entry per required member, found or not, for <see cref="HookTargets.Validate"/>.</summary>
        public IReadOnlyList<HookTarget> Targets { get; }

        /// <summary>SaveSystem.SaveGameData(string); valid only when every target was found.</summary>
        public MethodInfo SaveGameData
        {
            get { return Resolved<MethodInfo>(SaveSystemType, "SaveGameData"); }
        }

        /// <summary>SaveSystem.DeleteSaveData(string); valid only when every target was found.</summary>
        public MethodInfo DeleteSaveData
        {
            get { return Resolved<MethodInfo>(SaveSystemType, "DeleteSaveData"); }
        }

        /// <summary>SaveSystem.NewGameData(); valid only when every target was found.</summary>
        public MethodInfo NewGameData
        {
            get { return Resolved<MethodInfo>(SaveSystemType, "NewGameData"); }
        }

        /// <summary>A validated LoadGamePanel method (by name); valid only when every target was found.</summary>
        public MethodInfo LoadPanelMethod(string name)
        {
            return Resolved<MethodInfo>(LoadGamePanelType, name);
        }

        /// <summary>A validated SaveGamePanel method (by name); valid only when every target was found.</summary>
        public MethodInfo SavePanelMethod(string name)
        {
            return Resolved<MethodInfo>(SaveGamePanelType, name);
        }

        /// <summary>The private SaveSystem.GetUserFolder(); valid only when every target was found.</summary>
        public MethodInfo GetUserFolder
        {
            get { return Resolved<MethodInfo>(SaveSystemType, "GetUserFolder"); }
        }

        /// <summary>Looks every member up. Never throws: a member that cannot be examined counts as missing.</summary>
        public static HookCatalog Resolve()
        {
            var targets = new List<HookTarget>();
            var resolved = new Dictionary<string, MemberInfo>();
            foreach (var spec in Required)
            {
                var member = Find(spec);
                targets.Add(new HookTarget(DisplayName(spec), member != null));
                if (member != null) resolved[Key(spec.TypeName, spec.Name)] = member;
            }

            var catalog = new HookCatalog(targets);
            foreach (var pair in resolved) catalog._resolved[pair.Key] = pair.Value;
            return catalog;
        }

        private T Resolved<T>(string typeName, string name) where T : MemberInfo
        {
            MemberInfo member;
            if (!_resolved.TryGetValue(Key(typeName, name), out member))
                throw new InvalidOperationException("Game member was not resolved: " + typeName + "." + name);
            return (T)member;
        }

        private static MemberInfo Find(Spec spec)
        {
            try
            {
                var type = AccessTools.TypeByName(spec.TypeName);
                if (type == null) return null;

                switch (spec.Kind)
                {
                    case Kind.Field:
                    {
                        var field = type.GetField(spec.Name, Flags(spec));
                        return field != null && field.FieldType.FullName == spec.ValueType ? field : null;
                    }
                    case Kind.Property:
                    {
                        var property = type.GetProperty(spec.Name, Flags(spec));
                        return property != null && property.PropertyType.FullName == spec.ValueType
                            && property.GetGetMethod(true) != null
                            && (!spec.Setter || property.GetSetMethod(true) != null) ? property : null;
                    }
                    case Kind.Event:
                    {
                        var e = type.GetEvent(spec.Name, Flags(spec));
                        return e != null && e.EventHandlerType != null && e.EventHandlerType.FullName == spec.ValueType
                            && e.GetAddMethod(true) != null && e.GetRemoveMethod(true) != null ? e : null;
                    }
                    default:
                    {
                        // Interface methods (ILocaleResolver) are abstract by nature; class methods must have a body.
                        var matches = type.GetMethods(Flags(spec)).Where(method =>
                            method.Name == spec.Name
                            && (type.IsInterface || !method.IsAbstract) && !method.ContainsGenericParameters
                            && method.ReturnType.FullName == spec.ValueType
                            && method.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(spec.Parameters)).ToArray();
                        return matches.Length == 1 ? matches[0] : null;
                    }
                }
            }
            catch (Exception)
            {
                // A type that cannot be loaded or examined is as good as missing.
                return null;
            }
        }

        private static BindingFlags Flags(Spec spec)
        {
            return spec.Static
                ? BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                : Declared;
        }

        private static string DisplayName(Spec spec)
        {
            var typeName = Short(spec.TypeName) + "." + spec.Name;
            return spec.Kind == Kind.Method
                ? typeName + "(" + string.Join(", ", spec.Parameters.Select(Short)) + ")"
                : typeName;
        }

        private static string Short(string fullName)
        {
            return fullName.Substring(fullName.LastIndexOf('.') + 1);
        }

        private static string Key(string typeName, string name)
        {
            return typeName + "::" + name;
        }

        private static Spec Method(string type, string name, string returns, params string[] parameters)
        {
            return new Spec { TypeName = type, Kind = Kind.Method, Name = name, ValueType = returns, Parameters = parameters };
        }

        private static Spec Field(string type, string name, string fieldType)
        {
            return new Spec { TypeName = type, Kind = Kind.Field, Name = name, ValueType = fieldType };
        }

        private static Spec Property(string type, string name, string propertyType)
        {
            return new Spec { TypeName = type, Kind = Kind.Property, Name = name, ValueType = propertyType };
        }

        private static Spec Event(string type, string name, string handlerType)
        {
            return new Spec { TypeName = type, Kind = Kind.Event, Name = name, ValueType = handlerType };
        }

        private static Spec Static(Spec spec)
        {
            spec.Static = true;
            return spec;
        }

        private static Spec WithSetter(Spec spec)
        {
            spec.Setter = true;
            return spec;
        }
    }
}
