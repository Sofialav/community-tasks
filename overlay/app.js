/* =====================================================================
   Community Tasks — OBS overlay
   Settings come from the Browser Source URL, e.g.
     index.html?port=8080&password=secret&title=My%20Tasks
   Use setup.html to build that URL without typing it by hand.
   ===================================================================== */

const params = new URLSearchParams(location.search);
const flag = (key, def) => {
    if (!params.has(key)) return def;
    const v = (params.get(key) || "").toLowerCase();
    return !(v === "0" || v === "false" || v === "off" || v === "no");
};

// Same rule as setup.html: a number is clamped to 1–30; missing or not a number means 9.
function clampRows(raw) {
    const n = Number.parseInt(raw, 10);
    return Number.isFinite(n) ? Math.max(1, Math.min(30, n)) : 9;
}

const CONFIG = {
    host:      params.get("host")     || "127.0.0.1",
    port:      params.get("port")     || "8080",
    endpoint:  params.get("endpoint") || "/",
    password:  params.get("password") || "",
    action:    params.get("action")   || "TaskStore",   // name of the Streamer.bot action
    title:     params.get("title"),                     // null = keep the HTML title
    extra:     params.get("extra")    || "",            // extra line under Commands
    footer:    flag("footer", true),
    hideDone:  flag("hidedone", false),
    animate:   flag("anim", true),
    status:    flag("status", true),
    rows:      clampRows(params.get("rows")),          // visible tasks, 1–30 (default 9)
    autoscroll: flag("autoscroll", true),               // slowly scroll long lists up and down
    scrollSpeed: Math.max(5, parseInt(params.get("speed"), 10) || 25), // pixels per second
    demo:      flag("demo", false),
    debug:     flag("debug", false)                    // ?debug=1 logs every WebSocket message
};

// Username colours (picked from the name, so each viewer keeps theirs)
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
    for (const char of String(name)) hash += char.charCodeAt(0);
    return userColors[Math.abs(hash) % userColors.length];
}

/* ---------------------------------------------------------------------
   Rendering + animations
   --------------------------------------------------------------------- */

const EASE_OUT = "cubic-bezier(.22,1,.36,1)";
const EASE_SPRING = "cubic-bezier(.34,1.56,.64,1)";

