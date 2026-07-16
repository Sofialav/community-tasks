# Community Tasks

A lightweight real-time community task list powered by Streamer.bot and an OBS browser source.

## Features

- Live task updates (WebSocket)
- Persistent task storage
- Up to 50 tasks
- Per-user task limit
- Duplicate task prevention
- Task ownership protection
- OBS overlay included
- Automatic cleanup of old completed tasks when the list is full
- Complete or clear tasks with chat commands

## Requirements

- Streamer.bot 1.0+
- OBS Studio

## Installation

1. Create a new **TaskStore** Action in Streamer.bot.
2. Add an **Execute C#** sub-action.
3. Copy the contents of `TaskStore.cs` into the C# sub-action.
4. Import or create the command Actions (`!task` and `!done`) and configure them to execute **TaskStore** with the appropriate arguments (look **Command Setup** section).
5. Enable the **WebSocket Server** in Streamer.bot.
6. Copy the `overlay` folder anywhere on your PC.
7. Add `overlay/index.html` as a **Browser Source** in OBS.

## Command Setup

The backend is exposed through a single Action named **TaskStore**. Each chat command simply sets the required arguments before executing that Action.
ex: "Create community task" action in Streamer.bot:

<img width="654" height="408" alt="image" src="https://github.com/user-attachments/assets/fce1a805-10ae-401b-b587-dcf143cf8c4d" />


### !task

Create a new Action (e.g. **Create Community Task**) with the following sub-actions:

1. Set Argument
   - `operation` = `add`
2. Set Argument
   - `user` = `%userName%`
3. Set Argument
   - `text` = `%rawInput%`
4. Execute Action → **TaskStore**

Create a Command (`!task`) that triggers this Action.

---

### !done

Create a new Action (e.g. **Complete Community Task**) with the following sub-actions:

1. Set Argument
   - `operation` = `complete`
2. Set Argument
   - `user` = `%userName%`
3. Set Argument
   - `taskId` = `%rawInput%`
4. Execute Action → **TaskStore**

Create a Command (`!done`) that triggers this Action.

### !clear

Create an Action (for example **Clear Community Tasks**) with these sub-actions:

1. **Set Argument**
   - `operation` = `reset`
2. **Set Argument**
   - `filter` = `%rawInput%`
3. **Execute Action**
   - `TaskStore`

Create a chat command (`!clear`) that triggers this Action.

## Chat Commands

| Command -------------| Description --------------------------------------------------------|
|----------------------|---------------------------------------------------------------------|
| `!task <description>`| Create a new task.                                                  |
| `!done`              | Complete your oldest active task.                                   |
| `!done <task id>`    | Complete one of your tasks by ID.                                   |
| `!clear`             | Clear the entire task list (recommended for broadcaster/mods only). |
| `!clear completed`   | Remove only completed tasks.                                        |

## Customization

- You can change panel color scheme by modifying color variables in the `:root` in `style.css` 
- You can change username colors by modifying `userColors` array in `app.js`

## Architecture

- `TaskStore` is the central backend Action.
- Chat commands execute `TaskStore` with different operations (`add`, `complete`, `get`).
- Tasks are stored in a Streamer.bot Global Variable.
- Every change is automatically broadcast through the Streamer.bot WebSocket.
- The OBS overlay listens for those broadcasts and updates instantly.
