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

    // ===== Limits =====
    private const int MAX_TASK_LENGTH = 90;
    private const int MAX_TOTAL_TASKS = 50;
    private const int MAX_USER_ACTIVE_TASKS = 3;

    public bool Execute()
    {
        TaskDatabase db = LoadDatabase();
        string operation = OP_TEST;

        if (args.ContainsKey(ARG_OPERATION))
            operation = args[ARG_OPERATION].ToString().ToLower();

        switch (operation)
        {
            case OP_ADD:
                AddTask(db);
                break;

            case OP_COMPLETE:
                CPH.SendMessage("CompleteTask not implemented yet.");
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
            return JsonConvert.DeserializeObject<TaskDatabase>(json);
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

    private void AddTask(TaskDatabase db)
    {
        if (!args.ContainsKey(ARG_USER) || !args.ContainsKey(ARG_TEXT))
        {
            CPH.SendMessage("TaskStore: Missing arguments.");
            return;
        }

        string user = args[ARG_USER].ToString().Trim();
        string text = args[ARG_TEXT].ToString().Trim();

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

        foreach (CommunityTask task in db.Tasks)
        {
            if (task.Done)
                continue;

            if (task.User.Equals(user, StringComparison.OrdinalIgnoreCase))
            {
                activeUser++;
                
                if (task.Text.Equals(text, StringComparison.OrdinalIgnoreCase))
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
        SaveDatabase(db);
        CPH.SendMessage("✅ Task #" + task.Id + " added! (" + db.Tasks.Count + "/" + MAX_TOTAL_TASKS + ")");
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
}