const overlay = {
    rows: new Map(),       // key -> { el, task }
    firstRender: true,

    els: {
        list:    document.getElementById("taskList"),
        counter: document.getElementById("counter"),
        empty:   document.getElementById("empty"),
        status:  document.getElementById("status"),
        statusText: document.getElementById("statusText"),
        panel:   document.getElementById("panel")
    },

    init() {
        if (!CONFIG.animate) document.body.classList.add("no-anim");
        document.documentElement.style.setProperty("--visible-tasks", CONFIG.rows);
        if (CONFIG.title !== null) document.getElementById("title").textContent = CONFIG.title;
        if (!CONFIG.footer) document.getElementById("footer").hidden = true;
        if (CONFIG.extra) {
            document.getElementById("extraText").textContent = CONFIG.extra;
            document.getElementById("extraCommands").hidden = false;
        }
        this.sparkles = document.createElement("div");
        this.sparkles.className = "sparkle-layer";
        document.body.appendChild(this.sparkles);
        this.setTasks([]);
        this.firstRender = true;
    },

    key(task) {
        // id alone is reused after !clear, so include the creation time
        return task.id + "|" + (task.created || "");
    },

    setTasks(tasks) {
        if (!Array.isArray(tasks)) return;
        const visible = CONFIG.hideDone ? tasks.filter(t => !t.done) : tasks;
        const animate = CONFIG.animate && !this.firstRender;
        const list = this.els.list;
        const seen = new Set();
        const added = [];
        let anchor = null; // last row placed, used to keep DOM order in sync

        visible.forEach(task => {
            const k = this.key(task);
            seen.add(k);
            let row = this.rows.get(k);

            if (!row) {
                row = { el: this.createRow(task), task };
                this.rows.set(k, row);
                const next = anchor ? anchor.nextSibling : list.firstChild;
                list.insertBefore(row.el, next);
                added.push(row);
            } else {
                this.updateRow(row, task, animate);
                // keep order identical to the backend list
                const expected = anchor ? anchor.nextSibling : list.firstChild;
                if (expected !== row.el) list.insertBefore(row.el, expected);
            }
            row.task = task;
            anchor = row.el;
        });

        // rows that disappeared
        for (const [k, row] of this.rows) {
            if (seen.has(k)) continue;
            this.rows.delete(k);
            if (animate) this.animateOut(row.el);
            else row.el.remove();
        }

        if (this.firstRender && CONFIG.animate) {
            added.forEach((row, i) => this.animateFirstLoad(row.el, i));
        } else if (animate) {
            added.forEach((row, i) => this.animateIn(row.el, row.task, i));
        }

        this.updateCounter(tasks, animate);
        this.els.empty.hidden = visible.length > 0;

        if (added.length && !this.firstRender) {
            // show the new task, then let it sit there for a while
            scroller.hold(NEW_TASK_HOLD);
            this.scrollToRow(added[added.length - 1].el, animate);
        } else if (this.firstRender) {
            list.scrollTop = list.scrollHeight;
            scroller.hold(END_PAUSE);
        }

        this.firstRender = false;
    },

    createRow(task) {
        const el = document.createElement("div");
        el.className = "task";

        const id = document.createElement("div");
        id.className = "task-id";

        const user = document.createElement("div");
        user.className = "user";

        const content = document.createElement("div");
        content.className = "content";

        el.append(id, user, content);
        this.fill(el, task);
        el.classList.toggle("completed", !!task.done);
        return el;
    },

    fill(el, task) {
        // textContent only: chat input is never parsed as HTML
        const id = el.querySelector(".task-id");
        const user = el.querySelector(".user");
        const content = el.querySelector(".content");
        id.textContent = "#" + task.id;
        user.textContent = task.user;
        user.style.color = colorFromName(task.user);
        content.textContent = task.text;
        el.title = task.text;
    },

    updateRow(row, task, animate) {
        const wasDone = !!row.task.done;
        const isDone = !!task.done;
        if (row.task.text !== task.text || row.task.user !== task.user) this.fill(row.el, task);

        if (wasDone !== isDone) {
            row.el.classList.toggle("completed", isDone);
            if (isDone && animate) {
                row.el.classList.remove("just-completed");
                void row.el.offsetWidth; // restart CSS animation
                row.el.classList.add("just-completed");
                setTimeout(() => row.el.classList.remove("just-completed"), 700);
                this.burst(row.el.querySelector(".task-id"), "var(--accent)", 8, 30);
            }
        }
    },

    /* New task: row grows open, slides in, gets a colour wash + sparkles */
    animateIn(el, task, index) {
        const color = colorFromName(task.user);
        const delay = index * 90;
        const h = el.offsetHeight;

        // clip vertically while growing, but let the colour wash spill sideways
        el.style.clipPath = "inset(0 -14px)";

        const grow = el.animate(
            [
                { height: "0px", minHeight: "0px", paddingTop: "0px", paddingBottom: "0px" },
                { height: h + "px", minHeight: h + "px", paddingTop: "6px", paddingBottom: "6px" }
            ],
            { duration: 320, delay, easing: EASE_OUT, fill: "backwards" }
        );
        grow.onfinish = () => { el.style.clipPath = ""; };

        [...el.children].forEach((child, i) => {
            child.animate(
                [
                    { opacity: 0, transform: "translateX(-28px)", filter: "blur(4px)" },
                    { opacity: 1, transform: "translateX(0)", filter: "blur(0)" }
                ],
                { duration: 560, delay: delay + 120 + i * 60, easing: EASE_SPRING, fill: "backwards" }
            );
        });

        // soft colour wash behind the row
        const wash = document.createElement("span");
        wash.className = "wash";
        Object.assign(wash.style, {
            position: "absolute", inset: "2px -10px", borderRadius: "10px",
            background: color, pointerEvents: "none", opacity: "0", zIndex: "0"
        });
        el.prepend(wash);
        wash.animate(
            [
                { opacity: 0 },
                { opacity: 0.38, offset: 0.18 },
                { opacity: 0.18, offset: 0.5 },
                { opacity: 0 }
            ],
            { duration: 1800, delay: delay + 150, easing: "ease-out" }
        ).onfinish = () => wash.remove();

        setTimeout(() => this.burst(el.querySelector(".task-id"), color, 12, 44), delay + 300);
    },

    /* First load / reconnect: gentle staggered fade */
    animateFirstLoad(el, index) {
        el.animate(
            [
                { opacity: 0, transform: "translateY(6px)" },
                { opacity: getComputedStyle(el).opacity, transform: "translateY(0)" }
            ],
            { duration: 300, delay: Math.min(index, 15) * 35, easing: "ease-out", fill: "backwards" }
        );
    },

    /* Removed task: fade + slide right, then collapse */
    animateOut(el) {
        const h = el.offsetHeight;
        el.style.overflow = "hidden";
        el.style.pointerEvents = "none";
        const anim = el.animate(
            [
                { height: h + "px", minHeight: h + "px", paddingTop: "6px", paddingBottom: "6px", opacity: getComputedStyle(el).opacity, transform: "translateX(0)" },
                { height: h + "px", minHeight: h + "px", paddingTop: "6px", paddingBottom: "6px", opacity: 0, transform: "translateX(24px)", offset: 0.55 },
                { height: "0px", minHeight: "0px", paddingTop: "0px", paddingBottom: "0px", opacity: 0, transform: "translateX(24px)" }
            ],
            { duration: 420, easing: "ease-in-out", fill: "forwards" }
        );
        anim.onfinish = () => el.remove();
    },

    /* Little star burst from an element, drawn above the panel */
    burst(origin, color, count, distance) {
        if (!CONFIG.animate || !origin) return;
        const r = origin.getBoundingClientRect();
        const listRect = this.els.list.getBoundingClientRect();
        if (r.bottom < listRect.top || r.top > listRect.bottom) return; // scrolled away
        const cx = r.left + 10;
        const cy = r.top + r.height / 2;

        for (let i = 0; i < count; i++) {
            const s = document.createElement("span");
            s.className = "sparkle";
            s.style.left = cx + "px";
            s.style.top = cy + "px";
            s.style.setProperty("--c", i % 3 === 0 ? "#fff" : color);
            this.sparkles.appendChild(s);

            const angle = (Math.PI * 2 * i) / count + Math.random() * 0.6;
            const dist = distance * (0.6 + Math.random() * 0.6);
            const dx = Math.cos(angle) * dist;
            const dy = Math.sin(angle) * dist;
            const scale = 0.5 + Math.random() * 0.8;

            s.animate(
                [
                    { transform: "translate(0,0) scale(0) rotate(0deg)", opacity: 1 },
                    { transform: `translate(${dx * 0.7}px,${dy * 0.7}px) scale(${scale}) rotate(90deg)`, opacity: 1, offset: 0.5 },
                    { transform: `translate(${dx}px,${dy}px) scale(0) rotate(180deg)`, opacity: 0 }
                ],
                { duration: 700 + Math.random() * 300, easing: "cubic-bezier(.2,.7,.3,1)" }
            ).onfinish = () => s.remove();
        }
    },

    updateCounter(tasks, animate) {
        const completed = tasks.filter(t => t.done).length;
        const text = `${completed} / ${tasks.length}`;
        if (this.els.counter.textContent === text) return;
        this.els.counter.textContent = text;
        if (animate) {
            this.els.counter.animate(
                [{ transform: "scale(1)" }, { transform: "scale(1.3)", color: "var(--accent)" }, { transform: "scale(1)" }],
                { duration: 450, easing: EASE_SPRING }
            );
        }
    },

    scrollToRow(el, animate) {
        const list = this.els.list;
        // wait for the grow animation so the final height is known
        setTimeout(() => {
            const top = el.offsetTop - list.offsetTop;
            const bottom = top + el.offsetHeight;
            if (top < list.scrollTop || bottom > list.scrollTop + list.clientHeight) {
                list.scrollTo({ top: bottom - list.clientHeight + 12, behavior: animate ? "smooth" : "auto" });
            }
        }, animate ? 60 : 0);
    },

    setStatus(text) {
        if (!CONFIG.status) return;
        if (text) {
            this.els.statusText.textContent = text;
            this.els.status.hidden = false;
        } else {
            this.els.status.hidden = true;
        }
    }
};

