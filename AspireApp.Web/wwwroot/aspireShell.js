// aspireShell.js — layout chrome behaviour.
//
// Loaded as an ES module through JS interop (JS.InvokeVoidAsync("aspireShell.start")),
// because a plain <script src> placed after <Routes /> is not reliably executed
// under the static prerender / enhanced-navigation pipeline.
//
// The rail is docked on wide screens and an off-canvas drawer on phones. All the
// placement lives in CSS (see MainLayout.razor.css); this module owns only the
// interaction: the toggle, the scrim, Escape, and closing the drawer after a
// navigation. It is idempotent, so calling start() on every first render is safe.

let started = false;

function init() {
    const shell = document.getElementById("app-shell");
    if (!shell || started || shell.dataset.shellReady === "true") {
        return;
    }
    started = true;

    const toggle = document.getElementById("rail-toggle");
    const scrim = document.getElementById("app-scrim");
    const crumbs = document.getElementById("topbar-crumbs__current");

    const isOpen = () => shell.classList.contains("nav-open");

    function setOpen(open) {
        shell.classList.toggle("nav-open", open);
        if (toggle) {
            toggle.setAttribute("aria-expanded", open ? "true" : "false");
        }
        document.body.style.overflow = open ? "hidden" : "";
    }

    toggle?.addEventListener("click", () => setOpen(!isOpen()));
    scrim?.addEventListener("click", () => setOpen(false));

    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape" && isOpen()) {
            setOpen(false);
        }
    });

    // Blazor rewrites the DOM in place on navigation, so rail clicks are caught by
    // delegation rather than bound per link.
    shell.addEventListener("click", (e) => {
        if (e.target.closest(".rail-link") && isOpen()) {
            setOpen(false);
        }
    });

    // Mirror the active destination into the command bar as a breadcrumb. The nav is
    // re-rendered on navigation, so poll lightly rather than hook the router internals.
    function syncCrumbs() {
        if (!crumbs) {
            return;
        }
        const active = shell.querySelector(".rail-link.active .rail-link__label");
        const label = active?.textContent?.trim() ?? "";
        crumbs.textContent = label.length > 0 ? label : "Inicio";
    }

    let lastPath = location.pathname;
    syncCrumbs();
    setInterval(() => {
        syncCrumbs();
        if (location.pathname !== lastPath) {
            lastPath = location.pathname;
            if (isOpen()) {
                setOpen(false);
            }
        }
    }, 250);

    shell.dataset.shellReady = "true";
}

/// <summary>Entry point called from MainLayout via JS interop.</summary>
export function start() {
    // The layout may not be in the DOM yet on the very first interactive render.
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init, { once: true });
    } else {
        init();
    }
}
