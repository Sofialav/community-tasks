// =====================================================================
//  Community Tasks — Streamer.bot backend
//
//  Paste this whole file into ONE "Execute C# Code" sub-action inside an
//  action named "TaskStore", then attach your !task, !done and !clear
//  commands to that same action. No "Set Argument" sub-actions needed.
//
//  Everything you might want to change is in the SETTINGS block below.
// =====================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

// [JsonProperty(Required = Required.Always)] makes loading fail if saved data
// is missing a field (e.g. "{}"), instead of silently using default values.
public class CommunityTask
{
    [JsonProperty(Required = Required.Always)] public int Id { get; set; }
    [JsonProperty(Required = Required.Always)] public string User { get; set; }   // display name
    public string UserId { get; set; }                                             // Twitch user id (ownership)
    [JsonProperty(Required = Required.Always)] public string Text { get; set; }
    [JsonProperty(Required = Required.Always)] public bool Done { get; set; }
    [JsonProperty(Required = Required.Always)] public DateTime Created { get; set; }
}

// What the overlay receives (no user ids)
public class OverlayTask
{
    public int Id { get; set; }
    public string User { get; set; }
    public string Text { get; set; }
    public bool Done { get; set; }
    public DateTime Created { get; set; }
}

public class Caller
{
    public string Name = "";    // display name
    public string Id = "";
}

public class TaskDatabase
{
    [JsonProperty(Required = Required.Always)] public int Version { get; set; } = 1;
    [JsonProperty(Required = Required.Always)] public int NextId { get; set; } = 1;
    [JsonProperty(Required = Required.Always)] public List<CommunityTask> Tasks { get; set; } = new List<CommunityTask>();
}

public class OverlayData
{
    public string Source { get; set; } = "CommunityTasks";
    public string Event { get; set; }
    public int? TaskId { get; set; }
    public List<OverlayTask> Tasks { get; set; } = new List<OverlayTask>();
}

public class CPHInline
{
    // ============================ SETTINGS ============================

    // Limits
    private const int MAX_TASK_LENGTH       = 90;  // characters per task
    private const int MAX_TOTAL_TASKS       = 50;  // tasks kept on the list
    private const int MAX_USER_ACTIVE_TASKS = 3;   // unfinished tasks per viewer

    // Who may use !clear (whole list, "completed", or a single task id).
    // The broadcaster can always use it. Set to true to let moderators use it too.
    private const bool CLEAR_ALLOW_MODERATORS = false;

    // Reply in chat after each command
    private const bool CHAT_REPLIES = true;
    // Start replies with @username
    private const bool MENTION_USER = true;

