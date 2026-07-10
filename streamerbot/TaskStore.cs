using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public class CommunityTask
{
    public int Id { get; set; }
    public string User { get; set; }
    public string Text { get; set; }
    public bool Done { get; set; }
    public DateTime Created { get; set; }
}

public class TaskDatabase
{
    public int Version { get; set; } = 1;
    public int NextId { get; set; } = 1;
    public List<CommunityTask> Tasks { get; set; } = new List<CommunityTask>();
}

public class CPHInline
{
    // ===== Database =====
    private const string DATABASE_NAME = "CommunityTasks";

    // ===== Operations =====
    private const string OP_TEST = "test";
    private const string OP_ADD = "add";
    private const string OP_COMPLETE = "complete";
    private const string OP_REMOVE = "remove";
    private const string OP_GET = "get";
    private const string OP_RESET = "reset";

    // ===== Arguments =====
    private const string ARG_OPERATION = "operation";
    private const string ARG_USER = "user";
    private const string ARG_TEXT = "text";
    private const string ARG_TASK_ID = "taskId";

    // ===== Limits =====
    private const int MAX_TASK_LENGTH = 90;
    private const int MAX_TOTAL_TASKS = 50;
    private const int MAX_USER_ACTIVE_TASKS = 3;

    public bool Execute()
    {
        TaskDatabase db = LoadDatabase();
        string operation = OP_TEST;
        if (args.TryGetValue(ARG_OPERATION, out object op))
            operation = op.ToString().ToLower();

        switch (operation)
        {
            case OP_ADD:
                if (!args.ContainsKey(ARG_USER) || !args.ContainsKey(ARG_TEXT))
                {
                    CPH.SendMessage("TaskStore: Missing arguments.");
                    break;
                }

                AddTask( db, args[ARG_USER].ToString(), args[ARG_TEXT].ToString() );
                break;

            case OP_COMPLETE:
                if (!args.ContainsKey(ARG_USER))
                {
                    CPH.SendMessage("TaskStore: Missing user.");
                    break;
                }

                int taskId = 0;

                if (args.ContainsKey(ARG_TASK_ID))
                    int.TryParse(args[ARG_TASK_ID].ToString(), out taskId);

                CompleteTask( db, args[ARG_USER].ToString(), taskId );
                break;

            case OP_REMOVE:
                CPH.SendMessage("RemoveTask not implemented yet.");
                break;

            case OP_GET:
                CPH.SendMessage("Tasks: " + db.Tasks.Count);
                break;

            case OP_RESET:
                CPH.SendMessage("Reset not implemented yet.");
                break;

            default:
                CPH.SendMessage("TaskStore OK (" + db.Tasks.Count + " tasks)");
                break;
        }

        return true;
    }

    private TaskDatabase LoadDatabase()
    {
        string json = CPH.GetGlobalVar<string>(DATABASE_NAME, true);

        if (string.IsNullOrWhiteSpace(json))
        {
            TaskDatabase db = new TaskDatabase();
            SaveDatabase(db);
            return db;
        }

        try
        {
            TaskDatabase db = JsonConvert.DeserializeObject<TaskDatabase>(json);
            if (db == null || db.Version != 1)
                throw new Exception();
            return db;
        }
        catch
        {
            TaskDatabase db = new TaskDatabase();
            SaveDatabase(db);
            return db;
        }
    }

    private void SaveDatabase(TaskDatabase db)
    {
        string json = JsonConvert.SerializeObject(db, Formatting.Indented);
        CPH.SetGlobalVar(DATABASE_NAME, json, true);
    }

    private void BroadcastOverlay(TaskDatabase db)
    {
        // TODO: WebSocket broadcast
    }

    private void Save(TaskDatabase db)
    {
        SaveDatabase(db);
        BroadcastOverlay(db);
    }

    private void AddTask(TaskDatabase db, string user, string text)
    {   
        user = user.Trim();
        text = text.Trim();

        while (text.Contains("  "))
            text = text.Replace("  ", " ");

        if (text.Length == 0)
        {
            CPH.SendMessage("❌ Please provide a task.");
            return;
        }

        if (text.Length > MAX_TASK_LENGTH)
        {
            CPH.SendMessage("❌ Tasks are limited to " + MAX_TASK_LENGTH + " characters.");
            return;
        }

        int activeUser = 0;
        bool duplicate = false;

        foreach (CommunityTask existingTask in db.Tasks)
        {
            if (existingTask.Done)
                continue;

            if (existingTask.User.Equals(user, StringComparison.OrdinalIgnoreCase))
            {
                activeUser++;

                if (existingTask.Text.Trim().Equals(text, StringComparison.OrdinalIgnoreCase))
                    duplicate = true;
            }
        }

        if (activeUser >= MAX_USER_ACTIVE_TASKS)
        {
            CPH.SendMessage("❌ You already have " + MAX_USER_ACTIVE_TASKS + " active tasks.");
            return;
        }

        if (duplicate)
        {
            CPH.SendMessage("❌ You already have this task.");
            return;
        }

        if (!MakeRoomForNewTask(db))
        {
            CPH.SendMessage("❌ The community task list is full. Complete some tasks first!");
            return;
        }
        CommunityTask task = new CommunityTask();

        task.Id = db.NextId++;
        task.User = user;
        task.Text = text;
        task.Done = false;
        task.Created = DateTime.UtcNow;

        db.Tasks.Add(task);
        Save(db);
        CPH.SendMessage("✅ Task #" + task.Id + " added! (" + db.Tasks.Count + "/" + MAX_TOTAL_TASKS + ")");
    }

    private void CompleteTask(TaskDatabase db, string user, int taskId)
    {
        user = user.Trim();
        CommunityTask task = FindTask(db, user, taskId);

        if (task == null)
        {
            if (taskId > 0)
                CPH.SendMessage("❌ Task #" + taskId + " doesn't exist.");
            else
                CPH.SendMessage("❌ You don't have any active tasks.");
            return;
        }

        if (task.Done)
        {
            CPH.SendMessage("❌ Task #" + task.Id + " is already completed.");
            return;
        }

        if (!task.User.Equals(user, StringComparison.OrdinalIgnoreCase))
        {
            CPH.SendMessage("❌ Task #" + task.Id + " belongs to another user.");
            return;
        }

        task.Done = true;
        Save(db);
        CPH.SendMessage("✅ Task #" + task.Id + " completed!");
    }

    private bool MakeRoomForNewTask(TaskDatabase db)
    {
        if (db.Tasks.Count < MAX_TOTAL_TASKS)
            return true;

        CommunityTask oldestCompleted = null;

        foreach (CommunityTask task in db.Tasks)
        {
            if (!task.Done)
                continue;

             if (oldestCompleted == null || task.Created < oldestCompleted.Created)
            { oldestCompleted = task; }
        }

        if (oldestCompleted == null)
            return false;

        db.Tasks.Remove(oldestCompleted);
        return true;
    }

    private CommunityTask FindTask(TaskDatabase db, string requestingUser, int taskId)
    {
        // !done (no ID) -> oldest active task of this user
        if (taskId <= 0)
        {
            CommunityTask oldestTask = null;

            foreach (CommunityTask task in db.Tasks)
            {
                if (task.Done)
                    continue;

                if (!task.User.Equals(requestingUser, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (oldestTask == null || task.Created < oldestTask.Created)
                    oldestTask = task;
            }
            return oldestTask;
        }

        // !done <ID> -> find task by ID
        foreach (CommunityTask task in db.Tasks)
        {
            if (task.Id == taskId)
                return task;
        }
        return null;
    }
}