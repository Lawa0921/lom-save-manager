using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mortal.Core;
using UnityEngine;

namespace LoMSaveManager
{
    /// <summary>
    /// Reaches into the game for what it does not expose: SaveSystem's private GetUserFolder(), and the SaveSystem
    /// object itself. Only call this after <see cref="HookCatalog"/> has found every member.
    /// </summary>
    internal static class GameAccess
    {
        private static MethodInfo _getUserFolder;

        public static void Bind(HookCatalog catalog)
        {
            _getUserFolder = catalog.GetUserFolder;
        }

        /// <summary>
        /// The loaded SaveSystem asset, or null while it is not loaded (for example during plugin startup).
        /// Looks it up like SaveSystem.Instance does but stays silent: the game's own getter logs an error when it finds nothing.
        /// </summary>
        public static object FindSaveSystem()
        {
            return Resources.FindObjectsOfTypeAll<SaveSystem>().FirstOrDefault();
        }

        /// <summary>
        /// The account's save folder, <c>persistentDataPath/&lt;GetUserFolder()&gt;</c> (the Steam ID folder under
        /// Steam). Fails with a reason, rather than throwing, while Steam is not initialised yet.
        /// </summary>
        public static bool TryResolveUserFolder(object saveSystem, out string folder, out string problem)
        {
            folder = null;
            problem = null;
            if (_getUserFolder == null)
            {
                problem = "the game's save folder lookup was not bound";
                return false;
            }

            try
            {
                var userFolder = _getUserFolder.Invoke(saveSystem, null) as string;
                if (string.IsNullOrEmpty(userFolder))
                {
                    problem = "the game returned no user folder";
                    return false;
                }

                folder = Path.GetFullPath(Path.Combine(Application.persistentDataPath, userFolder));
                return true;
            }
            catch (TargetInvocationException ex)
            {
                // GetUserFolder asks Steamworks, which throws until Steam is up.
                problem = "the game cannot name its save folder yet (" + (ex.InnerException ?? ex).Message + ")";
                return false;
            }
            catch (Exception ex)
            {
                problem = "the save folder could not be resolved (" + ex.Message + ")";
                return false;
            }
        }
    }
}