    // Command words -> operations. If you rename or translate your commands
    // in Streamer.bot (e.g. !tarefa), add the new word here. No "!" needed.
    private static readonly Dictionary<string, string> COMMAND_MAP =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "task",  "add" },      { "addtask", "add" },
        { "done",  "complete" }, { "finish",  "complete" },
        { "clear", "reset" },    { "cleartasks", "reset" },
    };

    // ========================== END SETTINGS ==========================

    private const string DATABASE_NAME = "CommunityTasks"; // persisted global variable

    private const string OP_ADD      = "add";
    private const string OP_COMPLETE = "complete";
    private const string OP_GET      = "get";
    private const string OP_RESET    = "reset";

    // Streamer.bot can run the same action several times in parallel when
    // chat is busy. This lock stops two commands from overwriting each other.
    private static readonly object DbLock = new object();

    private string replyTo = "";

    public bool Execute()
    {
        lock (DbLock)
        {
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                CPH.LogError("[CommunityTasks] " + ex);
            }
        }
        return true;
    }

    private void Run()
    {
        string operation = ResolveOperation();
        Caller caller    = GetCaller();
        string input     = Arg("rawInput");
        replyTo = caller.Name;

        string loadError;
        TaskDatabase db = LoadDatabase(out loadError);
        if (db == null)
        {
            // Recovery: a plain "!clear" by the broadcaster (or a mod, if allowed)
            // backs up the unreadable data to a separate variable, then starts fresh.
            if (operation == OP_RESET && CanClear(caller) && FirstWord(ArgOr("filter", input)).Length == 0)
            {
                CPH.LogError("[CommunityTasks] " + loadError);
                RecoverByClearing();
                return;
            }

            // Otherwise stored data is kept exactly as it is; nothing is saved or broadcast.
            CPH.LogError("[CommunityTasks] " + loadError + " Operation '" + operation +
                         "' was cancelled and the saved data was left untouched.");
            if (operation != OP_GET)
                Reply("⚠️ The task list couldn't be loaded, so nothing was changed. The streamer can check the Streamer.bot log, or use !clear to back it up and start fresh.");
            return;
        }

        switch (operation)
        {
            case OP_ADD:
            {
                if (!IsIdentified(caller)) { CPH.LogWarn("[CommunityTasks] add refused: no user id."); return; }
                string text = Arg("text");
                if (text.Length == 0) text = input;
                AddTask(db, caller, text);
                break;
            }

            case OP_COMPLETE:
            {
                if (!IsIdentified(caller)) { CPH.LogWarn("[CommunityTasks] complete refused: no user id."); return; }
                string raw = Arg("taskId");
                if (raw.Length == 0) raw = input;
                raw = FirstWord(raw);

                // Empty = "finish my oldest task" (id 0). Otherwise the whole input must be a task number.
                int taskId = 0;
                if (raw.Length > 0 && !TryParseTaskId(raw, out taskId))
                {
                    Reply("❌ Use !done to finish your oldest task, or !done <task number> (e.g. !done 3).");
                    return;
                }
                CompleteTask(db, caller, taskId);   // 0 = no id given -> oldest task
                break;
            }

            case OP_GET:
                Broadcast(db, "sync", null);
                break;

            case OP_RESET:
            {
                if (!CanClear(caller))
                {
                    Reply(CLEAR_ALLOW_MODERATORS
                        ? "❌ Only the broadcaster and moderators can clear tasks."
                        : "❌ Only the broadcaster can clear tasks.");
                    return;
                }
                ResetTasks(db, ArgOr("filter", input));
                break;
            }

            default:
                CPH.LogWarn("[CommunityTasks] Unknown operation '" + operation +
                            "'. Add your command word to COMMAND_MAP or set an 'operation' argument.");
                break;
        }
    }

    // ---------------------------------------------------------------
    //  Operations
    // ---------------------------------------------------------------

    private void AddTask(TaskDatabase db, Caller caller, string text)
    {
        text = CleanText(text);

        if (text.Length == 0)
        {
            Reply("❌ Please describe your task, e.g. !task Study for 1 hour");
            return;
        }

        if (text.Length > MAX_TASK_LENGTH)
        {
            Reply("❌ Tasks are limited to " + MAX_TASK_LENGTH + " characters.");
            return;
        }

        int activeForUser = 0;
        foreach (CommunityTask t in db.Tasks)
        {
            if (t.Done || !Owns(t, caller))
                continue;

            activeForUser++;

            if (t.Text.Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                Reply("❌ You already have this task (#" + t.Id + ").");
                return;
            }
        }

        if (activeForUser >= MAX_USER_ACTIVE_TASKS)
        {
            Reply("❌ You already have " + MAX_USER_ACTIVE_TASKS + " active tasks. Finish one with !done first.");
            return;
        }

        if (db.NextId <= 0 || db.NextId == int.MaxValue)
        {
            CPH.LogError("[CommunityTasks] Can't add a task: NextId is " + db.NextId +
                         " and can't be increased. Use !clear to reset numbering.");
            Reply("❌ Task numbers have run out. The streamer needs to run !clear.");
            return;
        }

        if (!MakeRoomForNewTask(db))
        {
            Reply("❌ The community task list is full. Complete some tasks first!");
            return;
        }

        CommunityTask task = new CommunityTask
        {
            Id       = db.NextId++,
            User     = caller.Name,
            UserId   = caller.Id,
            Text     = text,
            Done    = false,
            Created = DateTime.UtcNow
        };

        db.Tasks.Add(task);
        if (!Save(db, "added", task.Id)) return;
        Reply("✅ Task #" + task.Id + " added! (" + db.Tasks.Count + "/" + MAX_TOTAL_TASKS + ")");
    }

    private void CompleteTask(TaskDatabase db, Caller caller, int taskId)
    {
        CommunityTask task = FindTask(db, caller, taskId);

        // Same reply whether the id doesn't exist or belongs to someone else,
        // so viewers can't probe other people's task numbers.
        if (task == null)
        {
            Reply(taskId > 0
                ? "❌ You don't have a task #" + taskId + "."
                : "❌ You don't have any active tasks. Add one with !task");
            return;
        }

        if (task.Done)
        {
            Reply("❌ Task #" + task.Id + " is already completed.");
            return;
        }

        task.Done = true;
        if (!Save(db, "completed", task.Id)) return;
        Reply("🎉 Task #" + task.Id + " completed!");
    }

    private void ResetTasks(TaskDatabase db, string filter)
    {
        filter = FirstWord(filter).ToLowerInvariant();

        // !clear  (no argument) -> everything
        if (filter.Length == 0)
        {
            db.Tasks.Clear();
            db.NextId = 1;
            if (!Save(db, "cleared", null)) return;
            Reply("🗑️ Community task list cleared.");
            return;
        }

        // !clear completed  -> remove finished tasks only
        if (filter == "completed" || filter == "done")
        {
            int removed = db.Tasks.RemoveAll(t => t.Done);
            if (!Save(db, "cleared", null)) return;
            Reply("🧹 Removed " + removed + " completed task(s).");
            return;
        }

        // !clear 7  -> remove a single task (moderation)
        int id;
        if (TryParseTaskId(filter, out id))
        {
            int removed = db.Tasks.RemoveAll(t => t.Id == id);
            if (removed == 0) { Reply("❌ Task #" + id + " doesn't exist."); return; }
            if (!Save(db, "removed", id)) return;
            Reply("🗑️ Task #" + id + " removed.");
            return;
        }

        // anything else -> explain, change nothing
        Reply("❌ Use !clear (everything), !clear completed, or !clear <task number>.");
    }

    // ---------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------

    private string ResolveOperation()
    {
        // 1) Explicit argument (old setup / overlay) wins.
        string op = Arg("operation").ToLowerInvariant();
        if (op.Length > 0)
            return op;

        // 2) Otherwise work it out from the command that triggered us.
        string cmd = Arg("command");
        if (cmd.Length == 0) cmd = Arg("commandName");
        cmd = cmd.Trim().TrimStart('!', '/', '.', '#').Trim();
        int space = cmd.IndexOf(' ');
        if (space > 0) cmd = cmd.Substring(0, space);

        string mapped;
        if (COMMAND_MAP.TryGetValue(cmd, out mapped))
            return mapped;

        return cmd.ToLowerInvariant();
    }

    private Caller GetCaller()
    {
        Caller c = new Caller();
        // Display name first (nicer on the overlay), then login name.
        c.Name = Arg("user");
        if (c.Name.Length == 0) c.Name = Arg("userName");
        c.Id = Arg("userId");
        return c;
    }

    private static bool IsIdentified(Caller c)
    {
        return c.Id.Length > 0 && c.Name.Length > 0;
    }

    // Ownership is decided only by the Twitch user id. Tasks saved by older
    // versions have no id, so nobody can finish them with !done; they stay on
    // the list until the broadcaster removes them with !clear.
    private static bool Owns(CommunityTask t, Caller c)
    {
        return !string.IsNullOrEmpty(t.UserId) && c.Id.Length > 0 && t.UserId == c.Id;
    }

    // Fails closed: anything that can't be positively identified is refused.
    // Privileges are looked up from Twitch through Streamer.bot (broadcaster
    // account, moderator list), never taken from flags in the arguments.
    private bool CanClear(Caller caller)
    {
        if (!IsIdentified(caller))
            return false;

        if (IsBroadcaster(caller.Id))
            return true;

        return CLEAR_ALLOW_MODERATORS && IsModerator(caller.Id);
    }

    private bool IsBroadcaster(string userId)
    {
        try
        {
            var bc = CPH.TwitchGetBroadcaster();
            return bc != null && !string.IsNullOrEmpty(bc.UserId) && bc.UserId.Trim() == userId;
        }
        catch (Exception ex)
        {
            CPH.LogWarn("[CommunityTasks] Couldn't read the Twitch broadcaster: " + ex.Message);
            return false;
        }
    }

    private bool IsModerator(string userId)
    {
        try
        {
            var mods = CPH.TwitchGetMods();
            if (mods == null) return false;
            foreach (var m in mods)
                if (m != null && !string.IsNullOrEmpty(m.UserId) && m.UserId.Trim() == userId)
                    return true;
            return false;
        }
        catch (Exception ex)
        {
            CPH.LogWarn("[CommunityTasks] Couldn't read the Twitch moderator list: " + ex.Message);
            return false;
        }
    }

    // Only the first word after the command counts; anything after it is
    // ignored ("!done 3 finally" -> "3", "!clear completed pls" -> "completed").
    private static string FirstWord(string raw)
    {
        raw = (raw ?? "").Trim();
        for (int i = 0; i < raw.Length; i++)
            if (char.IsWhiteSpace(raw[i]))
                return raw.Substring(0, i);
        return raw;
    }

    // The whole word must be a positive whole number, optionally prefixed
    // with "#": "3" and "#3" are valid; "", "#", "0", "-2", "3x" and "1.5" are not.
    private static bool TryParseTaskId(string raw, out int id)
    {
        id = 0;
        raw = (raw ?? "").Trim();
        if (raw.StartsWith("#")) raw = raw.Substring(1);

        int parsed;
        if (raw.Length == 0 ||
            !int.TryParse(raw, System.Globalization.NumberStyles.None,
                          System.Globalization.CultureInfo.InvariantCulture, out parsed) ||
            parsed <= 0)
            return false;

        id = parsed;
        return true;
    }

    private static string CleanText(string text)
    {
        text = (text ?? "").Trim();
        // Collapse whitespace, strip control characters
        System.Text.StringBuilder sb = new System.Text.StringBuilder(text.Length);
        bool lastSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
            }
            else if (!char.IsControl(c))
            {
                sb.Append(c);
                lastSpace = false;
            }
        }
        return sb.ToString().Trim();
    }

    private string ArgOr(string key, string fallback)
    {
        string v = Arg(key);
        return v.Length > 0 ? v : (fallback ?? "");
    }

    private string Arg(string key)
    {
        object v;
        if (args != null && args.TryGetValue(key, out v) && v != null)
            return v.ToString().Trim();
        return "";
    }

    private void Reply(string message)
    {
        if (!CHAT_REPLIES) return;
        if (MENTION_USER && replyTo.Length > 0)
            message = "@" + replyTo + " " + message;
        CPH.SendMessage(message);
    }

    private bool MakeRoomForNewTask(TaskDatabase db)
    {
        if (db.Tasks.Count < MAX_TOTAL_TASKS)
            return true;

        CommunityTask oldestCompleted = null;
        foreach (CommunityTask t in db.Tasks)
        {
            if (t.Done && (oldestCompleted == null || t.Created < oldestCompleted.Created))
                oldestCompleted = t;
        }

        if (oldestCompleted == null)
            return false;

        db.Tasks.Remove(oldestCompleted);
        return true;
    }

    private CommunityTask FindTask(TaskDatabase db, Caller caller, int taskId)
    {
        // !done (no id) -> this user's oldest unfinished task
        if (taskId <= 0)
        {
            CommunityTask oldest = null;
            foreach (CommunityTask t in db.Tasks)
            {
                if (t.Done || !Owns(t, caller)) continue;
                if (oldest == null || t.Created < oldest.Created) oldest = t;
            }
            return oldest;
        }

        // !done <id> -> only if this user owns it
        foreach (CommunityTask t in db.Tasks)
            if (t.Id == taskId && Owns(t, caller)) return t;

        return null;
    }

    // ---------------------------------------------------------------
    //  Storage + overlay broadcast
    // ---------------------------------------------------------------

    // Returns the stored database, a new empty one if nothing has been saved
    // yet, or null (with a reason) if saved data exists but can't be trusted.
    // It never writes: a broken or newer-format list is left for the streamer
    // to inspect instead of being replaced by an empty one.
    private TaskDatabase LoadDatabase(out string error)
    {
        error = null;
        string json;

        try
        {
            json = CPH.GetGlobalVar<string>(DATABASE_NAME, true);
        }
        catch (Exception ex)
        {
            error = "Couldn't read the global variable '" + DATABASE_NAME + "': " + ex.Message;
            return null;
        }

        // Nothing saved yet: start empty. It is written on the first change.
        if (string.IsNullOrWhiteSpace(json))
            return new TaskDatabase();

        TaskDatabase loaded;
        try
        {
            loaded = JsonConvert.DeserializeObject<TaskDatabase>(json);
        }
        catch (Exception ex)
        {
            error = "Saved task data in '" + DATABASE_NAME + "' couldn't be read (" + ex.Message + ").";
            return null;
        }

        string problem = ValidateDatabase(loaded);
        if (problem != null)
        {
            error = "Saved task data in '" + DATABASE_NAME + "' failed validation: " + problem;
            return null;
        }

        return loaded;
    }

    private static string ValidateDatabase(TaskDatabase db)
    {
        if (db == null)
            return "it is empty or null.";

        if (db.Version != 1)
            return "unsupported version " + db.Version + " (this script understands version 1). " +
                   "Update TaskStore.cs to the version that created this data.";

        if (db.Tasks == null)
            return "the task list is missing.";

        if (db.Tasks.Count > MAX_TOTAL_TASKS)
            return "the task list exceeds the configured limit of " +
                   MAX_TOTAL_TASKS + " entries.";

        Dictionary<int, bool> ids = new Dictionary<int, bool>();
        int maxId = 0;

        for (int i = 0; i < db.Tasks.Count; i++)
        {
            CommunityTask t = db.Tasks[i];
            if (t == null)
                return "entry " + i + " is empty.";
            if (t.Id <= 0)
                return "entry " + i + " has an invalid id (" + t.Id + ").";
            if (ids.ContainsKey(t.Id))
                return "task id #" + t.Id + " appears more than once.";
            ids[t.Id] = true;
            if (string.IsNullOrWhiteSpace(t.User))
                return "task #" + t.Id + " has no user.";
            if (string.IsNullOrWhiteSpace(t.Text))
                return "task #" + t.Id + " has no text.";
            if (t.Text.Length > MAX_TASK_LENGTH)
                return "task #" + t.Id + " is longer than " + MAX_TASK_LENGTH + " characters.";
            if (t.Id > maxId)
                maxId = t.Id;
        }

        if (db.NextId <= 0)
            return "NextId (" + db.NextId + ") must be positive.";

        if (db.NextId <= maxId)
            return "NextId (" + db.NextId + ") is not above the highest task id (#" + maxId + "), " +
                   "so new tasks would reuse existing numbers.";

        return null;
    }

    private void SaveDatabase(TaskDatabase db)
    {
        CPH.SetGlobalVar(DATABASE_NAME, JsonConvert.SerializeObject(db, Formatting.None), true);
    }

    // Returns false if writing failed: nothing was saved, the viewer is told,
    // and the caller must not send a success reply. If only the overlay update
    // fails, the change IS saved; that's logged and the command carries on.
    private bool Save(TaskDatabase db, string evt, int? taskId)
    {
        try
        {
            SaveDatabase(db);
        }
        catch (Exception ex)
        {
            CPH.LogError("[CommunityTasks] Couldn't save the task list, nothing was changed: " + ex.Message);
            Reply("⚠️ Couldn't save that change. Please try again in a moment.");
            return false;
        }

        try
        {
            Broadcast(db, evt, taskId);
        }
        catch (Exception ex)
        {
            CPH.LogWarn("[CommunityTasks] Change saved, but the overlay update failed: " + ex.Message +
                        " The overlay will catch up on the next change or refresh.");
        }
        return true;
    }

    private void RecoverByClearing()
    {
        string backupName = DATABASE_NAME + "_Backup_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string raw;
        try
        {
            object value = CPH.GetGlobalVar<object>(DATABASE_NAME, true);
            raw = value == null ? "" : value.ToString();
        }
        catch (Exception ex)
        {
            CPH.LogError("[CommunityTasks] Recovery cancelled: couldn't read '" + DATABASE_NAME +
                         "' to back it up (" + ex.Message + "). Nothing was changed.");
            Reply("⚠️ Couldn't back up the broken task list, so nothing was changed. Check the Streamer.bot log.");
            return;
        }

        try
        {
            CPH.SetGlobalVar(backupName, raw, true);
        }
        catch (Exception ex)
        {
            CPH.LogError("[CommunityTasks] Recovery cancelled: couldn't write the backup (" + ex.Message + "). Nothing was changed.");
            Reply("⚠️ Couldn't back up the broken task list, so nothing was changed. Check the Streamer.bot log.");
            return;
        }
        CPH.LogWarn("[CommunityTasks] Unreadable task data backed up to '" + backupName + "'. Starting a fresh list.");

        if (!Save(new TaskDatabase(), "cleared", null)) return;
        Reply("🗑️ The old task list couldn't be read, so it was backed up and a fresh list was started.");
    }

    private void Broadcast(TaskDatabase db, string evt, int? taskId)
    {
        OverlayData data = new OverlayData { Event = evt, TaskId = taskId };
        foreach (CommunityTask t in db.Tasks)
            data.Tasks.Add(new OverlayTask { Id = t.Id, User = t.User, Text = t.Text, Done = t.Done, Created = t.Created });

        string json = JsonConvert.SerializeObject(data, Formatting.None, new JsonSerializerSettings
        {
            ContractResolver  = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        });

        CPH.WebsocketBroadcastJson(json);
    }
}