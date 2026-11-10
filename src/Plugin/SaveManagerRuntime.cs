using System;
using System.Collections.Generic;

namespace LoMSaveManager
{
    /// <summary>
    /// The plugin's shared state: the <see cref="SaveSession"/> for the player's save folder and the few values the
    /// hooks and panels pass around. The save folder is only known once the game can name it (Steam up, SaveSystem
    /// loaded), which may be after plugin startup, so the session is opened lazily by the first caller that can.
    /// All members run on Unity's main thread.
    /// </summary>
    public sealed class SaveManagerRuntime
    {
        // Opening the session that throws is retried a few times, then left alone (the game itself is never affected).
        private const int MaxFailedOpens = 3;

        private SaveSession _session;
        private int _failedOpens;
        private string _lastProblem;

        private SaveManagerRuntime()
        {
        }

        public static SaveManagerRuntime Current { get; } = new SaveManagerRuntime();

        /// <summary>True once every game member was found and the hooks are installed; panels only customize the UI when this is true.</summary>
        public bool Enabled { get; internal set; }

        /// <summary>The category the save panel currently has selected: where a brand-new save is filed. Null means uncategorized.</summary>
        public string PendingCategoryId
        {
            get { return Pending.Id; }
        }

        /// <summary>Set by the open load/save panel, cleared when that panel closes (see <see cref="PendingCategory"/>).</summary>
        public PendingCategory Pending { get; } = new PendingCategory();

        public bool IsReady
        {
            get { return _session != null; }
        }

        public SaveCatalog Catalog
        {
            get { return Session.Catalog; }
        }

        public string UserFolder
        {
            get { return Session.UserFolder; }
        }

        internal SaveSession Session
        {
            get
            {
                if (!EnsureReady())
                    throw new InvalidOperationException("The save manager is not ready" + (_lastProblem == null ? "." : ": " + _lastProblem));
                return _session;
            }
        }

        /// <summary>Writes LoMSaveManager.json (a no-op, with one warning, when it must not be written this session: unreadable, from a newer version, or a v1 migration still pending).</summary>
        public void Persist()
        {
            Session.Persist();
        }

        /// <summary>
        /// Whether names and categories can be saved this session (false when LoMSaveManager.json is from a newer version,
        /// unreadable or too large, or a v1 migration is pending). The panels refuse edits while it is false.
        /// </summary>
        public bool CanPersist
        {
            get { return _session != null && _session.CanPersist; }
        }

        /// <summary>The manual saves on disk (Save_NNN.dat only) with their stamps.</summary>
        public IDictionary<string, SaveStamp> ScanManualSaves()
        {
            return Session.ScanManualSaves();
        }

        /// <summary>The slot keys taken by a .dat, .dat.tmp or .dat.bak file.</summary>
        public ISet<string> OccupiedKeys()
        {
            return Session.OccupiedKeys();
        }

        /// <summary>
        /// Opens the session if it is not open yet: migrates a v1 LoMSaveManager/index.json if there is one, reads LoMSaveManager.json,
        /// reconciles it with the save folder and writes it back.
        /// Returns false (quietly) while the game cannot name its save folder yet; a hook passes the SaveSystem it is
        /// running on, other callers pass null and the loaded asset is looked up.
        /// </summary>
        public bool EnsureReady(object saveSystem = null)
        {
            if (_session != null) return true;
            if (_failedOpens >= MaxFailedOpens) return false;

            try
            {
                var system = saveSystem ?? GameAccess.FindSaveSystem();
                if (system == null)
                {
                    _lastProblem = "the game's save system is not loaded yet";
                    return false;
                }

                string folder, problem;
                if (!GameAccess.TryResolveUserFolder(system, out folder, out problem))
                {
                    _lastProblem = problem;
                    return false;
                }

                _session = SaveSession.Open(folder, SaveManagerPlugin.LogWarn);
                _lastProblem = null;
                LogStartup(folder);
                return true;
            }
            catch (Exception ex)
            {
                _failedOpens++;
                _lastProblem = ex.Message;
                SaveManagerPlugin.LogWarn("Could not open the save index (" + ex.GetType().Name + ": " + ex.Message + ")"
                    + (_failedOpens >= MaxFailedOpens ? "; giving up for this session." : "."));
                return false;
            }
        }

        /// <summary>
        /// The one startup log: what the migration did, and where the index is. A failed migration and a read-only
        /// session were already warned about by <see cref="SaveSession.Open"/> (once, with the reason), so only the
        /// success line and a short read-only mark are added here.
        /// </summary>
        private void LogStartup(string folder)
        {
            var summary = _session.Migration == null ? null : _session.Migration.Summary;
            if (summary != null) SaveManagerPlugin.LogInfo(summary);
            SaveManagerPlugin.LogInfo("Save index ready in " + SaveSession.IndexPathFor(folder)
                + (_session.CanPersist ? "" : " (read-only this session; see the warning above)"));
        }
    }
}
