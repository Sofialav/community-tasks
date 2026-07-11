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

## Installation

1. Create a new **TaskStore** Action in Streamer.bot.
2. Add an **Execute C#** sub-action.
3. Copy the contents of `TaskStore.cs` into the C# sub-action.
4. Import or create the command Actions (`!task` and `!done`) and configure them to execute **TaskStore** with the appropriate arguments (look **Command Setup** section).
5. Enable the **WebSocket Server** in Streamer.bot.
6. Copy the `overlay` folder anywhere on your PC.
7. Add `overlay/index.html` as a **Browser Source** in OBS.

## Command Setup

### !task

Create a new Action (e.g. **Create Community Task**) with the following sub-actions:

1. Set Argument
   - `operation` = `add`
2. Set Argument
   - `user` = `%userName%`
3. Set Argument
   - `text` = `%rawInput%`
4. Execute Action → **TaskStore**

Create a Command that triggers this Action.

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

Create a Command that triggers this Action.

## Commands

!task [text] 
 -  Creates a new task.
!done
 - Completes your oldest active task.
!done [id]
 - Completes one of your tasks by ID.

## Requirements

- Streamer.bot 1.0+
- OBS Studio

## Architecture

- `TaskStore` is the central backend Action.
- Chat commands execute `TaskStore` with different operations (`add`, `complete`, `get`).
- The overlay connects to the Streamer.bot WebSocket and updates automatically whenever tasks change.