/* ---------------------------------------------------------------------
   Auto-scroll: when the list is longer than the visible rows, glide slowly
   to the bottom, pause, glide back to the top, pause, and repeat.
   A new task interrupts it: the list jumps to the task and waits there.
   --------------------------------------------------------------------- */

const END_PAUSE = 3000;       // ms to wait at the top and bottom
const NEW_TASK_HOLD = 6000;   // ms to keep a newly added task in view

const scroller = {
    pos: 0,
    dir: 1,
    holdUntil: 0,
    last: 0,

    start() {
        if (!CONFIG.autoscroll) return;
        requestAnimationFrame(t => this.tick(t));
    },

    hold(ms) {
        this.holdUntil = Math.max(this.holdUntil, performance.now() + ms);
    },

    tick(now) {
        const list = overlay.els.list;
        const max = list.scrollHeight - list.clientHeight;
        const dt = this.last ? Math.min((now - this.last) / 1000, 0.1) : 0;
        this.last = now;

        if (max <= 1) {
            // everything fits: nothing to scroll
            this.pos = 0;
            this.dir = 1;
        } else if (now < this.holdUntil) {
            // paused (or another scroll is running): follow wherever the list is
            this.pos = list.scrollTop;
        } else {
            // something else moved the list (new task): continue from there
            if (Math.abs(list.scrollTop - this.pos) > 2) this.pos = list.scrollTop;
            // parked at an end pointing into it (e.g. after jumping to a new
            // task at the bottom): turn around, the hold already was the pause
            if (this.dir > 0 && this.pos >= max - 0.5) this.dir = -1;
            else if (this.dir < 0 && this.pos <= 0.5) this.dir = 1;

            this.pos += this.dir * CONFIG.scrollSpeed * dt;

            // reached an end while gliding: stop exactly there, pause, turn around
            if (this.dir > 0 && this.pos >= max - 0.5) {
                this.pos = max; this.dir = -1; this.hold(END_PAUSE);
            } else if (this.dir < 0 && this.pos <= 0.5) {
                this.pos = 0; this.dir = 1; this.hold(END_PAUSE);
            }
            list.scrollTop = this.pos;
        }

        requestAnimationFrame(t => this.tick(t));
    }
};

