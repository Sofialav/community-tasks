const socket = new WebSocket("ws://127.0.0.1:8080/"); // settings of your streamer.bot websocket

socket.onopen = () => {
    // Subscribe to custom broadcasts
    socket.send(JSON.stringify({
        request: "Subscribe",
        id: "subscribeCustom",
        events: {
            General: ["Custom"]
        }
    }));
    // Then request the initial task list
    socket.send(JSON.stringify({
        request: "DoAction",
        id: "communityTasks",
        action: {
            name: "TaskStore" // name of your action in streamer.bot
        },
        args: {
            operation: "get"
        }
    }));
};

socket.onclose = () => {
    console.log("Disconnected");
};

socket.onerror = (err) => {
    console.error(err);
};

socket.onmessage = (event) => {
    let message;
    try {
        message = JSON.parse(event.data);
    }
    catch {
        return;
    }
    // Ignore Streamer.bot system messages
    if (message.data?.tasks) {
        overlay.setTasks(message.data.tasks);
    }
};

const overlay = {
    tasks: [],

    init() {
        this.tasks = [];
        this.render();
    },

    setTasks(tasks) {
        this.tasks = tasks;
        this.render();
    },

    render() {
        const container = document.getElementById("taskList");
        const counter = document.getElementById("counter");

        container.innerHTML = "";

        const completed = this.tasks.filter(t => t.done).length;
        counter.textContent = `${completed} / ${this.tasks.length}`;

        this.tasks.forEach(task => {
            const card = document.createElement("div");
            card.className = task.done ? "task completed" : "task";
            card.dataset.id = task.id;
            card.dataset.done = task.done;

            card.innerHTML = `
                <div class="task-id">#${task.id}</div>
                <div class="user"
                    style="color:${task.done ? "#A66A59" : colorFromName(task.user)}">
                    ${task.user}
                </div>
                <div class="content">
                    ${task.text}
                </div>
            `;

            container.appendChild(card);
        });

        if (container.scrollHeight > container.clientHeight) {
            container.scrollTo({
                top: container.scrollHeight,
                behavior: "smooth"
            });
        }
    }
};

const userColors = [
    "#C19BD6", // Lavender
    "#8FAFC0", // Dusty blue
    "#9DB99A", // Sage
    "#C7A878", // Muted gold
    "#C18D98", // Dusty rose
    "#B39478", // Warm brown
    "#82AAA2", // Teal
    "#A99BC8", // Soft violet
    "#BE96AD", // Mauve
    "#9BA989"  // Olive
];

function colorFromName(name) {
    let hash = 0;
    for (const char of name) {
        hash += char.charCodeAt(0);
    }
    return userColors[Math.abs(hash) % userColors.length];
}

overlay.init();