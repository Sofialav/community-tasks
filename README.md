# 🌸 Community Tasks

A cozy, real-time community task list for coworking and study streams.
Viewers add their goals with `!task`, tick them off with `!done`, and everyone sees the list update live in an OBS overlay.

Works with **Streamer.bot** + **OBS Studio**. No server, no accounts, no coding.

## Features

- Live overlay updates over the Streamer.bot WebSocket
- Animations: new tasks slide in with a colour wash and sparkles, finished tasks get struck through, removed ones fold away
- Tasks persist between streams
- Up to 50 tasks, max 3 unfinished per viewer, duplicates blocked
- Only the owner can finish a task; only the broadcaster can clear (optionally moderators too)
- Full list? The oldest finished task makes room automatically
- Auto-reconnects if Streamer.bot starts after OBS (or restarts mid-stream)
- Supports WebSocket password authentication
- Setup page with a connection tester and live preview

## Requirements

- [Streamer.bot](https://streamer.bot) 1.0+
- [OBS Studio](https://obsproject.com) 28+

---

## Install (about 5 minutes)

### 1. Download

Download this repository (**Code → Download ZIP**) and unzip it somewhere permanent, for example `Documents\community-tasks`. OBS loads the overlay from this folder, so don't delete it afterwards.

### 2. Turn on the Streamer.bot WebSocket Server

In Streamer.bot: **Servers/Clients → WebSocket Server**
- Tick **Auto Start**, then click **Start Server**.
- Note the **port** (default `8080`).
- **Recommended:** keep the address on `127.0.0.1` (OBS and Streamer.bot on the same PC) **and** turn on **authentication** with a password. Anything that can reach this server can run your actions with any arguments, including pretending to be you. Never expose it to your network or the internet without authentication.

### 3. Create the `TaskStore` action

1. Go to **Actions**, right-click the list → **Add**, name it exactly `TaskStore`.
2. In **Sub-Actions**, right-click → **Core → C# → Execute C# Code**.
3. Delete the example code, paste in everything from [`streamerbot/TaskStore.cs`](streamerbot/TaskStore.cs).
4. Click **Compile**. The log at the bottom should say *Compiled successfully*. Then **Save and Compile**.

### 4. Add the chat commands

1. Go to **Commands**, right-click → **Add**, and create these three (leave **Location** on *Start*):

   | Command  | Suggested permissions                                                    |
   |----------|--------------------------------------------------------------------------|
   | `!task`  | Everyone                                                                 |
   | `!done`  | Everyone                                                                 |
   | `!clear` | Bodcaster/Moderators (the code itself only lets the broadcaster through) |

2. Back in **Actions → TaskStore**, right-click **Triggers → Core → Commands → Command Triggered**, and pick `!task`. Repeat for `!done` and `!clear`.

All three commands go to the same action. The code works out what to do from the command name, so you don't need any *Set Argument* sub-actions.

### 5. Add the overlay to OBS

1. Open `overlay/setup.html` in your normal web browser (double-click it).
2. Check the port, enter your password if you have one, and click **Test connection**. You want the green "You're good to go!": it means `TaskStore` really ran and sent back the task list.
3. Optionally change the title or add a line with your own commands. The preview updates as you type.
4. Click **Copy**.
5. In OBS: **Sources → + → Browser**. Untick **Local file**, paste the link into **URL**, set **Width 560**, **Height 760**.

Done. Type `!task Test the overlay` in your chat. 🎉

---

## Chat commands

| Command               | What it does                                     | Who          |
|-----------------------|--------------------------------------------------|--------------|
| `!task <description>` | Add a task                                       | Everyone     |
| `!done`               | Finish your oldest unfinished task               | Everyone     |
| `!done <id>`          | Finish one of your tasks by number (`!done 3`)   | Everyone     |
| `!clear`              | Wipe the whole list and restart numbering at #1  | Broadcaster* |
| `!clear completed`    | Remove only finished tasks                       | Broadcaster* |
| `!clear <id>`         | Remove a single task (`!clear 7`), e.g. spam     | Broadcaster* |

\* Moderators too if you set `CLEAR_ALLOW_MODERATORS = true` in `TaskStore.cs`.

Invalid input never changes anything: `!clear typo` or `!clear 0` and `!done abc` or `!done 0` just reply with how to use the command.

## Settings

### Overlay (URL options)

`setup.html` builds these for you, but you can also add them to the Browser Source URL by hand, e.g. `.../index.html?port=8081&hidedone=1`.

| Option     | Default       | Description                                             |
|------------|---------------|---------------------------------------------------------|
| `host`     | `127.0.0.1`   | PC running Streamer.bot                                 |
| `port`     | `8080`        | Streamer.bot WebSocket Server port                      |
| `password` | *(none)*      | WebSocket password, only if authentication is on. It's stored in plain text in the OBS link, so don't show the Browser Source properties on stream or share scene collection exports |
| `action`   | `TaskStore`   | Name of the Streamer.bot action                         |
| `title`    | 🌸 Community Tasks 🌸 | Panel title                                     |
| `extra`    | *(none)*      | Extra line under Commands, e.g. `!lurk !help`           |
| `footer`   | `1`           | `0` hides the commands footer                           |
| `hidedone` | `0`           | `1` hides finished tasks                                |
| `anim`     | `1`           | `0` turns all animations off                            |
| `status`   | `1`           | `0` hides "Waiting for Streamer.bot…" messages          |
| `demo`     | `0`           | `1` shows fake chat activity (handy for theming)        |
| `debug`    | `0`           | `1` logs every WebSocket message to the console. Open the overlay link in Chrome/Edge and press F12 to see it |

### Backend (`TaskStore.cs`)

All of these are in the **SETTINGS** block at the top of the file:

| Setting                 | Default | Description                                     |
|-------------------------|---------|-------------------------------------------------|
| `MAX_TASK_LENGTH`       | `90`    | Characters per task                             |
| `MAX_TOTAL_TASKS`       | `50`    | Tasks kept on the list                          |
| `MAX_USER_ACTIVE_TASKS` | `3`     | Unfinished tasks per viewer                     |
| `CLEAR_ALLOW_MODERATORS` | `false` | `true` lets moderators use `!clear` as well as the broadcaster |
| `CHAT_REPLIES`          | `true`  | Bot replies in chat                             |
| `MENTION_USER`          | `true`  | Start replies with `@username`                  |
| `COMMAND_MAP`           |         | Command words → actions. Add yours here if you rename or translate commands (e.g. `{ "tarefa", "add" }`) |

After editing, click **Save and Compile**.

### Colours and layout

- Colours, panel width and list height are CSS variables at the top of `overlay/style.css`.
- Username colours are the `userColors` list near the top of `overlay/app.js`.
- The command list in the footer is plain HTML in `overlay/index.html`.

Tip: open `overlay/index.html?demo=1` in your browser to see changes with fake tasks.

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Overlay says **"Waiting for Streamer.bot…"** | Streamer.bot isn't running, or the WebSocket Server isn't started, or the port doesn't match. Check step 2. |
| **"Streamer.bot needs a password"** / **"rejected the password"** | Authentication is on: add the correct password in `setup.html` and copy the new link. |
| **"Connected, but TaskStore didn't answer"** | The overlay reached Streamer.bot, but the action sent nothing back. Open the C# sub-action and click **Compile**: fix any error, then **Save and Compile**. If it compiles, check the Streamer.bot log for `[CommunityTasks]` lines (e.g. the saved list couldn't be loaded). |
| **"Action TaskStore not found"** | The action name must match exactly (case included), or set `action=` in the URL. |
| Commands do nothing | Check that the action and the commands are **enabled**, and that each command has a *Command Triggered* trigger on `TaskStore`. Look at the Streamer.bot log for `[CommunityTasks]` messages. |
| Compile error | Make sure you pasted the **whole** file, starting at the `using` lines. |
| Overlay is blank in OBS but works in the browser | In the Browser Source properties, **Local file** must be unticked and **URL** must contain the exact link copied from `setup.html`. Click **Refresh cache of current page**. If it's still blank, open the same link in Chrome/Edge with `&debug=1` added (or `?debug=1` if it has no `?` yet), press F12 and check the console. |
| Chat says **"The task list couldn't be loaded"** | The saved data is damaged, too big for your current limits (e.g. you lowered `MAX_TOTAL_TASKS` or `MAX_TASK_LENGTH`), or was made by a newer `TaskStore.cs`. The reason is in the Streamer.bot log (search for `[CommunityTasks]`). Nothing is changed until you act. **Quick fix:** type `!clear` as the broadcaster: the old data is copied to a backup variable `CommunityTasks_Backup_<date>` (under **Variables → Persisted Globals**) and a fresh list starts. If the log mentions an unsupported version, update `TaskStore.cs` instead. |
| Bot doesn't reply | Replies go to Twitch chat through Streamer.bot's `SendMessage`. Check your bot/broadcaster account is connected. |

## Updating from an older version

Your existing tasks are kept and shown, but tasks created before this version have no Twitch user id, so viewers can't finish them with `!done`. Run `!clear` once (or `!clear <id>` for specific ones) and have viewers re-add them. Replace the C# code in `TaskStore` with the new file and **Save and Compile**, then replace the `overlay` folder.

The old setup (separate actions with *Set Argument* → *Execute Action TaskStore*) still works. To simplify it, delete those helper actions and attach the commands to `TaskStore` directly (step 4).

## How it works

- `TaskStore` is the only action. It handles `add`, `complete`, `reset` and `get`, either from the triggering command or from an `operation` argument.
- Tasks are stored as JSON in a persisted Streamer.bot global variable called `CommunityTasks`.
- Every change is broadcast over the Streamer.bot WebSocket. The overlay subscribes, asks for the current list on connect, and animates the differences.
- Chat text is always rendered as plain text, so viewers can't inject HTML into your overlay.
- Task ownership is tied to the viewer's Twitch user id, not their name, so renaming doesn't lose your tasks and someone who takes your old name can't finish them. Tasks saved by older versions have no id, so they can't be finished with `!done`; remove them with `!clear`.
- `!clear` refuses anything without an identified Twitch user. The broadcaster is checked against the Twitch account Streamer.bot is logged into, and moderators against the channel's moderator list from Twitch, never against `isModerator`/`isBroadcaster` flags.
- **Limit:** a Streamer.bot action only learns *who* triggered it from its arguments. Anything that can call actions directly (the WebSocket Server or HTTP Server) could pass a forged `userId`. So keep the WebSocket Server on `127.0.0.1` with **authentication turned on**, and leave the HTTP Server off unless you need it.

## License

MIT, see [LICENSE](LICENSE).