/* ---------------------------------------------------------------------
   Streamer.bot WebSocket connection (auto-reconnect + optional password)
   --------------------------------------------------------------------- */

const connection = {
    socket: null,
    retry: 0,
    ready: false,
    statusTimer: null,
    answerTimer: null,
    gotData: false,

    url() {
        const path = CONFIG.endpoint.startsWith("/") ? CONFIG.endpoint : "/" + CONFIG.endpoint;
        return `ws://${CONFIG.host}:${CONFIG.port}${path}`;
    },

    connect() {
        this.ready = false;
        // only show the status line if connecting takes a moment
        if (!this.statusTimer) {
            this.statusTimer = setTimeout(() => {
                if (!this.ready) overlay.setStatus(`Waiting for Streamer.bot on ${CONFIG.host}:${CONFIG.port}…`);
                this.statusTimer = null;
            }, 2500);
        }

        let socket;
        try {
            socket = new WebSocket(this.url());
        } catch (err) {
            console.error("[CommunityTasks] bad WebSocket URL", err);
            return this.scheduleReconnect();
        }
        this.socket = socket;

        // Streamer.bot always sends "Hello" first; nothing is sent until it
        // arrives (and, if a password is set, until authentication succeeds).
        socket.onopen = () => {
            if (CONFIG.debug) console.log("[CommunityTasks] WebSocket opened; waiting for Hello.");
        };

        socket.onmessage = async (event) => {
            let msg;
            try {
                msg = JSON.parse(event.data);
            } catch {
                if (CONFIG.debug) console.warn("[CommunityTasks] Non-JSON WebSocket message:", event.data);
                return;
            }
            if (CONFIG.debug) console.log("[CommunityTasks] WebSocket message:", msg);

            if (msg.request === "Hello") {
                if (msg.authentication) {
                    if (!CONFIG.password) {
                        overlay.setStatus("Streamer.bot needs a password — add &password=… to the URL");
                        console.warn("[CommunityTasks] WebSocket authentication is enabled but no password was given.");
                        return;
                    }
                    const auth = await makeAuth(CONFIG.password, msg.authentication.salt, msg.authentication.challenge);
                    socket.send(JSON.stringify({ request: "Authenticate", id: "ct-auth", authentication: auth }));
                } else {
                    this.start();
                }
                return;
            }

            if (msg.id === "ct-auth") {
                if (msg.status === "ok") this.start();
                else overlay.setStatus("Streamer.bot rejected the WebSocket password");
                return;
            }

            if (msg.id === "ct-get" && msg.status === "error") {
                clearTimeout(this.answerTimer);
                overlay.setStatus(`Action "${CONFIG.action}" not found in Streamer.bot`);
                return;
            }

            // TaskStore's broadcast arrives as a General.Custom event: { event: {...}, data: <our JSON> }.
            // Only accept data that TaskStore marked as its own.
            const payload = msg.data;
            if (!payload || payload.source !== "CommunityTasks" || !Array.isArray(payload.tasks)) return;
            if (!this.gotData) {
                this.gotData = true;
                clearTimeout(this.answerTimer);
                overlay.setStatus(null);
            }
            overlay.setTasks(payload.tasks);
        };

        socket.onclose = () => {
            clearTimeout(this.answerTimer);
            if (this.ready) {
                // lost an established connection: tell the streamer right away-ish
                this.statusTimer = setTimeout(() => {
                    overlay.setStatus("Reconnecting to Streamer.bot…");
                    this.statusTimer = null;
                }, 2500);
            }
            this.ready = false;
            this.scheduleReconnect();
        };

        socket.onerror = () => { /* onclose follows and handles it */ };
    },

    start() {
        if (this.ready || !this.socket || this.socket.readyState !== WebSocket.OPEN) return;
        this.ready = true;
        this.retry = 0;
        clearTimeout(this.statusTimer);
        this.statusTimer = null;
        overlay.setStatus(null);

        this.socket.send(JSON.stringify({
            request: "Subscribe",
            id: "ct-subscribe",
            events: { General: ["Custom"] }
        }));
        this.socket.send(JSON.stringify({
            request: "DoAction",
            id: "ct-get",
            action: { name: CONFIG.action },
            args: { operation: "get" }
        }));

        // Connected, but if TaskStore never sends the list back, its code didn't
        // compile or it couldn't load the saved tasks. Say so instead of
        // quietly showing an empty list.
        this.gotData = false;
        clearTimeout(this.answerTimer);
        this.answerTimer = setTimeout(() => {
            if (!this.gotData)
                overlay.setStatus(`Connected, but "${CONFIG.action}" didn't answer. Check its C# code compiles and the Streamer.bot log.`);
        }, 4000);
    },

    scheduleReconnect() {
        const delay = Math.min(1000 * 2 ** this.retry, 10000);
        this.retry++;
        setTimeout(() => this.connect(), delay);
    }
};

