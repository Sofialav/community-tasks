const overlay = {
    tasks: [],

    init() {
        // Fake data for now
        this.tasks = [
            {
                "id":1,
                "user":"Alice",
                "text":"Finish thumbnail",
                "done":false
            }
        ];
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
            const indicator = task.done
            ? `<div class="indicator"><div class="status">✓</div></div>`
            : `<div class="indicator"><div class="dot" style="background:${colorFromName(task.user)}"></div></div>`;

            card.innerHTML = `
                ${indicator}
                <div class="user">
                    ${task.user}
                </div>
                <div class="content">
                    ${task.text}
                </div>
            `;

            container.appendChild(card);
        });
    }
};

// Nice pastel colors
const userColors = [
    "#C084FC", // Purple
    "#60A5FA", // Blue
    "#34D399", // Green
    "#FBBF24", // Yellow
    "#FB7185", // Pink
    "#F97316", // Orange
    "#2DD4BF", // Teal
    "#A78BFA", // Lavender
    "#F472B6", // Rose
    "#22C55E", // Emerald

];

function colorFromName(name) {
    let hash = 0;
    for (const char of name) {
        hash += char.charCodeAt(0);
    }
    return userColors[Math.abs(hash) % userColors.length];
}

overlay.init();