async function sha256Base64(text) {
    const bytes = new TextEncoder().encode(text);
    const hash = await crypto.subtle.digest("SHA-256", bytes);
    return btoa(String.fromCharCode(...new Uint8Array(hash)));
}

async function makeAuth(password, salt, challenge) {
    const secret = await sha256Base64(password + salt);
    return sha256Base64(secret + challenge);
}

/* ---------------------------------------------------------------------
   Demo mode (?demo=1): fake chat activity, no Streamer.bot needed.
   Handy for previewing your theme in a normal browser.
   --------------------------------------------------------------------- */

const demo = {
    tasks: [],
    nextId: 1,
    users: ["MossyMoth", "lofi_owl", "PetalPusher", "QuietFern", "StudyBun", "nightjar", "CloverCat"],
    texts: ["Study for 1 hour", "Finish chapter 3 reading", "Reply to emails", "Water the plants",
            "30 min of Spanish", "Fix the login bug", "Sketch practice", "Clean my desk",
            "Write 500 words", "Review flashcards", "Draft the slide deck", "Stretch break"],

    start() {
        for (let i = 0; i < 4; i++) this.add();
        this.tasks[0].done = true;
        overlay.setTasks(this.copy());
        setInterval(() => this.tick(), 2600);
    },

    add() {
        const user = this.users[Math.floor(Math.random() * this.users.length)];
        const text = this.texts[Math.floor(Math.random() * this.texts.length)];
        this.tasks.push({ id: this.nextId++, user, text, done: false, created: new Date().toISOString() + Math.random() });
    },

    tick() {
        const open = this.tasks.filter(t => !t.done);
        const r = Math.random();
        const doneIdx = this.tasks.findIndex(t => t.done);
        if (this.tasks.length > 14 && doneIdx >= 0) this.tasks.splice(doneIdx, 1);
        else if (r < 0.55 || open.length === 0) this.add();
        else open[Math.floor(Math.random() * open.length)].done = true;
        overlay.setTasks(this.copy());
    },

    copy() { return this.tasks.map(t => ({ ...t })); }
};

/* --------------------------------------------------------------------- */

overlay.init();
scroller.start();
if (CONFIG.demo) demo.start();
else connection.connect();